using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class InputValidationTests
{
    [Test]
    public void RejectUnsafeStrings_PassesThroughCleanInput()
    {
        var result = InputValidation.RejectUnsafeStrings(
            ("locale", "de-DE"),
            ("profile", "full"),
            ("validationExceptionFile", "0123456789abcdef0123456789abcdef"));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RejectUnsafeStrings_PassesThroughNulls()
    {
        var result = InputValidation.RejectUnsafeStrings(
            ("locale", null),
            ("profile", null));
        Assert.That(result, Is.Null);
    }

    // The TestCase rows below cover what's expressible inline. Skipped:
    // U+0085 (NEL), U+2028 (line separator), U+2029 (paragraph separator)
    // — the C# spec classifies all three as source-line terminators, so
    // they cannot appear as literal char in a "..." literal. They ARE
    // rejected by ContainsUnsafeChar (the explicit hex-int comparison
    // covers them); inline-test coverage of those code points would need
    // a resource-file fixture rather than TestCase rows.
    //
    // The non-printable bytes (BEL, ESC, DEL) are injected post-build as
    // raw UTF-8 bytes via tooling — C#'s "\x" escape is greedy
    // (1-4 hex digits) so "\x1bescape" parses as U+01BE + "scape", not
    // ESC + "escape", which makes "\x" unsafe for these test cases.
    [TestCase("plain\ntext",   Description = "newline")]
    [TestCase("plain\rtext",   Description = "carriage return")]
    [TestCase("plain\r\ntext", Description = "CRLF")]
    [TestCase("plain\0text",   Description = "embedded NUL")]
    [TestCase("bell",     Description = "bell char (BEL, U+0007)")]
    [TestCase("escape",   Description = "ESC byte (U+001B)")]
    [TestCase("del",      Description = "DEL (U+007F)")]
    public void RejectUnsafeStrings_RejectsControlChars(string value)
    {
        var result = InputValidation.RejectUnsafeStrings(("locale", value));
        Assert.That(result, Is.Not.Null,
            "value containing control char must produce a 400 ProblemDetails");
    }

    [Test]
    public void RejectUnsafeStrings_AllowsTab()
    {
        var result = InputValidation.RejectUnsafeStrings(("displayName", "a\tb"));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RejectUnsafeStrings_FirstOffenderWins()
    {
        var result = InputValidation.RejectUnsafeStrings(
            ("first",  "ok"),
            ("second", "bad\n"),
            ("third",  "also\nbad"));
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void RejectUnsafeStringArray_NullArrayPasses()
    {
        Assert.That(InputValidation.RejectUnsafeStringArray("countryFilter", null), Is.Null);
    }

    [Test]
    public void RejectUnsafeStringArray_RejectsAnyElement()
    {
        var result = InputValidation.RejectUnsafeStringArray("countryFilter",
            ["DE", "GB\n", "ES"]);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void RejectUnsafeStringArray_PassesAllCleanElements()
    {
        var result = InputValidation.RejectUnsafeStringArray("countryFilter",
            ["DE", "GB", "ES"]);
        Assert.That(result, Is.Null);
    }
}
