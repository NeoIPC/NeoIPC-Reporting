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

    [TestCase("\"\"")]
    [TestCase("\"   \"")]
    public void Rules_RefusesAnEnglishRuleWithAnEmptySummary(string summary)
    {
        WriteStrings("content", $"""
            problems:
              "3":
                summary: {summary}
            """);

        Assert.That(() => Catalogue().Rules("en"),
            Throws.InvalidOperationException.With.Message.Contains("rule 3 has an empty 'summary'"));
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
