namespace NeoIPC.Reporting;

/// <summary>
/// Handler for <c>GET /admin/dhis2-public-base-url</c>: the address the
/// reports' links to DHIS2 are built on, as the service read it at startup,
/// so an administrator can see which address a change of the setting left
/// the running service with.
/// </summary>
/// <remarks>
/// The service reads the setting once, when it starts, from the environment
/// its container was created with, so a changed value takes effect only once
/// the container is recreated. The address carries no credential:
/// <see cref="Dhis2Endpoint.Build"/> refuses userinfo in it.
/// </remarks>
static class Dhis2PublicBaseUrlEndpoint
{
    public static IResult AdminGet(Dhis2Endpoint endpoint) =>
        Results.Ok(new AdminDhis2PublicBaseUrl(endpoint.PublicBaseUri.AbsoluteUri, endpoint.PublicBaseUriConfigured));
}

/// <summary>
/// Returned by <c>GET /admin/dhis2-public-base-url</c>.
/// </summary>
/// <param name="PublicBaseUrl">The base of the reports' links to DHIS2.</param>
/// <param name="Configured">
/// Whether it is <see cref="ReportingOptions.Dhis2PublicBaseUrl"/> as
/// configured (<c>true</c>) or, with that setting unset, the DHIS2 address
/// the service reads from, <see cref="ReportingOptions.Dhis2BaseUrl"/>
/// (<c>false</c>).
/// </param>
public sealed record AdminDhis2PublicBaseUrl(string PublicBaseUrl, bool Configured);
