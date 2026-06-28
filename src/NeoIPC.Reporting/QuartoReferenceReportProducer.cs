using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Renders the Reference-Report to PDF or HTML via Quarto. Both
/// stored-data mode (<c>?referenceDataId=…</c>) and ad-hoc preview
/// mode arrive at the same generator — the handler resolves the data
/// path before instantiating, so all the per-mode logic stays in
/// <see cref="ReferenceReport.Get"/>.
/// </summary>
sealed class QuartoReferenceReportProducer : QuartoReportProducer
{
    public const string ReportName = "Reference-Report";

    readonly ReferenceReportRenderParameters _renderParameters;

    public QuartoReferenceReportProducer(
        string mediaType,
        ResolvedLocale locale,
        ReferenceReportApiParameters apiParameters,
        ReferenceReportRenderParameters renderParameters,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
        : base(ReportName, mediaType, locale, apiParameters.SessionId,
            options, registry, environment, loggerFactory)
    {
        _renderParameters = renderParameters;
    }

    protected override string? ReportFileDownloadName => "Reference-Report";

    protected override IEnumerable<string> GetReportParameters() =>
        ReferenceReportQuartoArgumentBuilder.Build(_renderParameters);
}
