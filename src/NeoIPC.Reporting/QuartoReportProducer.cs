using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace NeoIPC.Reporting;

/// <summary>
/// Base class for any generator that renders a Quarto report. Sets up
/// a per-render symlink-forest workdir, composes <c>quarto render</c>
/// invocations with the right profile / language / media-type flags,
/// and parses the structured Quarto log on failure.
/// </summary>
/// <remarks>
/// <para>
/// Why a symlink-forest per render: every render needs a private
/// scratch dir for Quarto's intermediate files (<c>_output/</c>,
/// <c>_freeze/</c>, etc.) but must not duplicate the report sources
/// (could be tens of MB). The per-report dir's contents are mirrored
/// as a tree of symlinks — directory structure is real, files are
/// symlinks back to the read-only source tree.
/// </para>
///
/// <para>
/// Layout: each render gets a <c>render_&lt;random&gt;/</c> root under
/// <see cref="ReportingOptions.ReportsTempDir"/>, with a
/// <c>reports/&lt;reportName&gt;/</c> subtree as the QMD's working dir.
/// This matches the toolkit's repo layout (<c>&lt;toolkit&gt;/reports/&lt;Report&gt;/</c>
/// with <c>&lt;toolkit&gt;/glossary*.yaml</c> at the toolkit root) so the
/// QMD's relative reaches resolve:
/// <list type="bullet">
///   <item><description><c>../common/</c>, <c>../filters/</c>, <c>../logos/</c>
///   — top-level shared sibling dirs of <see cref="ReportingOptions.ReportsSourceDir"/>
///   are surfaced as single dir-symlinks under <c>render_xxx/reports/</c>.</description></item>
///   <item><description><c>../common.yaml</c>, <c>../common.&lt;lang&gt;.yaml</c>
///   — top-level files of the source dir are surfaced as file-symlinks
///   under <c>render_xxx/reports/</c>.</description></item>
///   <item><description><c>../../glossary.yaml</c>, <c>../../glossary.&lt;lang&gt;.yaml</c>
///   — the toolkit-root glossary files (one level above the source dir)
///   are surfaced as file-symlinks under <c>render_xxx/</c>. Optional;
///   missing source files are skipped.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Shared sibling dirs (<c>common/</c>, <c>filters/</c>, <c>logos/</c>)
/// are exposed as single directory symlinks rather than file-by-file
/// mirrors: they are read-only resources, never write targets, so a
/// dir-symlink is correct and saves N file-symlinks per render. The
/// per-report dir is mirrored file-by-file because Quarto writes
/// intermediates into it.
/// </para>
///
/// <para>
/// On dispose the entire <c>render_&lt;random&gt;/</c> root is
/// recursively deleted; the read-only source tree is never touched.
/// </para>
/// </remarks>
abstract class QuartoReportProducer : ExternalProcessReportProducer
{
    readonly DirectoryInfo _renderRoot;
    readonly DirectoryInfo _workingDirectory;
    readonly string _quartoLogFilePath;
    readonly ReportingOptions _options;

    // Set by the drain (see DrainDiagnostics / ReportLogDrain): the raw Quarto
    // records for the failure-path ProblemDetails payload, and whether the
    // well-known #13394 rename error (a false failure) was seen.
    JsonArray _quartoJsonData = new();
    bool _quartoIssue13394Success;

    public string SessionId { get; }
    public ResolvedLocale Locale { get; }

    protected QuartoReportProducer(
        string reportName,
        string mediaType,
        ResolvedLocale locale,
        string sessionId,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
        : base(mediaType, reportName, environment, loggerFactory)
    {
        Locale = locale;
        SessionId = sessionId;
        _options = options.Value;

        var languages = registry.ForReport(reportName);
        if (!languages.TryGetValue(locale.Language, out var qmdFileName))
            throw new InvalidOperationException(
                $"Report '{reportName}' has no QMD file registered for language '{locale.Language}'.");
        ReportFileName = qmdFileName;

        var srcRootDir = new DirectoryInfo(_options.ReportsSourceDir);
        if (!srcRootDir.Exists)
            throw new DirectoryNotFoundException(
                $"Reports source directory '{srcRootDir.FullName}' not found.");
        var srcDir = new DirectoryInfo(Path.Join(srcRootDir.FullName, reportName));
        if (!srcDir.Exists)
            throw new DirectoryNotFoundException($"Report directory '{srcDir.FullName}' not found.");

        // Reserve a unique render root and create the layered structure
        // expected by the toolkit's relative reaches:
        //   <renderRoot>/glossary*.yaml          (../../glossary*.yaml)
        //   <renderRoot>/reports/common*.yaml    (../common*.yaml)
        //   <renderRoot>/reports/{common,filters,logos,…}/   (../<sib>/)
        //   <renderRoot>/reports/<reportName>/   (Quarto cwd)
        var attempts = 0;
        const int maxAttempts = ushort.MaxValue;
        DirectoryInfo? renderRoot = null;
        do
        {
            attempts++;
            var renderRootName = Path.Join(_options.ReportsTempDir,
                $"render_{Path.GetRandomFileName()}");
            if (Directory.Exists(renderRootName))
                continue;
            renderRoot = new DirectoryInfo(renderRootName);
            renderRoot.Create();
            break;
        } while (attempts < maxAttempts);

        if (renderRoot == null)
            throw new IOException("Failed to create a temporary directory.");

        var reportsParent = renderRoot.CreateSubdirectory("reports");

        // Surface every top-level entry of <ReportsSourceDir>/ except the
        // current report's own directory under <renderRoot>/reports/. Files
        // become file-symlinks; sibling dirs become single dir-symlinks
        // (read-only resources — no need to mirror file-by-file).
        foreach (var srcChild in srcRootDir.EnumerateFileSystemInfos())
        {
            if (string.Equals(srcChild.Name, reportName, StringComparison.Ordinal))
                continue;
            if (srcChild.Name == ".gitignore") continue;
            // A .quarto at the reports-source root is Quarto's regenerable scratch
            // dir, never a source input. Dir-symlinking it would point Quarto's
            // read-write cache targets (project-cache/deno-kv-file) back into the
            // read-only source mount — the same failure the per-report mirror's
            // IsUnderQuartoScratch filter prevents. Skip it here too (defensive:
            // Quarto only opens the report's own .quarto, not a sibling's).
            if (string.Equals(srcChild.Name, QuartoScratchDirName, StringComparison.Ordinal))
                continue;
            var linkPath = Path.Join(reportsParent.FullName, srcChild.Name);
            if (srcChild is DirectoryInfo)
                Directory.CreateSymbolicLink(linkPath, srcChild.FullName);
            else
                File.CreateSymbolicLink(linkPath, srcChild.FullName);
        }

        // Glossary files live one level above the source dir in the toolkit's
        // own layout (<toolkit>/glossary*.yaml, <toolkit>/reports/...). Surface
        // them under <renderRoot>/ so ../../glossary*.yaml resolves. Missing
        // files are silently skipped — the cascade in helpers.R guards each
        // glossary read with file.exists().
        var toolkitRoot = srcRootDir.Parent;
        if (toolkitRoot is { Exists: true })
        {
            foreach (var glossary in toolkitRoot.EnumerateFiles("glossary*.yaml",
                         SearchOption.TopDirectoryOnly))
            {
                File.CreateSymbolicLink(
                    Path.Join(renderRoot.FullName, glossary.Name),
                    glossary.FullName);
            }
        }

        // Per-report dir: file-by-file symlink mirror, since Quarto writes
        // intermediates here and we need the read-write surface to be
        // private to this render. Quarto's own scratch/cache directory
        // (.quarto) is excluded from the mirror: it is regenerated on every
        // render and is not a source input. A host-side .quarto left by a
        // developer who rendered the report directly would otherwise be
        // mirrored as symlinks pointing back into the read-only source mount,
        // including .quarto/project-cache/deno-kv-file — and Quarto opens that
        // KV file read-write, so the render fails to open its project cache
        // ("unable to open database file").
        var reportDir = reportsParent.CreateSubdirectory(reportName);
        Parallel.ForEach(
            srcDir.EnumerateDirectories("*", SearchOption.AllDirectories)
                .Where(d => !IsUnderQuartoScratch(srcDir, d)),
            srcChild => Directory.CreateDirectory(Path.Join(reportDir.FullName,
                Path.GetRelativePath(srcDir.FullName, srcChild.FullName))));
        Parallel.ForEach(
            srcDir.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Name != ".gitignore" && !IsUnderQuartoScratch(srcDir, f)),
            srcFile =>
                File.CreateSymbolicLink(
                    Path.Join(reportDir.FullName,
                        Path.GetRelativePath(srcDir.FullName, srcFile.FullName)),
                    srcFile.FullName));

        _renderRoot = renderRoot;
        _workingDirectory = reportDir;
        _quartoLogFilePath = Path.Join(reportDir.FullName, "quarto-log.json");
        // The report's R code (run inside Quarto via knitr) writes its
        // structured logger records here; drained alongside the Quarto log.
        RLogFilePath = Path.Join(reportDir.FullName, "r-log.json");
        // The unique workdir name doubles as this render's correlation id.
        RenderId = renderRoot.Name;
    }

    // Quarto's per-project scratch/cache directory. Never a source input
    // (Quarto recreates it each render); excluded from the symlink-forest
    // mirror so its read-write targets are not symlinked back to the
    // read-only source mount — see the per-report mirror in the constructor.
    const string QuartoScratchDirName = ".quarto";

    static bool IsUnderQuartoScratch(DirectoryInfo srcRoot, FileSystemInfo entry)
    {
        var segments = Path.GetRelativePath(srcRoot.FullName, entry.FullName)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        return Array.Exists(segments,
            s => string.Equals(s, QuartoScratchDirName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Yields the <c>key:value</c> pairs the subclass wants emitted as
    /// <c>-P key value</c> on the Quarto command line. Each value must
    /// be YAML-safe — the subclass (or its source-generator-emitted
    /// argument builder) is responsible for quoting.
    /// </summary>
    protected abstract IEnumerable<string> GetReportParameters();

    /// <summary>The QMD filename to render (locale-resolved by <see cref="ReportLanguageRegistry"/>).</summary>
    protected string ReportFileName { get; }

    /// <summary>
    /// The per-render workdir where the symlink-forest lives. Subclasses
    /// can stage extra files into this dir before <c>Generate()</c>
    /// fires (e.g. <see cref="QuartoPartnerReportProducer"/> stages a
    /// transient partner-data JSON here in online mode).
    /// </summary>
    protected DirectoryInfo WorkingDirectory => _workingDirectory;

    protected sealed override ProcessStartInfo GetProcessStartInfo()
    {
        // One verbosity dial for the whole render, derived from the minimum
        // effective level across the render's per-source category sub-tree:
        // NEOIPC_LOG_LEVEL governs the R side (incl. the DHIS2 query trace),
        // NEOIPC_LOG_FILE is where the R logger writes its structured records,
        // and Quarto's --log-level (below) is derived too.
        var effectiveLevel = ReportLogging.EffectiveMinLevel(LoggerFactory, RenderCategory, DrainCategorySuffixes);
        var startInfo = new ProcessStartInfo("quarto", GetArguments())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workingDirectory.FullName,
            EnvironmentVariables =
            {
                ["NEOIPC_DHIS2_SESSION_ID"] = SessionId,
                ["NEOIPC_LOG_LEVEL"] = ReportLogging.ToNeoIpcLogLevel(effectiveLevel),
                ["NEOIPC_LOG_FILE"] = RLogFilePath,
                ["LANGUAGE"] = Locale.Language,
                ["LANG"] = Locale.LcAll,
                ["LC_ALL"] = Locale.LcAll,
            },
        };

        if (_options.BuildMode == BuildMode.Workspace)
            startInfo.EnvironmentVariables["NEOIPCR_DEV_PATH"] = _options.NeoIpcrDevPath;
        else
            startInfo.EnvironmentVariables.Remove("NEOIPCR_DEV_PATH");

        return startInfo;

        IEnumerable<string> GetArguments()
        {
            yield return "render";
            yield return ReportFileName;

            yield return "--log";
            yield return _quartoLogFilePath;

            yield return "--log-level";
            // Floored at info: Quarto re-logs every Pandoc line as an INFO
            // record, so a coarser level would drop Pandoc warnings/errors
            // before they reach the log file (see ReportLogging / the drain).
            yield return ReportLogging.ToQuartoLogLevel(effectiveLevel);

            yield return "--log-format";
            yield return "json-stream";

            yield return "--quiet";
            yield return "--to";
            switch (MediaType)
            {
                case "text/html":
                    yield return "html";
                    yield return "--embed-resources";
                    break;
                case "application/pdf":
                    yield return "pdf";
                    yield return "--pdf-engine=lualatex";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var profiles = new List<string> { Locale.Language };
            if (MediaType == "text/html") profiles.Add("minimal");
            yield return "--profile";
            yield return string.Join(",", profiles);

            foreach (var arg in GetReportParameters())
            {
                yield return "-P";
                yield return arg;
            }

            yield return "--output";
            yield return "-";
        }
    }

    // A Quarto render also drains the Quarto json-stream into the .Quarto /
    // .Pandoc channels, so they join the sub-tree the child write-floor derives
    // from (the base set covers only the root + R channels).
    protected override IReadOnlyList<string> DrainCategorySuffixes { get; } =
        ["", ".R.report", ".R.common", ".R.neoipcr", ".Quarto", ".Pandoc"];

    protected override async ValueTask DrainDiagnostics(int exitCode, CancellationToken cancellationToken)
    {
        await base.DrainDiagnostics(exitCode, cancellationToken); // R layout_json
        var result = await ReportLogDrain.DrainQuartoLogAsync(
            _quartoLogFilePath, LoggerFactory, RenderCategory, exitCode, cancellationToken);
        _quartoJsonData = result.RawRecords;
        _quartoIssue13394Success = result.Issue13394Success;
    }

    protected sealed override ValueTask<DataResult> HandleError(int processId, int exitCode,
        Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(stdErrString))
            Logger.LogDebug("{StdErr}", stdErrString);

        if (!File.Exists(_quartoLogFilePath))
        {
            // Quarto exited non-zero before writing its structured log (e.g. an early startup error):
            // the only signal is stderr, which is logged at Debug above and so is invisible in
            // Production. Surface the exit code + stderr so this failure mode is diagnosable.
            Logger.LogError("Quarto render process {QuartoRenderProcessId} exited {ExitCode} without writing a log file. Stderr: {StdErr}",
                processId, exitCode, stdErrString);
            return ValueTask.FromResult(new DataResult(detail: "The Quarto log file does not exist.", statusCode: 500,
                showMessage: Environment.IsDevelopment()));
        }

        // The Quarto log was already drained into ILogger by DrainDiagnostics,
        // which also captured the raw records and flagged the #13394 case.
        return ValueTask.FromResult(_quartoIssue13394Success
            ? DataResult.SimpleSuccess
            : new DataResult(
                title: "Quarto Error",
                detail: "An error occurred while executing Quarto to create a report",
                statusCode: 500,
                extensions: new Dictionary<string, object?> { { "quartoLog", _quartoJsonData } },
                showMessage: Environment.IsDevelopment()));
    }

    public override ValueTask DisposeAsync()
    {
        if (_renderRoot.Exists)
        {
            // Double-gated dev aid: keep a failed render's workdir (its .tex,
            // the Quarto/Pandoc/lualatex logs, and generated figures) for local
            // inspection. Gated on both the config flag AND Development because
            // the workdir holds the rendered report — surveillance data — and so
            // must never be retained on a production instance.
            if (RenderFailed && _options.KeepFailedRenderWorkdir && Environment.IsDevelopment())
                Logger.LogWarning(
                    "Render failed; keeping its workdir for inspection (Reporting:KeepFailedRenderWorkdir + Development). "
                    + "It holds the rendered report — delete it when done: {RenderRoot}",
                    _renderRoot.FullName);
            else
                _renderRoot.Delete(recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    public static bool IsMediaTypeSupported(string mediaType)
        => SupportedMediaTypeHeaderValues.ContainsKey(mediaType) ||
           SupportedMediaTypeHeaderValues.Values.Any(v =>
               v.IsSubsetOf(new MediaTypeHeaderValue(mediaType)));

    public static readonly FrozenDictionary<string, MediaTypeHeaderValue> SupportedMediaTypeHeaderValues =
        new[] { "text/html", "application/pdf" }
            .Select(s => new KeyValuePair<string, MediaTypeHeaderValue>(s,
                new MediaTypeHeaderValue(s)))
            .ToFrozenDictionary(StringComparer.Ordinal);
}
