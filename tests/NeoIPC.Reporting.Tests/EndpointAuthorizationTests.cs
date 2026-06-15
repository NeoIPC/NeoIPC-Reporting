using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Defense-in-depth backstop for the PR #15 review note: every mapped
/// endpoint must declare how it is authorized, so a future endpoint can't
/// be added silently public. An endpoint passes if it carries a route
/// authorization policy (<see cref="IAuthorizeData"/>), is marked
/// <see cref="InHandlerAuthorized"/>, or is marked
/// <see cref="PublicEndpoint"/>. Anything else fails — forcing the author
/// to make a conscious choice (gate it, or mark it deliberately public).
/// </summary>
/// <remarks>
/// Builds the endpoint set in-process via <c>ApiEndpoints.Map</c> (no
/// Kestrel, no hosted services, no DHIS2) and inspects endpoint metadata.
/// </remarks>
[TestFixture]
[Category("Unit")]
public class EndpointAuthorizationTests
{
    [Test]
    public void EveryEndpoint_IsAuthorizedOrExplicitlyPublic()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        // Register the types the endpoint handlers bind by inference, so the
        // minimal-API factory realizes their metadata as services rather than
        // failing to infer a body parameter. Registration only — the handlers
        // are never invoked here, so these need not be constructable, and the
        // host defaults to the Production environment (no build-time DI
        // validation).
        builder.Services.AddSingleton<ReportLanguageRegistry>();
        builder.Services.AddSingleton<ReferenceDataStorage>();
        builder.Services.AddSingleton<ValidationExceptionStorage>();
        builder.Services.AddSingleton<ReferenceDataMetadataExtractor>();
        var app = builder.Build();

        ApiEndpoints.Map(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.That(endpoints, Is.Not.Empty, "ApiEndpoints.Map mapped no endpoints.");

        var ungated = endpoints
            .Where(ep =>
                ep.Metadata.GetMetadata<IAuthorizeData>() is null &&
                ep.Metadata.GetMetadata<InHandlerAuthorized>() is null &&
                ep.Metadata.GetMetadata<PublicEndpoint>() is null)
            .Select(ep => ep.RoutePattern.RawText ?? ep.DisplayName ?? "<unknown>")
            .ToList();

        Assert.That(ungated, Is.Empty,
            "These endpoints declare no authorization and aren't marked PublicEndpoint. " +
            "Gate them (route policy, or in-handler + an InHandlerAuthorized marker), or mark " +
            "them PublicEndpoint if intentionally public: " + string.Join(", ", ungated));
    }
}
