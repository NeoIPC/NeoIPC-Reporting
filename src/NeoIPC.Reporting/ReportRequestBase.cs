using System.Collections.Immutable;
using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Base record for per-report API parameters. Carries the
/// header-derived <see cref="IReportRequest"/> contract plus the
/// optional explicit <see cref="Locale"/> override; report-specific
/// records (<c>ReferenceReportApiParameters</c>,
/// <c>PartnerReportApiParameters</c>, …) inherit and add the report's
/// own query/body parameters.
/// </summary>
public abstract record ReportRequestBase : IReportRequest
{
    public required string SessionId { get; init; }
    public required ImmutableArray<MediaTypeHeaderValue> AcceptHeaders { get; init; }
    public required ImmutableArray<StringWithQualityHeaderValue> AcceptLanguageHeaders { get; init; }

    /// <summary>
    /// Explicit locale override (e.g. <c>de_DE</c>). When supplied and
    /// supported, takes precedence over <see cref="AcceptLanguageHeaders"/>.
    /// See <see cref="LocaleResolver"/> for the precedence rules.
    /// </summary>
    [ApiParameter]
    public string? Locale { get; init; }

    /// <summary>
    /// Extracts JSESSIONID from the request cookies and the sorted
    /// Accept / Accept-Language header lists. Throws when JSESSIONID is
    /// missing — the report endpoints are unreachable without a DHIS2
    /// session, since the R subprocess needs it for neoipcr to
    /// authenticate.
    /// </summary>
    public static (string SessionId, ImmutableArray<MediaTypeHeaderValue> Accept,
        ImmutableArray<StringWithQualityHeaderValue> AcceptLanguage)
        ReadHeaders(HttpRequest httpRequest)
    {
        var headers = httpRequest.GetTypedHeaders();
        var sessionId = headers.Cookie.FirstOrDefault(c => c is
            { Name: { HasValue: true, Value: "JSESSIONID" }, Value.HasValue: true })
            ?.Value.ToString() ?? throw new ArgumentException("JSESSIONID is missing.");
        return (
            sessionId,
            [..OutputNegotiation.SortAccept(headers.Accept)],
            [..OutputNegotiation.SortAcceptLanguage(headers.AcceptLanguage)]);
    }
}
