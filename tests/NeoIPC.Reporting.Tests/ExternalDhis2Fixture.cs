using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Shared helper for the end-to-end integration tests that run against a
/// <b>live, already-running</b> NeoIPC stack (DHIS2 + neoipc-reporting),
/// brought up and seeded out-of-band with NeoIPC metadata and synthetic
/// data. Unlike
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
/// Why a real credential login and not basic auth: the reporting service
/// authenticates the caller by re-validating their <c>JSESSIONID</c> against
/// DHIS2, so an auth-bearing session cookie is mandatory. (Basic auth is not
/// session-free — a basic-auth GET mints an authenticated session on every
/// version via <c>HttpSessionSecurityContextRepository</c> — but only a real
/// login authenticates.) The login endpoint is version-dependent: DHIS2
/// 2.41+ authenticate JSON at <c>POST /api/auth/login</c>, 2.40 uses the legacy
/// Struts form <c>POST /dhis-web-commons-security/login.action</c> — see
/// <see cref="LoginAsync"/>.
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
    /// Establishes a real DHIS2 session and returns its authenticated
    /// <c>JSESSIONID</c>, or null when no session could be authenticated
    /// (wrong credentials or DHIS2 unreachable).
    /// </summary>
    /// <remarks>
    /// The login endpoint is version-dependent (see the type remarks): the
    /// modern JSON <c>POST /api/auth/login</c> (2.41+) is tried first with the
    /// SPA CSRF handshake — prime a readable <c>XSRF-TOKEN</c> cookie, echo it
    /// as <c>X-XSRF-TOKEN</c> (a no-op when CSRF is off) — falling back to the
    /// legacy 2.40 form. Redirects are NOT followed, so an unauthenticated
    /// session (which 302-redirects to the login page) is not mistaken for
    /// success; the session is proven by <c>/api/me</c> returning 200.
    /// </remarks>
    public static async Task<string?> LoginAsync(CancellationToken ct = default)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(Dhis2BaseUrl),
            Timeout = TimeSpan.FromSeconds(15),
        };

        if (!await ModernLoginAuthenticatesAsync(client, cookies, ct))
            await LegacyFormLoginAsync(client, ct);

        // Proof of authentication: /api/me must be 200 (an unauthenticated
        // session 302-redirects to the login page; redirects are not followed).
        using var me = await client.GetAsync("/api/me", ct);
        if (me.StatusCode != HttpStatusCode.OK) return null;

        var session = cookies.GetCookies(new Uri(Dhis2BaseUrl))["JSESSIONID"]?.Value;
        return string.IsNullOrEmpty(session) ? null : session;
    }

    /// <summary>
    /// Attempts the modern JSON login (<c>POST /api/auth/login</c>, DHIS2 2.41+)
    /// with the CSRF handshake, then confirms it authenticated via
    /// <c>/api/me</c>. Returns false when the endpoint is absent (404 on 2.40)
    /// or the login did not take, so the caller falls back to the legacy form.
    /// </summary>
    static async Task<bool> ModernLoginAuthenticatesAsync(
        HttpClient client, CookieContainer cookies, CancellationToken ct)
    {
        // Prime the CSRF token: DHIS2's CsrfCookieFilter sets a readable
        // XSRF-TOKEN cookie on any request when CSRF is on (a no-op when off).
        // Anonymous, NOT basic auth — a basic-auth GET mints an authenticated
        // session on every DHIS2 version (the /api basic-auth filter uses
        // HttpSessionSecurityContextRepository) and would mask a failed login.
        using (await client.GetAsync("/api/me", ct)) { }
        var xsrf = cookies.GetCookies(new Uri(Dhis2BaseUrl))["XSRF-TOKEN"]?.Value;

        var json = JsonSerializer.Serialize(new { username = AdminUser, password = AdminPass });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(xsrf)) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        using var resp = await client.SendAsync(req, ct);
        if (resp.StatusCode != HttpStatusCode.OK) return false;

        using var me = await client.GetAsync("/api/me", ct);
        return me.StatusCode == HttpStatusCode.OK;
    }

    /// <summary>Legacy DHIS2 2.40 Struts form login (superseded by <c>/api/auth/login</c> at 2.41+).</summary>
    static async Task LegacyFormLoginAsync(HttpClient client, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("j_username", AdminUser),
            new KeyValuePair<string, string>("j_password", AdminPass),
        });
        // The handler's CookieContainer captures the JSESSIONID from the login
        // response (the 302 carries it — redirects are not followed); the body
        // is not needed.
        using (await client.PostAsync("/dhis-web-commons-security/login.action", form, ct)) { }
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
