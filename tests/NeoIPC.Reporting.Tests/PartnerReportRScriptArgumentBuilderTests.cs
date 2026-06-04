using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class PartnerReportRScriptArgumentBuilderTests
{
    static string[] Build(PartnerReportRenderParameters p, string? outputFilePath = null) =>
        PartnerReportRScriptArgumentBuilder.Build(p, outputFilePath).ToArray();

    [Test]
    public void OutputFilePath_EmittedFirstAsFileFlag()
    {
        var args = Build(new PartnerReportRenderParameters(), outputFilePath: "/tmp/out.json");
        Assert.That(args[..2], Is.EqualTo(new[] { "--file", "/tmp/out.json" }));
    }

    [Test]
    public void UnitCodes_JoinedByComma()
    {
        var args = Build(new PartnerReportRenderParameters
        {
            UnitCodes = ["U-001", "U-002", "U-003"],
        });

        Assert.That(args, Is.EqualTo(new[] { "--unitCodes", "U-001,U-002,U-003" }));
    }

    [Test]
    public void IncludeNonCorePatients_True_EmitsPresenceFlag()
    {
        var args = Build(new PartnerReportRenderParameters { IncludeNonCorePatients = true });
        Assert.That(args, Is.EqualTo(new[] { "--includeNonCorePatients" }));
    }

    [Test]
    public void IncludeNonCorePatients_FalseOrNull_DoesNotEmit()
    {
        Assert.That(Build(new PartnerReportRenderParameters { IncludeNonCorePatients = false }), Is.Empty);
        Assert.That(Build(new PartnerReportRenderParameters { IncludeNonCorePatients = null }), Is.Empty);
    }

    [Test]
    public void IncludeTestData_True_EmitsPresenceFlag()
    {
        var args = Build(new PartnerReportRenderParameters { IncludeTestData = true });
        Assert.That(args, Is.EqualTo(new[] { "--includeTestData" }));
    }

    [Test]
    public void Dhis2EndpointFields_EmittedAsHostNotHostname()
    {
        // The R script's long_map maps "host" -> "hostname"; pass --host.
        var args = Build(new PartnerReportRenderParameters
        {
            Dhis2Scheme = "http",
            Dhis2Hostname = "dhis2.local",
            Dhis2Port = 8080,
            Dhis2Path = "/api",
        });

        Assert.That(args, Is.EqualTo(new[]
        {
            "--scheme", "http",
            "--host",   "dhis2.local",
            "--port",   "8080",
            "--path",   "/api",
        }));
    }
}
