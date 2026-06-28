using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Base class for generators that produce a JSON response by running
/// a single <c>Rscript</c> invocation and streaming its stdout back.
/// </summary>
/// <remarks>
/// Subclasses provide <see cref="ReportFilePath"/> and the args
/// (<see cref="ExternalProcessReportProducer.GetProcessStartInfo"/>
/// final composition lives here in the base; subclasses contribute
/// the per-script args via the abstract members). The R script is
/// invoked with the JSESSIONID as an env var so neoipcr authenticates
/// against the live DHIS2 instance.
/// </remarks>
abstract class RScriptReportProducer : ExternalProcessReportProducer
{
    readonly ReportingOptions _options;

    protected RScriptReportProducer(
        string mediaType,
        string reportName,
        ResolvedLocale locale,
        string sessionId,
        IOptions<ReportingOptions> options,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory) : base(mediaType, reportName, environment, loggerFactory)
    {
        Locale = locale;
        SessionId = sessionId;
        _options = options.Value;
        // No per-render workdir on the Rscript path; the R logger writes its
        // structured records to a transient file the drain reads and then
        // DisposeAsync deletes.
        RLogFilePath = Path.Combine(Path.GetTempPath(), $"neoipc-rlog-{Guid.NewGuid():N}.json");
    }

    public override ValueTask DisposeAsync()
    {
        if (RLogFilePath is not null && File.Exists(RLogFilePath))
            File.Delete(RLogFilePath);
        return ValueTask.CompletedTask;
    }

    public string SessionId { get; }
    public ResolvedLocale Locale { get; }
    protected abstract IEnumerable<string> GetReportParameters();
    protected abstract string ReportFilePath { get; }

    protected sealed override ProcessStartInfo GetProcessStartInfo()
    {
        // The Rscript path produces JSON on stdout (the response body), so its
        // diagnostics must not touch stdout: NEOIPC_LOG_FILE routes the R
        // logger to a transient file we drain, and NEOIPC_LOG_LEVEL sets the
        // verbosity from the minimum effective level across the render's
        // per-source category sub-tree (env-only — the Generate scripts fall
        // back to it when no CLI flag is given).
        var effectiveLevel = ReportLogging.EffectiveMinLevel(LoggerFactory, RenderCategory, DrainCategorySuffixes);
        var startInfo = new ProcessStartInfo("Rscript", GetArguments())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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
            yield return "--vanilla";
            yield return ReportFilePath;
            foreach (var arg in GetReportParameters())
                yield return arg;
        }
    }

    public static readonly FrozenDictionary<string, MediaTypeHeaderValue> SupportedMediaTypeHeaderValues =
        new[] { "application/json" }
            .Select(s => new KeyValuePair<string, MediaTypeHeaderValue>(s,
                new MediaTypeHeaderValue(s)))
            .ToFrozenDictionary(StringComparer.Ordinal);
}
