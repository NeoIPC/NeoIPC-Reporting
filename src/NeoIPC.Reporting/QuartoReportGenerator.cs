using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
abstract partial class QuartoReportGenerator : ExternalProcessReportGenerator
{
    readonly DirectoryInfo _renderRoot;
    readonly DirectoryInfo _workingDirectory;
    readonly string _quartoLogFilePath;
    readonly ReportingOptions _options;

    public string SessionId { get; }
    public ResolvedLocale Locale { get; }

    protected QuartoReportGenerator(
        string reportName,
        string mediaType,
        ResolvedLocale locale,
        string sessionId,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILogger logger)
        : base(mediaType, environment, logger)
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
        // private to this render.
        var reportDir = reportsParent.CreateSubdirectory(reportName);
        Parallel.ForEach(srcDir.EnumerateDirectories("*", SearchOption.AllDirectories),
            srcChild => Directory.CreateDirectory(Path.Join(reportDir.FullName,
                Path.GetRelativePath(srcDir.FullName, srcChild.FullName))));
        Parallel.ForEach(srcDir.EnumerateFiles("*", SearchOption.AllDirectories),
            srcFile =>
            {
                if (srcFile.Name != ".gitignore")
                    File.CreateSymbolicLink(
                        Path.Join(reportDir.FullName,
                            Path.GetRelativePath(srcDir.FullName, srcFile.FullName)),
                        srcFile.FullName);
            });

        _renderRoot = renderRoot;
        _workingDirectory = reportDir;
        _quartoLogFilePath = Path.Join(reportDir.FullName, "quarto-log.json");
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
    /// fires (e.g. <see cref="QuartoPartnerReportGenerator"/> stages a
    /// transient partner-data JSON here in online mode).
    /// </summary>
    protected DirectoryInfo WorkingDirectory => _workingDirectory;

    /// <summary>
    /// Lets a subclass contribute additional Quarto profile names
    /// (<c>--profile</c>) on top of the locale profile and the
    /// auto-injected "minimal" profile for HTML. Used by
    /// <see cref="QuartoPartnerReportGenerator"/> to select between
    /// <c>full</c> and <c>default</c> profile groups.
    /// </summary>
    protected virtual IEnumerable<string> GetAdditionalProfiles()
    {
        yield break;
    }

    protected sealed override ProcessStartInfo GetProcessStartInfo()
    {
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
            yield return Environment.IsDevelopment() ? "debug" : "warning";

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
            foreach (var p in GetAdditionalProfiles()) profiles.Add(p);
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

    protected sealed override async ValueTask<DataResult> HandleError(int processId, int exitCode,
        Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken)
    {
        var success = false;
        if (!string.IsNullOrWhiteSpace(stdErrString))
            Logger.LogDebug("{StdErr}", stdErrString);

        if (!File.Exists(_quartoLogFilePath))
            return new DataResult(detail: "The Quarto log file does not exist.", statusCode: 500,
                showMessage: Environment.IsDevelopment());

        var minLevel = LogLevel.None;
        for (var i = LogLevel.Trace; i < LogLevel.Critical; i++)
            if (Logger.IsEnabled(i))
            {
                minLevel = i;
                break;
            }

        var previousLogLevel = LogLevel.None;
        var sb = new StringBuilder();
        var jsonData = new JsonArray();
        await foreach (var line in File.ReadLinesAsync(_quartoLogFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var jsonLine = JsonNode.Parse(line);
            jsonData.Add(jsonLine);

            if (jsonLine is not JsonObject jsonObject ||
                !jsonObject.TryGetPropertyValue("levelName", out var levelNode) ||
                !jsonObject.TryGetPropertyValue("msg", out var messageNode))
                continue;

            var message = messageNode?.ToString();
            if (string.IsNullOrWhiteSpace(message))
                continue;

            var currentLogLevel = levelNode?.ToString() switch
            {
                "INFO" => LogLevel.Information,
                "WARNING" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                "CRITICAL" => LogLevel.Critical,
                _ => LogLevel.Debug,
            };

            if (exitCode == 1 &&
                currentLogLevel == LogLevel.Error &&
                QuartoIssue13394DetectionRegex().IsMatch(message))
            {
                if (sb.Length > 0)
                    Logger.Log(previousLogLevel,
                        "Quarto render process {QuartoRenderProcessId}: {Message}",
                        processId, sb.ToString());

                Logger.LogTrace(
                    "Quarto render process {QuartoRenderProcessId}: Hit well-known Quarto bug (https://github.com/quarto-dev/quarto-cli/issues/13394)\n{ Message}",
                    processId, message);
                sb.Length = 0;
                success = true;
                continue;
            }

            if (currentLogLevel < minLevel)
                continue;

            if (previousLogLevel != currentLogLevel)
            {
                Logger.Log(previousLogLevel,
                    "Quarto render process {QuartoRenderProcessId}: {Message}",
                    processId, sb.ToString());
                previousLogLevel = currentLogLevel;
                sb.Length = 0;
            }

            sb.AppendLine(message);
        }

        if (sb.Length > 0)
            Logger.Log(previousLogLevel,
                "Quarto render process {QuartoRenderProcessId}: {Message}",
                processId, sb.ToString());

        return success
            ? DataResult.SimpleSuccess
            : new DataResult(
                title: "Quarto Error",
                detail: "An error occurred while executing Quarto to create a report",
                statusCode: 500,
                extensions: new Dictionary<string, object?> { { "quartoLog", jsonData } },
                showMessage: Environment.IsDevelopment());
    }

    public override ValueTask DisposeAsync()
    {
        if (_renderRoot.Exists)
            _renderRoot.Delete(recursive: true);
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex(@"NotFound: No such file or directory \(os error 2\): rename '.+?(/|\\)-' -> '.+?(/|\\)_output(/|\\)-'")]
    private static partial Regex QuartoIssue13394DetectionRegex();

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
