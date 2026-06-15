using System.Collections.Immutable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

namespace NeoIPC.Reporting;

/// <summary>
/// Endpoint handler for <c>GET /reference-report</c>. The endpoint has
/// two modes distinguished entirely by whether <c>?referenceDataId=</c>
/// is present:
/// <list type="bullet">
///   <item><description><b>Stored-data mode</b> (id present) — render
///   an admin-uploaded dataset. Available to any holder of the
///   <c>F_NEOIPC_REPORT</c> authority (or higher). Live-fetch filter
///   params (<c>reportingPeriodFrom</c>, <c>birthWeightFrom</c>, …) are
///   rejected as a 400 if mixed in; the dataset is fixed and re-applying
///   its filters at render time would be either redundant or
///   contradictory.</description></item>
///   <item><description><b>Ad-hoc preview mode</b> (id absent) —
///   live-fetch render against DHIS2 with the supplied filter params.
///   Requires the <c>F_NEOIPC_ADMIN</c> authority; the handler enforces
///   the gate via <see cref="IAuthorizationService"/> rather than via a
///   route-level policy because the gating is conditional on the
///   query.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Both gates run in-handler, after request-shape validation, so the
/// negative-path checks (malformed/mixed/missing) stay reachable for the
/// integration tests' placeholder sessions. The single admin-uploaded
/// validation-exception file (if any) is folded in automatically. Each
/// content figure/table is an explicit <c>includeX</c> render flag; the
/// app maps presets onto them client-side. Section-text inclusion is
/// governed solely by <c>includeIntroductionTexts</c> /
/// <c>includeMethodsTexts</c>. The Quarto profile is derived server-side
/// from locale + output format and is not part of the API surface.
/// </remarks>
class ReferenceReport
{
    public static async Task<IResult> Get(
        [FromQuery] string? referenceDataId,
        [FromQuery] string? locale,
        [FromQuery] DateOnly? reportingPeriodFrom,
        [FromQuery] DateOnly? reportingPeriodTo,
        [FromQuery] ushort? birthWeightFrom,
        [FromQuery] ushort? birthWeightTo,
        [FromQuery] ushort? gestationalAgeFrom,
        [FromQuery] ushort? gestationalAgeTo,
        [FromQuery] string[] countryFilter,
        [FromQuery] string[] hospitalFilter,
        [FromQuery] bool? testUnitFilter,
        [FromQuery] bool? defaultPatientFilter,
        [FromQuery] ushort? sparseDataThreshold,
        [FromQuery] ConfidenceIntervalMode? confidenceIntervals,
        [FromQuery] bool? includeIntroductionTexts,
        [FromQuery] bool? includeMethodsTexts,
        [FromQuery] bool? includeBirthWeightFigure,
        [FromQuery] bool? includeGestationalAgeFigure,
        [FromQuery] bool? includeIncidenceDensityTable,
        [FromQuery] bool? includeDeviceAssociatedIncidenceDensityTable,
        [FromQuery] bool? includeAgentPerInfectionRateTable,
        [FromQuery] bool? includeInfectiousAgentDetectionRateTable,
        [FromQuery] bool? includeRiskDensityRateTable,
        [FromQuery] bool? includeAntibioticUtilisationTable,
        [FromQuery] bool? includeSurgicalProcedureRateTable,
        [FromQuery] bool? includeResistantPathogenInfectionRateTable,
        [FromQuery] bool? includeOrganismResistanceRateTable,
        [FromQuery] bool? includeAntibioticResistanceTestRateTable,
        [FromQuery] bool? includeSecondaryBsiRateTable,
        [FromQuery] bool fragmentMode,
        [FromServices] IOptions<ReportingOptions> options,
        [FromServices] ReportLanguageRegistry registry,
        [FromServices] ReferenceDataStorage referenceDataStorage,
        [FromServices] ValidationExceptionStorage validationExceptionStorage,
        [FromServices] Dhis2Endpoint dhis2Endpoint,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<ReferenceReport> logger,
        HttpRequest httpRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (sessionId, accept, acceptLang) = ReportRequestBase.ReadHeaders(httpRequest);
        if (accept.IsDefaultOrEmpty || acceptLang.IsDefaultOrEmpty)
            return Results.StatusCode(406);

        // API-boundary YAML safety: every string-typed param flows into a
        // Quarto -P single-line YAML scalar. Reject control characters
        // here rather than escaping them at the argv layer; see
        // InputValidation for the rule.
        var unsafeInput = InputValidation.RejectUnsafeStrings(
            (nameof(referenceDataId), referenceDataId),
            (nameof(locale), locale))
            ?? InputValidation.RejectUnsafeStringArray(nameof(countryFilter), countryFilter)
            ?? InputValidation.RejectUnsafeStringArray(nameof(hospitalFilter), hospitalFilter);
        if (unsafeInput is not null) return unsafeInput;

        var hasStoredDataMode = !string.IsNullOrEmpty(referenceDataId);

        if (hasStoredDataMode)
        {
            var rejected = CollectLiveFetchParams(
                reportingPeriodFrom, reportingPeriodTo,
                birthWeightFrom, birthWeightTo,
                gestationalAgeFrom, gestationalAgeTo,
                countryFilter);
            if (rejected.Count > 0)
                return ProblemDetailsHelper.BadRequest(
                    "Mixed mode is not allowed",
                    "When 'referenceDataId' is set, the dataset is fixed and the live-fetch filter " +
                    $"params must not be specified: {string.Join(", ", rejected)}.");

            if (!FileStorage.IsValidId(referenceDataId!))
                return ProblemDetailsHelper.BadRequest(
                    "Invalid referenceDataId",
                    "The 'referenceDataId' must be 32 hex characters.");
            if (!referenceDataStorage.Exists(referenceDataId!))
                return ProblemDetailsHelper.NotFound(
                    "Reference dataset not found",
                    $"No reference dataset is stored under id '{referenceDataId}'.");

            // Stored-data (view) mode is available to report viewers.
            var forbidden = await NeoIpcAuthorization.RequireAsync(
                authorizationService, httpContext.User, "NeoIpcReport",
                "Viewing reference reports requires the F_NEOIPC_REPORT authority.");
            if (forbidden is not null) return forbidden;
        }
        else
        {
            // Ad-hoc preview renders live against DHIS2 — admin only.
            var forbidden = await NeoIpcAuthorization.RequireAsync(
                authorizationService, httpContext.User, "NeoIpcAdmin",
                "Ad-hoc preview rendering against live DHIS2 requires the F_NEOIPC_ADMIN authority.");
            if (forbidden is not null) return forbidden;
        }

        var apiParameters = new ReferenceReportApiParameters
        {
            SessionId = sessionId,
            AcceptHeaders = accept,
            AcceptLanguageHeaders = acceptLang,
            Locale = locale,
            ReferenceDataId = referenceDataId,
            ReportingPeriodFrom = reportingPeriodFrom,
            ReportingPeriodTo = reportingPeriodTo,
            BirthWeightFrom = birthWeightFrom,
            BirthWeightTo = birthWeightTo,
            GestationalAgeFrom = gestationalAgeFrom,
            GestationalAgeTo = gestationalAgeTo,
            CountryFilter = countryFilter.Length > 0 ? countryFilter : null,
            HospitalFilter = hospitalFilter.Length > 0 ? hospitalFilter : null,
            TestUnitFilter = testUnitFilter,
            DefaultPatientFilter = defaultPatientFilter,
            SparseDataThreshold = sparseDataThreshold,
            ConfidenceIntervals = confidenceIntervals,
            IncludeIntroductionTexts = includeIntroductionTexts,
            IncludeMethodsTexts = includeMethodsTexts,
            IncludeBirthWeightFigure = includeBirthWeightFigure,
            IncludeGestationalAgeFigure = includeGestationalAgeFigure,
            IncludeIncidenceDensityTable = includeIncidenceDensityTable,
            IncludeDeviceAssociatedIncidenceDensityTable = includeDeviceAssociatedIncidenceDensityTable,
            IncludeAgentPerInfectionRateTable = includeAgentPerInfectionRateTable,
            IncludeInfectiousAgentDetectionRateTable = includeInfectiousAgentDetectionRateTable,
            IncludeRiskDensityRateTable = includeRiskDensityRateTable,
            IncludeAntibioticUtilisationTable = includeAntibioticUtilisationTable,
            IncludeSurgicalProcedureRateTable = includeSurgicalProcedureRateTable,
            IncludeResistantPathogenInfectionRateTable = includeResistantPathogenInfectionRateTable,
            IncludeOrganismResistanceRateTable = includeOrganismResistanceRateTable,
            IncludeAntibioticResistanceTestRateTable = includeAntibioticResistanceTestRateTable,
            IncludeSecondaryBsiRateTable = includeSecondaryBsiRateTable,
        };

        var renderParameters = ResolveRenderParameters(
            apiParameters, referenceDataStorage, validationExceptionStorage, dhis2Endpoint);

        var quartoLanguages = registry.ForReport(QuartoReferenceReportProducer.ReportName);

        var (generator, problem) = SelectProducer(
            apiParameters, renderParameters, quartoLanguages,
            options, registry, environment, logger);
        if (problem is not null) return problem;
        if (generator is null) return Results.StatusCode(415);

        await using (generator)
        {
            var dataResult = await generator.Generate(cancellationToken);
            return await HtmlFragmentTransformer.MaybeFragmentize(
                dataResult, generator.MediaType, fragmentMode, cancellationToken);
        }
    }

    static List<string> CollectLiveFetchParams(
        DateOnly? reportingPeriodFrom, DateOnly? reportingPeriodTo,
        ushort? birthWeightFrom, ushort? birthWeightTo,
        ushort? gestationalAgeFrom, ushort? gestationalAgeTo,
        string[] countryFilter)
    {
        var rejected = new List<string>();
        if (reportingPeriodFrom is not null) rejected.Add("reportingPeriodFrom");
        if (reportingPeriodTo is not null) rejected.Add("reportingPeriodTo");
        if (birthWeightFrom is not null) rejected.Add("birthWeightFrom");
        if (birthWeightTo is not null) rejected.Add("birthWeightTo");
        if (gestationalAgeFrom is not null) rejected.Add("gestationalAgeFrom");
        if (gestationalAgeTo is not null) rejected.Add("gestationalAgeTo");
        if (countryFilter.Length > 0) rejected.Add("countryFilter");
        return rejected;
    }

    static ReferenceReportRenderParameters ResolveRenderParameters(
        ReferenceReportApiParameters apiParameters,
        ReferenceDataStorage referenceDataStorage,
        ValidationExceptionStorage validationExceptionStorage,
        Dhis2Endpoint dhis2Endpoint)
    {
        var rp = apiParameters.MapTo();

        // The dhis2* params are server-side overrides (not in the API surface).
        // Always fold the configured endpoint in so neoipcr targets the
        // deployment's DHIS2 instance rather than its hardcoded production
        // default ("https://neoipc.charite.de/api"). Pass ApiPath (host
        // context + "/api") because neoipcr's `path` is the API mount.
        rp = rp with
        {
            Dhis2Scheme = dhis2Endpoint.Scheme,
            Dhis2Hostname = dhis2Endpoint.Host,
            Dhis2Port = dhis2Endpoint.Port,
            Dhis2Path = dhis2Endpoint.ApiPath,
        };

        if (!string.IsNullOrEmpty(apiParameters.ReferenceDataId))
            rp = rp with { ReferenceDataFile = referenceDataStorage.DataPath(apiParameters.ReferenceDataId) };

        // The validation-exception file is a single admin-managed resource,
        // auto-applied to every render when present.
        if (validationExceptionStorage.Exists())
            rp = rp with { ValidationExceptionFile = validationExceptionStorage.DataPath() };

        return rp;
    }

    static (IDataProducer? Generator, IResult? Problem) SelectProducer(
        ReferenceReportApiParameters apiParameters,
        ReferenceReportRenderParameters renderParameters,
        IReadOnlyDictionary<string, string> quartoLanguages,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var quartoSupported = quartoLanguages.Keys.ToHashSet(StringComparer.Ordinal);
        var rScriptSupported = RScriptReferenceReportProducer.SupportedLanguageDictionary.Keys
            .ToHashSet(StringComparer.Ordinal);

        // Format has priority over language; exact-match passes first, then subset.
        foreach (var acceptHeader in apiParameters.AcceptHeaders)
        {
            var mediaType = acceptHeader.MediaType.ToString();

            if (QuartoReportProducer.SupportedMediaTypeHeaderValues.ContainsKey(mediaType))
            {
                var (gen, problem) = TryQuarto(mediaType, apiParameters, renderParameters,
                    quartoSupported, options, registry, environment, logger);
                if (problem is not null) return (null, problem);
                if (gen is not null) return (gen, null);
            }

            if (RScriptReportProducer.SupportedMediaTypeHeaderValues.ContainsKey(mediaType))
            {
                var (gen, problem) = TryRScript(mediaType, apiParameters, renderParameters,
                    rScriptSupported, options, environment, logger);
                if (problem is not null) return (null, problem);
                if (gen is not null) return (gen, null);
            }
        }

        foreach (var acceptHeader in apiParameters.AcceptHeaders)
        foreach (var mediaType in ReturnMediaTypePriorityList)
        {
            if (QuartoReportProducer.SupportedMediaTypeHeaderValues.TryGetValue(mediaType,
                    out var quartoValue) &&
                quartoValue.IsSubsetOf(acceptHeader))
            {
                var (gen, problem) = TryQuarto(mediaType, apiParameters, renderParameters,
                    quartoSupported, options, registry, environment, logger);
                if (problem is not null) return (null, problem);
                if (gen is not null) return (gen, null);
            }

            if (RScriptReportProducer.SupportedMediaTypeHeaderValues.TryGetValue(mediaType,
                    out var rScriptValue) &&
                rScriptValue.IsSubsetOf(acceptHeader))
            {
                var (gen, problem) = TryRScript(mediaType, apiParameters, renderParameters,
                    rScriptSupported, options, environment, logger);
                if (problem is not null) return (null, problem);
                if (gen is not null) return (gen, null);
            }
        }

        return (null, null);
    }

    static (IDataProducer? Generator, IResult? Problem) TryQuarto(
        string mediaType,
        ReferenceReportApiParameters apiParameters,
        ReferenceReportRenderParameters renderParameters,
        IReadOnlyCollection<string> supportedLanguages,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var resolution = LocaleResolver.Resolve(apiParameters.Locale,
            apiParameters.AcceptLanguageHeaders, supportedLanguages);
        return resolution switch
        {
            { Status: LocaleResolver.Status.ExplicitUnsupported } =>
                (null, ProblemDetailsHelper.BadRequest(
                    "Unsupported locale",
                    $"The 'locale' parameter '{apiParameters.Locale}' is not supported by this report.")),
            { Status: LocaleResolver.Status.Resolved, Locale: { } loc } =>
                (new QuartoReferenceReportProducer(mediaType, loc, apiParameters, renderParameters,
                    options, registry, environment, logger), null),
            _ => (null, null),
        };
    }

    static (IDataProducer? Generator, IResult? Problem) TryRScript(
        string mediaType,
        ReferenceReportApiParameters apiParameters,
        ReferenceReportRenderParameters renderParameters,
        IReadOnlyCollection<string> supportedLanguages,
        IOptions<ReportingOptions> options,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var resolution = LocaleResolver.Resolve(apiParameters.Locale,
            apiParameters.AcceptLanguageHeaders, supportedLanguages);
        return resolution switch
        {
            { Status: LocaleResolver.Status.ExplicitUnsupported } =>
                (null, ProblemDetailsHelper.BadRequest(
                    "Unsupported locale",
                    $"The 'locale' parameter '{apiParameters.Locale}' is not supported by this report.")),
            { Status: LocaleResolver.Status.Resolved, Locale: { } loc } =>
                (new RScriptReferenceReportProducer(mediaType, loc, apiParameters, renderParameters,
                    options, environment, logger), null),
            _ => (null, null),
        };
    }

    // Priority list for return media types when doing subset matches.
    static readonly ImmutableArray<string> ReturnMediaTypePriorityList =
        ["text/html", "application/json", "application/pdf"];
}
