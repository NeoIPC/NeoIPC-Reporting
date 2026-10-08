using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// How <see cref="ValidationExceptionChecker"/> reads the check script's exit
/// status: 0 accepts the file, the refusal status refuses it with the reason
/// the script printed, and any other status, or a refusal without a reason,
/// is a failure of the check rather than a verdict on the file.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationExceptionCheckerTests
{
    [Test]
    public void ExitStatus0_AcceptsTheFile()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationExceptionChecker.TryReadOutcome(0, "", out var reason), Is.True);
            Assert.That(reason, Is.Null);
        });
    }

    [Test]
    public void TheRefusalStatus_RefusesTheFile_WithTheReasonAsPrinted()
    {
        var read = ValidationExceptionChecker.TryReadOutcome(
            ValidationExceptionChecker.RefusedExitCode,
            "The validation exception file \"List\" does not hold exception records.\r\n  Missing column.\r\n",
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(reason, Is.EqualTo(
                "The validation exception file \"List\" does not hold exception records.\n  Missing column."));
        });
    }

    // R exits with 1 on an uncaught error, and with 2 on a fatal error of its
    // own start-up, whose message may arrive on stdout.
    [TestCase(1, "Error: object 'x' not found")]
    [TestCase(2, "Fatal error: cannot open file 'scripts/check-validation-exceptions.R': No such file or directory")]
    [TestCase(137, "")]
    public void AnyOtherStatus_IsAFailureOfTheCheck(int exitCode, string stdout)
    {
        Assert.That(ValidationExceptionChecker.TryReadOutcome(exitCode, stdout, out _), Is.False);
    }

    [TestCase("")]
    [TestCase(" \r\n")]
    public void ARefusalWithoutAReason_IsAFailureOfTheCheck(string stdout)
    {
        Assert.That(
            ValidationExceptionChecker.TryReadOutcome(ValidationExceptionChecker.RefusedExitCode, stdout, out _),
            Is.False);
    }
}
