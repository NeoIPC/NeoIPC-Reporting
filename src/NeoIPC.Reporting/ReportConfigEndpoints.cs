using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Minimal-API handlers for the report-configuration endpoints the app
/// reads to drive its forms: the content <b>presets</b>, the supported
/// <b>locales</b> and the Validation Report's <b>rule catalogue</b>. All
/// derive from the report layer (the Surveillance-Toolkit tree mounted at
/// <see cref="ReportingOptions.ReportsSourceDir"/>) rather than from the
/// .NET API surface, so a change to them, such as a rule added to the
/// Validation Report, needs no change to this service or to the app; a
/// released image picks it up with the reports release it pins.
/// </summary>
public static class ReportConfigEndpoints
{
    /// <summary>
    /// Returns the named content presets for <paramref name="reportName"/>,
    /// read at request time from <c>{ReportsSourceDir}/{reportName}/presets.json</c>.
    /// The response body is the file's <c>presets</c> object verbatim — a
    /// map of preset name → the render-param overrides it sets (each
    /// preset lists only the params that differ from the QMD defaults).
    /// </summary>
    /// <remarks>
    /// The file is the single source of truth for the preset feature; the
    /// app applies the chosen preset client-side as <c>includeX</c> /
    /// confidence-interval / section-text render params. It is NOT a
    /// Quarto profile (profiles cannot set document params), so it is read
    /// as plain JSON here with no Quarto involvement.
    /// </remarks>
    public static IResult Presets(string reportName, IOptions<ReportingOptions> options)
    {
        // reportName is a fixed compile-time constant (the producer's
        // ReportName), never user input — no path-traversal surface.
        var path = Path.Combine(options.Value.ReportsSourceDir, reportName, "presets.json");
        if (!File.Exists(path))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "No presets",
                detail: $"No presets.json is present for report '{reportName}'.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("presets", out var presets))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Malformed presets",
                detail: $"presets.json for report '{reportName}' has no 'presets' object.");

        return Results.Text(presets.GetRawText(), "application/json");
    }

    /// <summary>
    /// Returns the locale tags <paramref name="reportName"/> offers in its
    /// language picker: the <b>distinct language subtags</b> it serves. Because
    /// <see cref="LocaleResolver"/> serves exactly one territory per language,
    /// each is offered as its bare subtag (<c>en</c>) — there is nothing to
    /// disambiguate — and the registry's redundant territory key (the master
    /// English QMD registers both <c>en</c> and <c>en-GB</c>, while the QMD
    /// lookup keys off the bare language) collapses into it. A language served
    /// in more than one territory would instead need territory-qualified tags
    /// (<c>en-GB</c>, <c>en-US</c>); that case is not handled here. Bare-tag
    /// requests resolve regardless. The app maps each tag to a human-readable
    /// language name client-side.
    /// </summary>
    public static IResult Locales(string reportName, ReportLanguageRegistry registry) =>
        Results.Ok(registry.ForReport(reportName).Keys
            .Select(LanguageSubtag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray());

    /// <summary>
    /// Returns the Validation Report's rule catalogue as <c>{ rules: [{ id, summary }] }</c>,
    /// ascending by id, each summary in the language <paramref name="locale"/>
    /// names where the report carries that translation and in English
    /// otherwise; see <see cref="ValidationRuleCatalogue"/>. Without
    /// <paramref name="locale"/> the summaries are English. A locale the report
    /// does not serve is a 400, as on the render endpoint, so the app can ask
    /// again in English.
    /// </summary>
    public static IResult ValidationRules(
        string? locale, ValidationRuleCatalogue catalogue, ReportLanguageRegistry registry,
        ILoggerFactory loggerFactory)
    {
        var unsafeInput = InputValidation.RejectUnsafeStrings((nameof(locale), locale));
        if (unsafeInput is not null) return unsafeInput;

        var language = "en";
        if (!string.IsNullOrWhiteSpace(locale))
        {
            var served = registry.ForReport(QuartoValidationReportProducer.ReportName).Keys
                .ToHashSet(StringComparer.Ordinal);
            var resolution = LocaleResolver.Resolve(locale, [], served);
            if (resolution is not { Status: LocaleResolver.Status.Resolved, Locale: { } resolved })
                return ProblemDetailsHelper.BadRequest(
                    ProblemCodes.UnsupportedLocale,
                    "Unsupported locale",
                    $"The 'locale' parameter '{locale}' is not supported by this report.");
            language = resolved.Language;
        }

        if (!TryReadRuleCatalogue(() => catalogue.Rules(language),
                loggerFactory.CreateLogger(typeof(ReportConfigEndpoints)), out var rules, out var problem))
            return problem;

        return Results.Ok(new
        {
            rules = rules.Select(r => new { id = r.Id, summary = r.Summary }),
        });
    }

    /// <summary>
    /// Reads from the Validation Report's rule catalogue through
    /// <paramref name="read"/>. When the report's string resources cannot be
    /// read (missing, unreadable, or malformed: an <see cref="IOException"/>,
    /// <see cref="UnauthorizedAccessException"/> or
    /// <see cref="InvalidOperationException"/>), the exception goes to
    /// <paramref name="logger"/> and <paramref name="problem"/> is a 500 whose
    /// detail names no path: the exception names one in the server's file
    /// system, which stays in the log.
    /// </summary>
    internal static bool TryReadRuleCatalogue<T>(
        Func<T> read, ILogger logger,
        [MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out IResult? problem)
    {
        try
        {
            value = read();
            problem = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(e, "The Validation Report's rule catalogue could not be read.");
            value = default;
            problem = Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Rule catalogue unavailable",
                detail: "The Validation Report's rule catalogue could not be read from the report sources.");
            return false;
        }
    }

    /// <summary>The lower-cased BCP-47 language subtag of a locale tag (<c>en-GB</c> → <c>en</c>).</summary>
    static string LanguageSubtag(string tag) => tag.Split('-', '_')[0].ToLowerInvariant();
}
