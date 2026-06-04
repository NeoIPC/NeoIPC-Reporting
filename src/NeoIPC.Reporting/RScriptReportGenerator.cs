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
/// (<see cref="ExternalProcessReportGenerator.GetProcessStartInfo"/>
/// final composition lives here in the base; subclasses contribute
/// the per-script args via the abstract members). The R script is
/// invoked with the JSESSIONID as an env var so neoipcr authenticates
/// against the live DHIS2 instance.
/// </remarks>
abstract class RScriptReportGenerator : ExternalProcessReportGenerator
{
    readonly ReportingOptions _options;

    protected RScriptReportGenerator(
        string mediaType,
        ResolvedLocale locale,
        string sessionId,
        IOptions<ReportingOptions> options,
        IWebHostEnvironment environment,
        ILogger logger) : base(mediaType, environment, logger)
    {
        Locale = locale;
        SessionId = sessionId;
        _options = options.Value;
    }

    public string SessionId { get; }
    public ResolvedLocale Locale { get; }
    protected abstract IEnumerable<string> GetReportParameters();
    protected abstract string ReportFilePath { get; }

    protected sealed override ProcessStartInfo GetProcessStartInfo()
    {
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
