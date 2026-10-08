using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class Dhis2EndpointTests
{
    // The service's address where a test is about the public one: an IPv4
    // address (from the range reserved for documentation) is checked for
    // loopback without the DNS lookup a host name costs, which can take
    // seconds on a machine that cannot resolve it.
    const string ServiceAddress = "http://192.0.2.1:8080";

    [TestCase("http://dhis2-backend:8080",      "http",  "dhis2-backend", 8080, "/",     "/api")]
    [TestCase("http://dhis2-backend:8080/",     "http",  "dhis2-backend", 8080, "/",     "/api")]
    [TestCase("http://dhis2-backend:8080/dhis", "http",  "dhis2-backend", 8080, "/dhis", "/dhis/api")]
    [TestCase("https://example.org/x/y/",       "https", "example.org",    443, "/x/y/", "/x/y/api")]
    public void Build_ParsesAndDerivesApiPath(
        string baseUrl, string scheme, string host, int port, string path, string apiPath)
    {
        var ep = Dhis2Endpoint.Build(baseUrl);
        Assert.Multiple(() =>
        {
            Assert.That(ep.Scheme,  Is.EqualTo(scheme));
            Assert.That(ep.Host,    Is.EqualTo(host));
            Assert.That(ep.Port,    Is.EqualTo(port));
            Assert.That(ep.Path,    Is.EqualTo(path));
            Assert.That(ep.ApiPath, Is.EqualTo(apiPath));
        });
    }

    [TestCase("",                       Description = "empty")]
    [TestCase("not-a-url",              Description = "not absolute")]
    [TestCase("ftp://dhis2-backend:21", Description = "non-http scheme")]
    [TestCase("http://user:pw@dhis2-backend", Description = "userinfo present")]
    [TestCase("http://localhost:8080",  Description = "loopback hostname")]
    [TestCase("http://127.0.0.1:8080",  Description = "loopback IP")]
    [TestCase("http://0.0.0.0:8080",    Description = "unspecified IP")]
    public void Build_RejectsInvalidOrUnsafeUrls(string baseUrl)
    {
        Assert.That(() => Dhis2Endpoint.Build(baseUrl), Throws.InvalidOperationException);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Build_WithoutAPublicBaseUrl_LinksToTheServicesAddress(string? publicBaseUrl)
    {
        var ep = Dhis2Endpoint.Build("http://dhis2-backend:8080/dhis", publicBaseUrl);
        Assert.That(ep.PublicBaseUri, Is.EqualTo(new Uri("http://dhis2-backend:8080/dhis")));
    }

    // The Validation Report holds the base it is given to the same shape, so
    // these are the shapes its own table accepts, with the base .NET passes on.
    [TestCase("https://neoipc.example.org",            "https://neoipc.example.org/",            Description = "a plain host")]
    [TestCase("http://neoipc.example.org:8080",        "http://neoipc.example.org:8080/",        Description = "a port")]
    [TestCase("https://neoipc.example.org/dhis",       "https://neoipc.example.org/dhis",        Description = "a context path")]
    [TestCase("https://neoipc.example.org/dhis/",      "https://neoipc.example.org/dhis/",       Description = "a trailing slash")]
    [TestCase("HTTPS://neoipc.example.org/dhis",       "https://neoipc.example.org/dhis",        Description = "an upper-case scheme")]
    [TestCase("http://dhis2_web:8080/",                "http://dhis2_web:8080/",                 Description = "an underscore in the host")]
    [TestCase("http://192.0.2.10:8080/dhis",           "http://192.0.2.10:8080/dhis",            Description = "an IPv4 address")]
    [TestCase("https://neoipc.example.org/dhis%20prod", "https://neoipc.example.org/dhis%20prod", Description = "a percent-encoded path segment")]
    [TestCase("http://localhost:8080",                 "http://localhost:8080/",                 Description = "a local stack's loopback host")]
    [TestCase("https://xn--bcher-kva.example/",        "https://xn--bcher-kva.example/",         Description = "a host in its xn-- form")]
    public void Build_TakesThePublicBaseUrl_ApartFromTheServicesAddress(string publicBaseUrl, string expected)
    {
        var ep = Dhis2Endpoint.Build(ServiceAddress, publicBaseUrl);
        Assert.Multiple(() =>
        {
            Assert.That(ep.PublicBaseUri.AbsoluteUri, Is.EqualTo(expected));
            Assert.That(ep.Host, Is.EqualTo("192.0.2.1"), "the service still reads from its own address");
        });
    }

    // One row per defect, the credential-bearing values among them, each with
    // the secret it carries: every refusal names the setting and repeats
    // neither the value nor that secret.
    [TestCase("https://neoipc.example.org/dhis prod",       LinkBaseDefect.WhitespaceOrControl, null,       Description = "a space")]
    [TestCase(" https://neoipc.example.org/",               LinkBaseDefect.WhitespaceOrControl, null,       Description = "leading whitespace")]
    [TestCase("https://neoipc.example.org/\tdhis",          LinkBaseDefect.WhitespaceOrControl, null,       Description = "a control character")]
    [TestCase("https://admin:s3 cret@neoipc.example.org/",  LinkBaseDefect.WhitespaceOrControl, "s3 cret",  Description = "a password with a space")]
    [TestCase("not-a-url",                                  LinkBaseDefect.Scheme,              null,       Description = "not absolute")]
    [TestCase("ftp://neoipc.example.org",                   LinkBaseDefect.Scheme,              null,       Description = "an ftp scheme")]
    [TestCase("ftp://user:secret@neoipc.example.org/",      LinkBaseDefect.Scheme,              "secret",   Description = "an ftp scheme with a password")]
    [TestCase("https:/neoipc.example.org/dhis",             LinkBaseDefect.Scheme,              null,       Description = "a single-slash authority")]
    [TestCase("https://user:pw@neoipc.example.org",         LinkBaseDefect.AtSign,              "user:pw",  Description = "userinfo")]
    [TestCase("https://@neoipc.example.org",                LinkBaseDefect.AtSign,              null,       Description = "empty userinfo")]
    [TestCase("https://admin:S3cret#1@neoipc.example.org/", LinkBaseDefect.AtSign,              "S3cret#1", Description = "a password with '#'")]
    [TestCase("https://admin:s3/cret@neoipc.example.org/",  LinkBaseDefect.AtSign,              "s3/cret",  Description = "a password with '/'")]
    [TestCase("https://admin:s3?cret@neoipc.example.org/",  LinkBaseDefect.AtSign,              "s3?cret",  Description = "a password with '?'")]
    [TestCase("https://neoipc.example.org/a@b",             LinkBaseDefect.AtSign,              null,       Description = "an '@' in the path")]
    [TestCase("https://neoipc.example.org/?",               LinkBaseDefect.QueryOrFragment,     null,       Description = "an empty query")]
    [TestCase("https://neoipc.example.org/#",               LinkBaseDefect.QueryOrFragment,     null,       Description = "an empty fragment")]
    [TestCase("https://neoipc.example.org/?token=s3cret",   LinkBaseDefect.QueryOrFragment,     "s3cret",   Description = "a query")]
    [TestCase("https://neoipc.example.org/#top",            LinkBaseDefect.QueryOrFragment,     Description = "a fragment")]
    [TestCase("http://[::1]:8080/",                         LinkBaseDefect.BracketedHost,       Description = "an IPv6 literal")]
    [TestCase("https://bücher.example/",                    LinkBaseDefect.HostOutsideAscii,    Description = "a host outside ASCII")]
    [TestCase("https://neoipc(1).example.org/",             LinkBaseDefect.Host,                Description = "a parenthesis in the host")]
    [TestCase("https://neoipc..example.org/",               LinkBaseDefect.Host,                Description = "an empty label")]
    [TestCase("https:///dhis",                              LinkBaseDefect.Host,                Description = "no host")]
    [TestCase("https://neoipc.example.org:0/",              LinkBaseDefect.Port,                Description = "port 0")]
    [TestCase("https://neoipc.example.org:65536/",          LinkBaseDefect.Port,                Description = "a port out of range")]
    [TestCase("https://neoipc.example.org:/",               LinkBaseDefect.Port,                Description = "an empty port")]
    [TestCase("https://neoipc.example.org:http/",           LinkBaseDefect.Port,                Description = "a port that is not a number")]
    [TestCase("https://neoipc.example.org/a)b",             LinkBaseDefect.Path,                Description = "a closing parenthesis")]
    [TestCase("https://neoipc.example.org/a(b",             LinkBaseDefect.Path,                Description = "an opening parenthesis")]
    [TestCase("https://neoipc.example.org/<dhis>",          LinkBaseDefect.Path,                Description = "angle brackets")]
    [TestCase("https://neoipc.example.org/dh\"is",          LinkBaseDefect.Path,                Description = "a quote")]
    [TestCase("https://neoipc.example.org/dh\\is",          LinkBaseDefect.Path,                Description = "a backslash")]
    [TestCase("https://neoipc.example.org/dh|is",           LinkBaseDefect.Path,                Description = "a vertical bar")]
    [TestCase("https://neoipc.example.org/{dhis}",          LinkBaseDefect.Path,                Description = "braces")]
    [TestCase("https://neoipc.example.org/dh^is",           LinkBaseDefect.Path,                Description = "a caret")]
    [TestCase("https://neoipc.example.org/dh`is",           LinkBaseDefect.Path,                Description = "a backtick")]
    [TestCase("https://neoipc.example.org/dh%zzis",         LinkBaseDefect.Path,                Description = "a malformed escape")]
    [TestCase("https://neoipc.example.org/bücher",          LinkBaseDefect.Path,                Description = "a path outside ASCII")]
    public void Build_RejectsAnUnusablePublicBaseUrl_WithoutRepeatingIt(
        string publicBaseUrl, LinkBaseDefect defect, string? secret = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Dhis2Endpoint.LinkBaseDefectOf(publicBaseUrl), Is.EqualTo(defect));
            var refusal = Assert.Throws<InvalidOperationException>(
                () => Dhis2Endpoint.Build(ServiceAddress, publicBaseUrl));
            Assert.That(refusal?.Message, Does.Contain("Reporting:Dhis2PublicBaseUrl")
                .And.Not.Contain(publicBaseUrl.Trim()));
            if (secret is not null)
                Assert.That(refusal?.Message, Does.Not.Contain(secret), "the secret the value carries");
        });
    }

    [Test]
    public void Build_RefusingAHostOutsideAscii_NamesItsXnForm()
    {
        Assert.That(() => Dhis2Endpoint.Build(ServiceAddress, "https://bücher.example/dhis"),
            Throws.InvalidOperationException.With.Message.Contains("'xn--bcher-kva.example'")
                .And.Message.Not.Contain("bücher"));
    }

    // Every refusal of the service's own address, the credential-bearing
    // values among them, each with the secret it carries: the message names
    // the setting and repeats neither the value nor that secret.
    [TestCase("https://admin:s3/cret@dhis2-backend/",   "is not a valid absolute URL", "s3/cret",   Description = "a password with '/'")]
    [TestCase("ftp://user:s3cret@dhis2-backend/",       "must use http or https",      "s3cret",    Description = "a non-http scheme with a password")]
    [TestCase("http://admin:s3cret@dhis2-backend:8080", "must not contain userinfo",   "s3cret",    Description = "userinfo")]
    [TestCase("http://localhost:8080/s3cret",           "resolves to a loopback",      "s3cret",    Description = "a loopback host")]
    [TestCase("http://192.0.2.1:8080/s3cret(1)",        "cannot serve as the base",    "s3cret(1)", Description = "no public address, and a path the links cannot carry")]
    public void Build_RefusingTheServicesAddress_DoesNotRepeatIt(string baseUrl, string refusal, string secret)
    {
        Assert.That(() => Dhis2Endpoint.Build(baseUrl),
            Throws.InvalidOperationException
                .With.Message.Contains("Reporting:Dhis2BaseUrl")
                .And.Message.Contains(refusal)
                .And.Message.Not.Contain(baseUrl)
                .And.Message.Not.Contain(secret));
    }

    [Test]
    public void Build_WithoutAPublicBaseUrl_LeavesOutTheQueryAndFragmentOfTheServicesAddress()
    {
        var ep = Dhis2Endpoint.Build("http://dhis2-backend:8080/dhis?x=1#top");
        Assert.That(ep.PublicBaseUri.AbsoluteUri, Is.EqualTo("http://dhis2-backend:8080/dhis"));
    }

    // Without a public address the links take the service's own, host as
    // written, so that address is held to the public one's shape and a
    // refusal says which setting to give instead.
    [TestCase("http://bücher-internal:8080/dhis", LinkBaseDefect.HostOutsideAscii, Description = "a host outside ASCII")]
    [TestCase("http://[fd00::1]:8080/dhis",       LinkBaseDefect.BracketedHost,    Description = "an IPv6 literal")]
    [TestCase("http://192.0.2.1:8080/a(b)",       LinkBaseDefect.Path,             Description = "parentheses in the path")]
    [TestCase("http://192.0.2.1:0/dhis",          LinkBaseDefect.Port,             Description = "port 0")]
    [TestCase("http://192.0.2.1:8080/a@b",        LinkBaseDefect.AtSign,           Description = "an '@' in the path")]
    public void Build_WithoutAPublicBaseUrl_RefusesAServicesAddressTheLinksCannotCarry(string baseUrl, LinkBaseDefect defect)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Dhis2Endpoint.LinkBaseDefectOf(new UriBuilder(new Uri(baseUrl)) { Query = "", Fragment = "" }.Uri.AbsoluteUri),
                Is.EqualTo(defect));
            Assert.That(() => Dhis2Endpoint.Build(baseUrl),
                Throws.InvalidOperationException
                    .With.Message.Contains("Reporting:Dhis2BaseUrl cannot serve as the base of the reports' links")
                    .And.Message.Contains("Set Reporting:Dhis2PublicBaseUrl")
                    .And.Message.Not.Contain(baseUrl));
            Assert.That(Dhis2Endpoint.Build(baseUrl, "https://neoipc.example.org/dhis").PublicBaseUri.AbsoluteUri,
                Is.EqualTo("https://neoipc.example.org/dhis"), "a public address makes the service's own usable");
        });
    }

    [Test]
    public void FromOptions_TakesThePublicBaseUrlFromTheConfiguration()
    {
        var ep = Dhis2Endpoint.FromOptions(new ReportingOptions
        {
            Dhis2BaseUrl = ServiceAddress,
            Dhis2PublicBaseUrl = "https://neoipc.example.org/dhis",
        });

        Assert.Multiple(() =>
        {
            Assert.That(ep.PublicBaseUri, Is.EqualTo(new Uri("https://neoipc.example.org/dhis")));
            Assert.That(ep.BaseUri, Is.EqualTo(new Uri(ServiceAddress)));
        });
    }

    [TestCase("https://neoipc.example.org/dhis", true)]
    [TestCase(null, false)]
    [TestCase("   ", false)]
    public void Build_RecordsWhetherThePublicBaseUrlIsConfigured(string? publicBaseUrl, bool configured)
    {
        Assert.That(Dhis2Endpoint.Build(ServiceAddress, publicBaseUrl).PublicBaseUriConfigured, Is.EqualTo(configured));
    }

    [Test]
    public void TheAdminEndpoint_ServesTheLinksBase_AndWhetherItIsConfigured()
    {
        var result = Dhis2PublicBaseUrlEndpoint.AdminGet(Dhis2Endpoint.Build(ServiceAddress));

        Assert.That(result, Is.InstanceOf<Ok<AdminDhis2PublicBaseUrl>>());
        var body = ((Ok<AdminDhis2PublicBaseUrl>)result).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(body, Is.EqualTo(new AdminDhis2PublicBaseUrl(ServiceAddress + "/", false)));
            // The member names the app reads, as the service's JSON options write them.
            Assert.That(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Is.EqualTo($$"""{"publicBaseUrl":"{{ServiceAddress}}/","configured":false}"""));
        });
    }

    [Test]
    public void FromOptions_RefusesAnUnusablePublicBaseUrlFromTheConfiguration()
    {
        Assert.That(() => Dhis2Endpoint.FromOptions(new ReportingOptions
            {
                Dhis2BaseUrl = ServiceAddress,
                Dhis2PublicBaseUrl = "https://neoipc.example.org/dhis?x=1",
            }),
            Throws.InvalidOperationException.With.Message.Contains("Reporting:Dhis2PublicBaseUrl"));
    }
}
