using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// End-to-end integration tests against a live, seeded NeoIPC stack
/// (DHIS2 + neoipc-reporting). See <see cref="ExternalDhis2Fixture"/> for
/// the environment contract and <c>tasks/integration-test-environment.md</c>
/// for how to bring the stack up and seed it
/// (<c>scripts/Initialize-TestDhis2.ps1</c>).
/// </summary>
/// <remarks>
/// The whole fixture self-skips (<see cref="Assert.Ignore(string)"/>) when
/// the reporting service is not reachable or a DHIS2 session cannot be
/// established, so <c>dotnet test --filter Category=Integration</c> is safe
/// to run with no stack up — it reports "ignored", not "failed". The
/// render test additionally skips unless the instance has been seeded
/// (<c>NEOIPC_TEST_DEPARTMENT_CODE</c> set).
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
                "Bring the stack up (scripts/Verify-NeoIpcApp.ps1) and seed it " +
                "(scripts/Initialize-TestDhis2.ps1) before running Category=Integration.");

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
        if (string.IsNullOrEmpty(department))
            Assert.Ignore(
                "NEOIPC_TEST_DEPARTMENT_CODE is not set — the instance has not been seeded. " +
                "Run scripts/Initialize-TestDhis2.ps1 to import metadata + synthetic data.");

        using var client = ExternalDhis2Fixture.CreateReportingClient(_session);
        client.Timeout = TimeSpan.FromMinutes(10); // live import + R/Quarto render

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"partner-report?unitCodes={Uri.EscapeDataString(department!)}");
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
}
