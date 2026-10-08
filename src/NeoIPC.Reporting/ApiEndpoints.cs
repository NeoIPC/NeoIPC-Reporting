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
        // validation, and conditionally on some values for Reference and
        // Validation) — see NeoIpcAuthorization. The InHandlerAuthorized
        // marker records that so the endpoint-coverage test doesn't flag
        // them as public.
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
        app.MapGet("validation-report", ValidationReport.Get)
            .WithName("GetValidationReport")
            .WithMetadata(new InHandlerAuthorized(
                "NeoIpcReport; NeoIpcAdmin for applyValidationExceptions=false or includeUnusedValidationExceptions=true"))
            .WithRequestTimeout(TimeSpan.FromSeconds(360));
        app.MapGet("validation-report/parameters", () =>
                Results.Ok(new { fields = ValidationReportApiParameters.Schema }))
            .WithName("GetValidationReportParameters")
            .WithMetadata(new PublicEndpoint("static source-generated parameter schema; no data"));

        // Report-layer configuration the app reads to drive its forms: content
        // presets (runtime-read from the toolkit's presets.json), supported
        // locales (the report-language registry), and the Validation Report's
        // rule catalogue (its content/_sR.yaml plus the language overlay). All
        // gated at the report tier.
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
        // The Validation Report has no presets; its form lists the rules instead,
        // read from the report's own string resources.
        app.MapGet("validation-report/locales",
                (ReportLanguageRegistry r) =>
                    ReportConfigEndpoints.Locales(QuartoValidationReportProducer.ReportName, r))
            .WithName("GetValidationReportLocales")
            .RequireAuthorization("NeoIpcReport");
        app.MapGet("validation-report/rules",
                (string? locale, ValidationRuleCatalogue c, ReportLanguageRegistry r, ILoggerFactory l) =>
                    ReportConfigEndpoints.ValidationRules(locale, c, r, l))
            .WithName("GetValidationReportRules")
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

        // The validation-exception file is a singleton (one file, applied to
        // every render unless an administrator switches it off), so its admin
        // API has no id segment: GET current metadata, PUT to upload-replace
        // once neoipcr has read the file, DELETE to remove.
        admin.MapGet("validation-exceptions", ValidationExceptionEndpoints.AdminGet)
            .WithName("AdminGetValidationException");
        admin.MapPut("validation-exceptions", ValidationExceptionEndpoints.AdminUpload)
            .WithName("AdminUploadValidationException")
            .DisableAntiforgery()
            .WithRequestTimeout(TimeSpan.FromSeconds(120));
        admin.MapDelete("validation-exceptions", ValidationExceptionEndpoints.AdminDelete)
            .WithName("AdminDeleteValidationException");

        admin.MapGet("dhis2-public-base-url", Dhis2PublicBaseUrlEndpoint.AdminGet)
            .WithName("AdminGetDhis2PublicBaseUrl");
    }
}
