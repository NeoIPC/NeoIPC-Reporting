using System.Net;
using System.Net.Http.Headers;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Shared helper for the end-to-end integration tests that run against a
/// <b>live, already-running</b> NeoIPC stack (DHIS2 + neoipc-reporting),
/// brought up and seeded out-of-band — see
/// <c>scripts/Initialize-TestDhis2.ps1</c> in the workspace and
/// <c>tasks/integration-test-environment.md</c>. Unlike
/// <see cref="NegativePathTests"/> / <see cref="ParametersEndpointTests"/>
/// (which spin up the built image in isolation via Testcontainers and use
/// a placeholder session), these tests exercise the real
/// auth-and-render path, so they need a real DHIS2 session.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is environment-driven so the same tests run against a
/// local <c>docker compose</c> stack (default) or any other deployment:
/// </para>
/// <list type="bullet">
///   <item><description><c>NEOIPC_DHIS2_BASE_URL</c> — DHIS2 root for the
///   form login (default <c>http://localhost:8080</c>).</description></item>
///   <item><description><c>NEOIPC_REPORTING_BASE_URL</c> — the reporting
///   API base (default <c>http://localhost:8080/neoipc/api</c>, the Nginx
///   mount the compose stack publishes).</description></item>
///   <item><description><c>NEOIPC_DHIS2_ADMIN_USER</c> /
///   <c>NEOIPC_DHIS2_ADMIN_PASS</c> — DHIS2 credentials (default
///   <c>admin</c> / <c>district</c>).</description></item>
///   <item><description><c>NEOIPC_TEST_DEPARTMENT_CODE</c> — the seeded
///   test department's org-unit code; render tests self-skip when it is
///   unset (i.e. the instance has not been seeded).</description></item>
/// </list>
/// <para>
/// Why a form login and not basic auth: DHIS2's API security is
/// <c>SessionCreationPolicy.NEVER</c>, so <c>curl -u admin:district
/// /api/me</c> authenticates statelessly and yields no usable
/// <c>JSESSIONID</c>. The reporting service authenticates the caller by
/// re-validating their <c>JSESSIONID</c> against DHIS2, so the tests must
/// establish a real session via the same form-login endpoint the browser
/// UI uses (<c>/dhis-web-commons-security/login.action</c>). This mirrors
/// the flow in the workspace's <c>Verify-NeoIpcApp.ps1</c>.
/// </para>
/// </remarks>
public static class ExternalDhis2Fixture
{
    public static string Dhis2BaseUrl =>
        TrimSlash(Environment.GetEnvironmentVariable("NEOIPC_DHIS2_BASE_URL"))
        ?? "http://localhost:8080";

    public static string ReportingBaseUrl =>
        TrimSlash(Environment.GetEnvironmentVariable("NEOIPC_REPORTING_BASE_URL"))
        ?? "http://localhost:8080/neoipc/api";

    public static string AdminUser =>
        Environment.GetEnvironmentVariable("NEOIPC_DHIS2_ADMIN_USER") ?? "admin";

    public static string AdminPass =>
        Environment.GetEnvironmentVariable("NEOIPC_DHIS2_ADMIN_PASS") ?? "district";

    /// <summary>The seeded test department's org-unit code, or null when unseeded.</summary>
    public static string? TestDepartmentCode =>
        Environment.GetEnvironmentVariable("NEOIPC_TEST_DEPARTMENT_CODE");

    /// <summary>
    /// Probes the reporting service via its anonymous
    /// <c>/partner-report/parameters</c> endpoint. Returns false (rather
    /// than throwing) when the stack is unreachable, so fixtures can
    /// <c>Assert.Ignore</c> with a helpful message instead of failing.
    /// </summary>
    public static async Task<bool> IsReportingUpAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await client.GetAsync(
                $"{ReportingBaseUrl}/partner-report/parameters", ct);
            return resp.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Performs the DHIS2 form login and returns the resulting
    /// authenticated <c>JSESSIONID</c>, or null when the login did not
    /// establish a session (wrong credentials or DHIS2 unreachable).
    /// </summary>
    public static async Task<string?> LoginAsync(CancellationToken ct = default)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(Dhis2BaseUrl),
            Timeout = TimeSpan.FromSeconds(15),
        };

        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("j_username", AdminUser),
            new KeyValuePair<string, string>("j_password", AdminPass),
        });
        using var resp = await client.PostAsync(
            "/dhis-web-commons-security/login.action", form, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var session = cookies.GetCookies(new Uri(Dhis2BaseUrl))["JSESSIONID"]?.Value;
        return string.IsNullOrEmpty(session) ? null : session;
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> targeting the reporting API with
    /// the given <paramref name="jsessionId"/> replayed as the
    /// <c>JSESSIONID</c> cookie (set explicitly rather than via a cookie
    /// container so it is sent regardless of cookie path scoping).
    /// </summary>
    public static HttpClient CreateReportingClient(string jsessionId)
    {
        var client = new HttpClient { BaseAddress = new Uri(ReportingBaseUrl + "/") };
        client.DefaultRequestHeaders.Add("Cookie", $"JSESSIONID={jsessionId}");
        return client;
    }

    static string? TrimSlash(string? url) =>
        string.IsNullOrEmpty(url) ? null : url.TrimEnd('/');
}
