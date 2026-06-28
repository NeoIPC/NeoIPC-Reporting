using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// JSON-output path for the Partner-Report (<c>Accept: application/json</c>
/// on GET). Runs <c>Generate-PartnerData.R</c> with the negotiated
/// filters; the R script writes the partner data JSON to stdout, which
/// the base class streams back as the response body.
/// </summary>
/// <remarks>
/// Not selectable in dataFile mode (POST) — the body of a POST already
/// IS the partner data JSON, and a JSON Accept on dataFile mode would
/// just echo the body. The handler short-circuits that case.
/// </remarks>
sealed class RScriptPartnerReportProducer : RScriptReportProducer
{
    readonly PartnerReportRenderParameters _renderParameters;
    readonly string _scriptPath;

    public RScriptPartnerReportProducer(
        string mediaType,
        ResolvedLocale locale,
        PartnerReportApiParameters apiParameters,
        PartnerReportRenderParameters renderParameters,
        IOptions<ReportingOptions> options,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
        : base(mediaType, QuartoPartnerReportProducer.ReportName, locale, apiParameters.SessionId,
            options, environment, loggerFactory)
    {
        _renderParameters = renderParameters;
        _scriptPath = Path.Combine(
            options.Value.ReportsSourceDir, "Partner-Report", "Generate-PartnerData.R");
    }

    protected sealed override ValueTask<DataResult> HandleError(int processId, int exitCode,
        Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new DataResult(stdErrString, statusCode: 500,
            showMessage: Environment.IsDevelopment()));
    }

    protected override string? ReportFileDownloadName => "Partner-Report-Data";

    public static FrozenDictionary<string, string> SupportedLanguageDictionary { get; } =
        new Dictionary<string, string>
        {
            { "en", "en" },
            { "en-GB", "en" },
            { "de", "de" },
            { "de-DE", "de" },
        }.ToFrozenDictionary();

    protected override IEnumerable<string> GetReportParameters() =>
        PartnerReportRScriptArgumentBuilder.Build(_renderParameters, outputFilePath: null);

    protected override string ReportFilePath => _scriptPath;
}
