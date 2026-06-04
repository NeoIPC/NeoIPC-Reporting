using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class ReferenceReportRScriptArgumentBuilderTests
{
    static string[] Build(ReferenceReportRenderParameters p) =>
        ReferenceReportRScriptArgumentBuilder.Build(p).ToArray();

    [Test]
    public void EmptyParameters_ProduceNoArgs()
    {
        Assert.That(Build(new ReferenceReportRenderParameters()), Is.Empty);
    }

    [Test]
    public void DateRange_UsesIso8601_AndCamelCaseFlagNames()
    {
        var args = Build(new ReferenceReportRenderParameters
        {
            ReportingPeriodFrom = new DateOnly(2024, 1, 1),
            ReportingPeriodTo = new DateOnly(2024, 12, 31),
        });

        Assert.That(args, Is.EqualTo(new[]
        {
            "--reportingPeriodFrom", "2024-01-01",
            "--reportingPeriodTo",   "2024-12-31",
        }));
    }

    [Test]
    public void Countries_JoinedByComma_UnderCamelCaseFlag()
    {
        var args = Build(new ReferenceReportRenderParameters
        {
            ReportingCountries = ["DE", "GB"],
        });

        Assert.That(args, Is.EqualTo(new[] { "--reportingCountries", "DE,GB" }));
    }

    [Test]
    public void HospitalFilter_IsNotForwarded()
    {
        // Hospital filtering is a Quarto render-time concern; the data-fetch
        // R script doesn't accept --hospitals.
        var args = Build(new ReferenceReportRenderParameters
        {
            HospitalFilter = ["U-001", "U-002"],
        });

        Assert.That(args, Is.Empty);
    }

    [Test]
    public void TestUnitFilter_DefaultTrue_DoesNotEmitIncludeTestUnits()
    {
        var args = Build(new ReferenceReportRenderParameters { TestUnitFilter = true });
        Assert.That(args, Does.Not.Contain("--includeTestUnits"));
    }

    [Test]
    public void TestUnitFilter_False_EmitsIncludeTestUnits()
    {
        var args = Build(new ReferenceReportRenderParameters { TestUnitFilter = false });
        Assert.That(args, Is.EqualTo(new[] { "--includeTestUnits" }));
    }

    [Test]
    public void DefaultPatientFilter_False_EmitsIncludeNonCorePatients()
    {
        var args = Build(new ReferenceReportRenderParameters { DefaultPatientFilter = false });
        Assert.That(args, Is.EqualTo(new[] { "--includeNonCorePatients" }));
    }

    [Test]
    public void ValidationExceptionFile_EmittedAsPath()
    {
        var args = Build(new ReferenceReportRenderParameters
        {
            ValidationExceptionFile = "/some/path/file.csv",
        });

        Assert.That(args, Is.EqualTo(new[] { "--validationExceptionFile", "/some/path/file.csv" }));
    }

    [Test]
    public void Args_ContainNoEmbeddedSpacesOrNewlines()
    {
        // Argv hygiene: each arg should be a single token. Catches any
        // future regression where a flag value picks up whitespace.
        var args = Build(new ReferenceReportRenderParameters
        {
            ReportingPeriodFrom = new DateOnly(2024, 1, 1),
            ReportingCountries = ["DE", "GB"],
            ValidationExceptionFile = "/tmp/foo.csv",
        });

        foreach (var a in args)
        {
            Assert.That(a, Does.Not.Contain(" "), $"arg '{a}' contains a space");
            Assert.That(a, Does.Not.Contain("\n"), $"arg '{a}' contains a newline");
        }
    }
}
