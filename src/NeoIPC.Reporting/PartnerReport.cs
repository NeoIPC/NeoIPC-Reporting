using System.Collections.Immutable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

namespace NeoIPC.Reporting;

/// <summary>
/// Endpoint handlers for <c>/partner-report</c>. The HTTP method
/// selects the data-acquisition mode:
/// <list type="bullet">
///   <item><description><b>GET</b> — online mode. The handler does
///   no staging on the Quarto path: <c>Partner-Report.qmd</c>'s
///   <c>_setup.qmd</c> calls <c>import_dhis2(...)</c> +
///   <c>calculate_department_data()</c> +
///   <c>get_benchmark_data(...)</c> directly when its
///   <c>partnerDataFile</c> param is null. For
///   <c>Accept: application/json</c> the handler picks
///   <see cref="RScriptPartnerReportProducer"/>, which spawns
///   <c>Generate-PartnerData.R</c> and streams the JSON back.</description></item>
///   <item><description><b>POST</b> with body — dataFile mode. The
///   request body IS the partner data JSON (the
///   <c>jsonlite::serializeJSON</c> output of
///   <c>Generate-PartnerData.R</c> run elsewhere). Handler streams
///   the body into the per-render workdir and Quarto loads it via
///   <c>partnerDataFile</c>. POST without a body is a 400 — there is
///   no path where "POST falls through to GET" makes sense.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Both modes accept the optional opaque <c>referenceDataFile</c> id
/// (resolved against <see cref="ReferenceDataStorage"/>) so the report
/// can compare the partner unit's metrics to the reference benchmark.
/// Access requires the <c>F_NEOIPC_REPORT</c> authority (or higher);
/// the gate is enforced in-handler, after request-shape validation, so
/// the negative-path checks (missing unitCodes, etc.) stay reachable
/// for the integration tests' placeholder sessions. The single
/// admin-uploaded validation-exception file (if any) is folded in
/// automatically. Each content figure/table is an explicit
/// <c>includeX</c> render flag; the app maps presets onto them
/// client-side. The Quarto profile is derived server-side from locale +
/// output format and is not part of the API surface.
/// </remarks>
class PartnerReport
{
    public static async Task<IResult> Get(
        [FromQuery] string? referenceDataFile,
        [FromQuery] string? locale,
        [FromQuery] string[] unitCodes,
        [FromQuery] DateOnly? reportingPeriodFrom,
        [FromQuery] DateOnly? reportingPeriodTo,
        [FromQuery] ushort? birthWeightFrom,
        [FromQuery] ushort? birthWeightTo,
        [FromQuery] ushort? gestationalAgeFrom,
        [FromQuery] ushort? gestationalAgeTo,
        [FromQuery] bool? includeNonCorePatients,
        [FromQuery] bool? includeTestData,
        [FromQuery] ushort? sparseDataThreshold,
        [FromQuery] ConfidenceIntervalMode? confidenceIntervals,
        [FromQuery] bool? includeIntroductionTexts,
        [FromQuery] bool? includeMethodsTexts,
        [FromQuery] bool? includeOutlierInterpretation,
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
        // Nullable so it stays optional: a non-nullable bool is a *required* minimal-API query
        // parameter (an absent value fails model binding with a 400). The app always sends
        // fragmentMode explicitly, so this default only affects consumers that omit it (curl /
        // ad-hoc): default false gives them a self-contained, browser-viewable standalone HTML
        // document; pass fragmentMode=true for the app-style body-only fragment.
        [FromQuery] bool? fragmentMode,
        [FromServices] IOptions<ReportingOptions> options,
        [FromServices] ReportLanguageRegistry registry,
        [FromServices] ReferenceDataStorage referenceDataStorage,
        [FromServices] ValidationExceptionStorage validationExceptionStorage,
        [FromServices] Dhis2Endpoint dhis2Endpoint,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<PartnerReport> logger,
        HttpRequest httpRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return await Handle(
            apiParameters: BuildApiParameters(
                referenceDataFile, locale,
                unitCodes, reportingPeriodFrom, reportingPeriodTo,
                birthWeightFrom, birthWeightTo, gestationalAgeFrom, gestationalAgeTo,
                includeNonCorePatients, includeTestData, sparseDataThreshold,
                confidenceIntervals, includeIntroductionTexts, includeMethodsTexts,
                includeOutlierInterpretation,
                includeBirthWeightFigure, includeGestationalAgeFigure,
                includeIncidenceDensityTable, includeDeviceAssociatedIncidenceDensityTable,
                includeAgentPerInfectionRateTable, includeInfectiousAgentDetectionRateTable,
                includeRiskDensityRateTable, includeAntibioticUtilisationTable,
                includeSurgicalProcedureRateTable, includeResistantPathogenInfectionRateTable,
                includeOrganismResistanceRateTable, includeAntibioticResistanceTestRateTable,
                includeSecondaryBsiRateTable,
                httpRequest),
            partnerDataBody: null,
            fragmentMode ?? false,
            options, registry, referenceDataStorage, validationExceptionStorage, dhis2Endpoint,
            authorizationService, environment, logger, httpContext, cancellationToken);
    }

    public static async Task<IResult> Post(
        [FromQuery] string? referenceDataFile,
        [FromQuery] string? locale,
        [FromQuery] string[] unitCodes,
        [FromQuery] ushort? sparseDataThreshold,
        [FromQuery] ConfidenceIntervalMode? confidenceIntervals,
        [FromQuery] bool? includeIntroductionTexts,
        [FromQuery] bool? includeMethodsTexts,
        [FromQuery] bool? includeOutlierInterpretation,
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
        // Nullable so it stays optional: a non-nullable bool is a *required* minimal-API query
        // parameter (an absent value fails model binding with a 400). The app always sends
        // fragmentMode explicitly, so this default only affects consumers that omit it (curl /
        // ad-hoc): default false gives them a self-contained, browser-viewable standalone HTML
        // document; pass fragmentMode=true for the app-style body-only fragment.
        [FromQuery] bool? fragmentMode,
        [FromServices] IOptions<ReportingOptions> options,
        [FromServices] ReportLanguageRegistry registry,
        [FromServices] ReferenceDataStorage referenceDataStorage,
        [FromServices] ValidationExceptionStorage validationExceptionStorage,
        [FromServices] Dhis2Endpoint dhis2Endpoint,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<PartnerReport> logger,
        HttpRequest httpRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpRequest.ContentLength is null or 0)
            return ProblemDetailsHelper.BadRequest(
                "Missing partnerData body",
                "POST /partner-report requires the partner-data JSON in the request body. " +
                "Use GET for online mode (no body).");

        return await Handle(
            apiParameters: BuildApiParameters(
                referenceDataFile, locale,
                unitCodes, reportingPeriodFrom: null, reportingPeriodTo: null,
                birthWeightFrom: null, birthWeightTo: null,
                gestationalAgeFrom: null, gestationalAgeTo: null,
                includeNonCorePatients: null, includeTestData: null,
                sparseDataThreshold, confidenceIntervals,
                includeIntroductionTexts, includeMethodsTexts, includeOutlierInterpretation,
                includeBirthWeightFigure, includeGestationalAgeFigure,
                includeIncidenceDensityTable, includeDeviceAssociatedIncidenceDensityTable,
                includeAgentPerInfectionRateTable, includeInfectiousAgentDetectionRateTable,
                includeRiskDensityRateTable, includeAntibioticUtilisationTable,
                includeSurgicalProcedureRateTable, includeResistantPathogenInfectionRateTable,
                includeOrganismResistanceRateTable, includeAntibioticResistanceTestRateTable,
                includeSecondaryBsiRateTable,
                httpRequest),
            partnerDataBody: httpRequest.Body,
            fragmentMode ?? false,
            options, registry, referenceDataStorage, validationExceptionStorage, dhis2Endpoint,
            authorizationService, environment, logger, httpContext, cancellationToken);
    }

    static PartnerReportApiParameters BuildApiParameters(
        string? referenceDataFile, string? locale, string[] unitCodes,
        DateOnly? reportingPeriodFrom, DateOnly? reportingPeriodTo,
        ushort? birthWeightFrom, ushort? birthWeightTo,
        ushort? gestationalAgeFrom, ushort? gestationalAgeTo,
        bool? includeNonCorePatients, bool? includeTestData,
        ushort? sparseDataThreshold, ConfidenceIntervalMode? confidenceIntervals,
        bool? includeIntroductionTexts, bool? includeMethodsTexts,
        bool? includeOutlierInterpretation,
        bool? includeBirthWeightFigure, bool? includeGestationalAgeFigure,
        bool? includeIncidenceDensityTable, bool? includeDeviceAssociatedIncidenceDensityTable,
        bool? includeAgentPerInfectionRateTable, bool? includeInfectiousAgentDetectionRateTable,
        bool? includeRiskDensityRateTable, bool? includeAntibioticUtilisationTable,
        bool? includeSurgicalProcedureRateTable, bool? includeResistantPathogenInfectionRateTable,
        bool? includeOrganismResistanceRateTable, bool? includeAntibioticResistanceTestRateTable,
        bool? includeSecondaryBsiRateTable,
        HttpRequest httpRequest)
    {
        var (sessionId, accept, acceptLang) = ReportRequestBase.ReadHeaders(httpRequest);
        return new PartnerReportApiParameters
        {
            SessionId = sessionId,
            AcceptHeaders = accept,
            AcceptLanguageHeaders = acceptLang,
            Locale = locale,
            ReferenceDataFile = referenceDataFile,
            UnitCodes = unitCodes.Length > 0 ? unitCodes : null,
            ReportingPeriodFrom = reportingPeriodFrom,
            ReportingPeriodTo = reportingPeriodTo,
            BirthWeightFrom = birthWeightFrom,
            BirthWeightTo = birthWeightTo,
            GestationalAgeFrom = gestationalAgeFrom,
            GestationalAgeTo = gestationalAgeTo,
            IncludeNonCorePatients = includeNonCorePatients,
            IncludeTestData = includeTestData,
            SparseDataThreshold = sparseDataThreshold,
            ConfidenceIntervals = confidenceIntervals,
            IncludeIntroductionTexts = includeIntroductionTexts,
            IncludeMethodsTexts = includeMethodsTexts,
            IncludeOutlierInterpretation = includeOutlierInterpretation,
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
    }

    static async Task<IResult> Handle(
        PartnerReportApiParameters apiParameters,
        Stream? partnerDataBody,
        bool fragmentMode,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        ReferenceDataStorage referenceDataStorage,
        ValidationExceptionStorage validationExceptionStorage,
        Dhis2Endpoint dhis2Endpoint,
        IAuthorizationService authorizationService,
        IWebHostEnvironment environment,
        ILogger logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (apiParameters.AcceptHeaders.IsDefaultOrEmpty
            || apiParameters.AcceptLanguageHeaders.IsDefaultOrEmpty)
            return Results.StatusCode(406);

        // API-boundary YAML safety; see ReferenceReport for the rationale.
        var unsafeInput = InputValidation.RejectUnsafeStrings(
            (nameof(apiParameters.ReferenceDataFile), apiParameters.ReferenceDataFile),
            (nameof(apiParameters.Locale), apiParameters.Locale))
            ?? InputValidation.RejectUnsafeStringArray(nameof(apiParameters.UnitCodes), apiParameters.UnitCodes);
        if (unsafeInput is not null) return unsafeInput;

        if (apiParameters.UnitCodes is null or { Length: 0 })
            return ProblemDetailsHelper.BadRequest(
                "Missing unitCodes",
                "The 'unitCodes' query parameter is required.");

        // Authorization runs after request-shape validation so the shape
        // checks above stay reachable without a valid DHIS2 session.
        var forbidden = await NeoIpcAuthorization.RequireAsync(
            authorizationService, httpContext.User, "NeoIpcReport",
            "Generating partner reports requires the F_NEOIPC_REPORT authority.");
        if (forbidden is not null) return forbidden;

        if (!string.IsNullOrEmpty(apiParameters.ReferenceDataFile))
        {
            if (!FileStorage.IsValidId(apiParameters.ReferenceDataFile))
                return ProblemDetailsHelper.BadRequest(
                    "Invalid referenceDataFile",
                    "The 'referenceDataFile' must be 32 hex characters.");
            if (!referenceDataStorage.Exists(apiParameters.ReferenceDataFile))
                return ProblemDetailsHelper.NotFound(
                    "Reference dataset not found",
                    $"No reference dataset is stored under id '{apiParameters.ReferenceDataFile}'.");
        }

        var renderParameters = ResolveRenderParameters(
            apiParameters, referenceDataStorage, validationExceptionStorage, dhis2Endpoint);

        var quartoLanguages = registry.ForReport(QuartoPartnerReportProducer.ReportName);

        var (generator, problem) = SelectProducer(
            apiParameters, renderParameters, partnerDataBody is not null,
            quartoLanguages, options, registry, environment, logger);
        if (problem is not null) return problem;
        if (generator is null) return Results.StatusCode(415);

        await using (generator)
        {
            // DataFile mode (POST): the request body IS the partner-data JSON.
            // Stream it into the per-render workdir so Quarto's _setup.qmd
            // picks it up via partnerDataFile=<path>. Cleaned up alongside
            // the workdir on dispose.
            //
            // Online mode (GET) needs no staging here — the QMD's
            // _setup.qmd does the live import_dhis2 + benchmark inline
            // when its partnerDataFile param is null.
            //
            // RScriptPartnerReportProducer (Accept: application/json) runs
            // Generate-PartnerData.R directly and streams stdout; not a
            // Quarto generator, so this branch is skipped for it.
            if (generator is QuartoPartnerReportProducer qpg && partnerDataBody is not null)
            {
                var stagedPath = qpg.PartnerDataStagingPath;
                await using var fs = File.Create(stagedPath);
                await partnerDataBody.CopyToAsync(fs, cancellationToken);
                qpg.SetPartnerDataPath(stagedPath);
            }

            var dataResult = await generator.Generate(cancellationToken);
            return await HtmlFragmentTransformer.MaybeFragmentize(
                dataResult, generator.MediaType, fragmentMode, cancellationToken);
        }
    }

    static PartnerReportRenderParameters ResolveRenderParameters(
        PartnerReportApiParameters apiParameters,
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

        if (!string.IsNullOrEmpty(apiParameters.ReferenceDataFile))
            rp = rp with { ReferenceDataFile = referenceDataStorage.DataPath(apiParameters.ReferenceDataFile) };

        // The validation-exception file is a single admin-managed resource,
        // auto-applied to every render when present.
        if (validationExceptionStorage.Exists())
            rp = rp with { ValidationExceptionFile = validationExceptionStorage.DataPath() };

        return rp;
    }

    static (IDataProducer? Generator, IResult? Problem) SelectProducer(
        PartnerReportApiParameters apiParameters,
        PartnerReportRenderParameters renderParameters,
        bool isDataFileMode,
        IReadOnlyDictionary<string, string> quartoLanguages,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var quartoSupported = quartoLanguages.Keys.ToHashSet(StringComparer.Ordinal);
        var rScriptSupported = RScriptPartnerReportProducer.SupportedLanguageDictionary.Keys
            .ToHashSet(StringComparer.Ordinal);

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

            if (!isDataFileMode &&
                RScriptReportProducer.SupportedMediaTypeHeaderValues.ContainsKey(mediaType))
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

            if (!isDataFileMode &&
                RScriptReportProducer.SupportedMediaTypeHeaderValues.TryGetValue(mediaType,
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
        PartnerReportApiParameters apiParameters,
        PartnerReportRenderParameters renderParameters,
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
                (new QuartoPartnerReportProducer(mediaType, loc, apiParameters, renderParameters,
                    options, registry, environment, logger), null),
            _ => (null, null),
        };
    }

    static (IDataProducer? Generator, IResult? Problem) TryRScript(
        string mediaType,
        PartnerReportApiParameters apiParameters,
        PartnerReportRenderParameters renderParameters,
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
                (new RScriptPartnerReportProducer(mediaType, loc, apiParameters, renderParameters,
                    options, environment, logger), null),
            _ => (null, null),
        };
    }

    static readonly ImmutableArray<string> ReturnMediaTypePriorityList =
        ["text/html", "application/json", "application/pdf"];
}
