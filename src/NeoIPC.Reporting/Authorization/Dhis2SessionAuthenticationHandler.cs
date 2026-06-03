using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Authorization;

/// <summary>
/// Authenticates each request against DHIS2 by exchanging the
/// <c>JSESSIONID</c> cookie for a <see cref="ClaimsPrincipal"/> populated
/// from <c>/api/me</c>. Successful principals are memoised by
/// <see cref="SessionPrincipalCache"/>.
/// </summary>
/// <remarks>
/// Authorities and group memberships are materialised as custom claim
/// types defined in <see cref="Dhis2ClaimTypes"/> — see those for the
/// rationale behind not reusing <c>ClaimTypes.Role</c>. Policies in
/// <c>Program.cs</c> match on the claim values (e.g. <c>RequiresAll</c>
/// matches <c>dhis2:authority = "ALL"</c>).
/// </remarks>
public sealed class Dhis2SessionAuthenticationHandler
    : AuthenticationHandler<Dhis2SessionAuthenticationOptions>
{
    readonly Dhis2SessionClient _client;
    readonly SessionPrincipalCache _cache;

    public Dhis2SessionAuthenticationHandler(
        IOptionsMonitor<Dhis2SessionAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        Dhis2SessionClient client,
        SessionPrincipalCache cache)
        : base(options, logger, encoder)
    {
        _client = client;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No JSESSIONID = anonymous; let `[Authorize]` produce a 401 if the
        // endpoint requires auth. We don't fail authentication here — that
        // would short-circuit other schemes if they were ever stacked.
        if (!Request.Cookies.TryGetValue("JSESSIONID", out var sessionId)
            || string.IsNullOrEmpty(sessionId))
            return AuthenticateResult.NoResult();

        var cached = _cache.TryGet(sessionId);
        if (cached is not null)
            return AuthenticateResult.Success(new AuthenticationTicket(cached, Scheme.Name));

        var info = await _client.GetUserInfoAsync(sessionId, Context.RequestAborted);
        if (info is null)
            return AuthenticateResult.Fail("DHIS2 session is invalid or expired.");

        var principal = BuildPrincipal(info);
        _cache.Set(sessionId, principal);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    ClaimsPrincipal BuildPrincipal(Dhis2UserInfo info)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, info.Id),
            new(ClaimTypes.Name, info.Username),
        };
        // One claim per authority: lets policies match via RequireClaim.
        // DHIS2's superuser "ALL" comes through as one of these.
        foreach (var authority in info.Authorities ?? [])
            claims.Add(new Claim(Dhis2ClaimTypes.Authority, authority));
        // Group id is stable across renames; group name is for diagnostics.
        foreach (var group in info.UserGroups ?? [])
        {
            claims.Add(new Claim(Dhis2ClaimTypes.UserGroup, group.Id));
            claims.Add(new Claim(Dhis2ClaimTypes.UserGroupName, group.Name));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
    }
}
