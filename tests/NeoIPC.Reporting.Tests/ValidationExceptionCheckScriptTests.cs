using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// <c>scripts/check-validation-exceptions.R</c> run inside the built image
/// as <see cref="ValidationExceptionChecker"/> runs it, with the neoipcr the
/// renders load there: the check the upload endpoint relies on accepts a list
/// the reports can apply, and refuses one they cannot with its exit status
/// and a reason that names the file by its display name rather than its
/// staged path.
/// </summary>
/// <remarks>
/// Runs the image <see cref="SmokeTestImage"/> resolves, as
/// <see cref="ImageFontTests"/> does.
/// </remarks>
[TestFixture]
[Category("Container")]
public class ValidationExceptionCheckScriptTests
{
    const string StagedPath = "/tmp/staging-check.tmp";

    const string Header = "RULE_ID,DEPARTMENT_CODE,NEOIPC_PATIENT_ID,ENROLMENT_DATE,EVENT_TYPE,EVENT_DATE\n";

    IContainer? _container;

    readonly List<string> _command = [];

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        var imageTag = await SmokeTestImage.ResolveAsync();

        // Skip rather than fail when there is no Docker to talk to; see
        // NegativePathTests.StartContainer for why the try spans Build().
        try
        {
            _container = new ContainerBuilder(imageTag).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            Assert.Ignore($"Category=Container tests need a running Docker daemon. {ex.Message}");
        }

        // The environment ValidationExceptionChecker gives the script: its
        // locale, and in a workspace image the neoipcr checkout, which the
        // service names to its R processes rather than the image to every
        // process, since a workspace image installs no neoipcr package.
        var locale = RScriptReportProducer.DefaultLocale;
        _command.AddRange(
            ["env", $"LANGUAGE={locale.Language}", $"LANG={locale.LcAll}", $"LC_ALL={locale.LcAll}"]);
        var buildMode = await _container!.ExecAsync(["printenv", "Reporting__BuildMode"]);
        if (buildMode.Stdout.Trim() == nameof(BuildMode.Workspace))
            _command.Add($"NEOIPCR_DEV_PATH={new ReportingOptions().NeoIpcrDevPath}");
        _command.AddRange(["Rscript", "--vanilla", "/app/scripts/check-validation-exceptions.R"]);
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    Task<ExecResult> Check(string csv) => Check(Encoding.UTF8.GetBytes(csv));

    async Task<ExecResult> Check(byte[] content)
    {
        // Readable by the service's own user, which runs the script.
        await _container!.CopyAsync(content, StagedPath, fileMode: Unix.FileMode644);
        return await _container.ExecAsync([.. _command, "--in", StagedPath, "--name", "Uploaded list"]);
    }

    [Test]
    public async Task AListWhoseRecordsNameTheirDepartment_IsAccepted()
    {
        var result = await Check(Header + "3,AT_TEST_TEST,PAT_1,2024-01-01,,\n");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
    }

    [Test]
    public async Task AListWithoutTheDepartments_IsRefused()
    {
        var result = await Check(
            "RULE_ID,NEOIPC_PATIENT_ID,ENROLMENT_DATE,EVENT_TYPE,EVENT_DATE\n"
            + "3,PAT_1,2024-01-01,,\n");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(ValidationExceptionChecker.RefusedExitCode), result.Stderr);
            Assert.That(result.Stdout, Does.Contain("\"Uploaded list\"").And.Contain("DEPARTMENT_CODE"));
        });
    }

    [Test]
    public async Task AFileTheReaderRefuses_IsRefusedWithItsReason_NamedByItsDisplayName()
    {
        var result = await Check("RULE_ID,PATIENT\n3,PAT_1\n");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(ValidationExceptionChecker.RefusedExitCode), result.Stderr);
            Assert.That(result.Stdout, Does.Contain("\"Uploaded list\"").And.Contain("NEOIPC_PATIENT_ID"));
            Assert.That(result.Stdout, Does.Not.Contain(StagedPath));
        });
    }

    // A spreadsheet's Unicode text export, and a list another encoding wrote,
    // are refused with a reason rather than failing the check.
    [TestCase("utf-16")]
    [TestCase("latin-1")]
    public async Task AFileThatIsNotUtf8Text_IsRefusedWithItsReason(string encoding)
    {
        var csv = Header + "3,AT_TEST_TEST,PAT_é,2024-01-01,,\n";
        var bytes = encoding == "utf-16"
            ? [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(csv)]
            : Encoding.Latin1.GetBytes(csv);

        var result = await Check(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(ValidationExceptionChecker.RefusedExitCode), result.Stderr);
            Assert.That(result.Stdout, Does.Contain("\"Uploaded list\"").And.Contain("UTF-8"));
        });
    }
}
