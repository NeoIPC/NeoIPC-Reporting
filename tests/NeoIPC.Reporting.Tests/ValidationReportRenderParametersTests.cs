using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The parameters of a Validation Report request, followed to the arguments
/// Quarto receives: the rules the caller names, the uploaded exception file
/// and the day it was uploaded, and the server-side DHIS2 parameters, by
/// which the report reads its data from the service's own DHIS2 address and
/// links its patients to the address the users reach DHIS2 at.
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

    string[] QuartoArguments(string? publicBaseUrl, int[]? rules = null, bool? applyValidationExceptions = null)
    {
        var apiParameters = new ValidationReportApiParameters
        {
            SessionId = "test-session",
            AcceptHeaders = [],
            AcceptLanguageHeaders = [],
            Rules = rules,
            ApplyValidationExceptions = applyValidationExceptions,
        };
        var renderParameters = ValidationReport.ResolveRenderParameters(apiParameters, Storage(),
            Dhis2Endpoint.Build("http://dhis2-backend:8080", publicBaseUrl));
        return [.. ValidationReportQuartoArgumentBuilder.Build(renderParameters)];
    }

    [Test]
    public void TheCallerParameters_AreTheOnesTheReportOffers()
    {
        // The exception file, the day it was uploaded, and the dhis2* params
        // are server-side; GET /validation-report/parameters serves this schema.
        var byName = ValidationReportApiParameters.Schema.ToDictionary(f => f.Name, f => f.Type);

        Assert.That(byName, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["departmentFilter"] = "character[]",
            ["rules"] = "integer[]",
            ["includeTestData"] = "logical",
            ["applyValidationExceptions"] = "logical",
            ["includeUnusedValidationExceptions"] = "logical",
        }));
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
            locale: null, departmentFilter: [], rules: [25, 3, 25], includeTestData: null,
            applyValidationExceptions: null, includeUnusedValidationExceptions: null);

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
            locale: null, departmentFilter: [], rules: [], includeTestData: null,
            applyValidationExceptions: null, includeUnusedValidationExceptions: null);

        Assert.Multiple(() =>
        {
            Assert.That(apiParameters.Rules, Is.Null);
            Assert.That(apiParameters.DepartmentFilter, Is.Null);
        });
    }

    // A stored file as an upload leaves it: the data file, and the sidecar
    // recording the moment of the upload, with fractions of a second.
    ValidationExceptionStorage StoredFile()
    {
        var storage = Storage();
        File.WriteAllText(storage.DataPath(), "RULE_ID,NEOIPC_PATIENT_ID\n");
        File.WriteAllText(storage.MetaPath(),
            """{"displayName":"List","contentType":"text/csv","sizeBytes":26,"createdAt":"2026-09-30T08:15:42.1234567+00:00"}""");
        return storage;
    }

    // Applied when the caller leaves the switch out, and when it sends it on,
    // as the app does on every request.
    [TestCase(null)]
    [TestCase(true)]
    public void AnUploadedExceptionFile_ReachesTheRender_WithTheMomentOfItsUpload(bool? applyValidationExceptions)
    {
        var storage = StoredFile();

        var arguments = QuartoArguments(null, applyValidationExceptions: applyValidationExceptions);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain($"validationExceptionFile:'{storage.DataPath()}'"));
            // In whole seconds and UTC, the form the report reads.
            Assert.That(arguments, Does.Contain("validationExceptionFileUploadedAt:'2026-09-30T08:15:42Z'"));
            if (applyValidationExceptions == true)
                Assert.That(arguments, Does.Contain("applyValidationExceptions:true"));
        });
    }

    [Test]
    public void AnExceptionFileSwitchedOff_IsNotPassed_ButTheReportLearnsItIsStored()
    {
        StoredFile();

        var arguments = QuartoArguments(null, applyValidationExceptions: false);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Has.None.StartsWith("validationExceptionFile:"));
            Assert.That(arguments, Does.Contain("validationExceptionFileUploadedAt:'2026-09-30T08:15:42Z'"));
            Assert.That(arguments, Does.Contain("applyValidationExceptions:false"));
        });
    }

    [Test]
    public void AnExceptionFileWhoseSidecarCannotBeRead_IsStillApplied_DatedByTheDataFile()
    {
        // The sidecar marks the upload as present, as one written by hand may;
        // the data file is what the render reads.
        var storage = Storage();
        File.WriteAllText(storage.MetaPath(), "{}");
        File.WriteAllText(storage.DataPath(), "RULE_ID,NEOIPC_PATIENT_ID\n");
        File.SetLastWriteTimeUtc(storage.DataPath(), new DateTime(2026, 9, 1, 6, 30, 0, DateTimeKind.Utc));

        var arguments = QuartoArguments(null);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain($"validationExceptionFile:'{storage.DataPath()}'"));
            Assert.That(arguments, Does.Contain("validationExceptionFileUploadedAt:'2026-09-01T06:30:00Z'"));
        });
    }

    [Test]
    public void WithoutAnUploadedExceptionFile_TheRenderGetsNone()
    {
        var arguments = QuartoArguments(null);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Has.None.StartsWith("validationExceptionFile:"));
            Assert.That(arguments, Has.None.StartsWith("validationExceptionFileUploadedAt:"));
        });
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
