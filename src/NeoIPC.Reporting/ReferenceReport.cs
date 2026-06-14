using System.Collections.Immutable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting.Resources;

namespace NeoIPC.Reporting;

/// <summary>
/// Endpoint handler for <c>GET /reference-report</c>. The endpoint has
/// two modes distinguished entirely by whether <c>?referenceDataId=</c>
/// is present:
/// <list type="bullet">
///   <item><description><b>Stored-data mode</b> (id present) — render
///   an admin-uploaded dataset. Available to any authenticated user.
///   Live-fetch filter params (<c>reportingPeriodFrom</c>,
///   <c>birthWeightFrom</c>, …) are rejected as a 400 if mixed in;
///   the dataset is fixed and re-applying its filters at render time
///   would be either redundant or contradictory.</description></item>
///   <item><description><b>Ad-hoc preview mode</b> (id absent) —
///   live-fetch render against DHIS2 with the supplied filter params.
///   Admin-only (DHIS2 <c>ALL</c> authority); the handler enforces the
///   gate via <see cref="IAuthorizationService"/> rather than via a
///   route-level policy because the gating is conditional on the
///   query.</description></item>
/// </list>
/// </summary>
class ReferenceReport
{
    public static async Task<IResult> Get(
        [FromQuery] string? referenceDataId,
        [FromQuery] string? locale,
        [FromQuery] string? profile,
        [FromQuery] string? validationExceptionFile,
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
        [FromQuery] ReferenceReportElement[] enabledElements,
        [FromQuery] ReferenceReportElement[] disabledElements,
        [FromQuery] ReferenceReportSectionText[] enabledSectionTexts,
        [FromQuery] ReferenceReportSectionText[] disabledSectionTexts,
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
            (nameof(locale), locale),
            (nameof(profile), profile),
            (nameof(validationExceptionFile), validationExceptionFile))
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
        }
        else
        {
            var auth = await authorizationService.AuthorizeAsync(httpContext.User, "RequiresAll");
            if (!auth.Succeeded)
                return ProblemDetailsHelper.Forbidden(
                    "Forbidden",
                    "Ad-hoc preview rendering against live DHIS2 requires the ALL authority.");
        }

        if (!string.IsNullOrEmpty(validationExceptionFile))
        {
            if (!FileStorage.IsValidId(validationExceptionFile))
                return ProblemDetailsHelper.BadRequest(
                    "Invalid validationExceptionFile",
                    "The 'validationExceptionFile' must be 32 hex characters.");
            if (!validationExceptionStorage.Exists(validationExceptionFile))
                return ProblemDetailsHelper.NotFound(
                    "Validation exception file not found",
                    $"No validation exception file is stored under id '{validationExceptionFile}'.");
        }

        var apiParameters = new ReferenceReportApiParameters
        {
            SessionId = sessionId,
            AcceptHeaders = accept,
            AcceptLanguageHeaders = acceptLang,
            Locale = locale,
            ReferenceDataId = referenceDataId,
            Profile = profile,
            ValidationExceptionFile = validationExceptionFile,
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
            EnabledElements = enabledElements.Length > 0 ? enabledElements : null,
            DisabledElements = disabledElements.Length > 0 ? disabledElements : null,
            EnabledSectionTexts = enabledSectionTexts.Length > 0 ? enabledSectionTexts : null,
            DisabledSectionTexts = disabledSectionTexts.Length > 0 ? disabledSectionTexts : null,
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

        if (!string.IsNullOrEmpty(apiParameters.ValidationExceptionFile))
            rp = rp with { ValidationExceptionFile = validationExceptionStorage.DataPath(apiParameters.ValidationExceptionFile) };

        foreach (var element in apiParameters.EnabledElements ?? [])
            rp = ReferenceReportProjection.Apply(rp, element, true);
        foreach (var element in apiParameters.DisabledElements ?? [])
            rp = ReferenceReportProjection.Apply(rp, element, false);
        foreach (var section in apiParameters.EnabledSectionTexts ?? [])
            rp = ReferenceReportProjection.Apply(rp, section, true);
        foreach (var section in apiParameters.DisabledSectionTexts ?? [])
            rp = ReferenceReportProjection.Apply(rp, section, false);

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
