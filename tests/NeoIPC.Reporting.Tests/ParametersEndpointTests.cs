using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Integration smoke test that spins up the built image via Testcontainers
/// and hits the public, no-auth-required <c>/reference-report/parameters</c>
/// endpoint. Validates that the .NET host comes up cleanly inside the
/// container and that the source-generator's <c>Schema</c> output reaches
/// the wire shape the future DHIS2 App expects.
/// </summary>
/// <remarks>
/// <para>
/// Skipped on the default <c>dotnet test</c> invocation (filter
/// <c>Category!=Integration</c> in CI's PR job). Runs on
/// <c>--filter Category=Integration</c>, which CI invokes only via
/// <c>workflow_dispatch</c> because it needs Docker available on
/// the runner.
/// </para>
/// <para>
/// The image tag is taken from the <c>NEOIPC_REPORTING_IMAGE_TAG</c>
/// environment variable; defaults to <c>neoipc-reporting:smoke-test</c>
/// to match the local docker-build invocation documented in the README.
/// The image must already be built — this test does not build it.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
public class ParametersEndpointTests
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
            // Wait until the parameters endpoint returns 200 — this implies
            // the host is up, options binding succeeded, and the source-
            // generator-emitted Schema is reachable.
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

    [Test]
    public async Task ReferenceReportParameters_Returns200WithFieldsArray()
    {
        Assert.That(_http, Is.Not.Null);
        var response = await _http!.GetAsync("/reference-report/parameters");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(doc.TryGetProperty("fields", out var fields), Is.True,
            "response must contain a 'fields' array");
        Assert.That(fields.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(fields.GetArrayLength(), Is.GreaterThan(0),
            "Reference-Report has API parameters; the schema should not be empty");
    }

    [Test]
    public async Task PartnerReportParameters_Returns200WithFieldsArray()
    {
        Assert.That(_http, Is.Not.Null);
        var response = await _http!.GetAsync("/partner-report/parameters");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(doc.TryGetProperty("fields", out var fields), Is.True);
        Assert.That(fields.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(fields.GetArrayLength(), Is.GreaterThan(0));
    }
}
