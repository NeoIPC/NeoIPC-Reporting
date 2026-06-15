using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace NeoIPC.Reporting.Authorization;

/// <summary>
/// Shared in-handler authorization gate for endpoints whose required
/// authority can't be expressed as a static route policy — e.g.
/// <see cref="ReferenceReport"/>, whose gate is conditional on the query
/// (<c>F_NEOIPC_REPORT</c> for stored data, <c>F_NEOIPC_ADMIN</c> for the
/// ad-hoc live preview). Returns a 403 ProblemDetails when the user lacks
/// <paramref name="policy"/>, or <c>null</c> when authorized.
/// </summary>
/// <remarks>
/// Render endpoints authorize <em>after</em> request-shape validation, so
/// malformed/missing/mixed requests still surface 400/404/406 without a
/// DHIS2 session (the standalone <c>NegativePathTests</c> rely on this and
/// cannot mint an authenticated principal — there is no DHIS2 to call).
/// That is why the gate lives here rather than in a route-level policy.
/// Endpoints using this helper must carry the <see cref="InHandlerAuthorized"/>
/// endpoint marker so the <c>EndpointAuthorizationTests</c> backstop
/// recognises them as gated rather than silently public.
/// </remarks>
public static class NeoIpcAuthorization
{
    public static async Task<IResult?> RequireAsync(
        IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string policy,
        string forbiddenDetail)
    {
        var result = await authorizationService.AuthorizeAsync(user, policy);
        return result.Succeeded
            ? null
            : ProblemDetailsHelper.Forbidden("Forbidden", forbiddenDetail);
    }
}

/// <summary>
/// Endpoint metadata marking a route as authorized inside its handler
/// rather than via a route-level policy. Lets the endpoint-coverage
/// regression test treat the route as gated, and documents which
/// authority applies.
/// </summary>
public sealed class InHandlerAuthorized(string description)
{
    public string Description { get; } = description;
}

/// <summary>
/// Endpoint metadata marking a route as intentionally public (no
/// authorization) — used for the static, data-free parameter-schema
/// endpoints so the endpoint-coverage regression test can distinguish
/// "deliberately public" from "forgot to gate".
/// </summary>
public sealed class PublicEndpoint(string reason)
{
    public string Reason { get; } = reason;
}
