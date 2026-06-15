using Microsoft.Extensions.Options;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

namespace NeoIPC.Reporting;

/// <summary>
/// Maps the service's HTTP endpoints. Extracted from <c>Program.cs</c> so
/// the endpoint set is constructable in a unit test
/// (<c>EndpointAuthorizationTests</c>), which asserts that every endpoint
/// is either route-authorized (carries <c>IAuthorizeData</c>), marked
/// <see cref="InHandlerAuthorized"/>, or marked <see cref="PublicEndpoint"/>
/// — so no endpoint can be added silently public.
/// </summary>
static class ApiEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Render endpoints authorize in-handler (after request-shape
        // validation, and conditionally for Reference) — see
        // NeoIpcAuthorization. The InHandlerAuthorized marker records that
        // so the endpoint-coverage test doesn't flag them as public.
        app.MapGet("reference-report", ReferenceReport.Get)
            .WithName("GetReferenceReport")
            .WithMetadata(new InHandlerAuthorized(
                "NeoIpcReport for stored-data mode; NeoIpcAdmin for ad-hoc live preview (conditional on referenceDataId)"))
            .WithRequestTimeout(TimeSpan.FromSeconds(360));
        app.MapGet("reference-report/parameters", () =>
                Results.Ok(new { fields = ReferenceReportApiParameters.Schema }))
            .WithName("GetReferenceReportParameters")
            .WithMetadata(new PublicEndpoint("static source-generated parameter schema; no data"));
        app.MapGet("partner-report", PartnerReport.Get)
            .WithName("GetPartnerReport")
            .WithMetadata(new InHandlerAuthorized("NeoIpcReport"))
            .WithRequestTimeout(TimeSpan.FromSeconds(600));
        app.MapPost("partner-report", PartnerReport.Post)
            .WithName("PostPartnerReport")
            .WithMetadata(new InHandlerAuthorized("NeoIpcReport"))
            .DisableAntiforgery()
            .WithRequestTimeout(TimeSpan.FromSeconds(600));
        app.MapGet("partner-report/parameters", () =>
                Results.Ok(new { fields = PartnerReportApiParameters.Schema }))
            .WithName("GetPartnerReportParameters")
            .WithMetadata(new PublicEndpoint("static source-generated parameter schema; no data"));

        // Report-layer configuration the app reads to drive its forms: content
        // presets (runtime-read from the toolkit's presets.json) and supported
        // locales (the report-language registry). Both gated at the report tier.
        app.MapGet("reference-report/presets",
                (IOptions<ReportingOptions> o) =>
                    ReportConfigEndpoints.Presets(QuartoReferenceReportProducer.ReportName, o))
            .WithName("GetReferenceReportPresets")
            .RequireAuthorization("NeoIpcReport");
        app.MapGet("reference-report/locales",
                (ReportLanguageRegistry r) =>
                    ReportConfigEndpoints.Locales(QuartoReferenceReportProducer.ReportName, r))
            .WithName("GetReferenceReportLocales")
            .RequireAuthorization("NeoIpcReport");
        app.MapGet("partner-report/presets",
                (IOptions<ReportingOptions> o) =>
                    ReportConfigEndpoints.Presets(QuartoPartnerReportProducer.ReportName, o))
            .WithName("GetPartnerReportPresets")
            .RequireAuthorization("NeoIpcReport");
        app.MapGet("partner-report/locales",
                (ReportLanguageRegistry r) =>
                    ReportConfigEndpoints.Locales(QuartoPartnerReportProducer.ReportName, r))
            .WithName("GetPartnerReportLocales")
            .RequireAuthorization("NeoIpcReport");

        // Report-tier listing — partners pick a referenceDataId from this
        // listing to feed into /reference-report (stored-data mode).
        app.MapGet("reference-data", ReferenceDataEndpoints.List)
            .WithName("ListReferenceData")
            .RequireAuthorization("NeoIpcReport");

        // Everything under /admin/* requires the NeoIPC admin authority.
        var admin = app.MapGroup("admin").RequireAuthorization("NeoIpcAdmin");

        admin.MapGet("reference-data", ReferenceDataEndpoints.AdminList)
            .WithName("AdminListReferenceData");
        admin.MapGet("reference-data/{id}", ReferenceDataEndpoints.AdminDownload)
            .WithName("AdminDownloadReferenceData");
        admin.MapPost("reference-data", ReferenceDataEndpoints.AdminUpload)
            .WithName("AdminUploadReferenceData")
            .DisableAntiforgery()
            .WithRequestTimeout(TimeSpan.FromSeconds(120));
        admin.MapDelete("reference-data/{id}", ReferenceDataEndpoints.AdminDelete)
            .WithName("AdminDeleteReferenceData");

        // The validation-exception file is a singleton (one file, auto-applied),
        // so its admin API has no id segment: GET current metadata, PUT to
        // upload-replace, DELETE to remove.
        admin.MapGet("validation-exceptions", ValidationExceptionEndpoints.AdminGet)
            .WithName("AdminGetValidationException");
        admin.MapPut("validation-exceptions", ValidationExceptionEndpoints.AdminUpload)
            .WithName("AdminUploadValidationException")
            .DisableAntiforgery();
        admin.MapDelete("validation-exceptions", ValidationExceptionEndpoints.AdminDelete)
            .WithName("AdminDeleteValidationException");
    }
}
