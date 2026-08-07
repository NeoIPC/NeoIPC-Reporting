using System.Collections.Immutable;
using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Helpers for HTTP content negotiation on the report endpoints. Sorts
/// <c>Accept</c> / <c>Accept-Language</c> by q-value (descending) with
/// the original header order as tiebreaker.
/// </summary>
public static class OutputNegotiation
{
    /// <summary>
    /// Drops entries with <c>q=0</c> (RFC 9110 §12.4.2 "not acceptable"), then sorts
    /// <c>Accept</c> headers by q-value (descending), preserving header order on ties.
    /// </summary>
    public static IEnumerable<MediaTypeHeaderValue> SortAccept(IList<MediaTypeHeaderValue> headers)
        => headers
            .Select((h, i) => (h.MediaType, Quality: h.Quality ?? 1.0, Index: i, Value: h))
            // A q-value of 0 means "not acceptable" (RFC 9110 §12.4.2) — drop it so it is
            // neither offered as a fallback nor able to slip past the locale gate.
            .Where(h => h.MediaType.HasValue && h.Quality > 0)
            .OrderByDescending(h => h.Quality).ThenBy(h => h.Index)
            .Select(h => h.Value);

    /// <summary>
    /// Drops entries with <c>q=0</c> (RFC 9110 §12.4.2 "not acceptable"), then sorts
    /// <c>Accept-Language</c> headers by q-value (descending), preserving header order on ties.
    /// </summary>
    public static IEnumerable<StringWithQualityHeaderValue> SortAcceptLanguage(
        IList<StringWithQualityHeaderValue> headers)
        => headers
            .Select((h, i) => (Quality: h.Quality ?? 1.0, Index: i, Language: h.Value, Value: h))
            .Where(h => h.Language.HasValue && h.Quality > 0)
            .OrderByDescending(h => h.Quality).ThenBy(h => h.Index)
            .Select(h => h.Value);

    /// <summary>
    /// True when the request can be satisfied <em>only</em> by a rendered
    /// (html/pdf) output — a rendered media type is acceptable and the
    /// locale-independent <c>application/json</c> data output is not. A
    /// rendering locale is mandatory for the rendered outputs, so a caller who
    /// offers none (no <c>Accept-Language</c>, no explicit <c>?locale=</c>) and
    /// accepts only rendered formats cannot be served and is refused (406). When
    /// <c>application/json</c> is also acceptable this returns false: the data
    /// output is the raw neoipcr dataset (codes), locale-independent, so the
    /// request is serviceable without a locale (the JSON producer defaults its
    /// locale — see <see cref="RScriptReportProducer.DefaultLocale"/>). Returns
    /// false too when no supported output is acceptable at all — producer
    /// selection then refuses that with a coded 406, so it is not a locale
    /// refusal and must not be reported as one.
    /// </summary>
    /// <param name="acceptHeaders">The request's acceptable media types.</param>
    /// <param name="dataOutputAvailable">
    /// Whether the data output can serve <em>this</em> request. Both handlers skip
    /// the R-script producer when the dataset is given rather than fetched — a
    /// stored <c>referenceDataId</c>, or an uploaded partner-data body — because it
    /// would answer from a live DHIS2 fetch instead. Passing <c>true</c> there would
    /// let a caller offering <c>*&#47;*</c> and no locale past this refusal and into
    /// producer selection, which finds nothing and refuses on media type — naming a
    /// cause that is not the request's real one, the missing locale.
    /// </param>
    public static bool OnlyRenderedOutputsAreAcceptable(
        ImmutableArray<MediaTypeHeaderValue> acceptHeaders,
        bool dataOutputAvailable = true)
    {
        var rendered = false;
        var data = false;
        foreach (var header in acceptHeaders)
        {
            // A q-value of 0 means "not acceptable" (RFC 9110 §12.4.2). SortAccept
            // already drops these upstream, but the helper must not depend on the
            // caller pre-filtering — a q=0 type is never an acceptable output.
            if (header.Quality is <= 0) continue;
            var mediaType = header.MediaType.ToString();
            if (QuartoReportProducer.IsMediaTypeSupported(mediaType)) rendered = true;
            if (dataOutputAvailable && RScriptReportProducer.IsMediaTypeSupported(mediaType))
                data = true;
        }
        return rendered && !data;
    }

    /// <summary>
    /// Walks a sorted Accept-Language list and invokes
    /// <paramref name="factory"/> for the first language that
    /// <paramref name="isSupported"/> accepts. Tries the full tag first
    /// (e.g. <c>de-DE</c>), then the neutral subtag (<c>de</c>) for any
    /// regional tag that didn't match directly. Returns null when nothing
    /// matches.
    /// </summary>
    public static T? FindByLanguages<T>(
        IEnumerable<StringWithQualityHeaderValue> acceptLanguageHeaders,
        Func<string, bool> isSupported,
        Func<string, T> factory) where T : class
    {
        var headers = acceptLanguageHeaders.ToImmutableArray();
        foreach (var lang in headers)
        {
            var language = lang.Value.ToString();
            if (isSupported(language))
                return factory(language);
        }

        foreach (var lang in headers)
        {
            var parts = lang.Value.ToString().Split('-');
            if (parts.Length < 2) continue;
            var neutralLanguage = parts[0];
            if (isSupported(neutralLanguage))
                return factory(neutralLanguage);
        }

        return null;
    }
}
