using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Renders the Validation-Report to PDF or HTML via Quarto. The report
/// always fetches live from DHIS2 through neoipcr, so there is a single
/// mode and the handler (<see cref="ValidationReport.Get"/>) only resolves
/// the server-side parameters before instantiating.
/// </summary>
sealed class QuartoValidationReportProducer : QuartoReportProducer
{
    public const string ReportName = "Validation-Report";

    readonly ValidationReportRenderParameters _renderParameters;

    public QuartoValidationReportProducer(
        string mediaType,
        ResolvedLocale locale,
        ValidationReportApiParameters apiParameters,
        ValidationReportRenderParameters renderParameters,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
        : base(ReportName, mediaType, locale, apiParameters.SessionId,
            options, registry, environment, loggerFactory)
    {
        _renderParameters = renderParameters;
    }

    protected override string? ReportFileDownloadName => "Validation-Report";

    protected override IEnumerable<string> GetReportParameters() =>
        ValidationReportQuartoArgumentBuilder.Build(_renderParameters);
}
