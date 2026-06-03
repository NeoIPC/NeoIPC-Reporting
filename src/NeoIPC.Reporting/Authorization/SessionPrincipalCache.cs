using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Authorization;

/// <summary>
/// Memoises the <see cref="ClaimsPrincipal"/> we materialised from a DHIS2
/// session, keyed on the JSESSIONID, so repeated requests within the TTL
/// don't each round-trip to <c>/api/me</c>.
/// </summary>
/// <remarks>
/// Only successful resolutions are cached. The TTL comes from
/// <see cref="Dhis2SessionAuthenticationOptions.PrincipalCacheTtl"/>
/// — short enough that a logout in DHIS2 surfaces here within seconds,
/// not hours.
/// </remarks>
public sealed class SessionPrincipalCache
{
    readonly IMemoryCache _cache;
    readonly IOptionsMonitor<Dhis2SessionAuthenticationOptions> _options;

    public SessionPrincipalCache(
        IMemoryCache cache,
        IOptionsMonitor<Dhis2SessionAuthenticationOptions> options)
    {
        _cache = cache;
        _options = options;
    }

    public ClaimsPrincipal? TryGet(string sessionId) =>
        _cache.TryGetValue(Key(sessionId), out ClaimsPrincipal? cached) ? cached : null;

    public void Set(string sessionId, ClaimsPrincipal principal)
    {
        var ttl = _options.Get(Dhis2SessionAuthenticationDefaults.AuthenticationScheme).PrincipalCacheTtl;
        _cache.Set(Key(sessionId), principal, ttl);
    }

    static string Key(string sessionId) => "dhis2:session:" + sessionId;
}
