using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The parameters of a Validation Report request, followed to the arguments
/// Quarto receives: the rules the caller names, the uploaded exception file,
/// and the server-side DHIS2 parameters, by which the report reads its data
/// from the service's own DHIS2 address and links its patients to the
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

    ValidationExceptionStorage Storage() =>
        new(Options.Create(new ReportingOptions { ValidationExceptionsDir = _exceptionsDir }));

    string[] QuartoArguments(string? publicBaseUrl, int[]? rules = null)
    {
        var apiParameters = new ValidationReportApiParameters
        {
            SessionId = "test-session",
            AcceptHeaders = [],
            AcceptLanguageHeaders = [],
            Rules = rules,
        };
        var renderParameters = ValidationReport.ResolveRenderParameters(apiParameters, Storage(),
            Dhis2Endpoint.Build("http://dhis2-backend:8080", publicBaseUrl));
        return [.. ValidationReportQuartoArgumentBuilder.Build(renderParameters)];
    }

    [Test]
    public void TheRules_ReachQuarto_AsAFlowSequenceOfIntegers()
    {
        var arguments = QuartoArguments(null, [3, 25]);

        Assert.That(arguments, Does.Contain("rules:[3,25]"));
    }

    [Test]
    public void TheRequest_CarriesTheRules_DeduplicatedAndAscending()
    {
        var apiParameters = ValidationReport.ApiParameters("test-session", [], [],
            locale: null, departmentFilter: [], rules: [25, 3, 25], includeTestData: null);

        Assert.Multiple(() =>
        {
            Assert.That(apiParameters.Rules, Is.EqualTo(new[] { 3, 25 }));
            Assert.That(apiParameters.MapTo().Rules, Is.EqualTo(new[] { 3, 25 }), "the render parameters carry them");
        });
    }

    [Test]
    public void TheRequest_WithoutRulesOrDepartments_LeavesThemToTheReport()
    {
        var apiParameters = ValidationReport.ApiParameters("test-session", [], [],
            locale: null, departmentFilter: [], rules: [], includeTestData: null);

        Assert.Multiple(() =>
        {
            Assert.That(apiParameters.Rules, Is.Null);
            Assert.That(apiParameters.DepartmentFilter, Is.Null);
        });
    }

    [Test]
    public void AnUploadedExceptionFile_ReachesTheRender()
    {
        // The metadata sidecar marks the upload as present; the data file is
        // what the render reads.
        var storage = Storage();
        File.WriteAllText(storage.MetaPath(), "{}");
        File.WriteAllText(storage.DataPath(), "rule,record\n");

        Assert.That(QuartoArguments(null), Does.Contain($"validationExceptionFile:'{storage.DataPath()}'"));
    }

    [Test]
    public void WithoutAnUploadedExceptionFile_TheRenderGetsNone()
    {
        Assert.That(QuartoArguments(null), Has.None.StartsWith("validationExceptionFile:"));
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
