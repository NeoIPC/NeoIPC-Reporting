using System.Collections.Immutable;
using Microsoft.Net.Http.Headers;

namespace NeoIPC.Reporting;

/// <summary>
/// Common header-derived inputs every report endpoint receives:
/// the DHIS2 session id (forwarded to the R/Quarto subprocess so
/// neoipcr authenticates), the negotiated Accept media types, the
/// Accept-Language header, and an optional explicit locale override.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ReportRequestBase"/>; report-specific
/// API parameter records inherit from that base, then add their own
/// <c>[ApiParameter]</c> / <c>[RenderParameter]</c> properties.
/// </remarks>
public interface IReportRequest
{
    string SessionId { get; }
    ImmutableArray<MediaTypeHeaderValue> AcceptHeaders { get; }
    ImmutableArray<StringWithQualityHeaderValue> AcceptLanguageHeaders { get; }
    string? Locale { get; }
}
