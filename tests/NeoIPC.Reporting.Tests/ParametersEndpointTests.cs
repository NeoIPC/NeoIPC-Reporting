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
/// Tagged <c>Category=Container</c>: it builds and runs the image in
/// isolation, so it is excluded from the PR unit job (filter
/// <c>Category!=Integration&amp;Category!=Container</c>) and runs only on
/// the <c>workflow_dispatch</c> container-smoke job, which has Docker and
/// builds the image first. This is the repository's own self-contained
/// gate; it shares no DHIS2 with the workspace-driven
/// <c>Category=Integration</c> tests (<see cref="RenderingIntegrationTests"/>).
/// </para>
/// <para>
/// The image is the one <see cref="SmokeTestImage"/> resolves: the tag
/// <c>NEOIPC_REPORTING_IMAGE_TAG</c> names, or <c>neoipc-reporting:smoke-test</c>
/// built from this repository's Dockerfile by the run itself.
/// </para>
/// </remarks>
[TestFixture]
[Category("Container")]
public class ParametersEndpointTests
{
    IContainer? _container;
    HttpClient? _http;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        var imageTag = await SmokeTestImage.ResolveAsync();

        // Skip rather than fail when there is no Docker to talk to, matching
        // RenderingIntegrationTests' behaviour for an absent stack: a plain
        // `dotnet test` on a developer machine should report "ignored" for the
        // environment it lacks, not a wall of failures that hides real ones.
        //
        // The try has to span Build() as well as StartAsync(): Testcontainers
        // resolves the Docker endpoint while building the configuration, so
        // with no daemon running it throws before a container is ever started.
        // Only DockerUnavailableException is caught — a missing image or a
        // container that comes up wrong must still fail, or this would stop
        // verifying the thing it exists to verify.
        try
        {
            _container = new ContainerBuilder(imageTag)
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
        }
        catch (DockerUnavailableException ex)
        {
            Assert.Ignore($"Category=Container tests need a running Docker daemon. {ex.Message}");
        }

        var port = _container!.GetMappedPublicPort(8080);
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
