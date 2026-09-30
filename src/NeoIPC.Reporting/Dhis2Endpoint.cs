using System.Net;
using System.Net.Sockets;

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
/// place to it; <paramref name="BaseUri"/> when none is configured.
/// </param>
public sealed record Dhis2Endpoint(string Scheme, string Host, int Port, string Path, Uri BaseUri, Uri PublicBaseUri)
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
    /// that resolves to loopback or the unspecified address; and when
    /// <paramref name="publicBaseUrl"/> is not a valid absolute URL, uses a
    /// scheme other than http/https, carries a userinfo component (an empty
    /// one included), a query or a fragment, or names a host outside ASCII.
    /// A loopback public host is accepted: it is where a local stack's users
    /// reach DHIS2.
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
                $"Reporting:Dhis2BaseUrl '{baseUrl}' is not a valid absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"Reporting:Dhis2BaseUrl '{baseUrl}' must use http or https.");

        // The value is not repeated: it holds a password, and this message
        // goes to the host's log.
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                "Reporting:Dhis2BaseUrl must not contain userinfo (user:pass@host).");

        if (IsRejectedHost(uri))
            throw new InvalidOperationException(
                $"Reporting:Dhis2BaseUrl '{baseUrl}' resolves to a loopback or unspecified address; " +
                "configure the in-cluster DHIS2 service hostname.");

        // Without a public address the links go to the service's own, as
        // scheme, host, port and path: a query or fragment the setting may
        // carry is no part of a base the reports append paths to.
        var publicUri = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri
            : ParsePublicBaseUrl(publicBaseUrl);
        return new Dhis2Endpoint(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri, publicUri);
    }

    // The public address ends up in every report that links to DHIS2, as a
    // document a reader may forward, so it must not carry credentials, not even
    // an empty userinfo the reports would still refuse; a query or fragment
    // would break the paths the reports append to it. A host outside ASCII is
    // refused because the links carry the host as written, and its xn-- form
    // names the same host.
    static Uri ParsePublicBaseUrl(string publicBaseUrl)
    {
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl '{publicBaseUrl}' is not a valid absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl '{publicBaseUrl}' must use http or https.");

        // The value is not repeated: it may hold a password, and this message
        // goes to the host's log.
        if (!string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsoluteUri.StartsWith(uri.Scheme + "://@", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Reporting:Dhis2PublicBaseUrl must not contain userinfo (user:pass@host).");

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl '{publicBaseUrl}' must not contain a query or fragment.");

        if (!System.Text.Ascii.IsValid(uri.Host))
            throw new InvalidOperationException(
                $"Reporting:Dhis2PublicBaseUrl '{publicBaseUrl}' must name its host in ASCII; " +
                $"write it as '{uri.IdnHost}'.");

        return uri;
    }

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
