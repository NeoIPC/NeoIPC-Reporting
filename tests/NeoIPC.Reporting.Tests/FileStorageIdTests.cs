using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class FileStorageIdTests
{
    [Test]
    public void GenerateId_Produces32LowercaseHexChars()
    {
        for (var i = 0; i < 5; i++)
        {
            var id = FileStorage.GenerateId();
            Assert.That(id, Has.Length.EqualTo(32), "id length");
            Assert.That(id, Does.Match("^[0-9a-f]{32}$"), $"hex shape for {id}");
        }
    }

    [Test]
    public void IsValidId_AcceptsAGeneratedId()
        => Assert.That(FileStorage.IsValidId(FileStorage.GenerateId()), Is.True);

    [TestCase("../../etc/passwd",                 Description = "path traversal")]
    [TestCase("/etc/passwd",                      Description = "absolute path")]
    [TestCase("c:\\windows\\system32",            Description = "windows path")]
    [TestCase("",                                 Description = "empty")]
    [TestCase("0123456789abcdef0123456789abcde",  Description = "31 hex chars")]
    [TestCase("0123456789abcdef0123456789abcdef0", Description = "33 hex chars")]
    [TestCase("0123456789ABCDEF0123456789ABCDEF", Description = "uppercase hex")]
    [TestCase("0123456789abcdef0123456789abcdeg", Description = "non-hex char")]
    [TestCase("0123456789abcdef-123456789abcdef", Description = "dash inside")]
    public void IsValidId_RejectsMalformed(string candidate)
        => Assert.That(FileStorage.IsValidId(candidate), Is.False);
}
