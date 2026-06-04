using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Integration tests for the negative-path responses spelled out in the
/// plan's Verification step 6 (406 / 400 / 403 / 404). These are
/// reachable without a real DHIS2 session because the relevant checks
/// (header presence, parameter shape, mode-mixing rejection,
/// authorization-without-claims) run before the auth round-trip.
/// </summary>
/// <remarks>
/// Shares a container with <see cref="ParametersEndpointTests"/>'s image
/// tag (<c>NEOIPC_REPORTING_IMAGE_TAG</c>, default
/// <c>neoipc-reporting:smoke-test</c>) but spins its own container
/// instance up for isolation.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class NegativePathTests
{
    static readonly string ImageTag =
        Environment.GetEnvironmentVariable("NEOIPC_REPORTING_IMAGE_TAG") ?? "neoipc-reporting:smoke-test";

    IContainer? _container;
    HttpClient? _http;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        _container = new ContainerBuilder()
            .WithImage(ImageTag)
            .WithPortBinding(8080, true)
            .WithEnvironment("ASPNETCORE_HTTP_PORTS", "8080")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8080)
                    .ForPath("/reference-report/parameters")
                    .ForStatusCode(HttpStatusCode.OK)))
            .Build();
        await _container.StartAsync();
        var port = _container.GetMappedPublicPort(8080);
        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        _http?.Dispose();
        if (_container is not null) await _container.DisposeAsync();
    }

    HttpRequestMessage Get(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        // Set the common headers + cookie most handlers expect; individual
        // tests override these to exercise the missing-header paths.
        // ReportRequestBase.ReadHeaders reads JSESSIONID upfront and
        // throws when absent, so even pre-render negative paths need a
        // placeholder cookie. The auth handler will fail to validate this
        // session (no DHIS2 to call) — anywhere claims are checked the
        // principal will be unauthenticated, which is what these tests
        // want for the 403 / 401 paths.
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/pdf");
        req.Headers.Add("Accept-Language", "en");
        return req;
    }

    [Test]
    public async Task ReferenceReport_MissingAcceptLanguage_Returns406()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/reference-report?referenceDataId=00000000000000000000000000000000");
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/pdf");
        // No Accept-Language header.
        var response = await _http!.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotAcceptable));
    }

    [Test]
    public async Task ReferenceReport_MalformedReferenceDataId_Returns400()
    {
        var response = await _http!.SendAsync(Get("/reference-report?referenceDataId=not-32-hex"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReferenceReport_NonexistentReferenceDataId_Returns404()
    {
        // A well-formed but non-stored 32-hex id passes the format gate
        // and reaches the storage existence check.
        var response = await _http!.SendAsync(
            Get("/reference-report?referenceDataId=00000000000000000000000000000000"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ReferenceReport_MixedMode_Returns400()
    {
        // Stored-data mode + live-fetch filter param = mixed mode (400).
        // The mixed-mode check runs before ID format validation, so even
        // with a deliberately invalid id this still surfaces as a mixed-
        // mode error rather than an id-format error.
        var response = await _http!.SendAsync(Get(
            "/reference-report?referenceDataId=any&reportingPeriodFrom=2024-01-01"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReferenceReport_AdHocMode_WithoutAuth_Returns403()
    {
        // No referenceDataId -> ad-hoc preview mode -> requires the ALL
        // authority. Without a JSESSIONID cookie the principal is
        // unauthenticated and AuthorizeAsync rejects.
        var response = await _http!.SendAsync(Get(
            "/reference-report?reportingPeriodFrom=2024-01-01"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ReferenceReport_LocaleWithControlChar_Returns400()
    {
        // YAML-safety enforced at the API boundary by InputValidation.
        // Embedding a literal newline (URL-encoded as %0A) in the locale
        // value must surface as a 400 ProblemDetails rather than reaching
        // the Quarto -P arg builder.
        var response = await _http!.SendAsync(Get(
            "/reference-report?referenceDataId=00000000000000000000000000000000&locale=de%0Aexploit"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PartnerReport_MissingUnitCodes_Returns400()
    {
        // POST with a body but no unitCodes query param. The body has to
        // be present (otherwise it short-circuits to "Missing partnerData
        // body" first); use a tiny placeholder.
        var req = new HttpRequestMessage(HttpMethod.Post, "/partner-report")
        {
            Content = new StringContent("{}"),
        };
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/pdf");
        req.Headers.Add("Accept-Language", "en");
        var response = await _http!.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdminEndpoint_WithoutAuth_Returns401Or403()
    {
        // The /admin/* group has .RequireAuthorization("RequiresAll").
        // Without a JSESSIONID cookie, the auth handler returns NoResult
        // and the policy gate rejects.
        var response = await _http!.SendAsync(Get("/admin/reference-data"));
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
    }
}
