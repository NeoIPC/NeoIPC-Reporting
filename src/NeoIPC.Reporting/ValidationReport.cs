using System.Collections.Immutable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

namespace NeoIPC.Reporting;

/// <summary>
/// Endpoint handler for <c>GET /validation-report</c>: renders the Validation
/// Report, which lists every record a NeoIPC validation rule flags, for the
/// departments the caller names (all the DHIS2 session can see when none is
/// named). It validates live DHIS2 data and renders to HTML or PDF; there is
/// no stored-dataset mode and no JSON data output.
/// </summary>
/// <remarks>
/// Request-shape checks run before the authorization check, as in the other
/// render handlers, so the negative paths stay reachable for the integration
/// tests' placeholder sessions. Every id in <c>rules</c> must be in the rule
/// catalogue <c>GET /validation-report/rules</c> lists; an absent or empty
/// <c>rules</c> applies every rule. The single admin-uploaded
/// validation-exception file, if any, is folded in automatically.
/// </remarks>
class ValidationReport
{
    public static async Task<IResult> Get(
        [FromQuery] string? locale,
        [FromQuery] string[] departmentFilter,
        [FromQuery] int[] rules,
        [FromQuery] bool? includeTestData,
        // Nullable so it stays optional; see ReferenceReport.Get.
        [FromQuery] bool? fragmentMode,
        [FromServices] IOptions<ReportingOptions> options,
        [FromServices] ReportLanguageRegistry registry,
        [FromServices] ValidationRuleCatalogue catalogue,
        [FromServices] ValidationExceptionStorage validationExceptionStorage,
        [FromServices] Dhis2Endpoint dhis2Endpoint,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILoggerFactory loggerFactory,
        HttpRequest httpRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (sessionId, accept, acceptLang) = ReportRequestBase.ReadHeaders(httpRequest);
        if (accept.IsDefaultOrEmpty)
            return Results.StatusCode(406);

        // The report has no data output, so a rendering locale is always needed.
        if (acceptLang.IsDefaultOrEmpty
            && string.IsNullOrWhiteSpace(locale)
            && OutputNegotiation.OnlyRenderedOutputsAreAcceptable(accept, dataOutputAvailable: false))
            return Results.StatusCode(406);

        if (!OutputNegotiation.AnyRenderedOutputIsAcceptable(accept))
            return ProblemDetailsHelper.NotAcceptable(ProblemCodes.NoAcceptableOutput, RenderedOutputs);

        // API-boundary YAML safety: every string-typed param flows into a
        // Quarto -P single-line YAML scalar; see InputValidation.
        var unsafeInput = InputValidation.RejectUnsafeStrings((nameof(locale), locale))
            ?? InputValidation.RejectUnsafeStringArray(nameof(departmentFilter), departmentFilter);
        if (unsafeInput is not null) return unsafeInput;

        if (rules.Length > 0)
        {
            if (!ReportConfigEndpoints.TryReadRuleCatalogue(() => catalogue.Ids,
                    loggerFactory.CreateLogger<ValidationReport>(), out var ids, out var catalogueProblem))
                return catalogueProblem;
            var unknown = rules.Except(ids).Order().ToArray();
            if (unknown.Length > 0)
                return ProblemDetailsHelper.BadRequest(
                    ProblemCodes.UnknownValidationRule,
                    "Unknown validation rule",
                    $"The Validation Report has no rule {string.Join(", ", unknown)}; "
                    + "GET /validation-report/rules lists the rules it applies.");
        }

        var forbidden = await NeoIpcAuthorization.RequireAsync(
            authorizationService, httpContext.User, "NeoIpcReport",
            "Rendering the Validation Report requires the F_NEOIPC_REPORT authority.");
        if (forbidden is not null) return forbidden;

        var apiParameters = new ValidationReportApiParameters
        {
            SessionId = sessionId,
            AcceptHeaders = accept,
            AcceptLanguageHeaders = acceptLang,
            Locale = locale,
            DepartmentFilter = departmentFilter.Length > 0 ? departmentFilter : null,
            Rules = rules.Length > 0 ? [.. rules.Distinct().Order()] : null,
            IncludeTestData = includeTestData,
        };

        var renderParameters = ResolveRenderParameters(
            apiParameters, validationExceptionStorage, dhis2Endpoint);

        var quartoLanguages = registry.ForReport(QuartoValidationReportProducer.ReportName);

        var (generator, problem) = SelectProducer(
            apiParameters, renderParameters, quartoLanguages,
            options, registry, environment, loggerFactory);
        if (problem is not null) return problem;
        // A rendered output was acceptable (checked above) but no supported
        // language: proactive-negotiation failure (RFC 9110 §15.5.7).
        if (generator is null)
            return ProblemDetailsHelper.NotAcceptable(ProblemCodes.NoAcceptableOutput, RenderedOutputs);

        await using (generator)
        {
            var dataResult = await generator.Generate(cancellationToken);
            return await HtmlFragmentTransformer.MaybeFragmentize(
                dataResult, generator.MediaType, fragmentMode ?? false, cancellationToken);
        }
    }

    const string RenderedOutputs =
        "No output matching this request's Accept and Accept-Language can be produced. "
        + "The Validation Report renders to text/html or application/pdf only.";

    internal static ValidationReportRenderParameters ResolveRenderParameters(
        ValidationReportApiParameters apiParameters,
        ValidationExceptionStorage validationExceptionStorage,
        Dhis2Endpoint dhis2Endpoint)
    {
        // The dhis2* params are server-side overrides, not part of the API
        // surface: neoipcr targets the deployment's DHIS2 instance. ApiPath is
        // the host context plus "/api", neoipcr's API mount. The report's
        // Tracker Capture links go to the address the users reach DHIS2 at,
        // which inside a cluster is not the one the service reads from.
        var rp = apiParameters.MapTo() with
        {
            Dhis2Scheme = dhis2Endpoint.Scheme,
            Dhis2Hostname = dhis2Endpoint.Host,
            Dhis2Port = dhis2Endpoint.Port,
            Dhis2Path = dhis2Endpoint.ApiPath,
            Dhis2PublicBaseUrl = dhis2Endpoint.PublicBaseUri.AbsoluteUri,
        };

        // The validation-exception file is a single admin-managed resource,
        // auto-applied to every render when present.
        if (validationExceptionStorage.Exists())
            rp = rp with { ValidationExceptionFile = validationExceptionStorage.DataPath() };

        return rp;
    }

    static (IDataProducer? Generator, IResult? Problem) SelectProducer(
        ValidationReportApiParameters apiParameters,
        ValidationReportRenderParameters renderParameters,
        IReadOnlyDictionary<string, string> quartoLanguages,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
    {
        var supported = quartoLanguages.Keys.ToHashSet(StringComparer.Ordinal);

        // Format has priority over language; exact-match passes first, then subset.
        foreach (var acceptHeader in apiParameters.AcceptHeaders)
        {
            var mediaType = acceptHeader.MediaType.ToString();
            if (!QuartoReportProducer.SupportedMediaTypeHeaderValues.ContainsKey(mediaType)) continue;
            var (gen, problem) = TryQuarto(mediaType, apiParameters, renderParameters,
                supported, options, registry, environment, loggerFactory);
            if (problem is not null) return (null, problem);
            if (gen is not null) return (gen, null);
        }

        foreach (var acceptHeader in apiParameters.AcceptHeaders)
        foreach (var mediaType in ReturnMediaTypePriorityList)
        {
            if (!QuartoReportProducer.SupportedMediaTypeHeaderValues.TryGetValue(mediaType, out var value)
                || !value.IsSubsetOf(acceptHeader)) continue;
            var (gen, problem) = TryQuarto(mediaType, apiParameters, renderParameters,
                supported, options, registry, environment, loggerFactory);
            if (problem is not null) return (null, problem);
            if (gen is not null) return (gen, null);
        }

        return (null, null);
    }

    static (IDataProducer? Generator, IResult? Problem) TryQuarto(
        string mediaType,
        ValidationReportApiParameters apiParameters,
        ValidationReportRenderParameters renderParameters,
        IReadOnlyCollection<string> supportedLanguages,
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory)
    {
        var resolution = LocaleResolver.Resolve(apiParameters.Locale,
            apiParameters.AcceptLanguageHeaders, supportedLanguages);
        return resolution switch
        {
            { Status: LocaleResolver.Status.ExplicitUnsupported } =>
                (null, ProblemDetailsHelper.BadRequest(
                    ProblemCodes.UnsupportedLocale,
                    "Unsupported locale",
                    $"The 'locale' parameter '{apiParameters.Locale}' is not supported by this report.")),
            { Status: LocaleResolver.Status.Resolved, Locale: { } loc } =>
                (new QuartoValidationReportProducer(mediaType, loc, apiParameters, renderParameters,
                    options, registry, environment, loggerFactory), null),
            _ => (null, null),
        };
    }

    // Priority list for return media types when doing subset matches.
    static readonly ImmutableArray<string> ReturnMediaTypePriorityList = ["text/html", "application/pdf"];
}
