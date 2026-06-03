using System.Net;
using System.Net.Sockets;

namespace NeoIPC.Reporting;

/// <summary>
/// Parsed and validated form of <see cref="ReportingOptions.Dhis2BaseUrl"/>.
/// Built once at startup and registered as a singleton; constructor-injected
/// into <see cref="ReportingWarmupHostedService"/> so any validation failure
/// aborts host startup rather than surfacing on the first request.
/// </summary>
public sealed record Dhis2Endpoint(string Scheme, string Host, int Port, string Path, Uri BaseUri)
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
    /// Parses and validates <paramref name="baseUrl"/>. Throws
    /// <see cref="InvalidOperationException"/> on any of:
    /// not a valid absolute URL; scheme other than http/https; userinfo
    /// component (<c>user:pass@host</c>) present; the host resolves to
    /// loopback or the unspecified address.
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
    public static Dhis2Endpoint Build(string baseUrl)
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

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                $"Reporting:Dhis2BaseUrl '{baseUrl}' must not contain userinfo (user:pass@host).");

        if (IsRejectedHost(uri))
            throw new InvalidOperationException(
                $"Reporting:Dhis2BaseUrl '{baseUrl}' resolves to a loopback or unspecified address; " +
                "configure the in-cluster DHIS2 service hostname.");

        return new Dhis2Endpoint(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri);
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
