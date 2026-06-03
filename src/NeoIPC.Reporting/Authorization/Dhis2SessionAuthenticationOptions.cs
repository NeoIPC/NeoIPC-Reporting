using Microsoft.AspNetCore.Authentication;

namespace NeoIPC.Reporting.Authorization;

/// <summary>Options for <see cref="Dhis2SessionAuthenticationHandler"/>.</summary>
public sealed class Dhis2SessionAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// How long a successfully resolved <c>JSESSIONID</c> → principal
    /// mapping stays in <see cref="SessionPrincipalCache"/> before the
    /// handler re-validates against DHIS2 <c>/api/me</c>.
    /// </summary>
    /// <remarks>
    /// Short by default (60 s) so a logout in DHIS2 is reflected
    /// quickly without forcing a round-trip on every request. Failure
    /// results are not cached.
    /// </remarks>
    public TimeSpan PrincipalCacheTtl { get; set; } = TimeSpan.FromSeconds(60);
}
