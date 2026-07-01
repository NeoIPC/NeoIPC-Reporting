using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Two-component locale: BCP-47 language subtag plus an ISO 3166-1
/// alpha-2 territory subtag. Used to compose <c>LC_ALL</c> for the
/// child R / Quarto process and to pick a Quarto language profile.
/// </summary>
public sealed record ResolvedLocale(string Language, string Territory)
{
    /// <summary><c>LC_ALL</c> form: <c>language_TERRITORY.UTF-8</c>.</summary>
    public string LcAll => $"{Language}_{Territory}.UTF-8";

    /// <summary>Locale code without codeset: <c>language_TERRITORY</c>.</summary>
    public string Code => $"{Language}_{Territory}";
}

/// <summary>
/// Resolves the rendering locale from an optional explicit
/// <c>?locale=</c> query parameter, falling back to <c>Accept-Language</c>
/// content negotiation.
/// </summary>
/// <remarks>
/// Explicit-locale-wins precedence: when the caller passes
/// <c>?locale=</c>, that's a deterministic override (used by the
/// future DHIS2 App Platform app and by integration tests to take
/// content negotiation out of the equation). An *unsupported* explicit
/// locale produces <see cref="Status.ExplicitUnsupported"/> rather
/// than silently falling back to <c>Accept-Language</c> — the caller
/// asked for something specific and shouldn't get something else.
/// </remarks>
public static class LocaleResolver
{
    /// <summary>
    /// Default territory per language for territory-less inputs, so a
    /// request for <c>locale=de</c> composes <c>LC_ALL=de_DE.UTF-8</c>
    /// without the caller having to spell out <c>de_DE</c>. Mirrors how
    /// the report rendering pipeline resolves a bare language to a full
    /// locale.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultTerritories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = "DE",
            ["el"] = "GR",
            ["en"] = "GB",
            ["es"] = "ES",
            ["et"] = "EE",
            ["it"] = "IT",
            ["tr"] = "TR",
        };

    public enum Status
    {
        /// <summary>Locale matched a supported language (explicit or via Accept-Language).</summary>
        Resolved,

        /// <summary>No supported language matched — the caller didn't ask for any locale we can produce.</summary>
        NoMatch,

        /// <summary>Explicit <c>?locale=</c> named a language we don't support.</summary>
        ExplicitUnsupported,
    }

    public readonly record struct Result(ResolvedLocale? Locale, Status Status);

    /// <summary>
    /// Resolves the rendering locale. <paramref name="explicitLocale"/>
    /// wins when supplied and supported; otherwise the first matching
    /// entry in <paramref name="acceptLanguageHeaders"/> is used; on no
    /// match, returns <see cref="Status.NoMatch"/>. An unsupported
    /// explicit locale returns <see cref="Status.ExplicitUnsupported"/>
    /// without falling back.
    /// </summary>
    public static Result Resolve(
        string? explicitLocale,
        IEnumerable<StringWithQualityHeaderValue> acceptLanguageHeaders,
        IReadOnlyCollection<string> supportedLanguages)
    {
        if (!string.IsNullOrWhiteSpace(explicitLocale))
        {
            var parsed = Parse(explicitLocale);
            return supportedLanguages.Contains(parsed.Language)
                ? new Result(parsed, Status.Resolved)
                : new Result(null, Status.ExplicitUnsupported);
        }

        foreach (var header in acceptLanguageHeaders)
        {
            var value = header.Value.ToString();
            if (string.IsNullOrEmpty(value)) continue;
            var parsed = Parse(value);
            if (supportedLanguages.Contains(parsed.Language))
                return new Result(parsed, Status.Resolved);
        }

        return new Result(null, Status.NoMatch);
    }

    /// <summary>
    /// Splits a locale string (<c>de_DE</c>, <c>de-DE</c>, or just
    /// <c>de</c>) into a <see cref="ResolvedLocale"/>, applying
    /// <see cref="DefaultTerritories"/> when the territory is missing.
    /// </summary>
    public static ResolvedLocale Parse(string locale)
    {
        var parts = locale.Split(['_', '-'], 2);
        var language = parts[0].ToLowerInvariant();
        string territory;
        if (parts.Length > 1 && parts[1].Length > 0)
            territory = parts[1].ToUpperInvariant();
        else if (DefaultTerritories.TryGetValue(language, out var defaultTerritory))
            territory = defaultTerritory;
        else
            territory = language.ToUpperInvariant();
        return new ResolvedLocale(language, territory);
    }
}
