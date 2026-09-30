using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The Validation Report's rule catalogue, read from a fixture toolkit tree:
/// the English source alone, a translation overlaid per key with English for
/// what it lacks, the refusals of a source the report could not render from
/// either, and the re-read when either file changes.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationRuleCatalogueTests
{
    string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "neoipc-catalogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Validation-Report", "content"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    ValidationRuleCatalogue Catalogue() =>
        new(Options.Create(new ReportingOptions { ReportsSourceDir = _root }));

    void WriteStrings(string directory, string yaml)
    {
        var path = Path.Combine(_root, "Validation-Report", directory, "_sR.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml.Replace("\r\n", "\n"));
    }

    const string English = """
        title: Validation Report
        problems:
          "25":
            description: The enrolment {x} has no admission form.
            summary: An enrolment without an admission form.
          "3":
            description: The admission date differs.
            summary: no
          "17":
            summary: Overlapping enrolments of one patient.
        """;

    [Test]
    public void Rules_ReadsEveryEnglishSummaryInIdOrder_AndKeepsAYamlBooleanWordAsText()
    {
        WriteStrings("content", English);

        var rules = Catalogue().Rules("en");

        Assert.That(rules.Select(r => r.Id), Is.EqualTo(new[] { 3, 17, 25 }));
        Assert.That(rules.Select(r => r.Summary), Is.EqualTo(new[]
        {
            "no",
            "Overlapping enrolments of one patient.",
            "An enrolment without an admission form.",
        }));
    }

    [Test]
    public void Rules_OverlaysATranslationPerKey_AndKeepsEnglishForWhatItLacks()
    {
        WriteStrings("content", English);
        WriteStrings("content.de", """
            problems:
              "25":
                summary: Eine Aufnahme ohne Aufnahmeformular.
              "17":
                description: Nur die Beschreibung ist übersetzt.
              "99":
                summary: Eine Regel, die es im Englischen nicht gibt.
            """);

        var rules = Catalogue().Rules("de").ToDictionary(r => r.Id, r => r.Summary);

        Assert.That(rules.Keys, Is.EquivalentTo(new[] { 3, 17, 25 }), "a translation cannot add a rule");
        Assert.That(rules[25], Is.EqualTo("Eine Aufnahme ohne Aufnahmeformular."));
        Assert.That(rules[17], Is.EqualTo("Overlapping enrolments of one patient."));
        Assert.That(rules[3], Is.EqualTo("no"));
    }

    [Test]
    public void Rules_WithoutATranslation_IsEnglish()
    {
        WriteStrings("content", English);

        Assert.That(Catalogue().Rules("it").Select(r => r.Summary),
            Is.EqualTo(Catalogue().Rules("en").Select(r => r.Summary)));
    }

    [Test]
    public void Rules_RefusesAnEnglishRuleWithoutASummary()
    {
        WriteStrings("content", """
            problems:
              "3":
                description: The admission date differs.
            """);

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains("rule 3 has no 'summary'"));
    }

    [TestCase("\"\"",    "rule 3 has an empty 'summary'")]
    [TestCase("\"   \"", "rule 3 has a blank 'summary'")]
    [TestCase("[a, b]",  "rule 3 has a 'summary' that is not text")]
    public void Rules_RefusesAnEnglishRuleWithoutASummaryToShow(string summary, string refusal)
    {
        WriteStrings("content", $"""
            problems:
              "3":
                summary: {summary}
            """);

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains(refusal));
    }

    // The report's cascade takes a translation's summary whatever it holds,
    // and fails the render in that language unless it is one non-empty string.
    [TestCase("\"\"",   "rule 25 has an empty 'summary'")]
    [TestCase("[a, b]", "rule 25 has a 'summary' that is not text")]
    public void Rules_RefusesATranslationWhoseSummaryTheReportRefuses(string summary, string refusal)
    {
        WriteStrings("content", English);
        WriteStrings("content.de", $"problems:\n  \"25\":\n    summary: {summary}\n");

        Assert.That(() => Catalogue().Rules("de"),
            Throws.InvalidOperationException.With.Message.Contains(refusal));
    }

    [Test]
    public void Rules_KeepsEnglishForABlankTranslatedSummary()
    {
        WriteStrings("content", English);
        WriteStrings("content.de", "problems:\n  \"25\":\n    summary: \"   \"\n");

        Assert.That(Catalogue().Rules("de").Single(r => r.Id == 25).Summary,
            Is.EqualTo("An enrolment without an admission form."));
    }

    // Plain scalars the report's YAML reader (R's yaml package with the
    // reports' string-resource handlers) reads as a null, a logical or a
    // number, which fails the render.
    [TestCase("",            Description = "empty: null")]
    [TestCase("~",           Description = "null")]
    [TestCase("null",        Description = "null")]
    [TestCase("NULL",        Description = "null")]
    [TestCase("true",        Description = "logical")]
    [TestCase("False",       Description = "logical")]
    [TestCase(".na",         Description = "a missing logical")]
    [TestCase("42",          Description = "an integer")]
    [TestCase("-7",          Description = "an integer")]
    [TestCase("017",         Description = "an octal integer")]
    [TestCase("0x1F",        Description = "a hexadecimal integer")]
    [TestCase("1,000",       Description = "an integer the comma makes missing")]
    [TestCase(".na.integer", Description = "a missing integer")]
    [TestCase("1.5",         Description = "a double")]
    [TestCase(".5",          Description = "a double")]
    [TestCase("1.0e+3",      Description = "a double with an exponent")]
    [TestCase(".inf",        Description = "infinity")]
    [TestCase("-.Inf",       Description = "negative infinity")]
    [TestCase(".NaN",        Description = "not a number")]
    [TestCase(".na.real",    Description = "a missing double")]
    public void Rules_RefusesAPlainSummaryTheReportDoesNotReadAsText(string summary)
    {
        WriteStrings("content", $"problems:\n  \"3\":\n    summary: {summary}\n");

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains("rule 3 has an unquoted 'summary'"));
    }

    // The same words quoted, and plain words the report reads as text:
    // YAML 1.1 booleans other than true and false, timestamps, sexagesimal
    // numbers, underscores in numbers, and the 0o, 0b and 0X prefixes.
    [TestCase("\"~\"",         "~")]
    [TestCase("'true'",        "true")]
    [TestCase("\"42\"",        "42")]
    [TestCase("'.inf'",        ".inf")]
    [TestCase("\"1.5\"",       "1.5")]
    [TestCase("yes",           "yes")]
    [TestCase("Off",           "Off")]
    [TestCase("y",             "y")]
    [TestCase("nULL",          "nULL")]
    [TestCase("2026-09-30",    "2026-09-30")]
    [TestCase("1:20",          "1:20")]
    [TestCase("1_000",         "1_000")]
    [TestCase("0o17",          "0o17")]
    [TestCase("0b101",         "0b101")]
    [TestCase("0X1F",          "0X1F")]
    [TestCase("1e3",           "1e3")]
    [TestCase("Infinity",      "Infinity")]
    public void Rules_ReadsASummaryTheReportReadsAsText(string summary, string text)
    {
        WriteStrings("content", $"problems:\n  \"3\":\n    summary: {summary}\n");

        Assert.That(Catalogue().Rules("en").Single().Summary, Is.EqualTo(text));
    }

    [Test]
    public void Rules_RefusesATranslationWithAPlainSummaryTheReportDoesNotReadAsText()
    {
        // The report's cascade takes the null for the translation's value and
        // drops the English summary, which fails its render in that language.
        WriteStrings("content", English);
        WriteStrings("content.de", "problems:\n  \"25\":\n    summary: null\n");

        Assert.Multiple(() =>
        {
            Assert.That(() => Catalogue().Rules("de"),
                Throws.InvalidOperationException.With.Message.Contains("rule 25 has an unquoted 'summary'"));
            Assert.That(Catalogue().Rules("en"), Has.Length.EqualTo(3), "English does not read the translation");
        });
    }

    [Test]
    public void Rules_ReadAnOverlayThatIsGone_AsAbsent()
    {
        // A link to nothing exists when the overlay is looked at and is gone
        // when it is read, as an overlay deleted between the two would be.
        WriteStrings("content", English);
        var overlay = Path.Combine(_root, "Validation-Report", "content.de", "_sR.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
        try
        {
            File.CreateSymbolicLink(overlay, Path.Combine(_root, "no-such-file.yaml"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"This platform does not let the test create a symbolic link: {e.Message}");
        }

        Assert.That(Catalogue().Rules("de").Select(r => r.Summary),
            Is.EqualTo(Catalogue().Rules("en").Select(r => r.Summary)));
    }

    [Test]
    public void Rules_RefusesARuleKeyThatIsNotAnId()
    {
        WriteStrings("content", """
            problems:
              three:
                summary: Not a rule id.
            """);

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains("is not a rule id"));
    }

    [Test]
    public void Rules_RefusesAFileYamlCannotParse_AsMalformed()
    {
        WriteStrings("content", "problems:\n  \"3\":\n    summary: a: b: c\n");

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains("malformed"));
    }

    [Test]
    public void Rules_RefusesAMissingSource()
    {
        Assert.That(() => Catalogue().Rules("en"), Throws.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void Rules_ReadsTheSourceAgainWhenItChanges()
    {
        WriteStrings("content", English);
        var catalogue = Catalogue();
        Assert.That(catalogue.Ids, Is.EqualTo(new[] { 3, 17, 25 }));

        WriteStrings("content", English + "\n  \"43\":\n    summary: An enrolment left open.\n");
        File.SetLastWriteTimeUtc(
            Path.Combine(_root, "Validation-Report", "content", "_sR.yaml"), DateTime.UtcNow.AddMinutes(1));

        Assert.That(catalogue.Ids, Is.EqualTo(new[] { 3, 17, 25, 43 }));
    }

    [Test]
    public void Rules_ReadsTheTranslationAgainWhenItChanges()
    {
        WriteStrings("content", English);
        WriteStrings("content.de", """
            problems:
              "25":
                summary: Eine Aufnahme ohne Aufnahmeformular.
            """);
        var catalogue = Catalogue();
        Assert.That(catalogue.Rules("de").Single(r => r.Id == 25).Summary,
            Is.EqualTo("Eine Aufnahme ohne Aufnahmeformular."));

        WriteStrings("content.de", """
            problems:
              "25":
                summary: Eine Einschreibung ohne Aufnahmeformular.
            """);
        File.SetLastWriteTimeUtc(
            Path.Combine(_root, "Validation-Report", "content.de", "_sR.yaml"), DateTime.UtcNow.AddMinutes(1));

        Assert.That(catalogue.Rules("de").Single(r => r.Id == 25).Summary,
            Is.EqualTo("Eine Einschreibung ohne Aufnahmeformular."));
    }
}
