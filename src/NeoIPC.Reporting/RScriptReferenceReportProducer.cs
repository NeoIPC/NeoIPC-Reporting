using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// JSON-output path for the Reference-Report (<c>Accept: application/json</c>).
/// Returns the raw R-script output without rendering Quarto. Today only
/// the <c>en</c> language is supported on this path; the heavier locale
/// pipeline lives entirely in the Quarto generator.
/// </summary>
sealed class RScriptReferenceReportProducer : RScriptReportProducer
{
    readonly ReferenceReportRenderParameters _renderParameters;
    readonly string _scriptPath;

    public RScriptReferenceReportProducer(
        string mediaType,
        ResolvedLocale locale,
        ReferenceReportApiParameters apiParameters,
        ReferenceReportRenderParameters renderParameters,
        IOptions<ReportingOptions> options,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
        : base(mediaType, QuartoReferenceReportProducer.ReportName, locale, apiParameters.SessionId,
            options, environment, loggerFactory)
    {
        _renderParameters = renderParameters;
        _scriptPath = Path.Combine(
            options.Value.ReportsSourceDir, "Reference-Report", "Generate-ReferenceData.R");
    }

    protected sealed override ValueTask<DataResult> HandleError(int processId, int exitCode,
        Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new DataResult(stdErrString, statusCode: 500,
            showMessage: Environment.IsDevelopment()));
    }

    protected override string? ReportFileDownloadName => "Reference-Report-Data";

    public static FrozenDictionary<string, string> SupportedLanguageDictionary { get; } =
        new Dictionary<string, string>
        {
            { "en", "en" },
            { "en-GB", "en" },
        }.ToFrozenDictionary();

    protected override IEnumerable<string> GetReportParameters() =>
        ReferenceReportRScriptArgumentBuilder.Build(_renderParameters);

    protected override string ReportFilePath => _scriptPath;
}
