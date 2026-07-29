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
}
