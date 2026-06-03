using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NeoIPC.Reporting.Authorization;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper that resolves a DHIS2
/// <c>JSESSIONID</c> cookie to the user's authorities and group memberships
/// by calling <c>&lt;Dhis2BaseUrl&gt;/api/me</c>.
/// </summary>
/// <remarks>
/// Registered via <c>AddHttpClient&lt;Dhis2SessionClient&gt;</c> with a
/// <see cref="SocketsHttpHandler"/> configured <c>AllowAutoRedirect = false</c>
/// (a 302 to the DHIS2 login page indicates an invalid/expired session and
/// must not be followed) and <c>UseCookies = false</c> (the session cookie
/// is set explicitly per request, not by a shared cookie container).
/// </remarks>
public sealed class Dhis2SessionClient
{
    readonly HttpClient _http;
    readonly Dhis2Endpoint _endpoint;
    readonly ILogger<Dhis2SessionClient> _logger;

    public Dhis2SessionClient(HttpClient http, Dhis2Endpoint endpoint, ILogger<Dhis2SessionClient> logger)
    {
        _http = http;
        _endpoint = endpoint;
        _logger = logger;
    }

    /// <summary>
    /// Looks up the user owning <paramref name="sessionId"/>, or returns
    /// <c>null</c> when the session is invalid/expired or the upstream call
    /// fails. Network and parse errors are logged and surface as a null
    /// result, which the auth handler treats as a failed authentication.
    /// </summary>
    public async Task<Dhis2UserInfo?> GetUserInfoAsync(string sessionId, CancellationToken ct)
    {
        var basePath = _endpoint.Path.TrimEnd('/');
        var requestUri = new UriBuilder(_endpoint.BaseUri)
        {
            Path = basePath + "/api/me",
            Query = "fields=id,username,authorities,userGroups[id,name]",
        }.Uri;

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        req.Headers.Add("Cookie", "JSESSIONID=" + sessionId);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "DHIS2 /api/me request failed.");
            return null;
        }

        try
        {
            // 401/403 = session is bad. Any redirect status = DHIS2 wants to
            // bounce us to the login page; treat as a bad session too. Don't
            // follow — `AllowAutoRedirect = false` on the handler means the
            // 3xx responses surface here verbatim.
            if (res.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Found
                or HttpStatusCode.MovedPermanently
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
                return null;

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("DHIS2 /api/me returned status {Status}.", (int)res.StatusCode);
                return null;
            }

            return await res.Content.ReadFromJsonAsync<Dhis2UserInfo>(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to parse DHIS2 /api/me response.");
            return null;
        }
        finally
        {
            res.Dispose();
        }
    }
}

/// <summary>Shape of the DHIS2 <c>/api/me</c> response we consume.</summary>
public sealed record Dhis2UserInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("authorities")] string[]? Authorities,
    [property: JsonPropertyName("userGroups")] Dhis2UserGroup[]? UserGroups);

/// <summary>One entry of <c>userGroups[id,name]</c>.</summary>
public sealed record Dhis2UserGroup(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
