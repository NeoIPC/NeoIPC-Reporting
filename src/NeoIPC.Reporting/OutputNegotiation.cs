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
    /// <summary>Sorts <c>Accept</c> headers by q-value (descending), preserving header order on ties.</summary>
    public static IEnumerable<MediaTypeHeaderValue> SortAccept(IList<MediaTypeHeaderValue> headers)
        => headers
            .Select((h, i) => (h.MediaType, Quality: h.Quality ?? 1.0, Index: i, Value: h))
            .Where(h => h.MediaType.HasValue)
            .OrderByDescending(h => h.Quality).ThenBy(h => h.Index)
            .Select(h => h.Value);

    /// <summary>Sorts <c>Accept-Language</c> headers by q-value (descending), preserving header order on ties.</summary>
    public static IEnumerable<StringWithQualityHeaderValue> SortAcceptLanguage(
        IList<StringWithQualityHeaderValue> headers)
        => headers
            .Select((h, i) => (Quality: h.Quality ?? 1.0, Index: i, Language: h.Value, Value: h))
            .Where(h => h.Language.HasValue)
            .OrderByDescending(h => h.Quality).ThenBy(h => h.Index)
            .Select(h => h.Value);

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
