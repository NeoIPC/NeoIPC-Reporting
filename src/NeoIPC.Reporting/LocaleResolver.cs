using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Two-component locale: BCP-47 language subtag plus an ISO 3166-1
/// alpha-2 territory subtag. Used to compose <c>LC_ALL</c> for the
/// child R / Quarto process and to pick a Quarto language profile.
/// </summary>
public sealed record ResolvedLocale(string Language, string Territory)
{
    /// <summary>
    /// <c>LC_ALL</c>/<c>LANG</c> form: <c>language_TERRITORY.UTF-8</c>. The
    /// <see cref="Territory"/> is always one the container actually generates a
    /// locale for — <see cref="LocaleResolver"/> never resolves to an
    /// ungenerated territory. An ungenerated <c>LC_ALL</c> makes lualatex/tlmgr
    /// hard-exit with "Unable to read locale data ... Exiting now" (lualatex
    /// then returns non-zero even though it wrote a valid PDF), which Quarto in
    /// turn misreports as "missing packages (automatic installation failed)".
    /// </summary>
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
/// <para>
/// Explicit-locale-wins precedence: when the caller passes <c>?locale=</c>,
/// that's a deterministic override (used by the DHIS2 App Platform app and by
/// integration tests to take content negotiation out of the equation). An
/// <em>unserved</em> explicit locale produces
/// <see cref="Status.ExplicitUnsupported"/> rather than silently falling back
/// to <c>Accept-Language</c> — the caller asked for something specific and
/// shouldn't get something else.
/// </para>
/// <para>
/// Matching is by <b>exact tag</b>, not by stripping the territory. A
/// territory-bearing tag (<c>en-US</c>) matches only a locale we actually
/// serve; a bare-language tag (<c>en</c>) resolves to that language's served
/// locale. We deliberately do <b>not</b> invent a territory fallback: a request
/// that offers only <c>en-US</c> (a territory we don't serve, with no generic
/// <c>en</c>) is honestly refused, whereas a typical browser's
/// <c>en-US,en;q=0.9</c> succeeds via the <c>en</c> it already offered. That is
/// also what keeps <c>LC_ALL</c> to a container-generated locale — the previous
/// language-stripping behaviour composed <c>en_US.UTF-8</c> from an
/// <c>en-US</c> request and crashed the LaTeX toolchain.
/// </para>
/// </remarks>
public static class LocaleResolver
{
    /// <summary>
    /// The single territory whose locale the container generates for each
    /// supported language — the one locale we serve per language today
    /// (<c>en</c> → <c>en_GB</c>, <c>de</c> → <c>de_DE</c>, …) and the default a
    /// bare-language tag resolves to.
    /// </summary>
    // Keep in sync with the `locale-gen` list in src/NeoIPC.Reporting/Dockerfile:
    // an entry here without a matching generated locale would let LcAll name a
    // locale the OS cannot load, crashing lualatex/tlmgr. Adding a SECOND
    // territory for a language (e.g. en_US alongside en_GB, or fr_CH alongside
    // fr_FR for Swiss vs French number/date formatting) is a larger, cross-
    // cutting change — it also needs po4a to emit that locale's content and
    // Weblate to carry it — and is tracked as its own task, not done here.
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
        /// <summary>A requested tag matched a locale we serve (explicit or via Accept-Language).</summary>
        Resolved,

        /// <summary>None of the requested tags matched a locale we serve.</summary>
        NoMatch,

        /// <summary>Explicit <c>?locale=</c> named a locale we don't serve.</summary>
        ExplicitUnsupported,
    }

    public readonly record struct Result(ResolvedLocale? Locale, Status Status);

    /// <summary>
    /// Resolves the rendering locale. <paramref name="explicitLocale"/> wins
    /// when supplied and served; otherwise the first
    /// <paramref name="acceptLanguageHeaders"/> entry (already q-sorted by the
    /// caller) that we serve is used. On no match, returns
    /// <see cref="Status.NoMatch"/>; an unserved explicit locale returns
    /// <see cref="Status.ExplicitUnsupported"/> without falling back.
    /// <paramref name="supportedLanguages"/> is the set of languages the report
    /// has content for.
    /// </summary>
    public static Result Resolve(
        string? explicitLocale,
        IEnumerable<StringWithQualityHeaderValue> acceptLanguageHeaders,
        IReadOnlyCollection<string> supportedLanguages)
    {
        if (!string.IsNullOrWhiteSpace(explicitLocale))
        {
            var resolved = Negotiate(explicitLocale, supportedLanguages);
            return resolved is not null
                ? new Result(resolved, Status.Resolved)
                : new Result(null, Status.ExplicitUnsupported);
        }

        foreach (var header in acceptLanguageHeaders)
        {
            var value = header.Value.ToString();
            if (string.IsNullOrEmpty(value)) continue;
            var resolved = Negotiate(value, supportedLanguages);
            if (resolved is not null)
                return new Result(resolved, Status.Resolved);
        }

        return new Result(null, Status.NoMatch);
    }

    /// <summary>
    /// Matches one requested tag against the locales we serve, returning the
    /// served locale or <c>null</c> when we can't serve the tag (the caller then
    /// tries the next Accept-Language entry, or fails). We serve exactly one
    /// locale per supported language — its <see cref="DefaultTerritories"/>
    /// territory:
    /// <list type="bullet">
    ///   <item><description>A tag <em>with</em> a territory must match that
    ///   served locale exactly (<c>en-GB</c> ✓, <c>en-US</c> ✗). We never degrade
    ///   a territory-bearing tag to the bare language ourselves — the client's
    ///   own bare-language entry is its stated fallback.</description></item>
    ///   <item><description>A bare-language tag resolves to the language's served
    ///   locale (<c>en</c> → <c>en_GB</c>).</description></item>
    /// </list>
    /// A supported language with no generated locale (i.e. absent from
    /// <see cref="DefaultTerritories"/>) is refused rather than resolved to an
    /// ungenerated locale that would crash the LaTeX toolchain.
    /// </summary>
    private static ResolvedLocale? Negotiate(string tag, IReadOnlyCollection<string> supportedLanguages)
    {
        var (language, requestedTerritory) = SplitTag(tag);
        // Case-insensitive membership: `language` is already lower-cased by
        // SplitTag, but supportedLanguages carries the registry keys verbatim
        // (from the qmd filenames), which are not guaranteed lower-case and may
        // be an Ordinal set — so match with the same case-insensitivity as
        // DefaultTerritories rather than depending on every key being lower-case.
        if (!supportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            return null;
        if (!DefaultTerritories.ContainsKey(language))
            return null; // served language without a generated locale — refuse, never crash
        var served = Parse(language);
        if (requestedTerritory is not null && requestedTerritory != served.Territory)
            return null; // a territory we don't serve, and we invent no fallback
        return served;
    }

    /// <summary>
    /// Splits a locale/language tag (<c>de_DE</c>, <c>de-DE</c>, <c>de</c>) into
    /// a lower-cased language subtag and an upper-cased territory subtag
    /// (<c>null</c> when the tag carries no territory). Applies no default.
    /// </summary>
    private static (string Language, string? Territory) SplitTag(string tag)
    {
        var parts = tag.Split(['_', '-'], 2);
        var language = parts[0].ToLowerInvariant();
        var territory = parts.Length > 1 && parts[1].Length > 0
            ? parts[1].ToUpperInvariant()
            : null;
        return (language, territory);
    }

    /// <summary>
    /// Splits a locale string (<c>de_DE</c>, <c>de-DE</c>, or just <c>de</c>)
    /// into a <see cref="ResolvedLocale"/>, applying
    /// <see cref="DefaultTerritories"/> when the territory is missing.
    /// </summary>
    public static ResolvedLocale Parse(string locale)
    {
        var (language, territory) = SplitTag(locale);
        territory ??= DefaultTerritories.TryGetValue(language, out var defaultTerritory)
            ? defaultTerritory
            : language.ToUpperInvariant();
        return new ResolvedLocale(language, territory);
    }
}
