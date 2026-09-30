using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The server-side parameters the Validation Report handler adds to a
/// request, followed to the arguments Quarto receives: the report reads its
/// data from the service's own DHIS2 address and links its patients to the
/// address the users reach DHIS2 at.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationReportRenderParametersTests
{
    string _exceptionsDir = null!;

    [SetUp]
    public void SetUp()
    {
        _exceptionsDir = Path.Combine(Path.GetTempPath(), "neoipc-validation-params-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_exceptionsDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_exceptionsDir, recursive: true);

    string[] QuartoArguments(string? publicBaseUrl)
    {
        var apiParameters = new ValidationReportApiParameters
        {
            SessionId = "test-session",
            AcceptHeaders = [],
            AcceptLanguageHeaders = [],
        };
        var storage = new ValidationExceptionStorage(
            Options.Create(new ReportingOptions { ValidationExceptionsDir = _exceptionsDir }));
        var renderParameters = ValidationReport.ResolveRenderParameters(apiParameters, storage,
            Dhis2Endpoint.Build("http://dhis2-backend:8080", publicBaseUrl));
        return [.. ValidationReportQuartoArgumentBuilder.Build(renderParameters)];
    }

    [Test]
    public void TheLinks_GoToThePublicBaseUrl_WhileTheDataComesFromTheServicesAddress()
    {
        var arguments = QuartoArguments("https://neoipc.example.org/dhis");

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain("dhis2PublicBaseUrl:'https://neoipc.example.org/dhis'"));
            Assert.That(arguments, Does.Contain("dhis2Hostname:'dhis2-backend'"));
            Assert.That(arguments, Does.Contain("dhis2Path:'/api'"));
        });
    }

    [Test]
    public void WithoutAPublicBaseUrl_TheLinksGoToTheServicesAddress()
    {
        Assert.That(QuartoArguments(null), Does.Contain("dhis2PublicBaseUrl:'http://dhis2-backend:8080/'"));
    }
}
