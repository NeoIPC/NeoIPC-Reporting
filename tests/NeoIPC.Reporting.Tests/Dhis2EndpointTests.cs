using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class Dhis2EndpointTests
{
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

    [TestCase("http://localhost:8080",           "http://localhost:8080/",           Description = "a local stack's loopback host")]
    [TestCase("https://neoipc.example.org/dhis", "https://neoipc.example.org/dhis", Description = "a context path")]
    [TestCase("http://[::1]:8080",               "http://[::1]:8080/",               Description = "an IPv6 literal")]
    [TestCase("https://xn--bcher-kva.example/",  "https://xn--bcher-kva.example/",  Description = "a host in its xn-- form")]
    public void Build_TakesThePublicBaseUrl_ApartFromTheServicesAddress(string publicBaseUrl, string expected)
    {
        var ep = Dhis2Endpoint.Build("http://dhis2-backend:8080", publicBaseUrl);
        Assert.Multiple(() =>
        {
            Assert.That(ep.PublicBaseUri.AbsoluteUri, Is.EqualTo(expected));
            Assert.That(ep.Host, Is.EqualTo("dhis2-backend"), "the service still reads from its own address");
        });
    }

    [TestCase("not-a-url",                          Description = "not absolute")]
    [TestCase("ftp://neoipc.example.org",           Description = "non-http scheme")]
    [TestCase("https://user:pw@neoipc.example.org", Description = "userinfo present")]
    [TestCase("https://@neoipc.example.org",        Description = "empty userinfo")]
    [TestCase("https://neoipc.example.org/?x=1",    Description = "query present")]
    [TestCase("https://neoipc.example.org/#top",    Description = "fragment present")]
    [TestCase("https://bücher.example/",            Description = "host outside ASCII")]
    public void Build_RejectsAnUnusablePublicBaseUrl(string publicBaseUrl)
    {
        Assert.That(() => Dhis2Endpoint.Build("http://dhis2-backend:8080", publicBaseUrl),
            Throws.InvalidOperationException.With.Message.Contains("Reporting:Dhis2PublicBaseUrl"));
    }

    [Test]
    public void Build_RefusingUserinfo_DoesNotRepeatTheCredentials()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Dhis2Endpoint.Build("http://admin:s3cret@dhis2-backend:8080"),
                Throws.InvalidOperationException.With.Message.Not.Contain("s3cret"));
            Assert.That(() => Dhis2Endpoint.Build("http://dhis2-backend:8080", "https://admin:s3cret@neoipc.example.org"),
                Throws.InvalidOperationException.With.Message.Not.Contain("s3cret"));
        });
    }

    [Test]
    public void Build_WithoutAPublicBaseUrl_LeavesOutTheQueryAndFragmentOfTheServicesAddress()
    {
        var ep = Dhis2Endpoint.Build("http://dhis2-backend:8080/dhis?x=1#top");
        Assert.That(ep.PublicBaseUri.AbsoluteUri, Is.EqualTo("http://dhis2-backend:8080/dhis"));
    }
}
