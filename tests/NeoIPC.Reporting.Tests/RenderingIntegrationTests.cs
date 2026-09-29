using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// End-to-end integration tests against a live, seeded NeoIPC stack
/// (DHIS2 + neoipc-reporting). See <see cref="ExternalDhis2Fixture"/> for
/// the environment contract. These tests require the stack to be brought
/// up and the DHIS2 instance seeded with NeoIPC metadata and synthetic
/// data out-of-band.
/// </summary>
/// <remarks>
/// The whole fixture self-skips (<see cref="Assert.Ignore(string)"/>) when
/// the reporting service is not reachable or a DHIS2 session cannot be
/// established, so <c>dotnet test --filter Category=Integration</c> is safe
/// to run with no stack up — it reports "ignored", not "failed". The
/// render test additionally skips unless the instance holds the test
/// department (<c>AT_TEST_TEST</c>, or the code
/// <c>NEOIPC_TEST_DEPARTMENT_CODE</c> names).
/// </remarks>
[TestFixture]
[Category("Integration")]
public class RenderingIntegrationTests
{
    string _session = null!;

    [OneTimeSetUp]
    public async Task EstablishSession()
    {
        if (!await ExternalDhis2Fixture.IsReportingUpAsync())
            Assert.Ignore(
                $"Reporting service not reachable at {ExternalDhis2Fixture.ReportingBaseUrl}. " +
                "Bring the stack up and seed the DHIS2 instance with NeoIPC metadata + " +
                "synthetic data before running Category=Integration.");

        var session = await ExternalDhis2Fixture.LoginAsync();
        if (session is null)
            Assert.Ignore(
                $"Could not establish a DHIS2 session at {ExternalDhis2Fixture.Dhis2BaseUrl} " +
                $"as '{ExternalDhis2Fixture.AdminUser}'. Is DHIS2 up with the expected credentials?");
        _session = session;
    }

    [Test]
    public async Task ReferenceData_Authenticated_Returns200()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("reference-data");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "an authenticated user with F_NEOIPC_REPORT (admin has ALL) must list reference data");
    }

    [Test]
    public async Task PartnerReportPresets_Returns200_WithDefaultPreset()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("partner-report/presets");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object),
            "the presets endpoint returns the 'presets' object (name -> overrides)");
        Assert.That(doc.RootElement.TryGetProperty("default", out _), Is.True,
            "every report defines a 'default' (empty-override) preset");
    }

    [Test]
    public async Task PartnerReportLocales_Returns200_ContainsEn()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("partner-report/locales");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        var locales = doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.That(locales, Does.Contain("en"),
            "the Partner Report ships an English master QMD");
    }

    [Test]
    public async Task PartnerReport_Online_Pdf_RendersForSeededDepartment()
    {
        var department = ExternalDhis2Fixture.TestDepartmentCode;
        var (exists, problem) = await ExternalDhis2Fixture.LookupOrgUnitAsync(_session, department);
        if (problem is not null)
            Assert.Ignore(
                $"Could not look up organisation unit '{department}' at " +
                $"{ExternalDhis2Fixture.Dhis2BaseUrl}: {problem}.");
        if (!exists)
            Assert.Ignore(
                $"No organisation unit with code '{department}' at {ExternalDhis2Fixture.Dhis2BaseUrl}. " +
                "Seed the instance with the play package, or set NEOIPC_TEST_DEPARTMENT_CODE to a " +
                "seeded test department's code.");

        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        client.Timeout = TimeSpan.FromMinutes(10); // live import + R/Quarto render

        // A department inside the TEST_UNITS group is excluded by the report's
        // default (include_test_data = false), so it would resolve to zero org
        // units and the DHIS2 query would 409. Requesting test data admits such a
        // department and changes nothing for a regular one like the default
        // AT_TEST_TEST, so the render works whichever kind the code names.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"partner-report?unitCodes={Uri.EscapeDataString(department)}&includeTestData=true");
        request.Headers.Add("Accept", "application/pdf");
        request.Headers.Add("Accept-Language", "en");

        using var resp = await client.SendAsync(request);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the online Partner Report must render for the seeded department");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.That(bytes.Length, Is.GreaterThan(1000), "the PDF must not be empty");
        Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"),
            "the response body must be a PDF");
    }

    [Test]
    public async Task ValidationReportLocales_Returns200_ContainsEn()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("validation-report/locales");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var locales = doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.That(locales, Does.Contain("en"), "the Validation Report ships an English master QMD");
    }

    [Test]
    public async Task ValidationReportRules_Returns200_WithTheCatalogueInIdOrder()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("validation-report/rules?locale=en");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var rules = doc.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => (Id: r.GetProperty("id").GetInt32(), Summary: r.GetProperty("summary").GetString()))
            .ToArray();
        var ids = rules.Select(r => r.Id).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Ordered.Ascending);
            Assert.That(ids, Does.Contain(1).And.Contain(25));
            Assert.That(ids, Does.Not.Contain(16), "rule 16 is withdrawn");
            Assert.That(rules.Select(r => r.Summary), Has.All.Not.Empty);
        });
    }

    [Test]
    public async Task ValidationReportRules_UnsupportedLocale_Returns400()
    {
        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        using var resp = await client.GetAsync("validation-report/rules?locale=xx");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain(ProblemCodes.UnsupportedLocale));
        });
    }

    [Test]
    public async Task ValidationReport_Online_Html_RendersTheRulesItApplies()
    {
        var department = ExternalDhis2Fixture.TestDepartmentCode;
        var (exists, problem) = await ExternalDhis2Fixture.LookupOrgUnitAsync(_session, department);
        if (problem is not null)
            Assert.Ignore(
                $"Could not look up organisation unit '{department}' at " +
                $"{ExternalDhis2Fixture.Dhis2BaseUrl}: {problem}.");
        if (!exists)
            Assert.Ignore(
                $"No organisation unit with code '{department}' at {ExternalDhis2Fixture.Dhis2BaseUrl}. " +
                "Seed the instance with the play package, or set NEOIPC_TEST_DEPARTMENT_CODE to a " +
                "seeded test department's code.");

        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        client.Timeout = TimeSpan.FromMinutes(10); // live import + R/Quarto render

        // Rule 25 alone: the header names the rules not applied, which proves the
        // `rules` parameter reached the report.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"validation-report?departmentFilter={Uri.EscapeDataString(department)}" +
            "&includeTestData=true&rules=25&fragmentMode=false");
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", "en");

        using var resp = await client.SendAsync(request);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "the Validation Report must render for the seeded department");
            Assert.That(html, Does.Contain("1 of "), "the header states the rules applied");
            Assert.That(html, Does.Contain("the rules not applied are"));
        });
    }
}
