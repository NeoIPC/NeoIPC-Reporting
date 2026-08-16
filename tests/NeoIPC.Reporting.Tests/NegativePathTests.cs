using System.Net;
using System.Text;
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
[Category("Container")]
public class NegativePathTests
{
    static readonly string ImageTag =
        Environment.GetEnvironmentVariable("NEOIPC_REPORTING_IMAGE_TAG") ?? "neoipc-reporting:smoke-test";

    IContainer? _container;
    HttpClient? _http;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
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
            _container = new ContainerBuilder(ImageTag)
                .WithPortBinding(8080, true)
                .WithEnvironment("ASPNETCORE_HTTP_PORTS", "8080")
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
            Assert.Ignore(
                $"Category=Container tests need a running Docker daemon and the '{ImageTag}' " +
                $"image already built. {ex.Message}");
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

    HttpRequestMessage Post(string path, string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // The app is the only real client of this route and sends the file's
            // own type, falling back to application/json. Bare StringContent
            // sends text/plain, so a test built that way differs in shape from
            // every request the route actually receives — not a difference the
            // handler reads today, since it streams the body through without a
            // content-type gate, but the fidelity is free.
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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
    public async Task PartnerReport_JsonOutput_WithoutAcceptLanguage_IsNotRejectedWith406()
    {
        // The application/json data output is the raw, locale-independent neoipcr
        // dataset, so a missing Accept-Language must NOT 406 it (unlike the
        // rendered pdf/html outputs — see ReferenceReport_MissingAcceptLanguage).
        // With no unitCodes it now reaches the shape check (400), proving
        // negotiation let the JSON path through rather than refusing it up front.
        var req = new HttpRequestMessage(HttpMethod.Get, "/partner-report");
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/json");
        // No Accept-Language header.
        var response = await _http!.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReferenceReport_JsonOutput_WithoutAcceptLanguage_IsNotRejectedWith406()
    {
        // Same rule for the reference report's JSON output: a missing
        // Accept-Language must not 406 the locale-independent data output. A
        // malformed referenceDataId now surfaces the id-format 400 that the
        // blanket 406 previously masked.
        var req = new HttpRequestMessage(HttpMethod.Get, "/reference-report?referenceDataId=not-32-hex");
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/json");
        // No Accept-Language header.
        var response = await _http!.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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

    // Each live-fetch parameter needs its own case: asserting the status for one of
    // them leaves every other member of the rejected set untested, so dropping one
    // would keep the suite green while the endpoint answered 200 over the whole
    // stored dataset. The body is checked for the parameter's own name, because a
    // 400 alone is also what an id-format or confidence-interval failure returns.
    [TestCase("reportingPeriodFrom=2024-01-01", "reportingPeriodFrom")]
    [TestCase("reportingPeriodTo=2024-12-31", "reportingPeriodTo")]
    [TestCase("birthWeightFrom=500", "birthWeightFrom")]
    [TestCase("birthWeightTo=2500", "birthWeightTo")]
    [TestCase("gestationalAgeFrom=24", "gestationalAgeFrom")]
    [TestCase("gestationalAgeTo=32", "gestationalAgeTo")]
    [TestCase("countryFilter=AT", "countryFilter")]
    [TestCase("departmentFilter=AT_TEST_TEST", "departmentFilter")]
    [TestCase("testUnitFilter=false", "testUnitFilter")]
    [TestCase("defaultPatientFilter=false", "defaultPatientFilter")]
    public async Task ReferenceReport_StoredDataset_RejectsEveryLiveFetchParam(
        string query, string expectedName)
    {
        var response = await _http!.SendAsync(Get(
            $"/reference-report?referenceDataId=any&{query}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain(ProblemCodes.MixedModeNotAllowed));
            Assert.That(body, Does.Contain(expectedName));
        });
    }

    [Test]
    public async Task ReferenceReport_AdHocMode_WithoutAuth_Returns403()
    {
        // No referenceDataId -> ad-hoc preview mode -> requires the
        // F_NEOIPC_ADMIN authority. Without a valid DHIS2 session the
        // principal is unauthenticated and AuthorizeAsync rejects.
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
    public async Task PartnerReportOnline_MissingUnitCodes_Returns400()
    {
        // Online mode names the department to fetch, so it is the only mode
        // that requires unitCodes — see the dataFile counterpart below. The
        // check runs ahead of authorization, so it stays reachable with a
        // session the container cannot validate.
        var response = await _http!.SendAsync(Get("/partner-report"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Name the guard rather than trusting the status. Several checks
        // ahead of this one also answer 400, so a status-only assertion
        // passes whichever fired — and would stay green if this guard
        // became unreachable, which is exactly how it broke before.
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain(ProblemCodes.MissingUnitCodes));
    }

    [Test]
    public async Task PartnerReportDataFile_MissingUnitCodes_IsAllowed()
    {
        // The uploaded dataset IS the department, so dataFile mode must not
        // demand unitCodes — and the requirement is skipped rather than
        // satisfied, which no test would notice if it were reinstated.
        //
        // Asserted as the absence of that rejection, not as a positive
        // status: the request goes on to authorization, which this
        // container cannot satisfy, so what it finally answers says nothing
        // about this guard. Not-400 is still provable — the body is present,
        // so the missing-body guard cannot fire either, and the inputs
        // carry nothing the YAML-safety check rejects.
        var response = await _http!.SendAsync(Post("/partner-report", "{}"));

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain(ProblemCodes.MissingUnitCodes));
    }

    [Test]
    public async Task PartnerReportDataFile_WithUnitCodes_Returns400()
    {
        // The uploaded dataset fixes the department, and the subtitle is front
        // matter — evaluated before the setup chunk that reads the real
        // department out of the dataset's metadata — so a unitCodes that
        // disagrees cannot be corrected during the render. It would title the
        // document with a department the document does not contain, so it is
        // refused rather than ignored.
        var req = new HttpRequestMessage(
            HttpMethod.Post, "/partner-report?unitCodes=DE_TEST_TEST")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/pdf");
        req.Headers.Add("Accept-Language", "en");

        var response = await _http!.SendAsync(req);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        // Its own code, not the stored-reference one: a consumer maps each to a
        // different message, so sharing a code would name the wrong cause.
        Assert.That(body, Does.Contain(ProblemCodes.UploadedDataFixesScope));
    }

    [Test]
    public async Task ReferenceReport_StoredDataset_WildcardAcceptWithoutLocale_Returns406()
    {
        // With a stored dataset there is no JSON output to fall back on — the
        // R-script producer is skipped, because it would answer from a live
        // DHIS2 fetch rather than from the stored dataset. So a caller offering
        // */* and no locale can be served only by a rendered output, and the
        // real blocker is the missing locale. Without this the request slips
        // past the locale gate (*/* "accepts" a JSON output that cannot be
        // produced) and is refused later for the wrong reason.
        //
        // The id need not exist: this gate runs before the dataset is looked up.
        var req = new HttpRequestMessage(
            HttpMethod.Get, "/reference-report?referenceDataId=00000000000000000000000000000000");
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "*/*");
        // No Accept-Language header, and no ?locale=.

        var response = await _http!.SendAsync(req);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotAcceptable));
    }

    [Test]
    public async Task ReferenceReport_StoredDataset_NoProducibleOutput_CarriesItsCode()
    {
        // The argument for 406-with-a-code over the bodiless refusal it replaced is
        // precisely that a caller can now tell "no JSON for a stored dataset" from
        // "no JSON at all" — so the status alone is the part that proves nothing.
        var req = new HttpRequestMessage(
            HttpMethod.Get, "/reference-report?referenceDataId=00000000000000000000000000000000");
        req.Headers.Add("Cookie", "JSESSIONID=test-placeholder-session-id");
        req.Headers.Add("Accept", "application/json");
        req.Headers.Add("Accept-Language", "en");

        var response = await _http!.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotAcceptable));
            Assert.That(body, Does.Contain(ProblemCodes.NoAcceptableOutput));
            Assert.That(body, Does.Contain("referenceDataId"),
                "the detail must name what to drop, not merely refuse");
        });
    }

    [Test]
    public async Task AdminEndpoint_WithoutAuth_Returns401Or403()
    {
        // The /admin/* group has .RequireAuthorization("NeoIpcAdmin").
        // Without a JSESSIONID cookie, the auth handler returns NoResult
        // and the policy gate rejects.
        var response = await _http!.SendAsync(Get("/admin/reference-data"));
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
    }
}
