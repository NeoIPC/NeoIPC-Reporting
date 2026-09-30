using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NeoIPC.Reporting;

/// <summary>
/// Parsed and validated form of <see cref="ReportingOptions.Dhis2BaseUrl"/>
/// and <see cref="ReportingOptions.Dhis2PublicBaseUrl"/>.
/// Built once at startup and registered as a singleton; constructor-injected
/// into <see cref="ReportingWarmupHostedService"/> so any validation failure
/// aborts host startup rather than surfacing on the first request.
/// </summary>
/// <param name="Scheme">The scheme the service reaches DHIS2 with.</param>
/// <param name="Host">The host the service reaches DHIS2 at.</param>
/// <param name="Port">The port the service reaches DHIS2 at.</param>
/// <param name="Path">The DHIS2 context path on that host.</param>
/// <param name="BaseUri">The address the service reaches DHIS2 at.</param>
/// <param name="PublicBaseUri">
/// The address the users' browsers reach DHIS2 at, for the links reports
/// place to it; <paramref name="BaseUri"/> without its query and fragment
/// when none is configured.
/// </param>
public sealed partial record Dhis2Endpoint(string Scheme, string Host, int Port, string Path, Uri BaseUri, Uri PublicBaseUri)
{
    /// <summary>
    /// API mount path under <see cref="Path"/> (the DHIS2 context path).
    /// E.g. <c>"/"</c> → <c>"/api"</c>, <c>"/dhis"</c> → <c>"/dhis/api"</c>.
    /// Use this when configuring an API client (neoipcr's
    /// <c>dhis2_connection_options(path = …)</c>) where <c>path</c> is the
    /// API root, not the host context root.
    /// </summary>
    public string ApiPath => Path.TrimEnd('/') + "/api";

    /// <summary>
    /// Parses and validates <paramref name="baseUrl"/> and, when given,
    /// <paramref name="publicBaseUrl"/>. Throws
    /// <see cref="InvalidOperationException"/> when <paramref name="baseUrl"/>
    /// is not a valid absolute URL, uses a scheme other than http/https,
    /// carries a userinfo component (<c>user:pass@host</c>), or names a host
    /// that resolves to loopback or the unspecified address; when
    /// <paramref name="publicBaseUrl"/> does not have the shape
    /// <see cref="LinkBaseDefectOf"/> checks; and, when no
    /// <paramref name="publicBaseUrl"/> is given, when
    /// <paramref name="baseUrl"/> without its query and fragment, the base
    /// the links then take, does not have that shape either. A loopback
    /// public host is accepted: it is where a local stack's users reach DHIS2.
    /// No message repeats either value, which may carry a password and goes
    /// to the host's log.
    /// </summary>
    /// <remarks>
    /// Threat model: an attacker who can flip the configured base URL
    /// would harvest every JSESSIONID the service forwards. This is a
    /// deployment-config concern, not a runtime input — anyone with the
    /// privilege to set it can already compromise the service many other
    /// ways. The startup validation is a best-effort sanity check that
    /// catches the obvious misconfigurations; defence-in-depth lives at
    /// the network-policy layer (egress restricted to the in-cluster
    /// DHIS2 service).
    /// </remarks>
    public static Dhis2Endpoint Build(string baseUrl, string? publicBaseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl is empty. Configure a DHIS2 base URL.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl is not a valid absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl must use http or https.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl must not contain userinfo (user:pass@host).");

        if (IsRejectedHost(uri))
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl resolves to a loopback or unspecified address; " +
                "configure the in-cluster DHIS2 service hostname.");

        var publicUri = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? FallbackPublicBaseUri(uri)
            : ParsePublicBaseUrl(publicBaseUrl);
        return new Dhis2Endpoint(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri, publicUri);
    }

    /// <summary>
    /// Builds the endpoint the service registers, from
    /// <see cref="ReportingOptions.Dhis2BaseUrl"/> and
    /// <see cref="ReportingOptions.Dhis2PublicBaseUrl"/>; see <see cref="Build"/>.
    /// </summary>
    internal static Dhis2Endpoint FromOptions(ReportingOptions options) =>
        Build(options.Dhis2BaseUrl, options.Dhis2PublicBaseUrl);

    /// <summary>
    /// Checks <paramref name="text"/>, as written, against the shape the
    /// reports require of the base of their links to DHIS2: <c>http://</c> or
    /// <c>https://</c> in any case, a host of dot-separated labels of ASCII
    /// letters, digits, <c>-</c> and <c>_</c> (a final dot allowed), an
    /// optional port from 1 to 65535, and a path of <c>/</c>-separated
    /// segments of ASCII letters, digits, <c>-</c>, <c>.</c>, <c>_</c>,
    /// <c>~</c> and <c>%</c>-escapes, with nothing else. Returns the first
    /// defect found, or <see cref="LinkBaseDefect.None"/>.
    /// </summary>
    /// <remarks>
    /// The shape is an allow-list, and the Validation Report applies the same
    /// one to the <c>dhis2PublicBaseUrl</c> it is given. It is stricter than
    /// what a link needs, deliberately: the reports place the base in a
    /// Markdown link destination, where Pandoc percent-encodes whitespace and
    /// <c>&lt;&gt;|"{}[]^`</c> (<c>escapeURI</c> in Pandoc's
    /// <c>src/Text/Pandoc/URI.hs</c>), so an IPv6 literal's brackets no longer
    /// name its host, and an unbalanced <c>)</c> ends the destination early.
    /// Userinfo would publish a credential in every report, and a query or
    /// fragment would break the paths the reports append.
    /// </remarks>
    internal static LinkBaseDefect LinkBaseDefectOf(string text)
    {
        foreach (var c in text)
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return LinkBaseDefect.WhitespaceOrControl;

        var parts = LinkBaseParts().Match(text);
        if (!parts.Success) return LinkBaseDefect.Scheme;
        if (text.Contains('@')) return LinkBaseDefect.AtSign;
        if (text.AsSpan().IndexOfAny('?', '#') >= 0) return LinkBaseDefect.QueryOrFragment;

        var authority = parts.Groups["authority"].Value;
        if (authority.AsSpan().IndexOfAny('[', ']') >= 0) return LinkBaseDefect.BracketedHost;

        var colon = authority.IndexOf(':');
        var host = colon < 0 ? authority : authority[..colon];
        if (!System.Text.Ascii.IsValid(host)) return LinkBaseDefect.HostOutsideAscii;
        if (!LinkBaseHost().IsMatch(host)) return LinkBaseDefect.Host;
        if (colon >= 0 && !IsPort(authority[(colon + 1)..])) return LinkBaseDefect.Port;
        if (!LinkBasePath().IsMatch(parts.Groups["path"].Value)) return LinkBaseDefect.Path;

        return LinkBaseDefect.None;

        static bool IsPort(string digits) =>
            PortDigits().IsMatch(digits)
            && int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= 65535;
    }

    // Each phrase follows the setting's name, and none quotes the value.
    static string Describe(LinkBaseDefect defect) => defect switch
    {
        LinkBaseDefect.WhitespaceOrControl => "contains whitespace or a control character",
        LinkBaseDefect.Scheme => "does not begin with http:// or https://",
        LinkBaseDefect.AtSign => "contains '@', which is refused wherever it stands, since before the host it marks userinfo (user:pass@host)",
        LinkBaseDefect.QueryOrFragment => "contains '?' or '#', which begin a query or fragment",
        LinkBaseDefect.BracketedHost => "names its host in brackets, as an IPv6 literal",
        LinkBaseDefect.HostOutsideAscii => "names its host outside ASCII",
        LinkBaseDefect.Host => "names a host that is not dot-separated labels of ASCII letters, digits, '-' and '_'",
        LinkBaseDefect.Port => "gives a port that is not a number from 1 to 65535",
        LinkBaseDefect.Path => "has a path character other than an ASCII letter, a digit, '-', '.', '_', '~' or a %-escape",
        _ => throw new ArgumentOutOfRangeException(nameof(defect)),
    };

    const string ExpectedLinkBase =
        "The expected shape is http:// or https://, a host of dot-separated labels of ASCII letters, digits, " +
        "'-' and '_', an optional :port, and an optional path of ASCII letters, digits, '-', '.', '_', '~' and " +
        "%-escapes, with nothing after it.";

    // The public address ends up in every report that links to DHIS2, as a
    // document a reader may forward. A host outside ASCII is refused because
    // the links carry the host as written; the message may name its xn-- form,
    // which names the same host, since a value that reaches that check carries
    // no userinfo.
    static Uri ParsePublicBaseUrl(string publicBaseUrl)
    {
        var defect = LinkBaseDefectOf(publicBaseUrl);
        if (defect == LinkBaseDefect.HostOutsideAscii
            && Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var idn))
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl {Describe(defect)}; write its host as '{idn.IdnHost}'. {ExpectedLinkBase}");
        if (defect != LinkBaseDefect.None)
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl {Describe(defect)}. {ExpectedLinkBase}");

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl is not a valid absolute URL. {ExpectedLinkBase}");
        return uri;
    }

    // Without a public address the links go to the service's own, as scheme,
    // host, port and path: a query or fragment the setting may carry is no
    // part of a base the reports append paths to. That base is held to the
    // shape of the public one, host exactly as the links would carry it, so
    // an in-cluster address that cannot serve as one stops the service rather
    // than every report's links.
    static Uri FallbackPublicBaseUri(Uri baseUri)
    {
        var fallback = new UriBuilder(baseUri) { Query = string.Empty, Fragment = string.Empty }.Uri;
        var defect = LinkBaseDefectOf(fallback.AbsoluteUri);
        if (defect != LinkBaseDefect.None)
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl cannot serve as the base of the reports' links to DHIS2, which it is " +
                $"while Reporting:Dhis2PublicBaseUrl is unset: it {Describe(defect)}. Set " +
                "Reporting:Dhis2PublicBaseUrl to the address the users' browsers reach DHIS2 at. " +
                ExpectedLinkBase);
        return fallback;
    }

    [GeneratedRegex(@"\A[Hh][Tt][Tt][Pp][Ss]?://(?<authority>[^/]*)(?<path>.*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex LinkBaseParts();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\.?\z", RegexOptions.CultureInvariant)]
    private static partial Regex LinkBaseHost();

    [GeneratedRegex(@"\A[0-9]{1,5}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PortDigits();

    [GeneratedRegex(@"\A(?:/(?:[A-Za-z0-9._~-]|%[0-9A-Fa-f]{2})*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LinkBasePath();

    static bool IsRejectedHost(Uri uri)
    {
        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (IPAddress.TryParse(host, out var direct))
            return IPAddress.IsLoopback(direct) || direct.Equals(IPAddress.Any) || direct.Equals(IPAddress.IPv6Any);

        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
            return false;

        try
        {
            var addresses = Dns.GetHostAddresses(host, AddressFamily.Unspecified);
            foreach (var addr in addresses)
                if (IPAddress.IsLoopback(addr) || addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any))
                    return true;
        }
        catch (SocketException)
        {
            return false;
        }

        return false;
    }
}

/// <summary>
/// What <see cref="Dhis2Endpoint.LinkBaseDefectOf"/> finds wrong with a base
/// the reports' links to DHIS2 would be built on: the first defect in the
/// order listed, or <see cref="None"/>.
/// </summary>
public enum LinkBaseDefect
{
    /// <summary>The text has the shape of a link base.</summary>
    None,

    /// <summary>Whitespace or a control character, anywhere.</summary>
    WhitespaceOrControl,

    /// <summary>No <c>http://</c> or <c>https://</c> at the start, a single-slash authority included.</summary>
    Scheme,

    /// <summary>An <c>@</c>, anywhere; before the host it marks userinfo.</summary>
    AtSign,

    /// <summary>A <c>?</c> or <c>#</c>, anywhere, an empty query or fragment included.</summary>
    QueryOrFragment,

    /// <summary>A bracketed host, such as an IPv6 literal.</summary>
    BracketedHost,

    /// <summary>A host with a character outside ASCII.</summary>
    HostOutsideAscii,

    /// <summary>A host that is not dot-separated labels of ASCII letters, digits, <c>-</c> and <c>_</c>.</summary>
    Host,

    /// <summary>A port that is not a number from 1 to 65535.</summary>
    Port,

    /// <summary>A path character other than an ASCII letter, a digit, <c>-</c>, <c>.</c>, <c>_</c>, <c>~</c> or a <c>%</c>-escape.</summary>
    Path,
}
