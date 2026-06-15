using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Renders the Partner-Report to PDF or HTML via Quarto. Always needs
/// a partner-data JSON on disk before <c>Generate()</c> fires. In
/// dataFile (POST) mode the handler streams the request body to disk;
/// in online (GET) mode <c>Partner-Report.qmd</c>'s <c>_setup.qmd</c>
/// runs the neoipcr import inline and writes the JSON itself. Either
/// way the handler tells the generator where the file landed via
/// <see cref="SetPartnerDataPath"/>.
/// </summary>
sealed class QuartoPartnerReportProducer : QuartoReportProducer
{
    public const string ReportName = "Partner-Report";

    readonly PartnerReportRenderParameters _renderParameters;
    string? _partnerDataPath;

    public QuartoPartnerReportProducer(
        string mediaType,
        ResolvedLocale locale,
        PartnerReportApiParameters apiParameters,
        PartnerReportRenderParameters renderParameters,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILogger logger)
        : base(ReportName, mediaType, locale, apiParameters.SessionId,
            options, registry, environment, logger)
    {
        _renderParameters = renderParameters;
    }

    /// <summary>
    /// The path the handler should stage the partner-data JSON to —
    /// inside the per-render workdir so it gets cleaned up alongside
    /// every other intermediate file when the generator disposes.
    /// </summary>
    public string PartnerDataStagingPath =>
        Path.Join(WorkingDirectory.FullName, "partner-data.json");

    /// <summary>Records the partner-data file path picked up by Quarto's <c>-P partnerDataFile</c>.</summary>
    public void SetPartnerDataPath(string path) => _partnerDataPath = path;

    protected override string? ReportFileDownloadName => "Partner-Report";

    protected override IEnumerable<string> GetReportParameters()
    {
        var rp = _renderParameters with { PartnerDataFile = _partnerDataPath };
        return PartnerReportQuartoArgumentBuilder.Build(rp);
    }
}
