using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The handler of <c>GET /validation-report/rules</c>, called over a fixture
/// toolkit tree: the language its summaries come in, and the 500 it answers
/// when the report's string resources cannot be read, whose detail must not
/// carry the server path the exception names.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationRulesEndpointTests
{
    string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "neoipc-rules-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    IResult Rules(string? locale, params string[] servedLanguages)
    {
        var registry = new ReportLanguageRegistry();
        registry.Set(QuartoValidationReportProducer.ReportName,
            servedLanguages.ToDictionary(language => language, language => $"Validation-Report.{language}.qmd"));
        return ReportConfigEndpoints.ValidationRules(locale,
            new ValidationRuleCatalogue(Options.Create(new ReportingOptions { ReportsSourceDir = _root })),
            registry, NullLoggerFactory.Instance);
    }

    void WriteStrings(string directory, string yaml)
    {
        var path = Path.Combine(_root, "Validation-Report", directory, "_sR.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml.Replace("\r\n", "\n"));
    }

    static string?[] Summaries(IResult result)
    {
        Assert.That(result, Is.InstanceOf<IValueHttpResult>());
        var body = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
        return [.. body.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("summary").GetString())];
    }

    [TestCase(null, TestName = "ValidationRules_WhenTheSourceIsMissing_Is500WithoutTheServerPath")]
    [TestCase("problems:\n  \"3\":\n    summary: a: b: c\n",
        TestName = "ValidationRules_WhenTheSourceIsMalformed_Is500WithoutTheServerPath")]
    public void ValidationRules_WhenTheCatalogueCannotBeRead_Is500WithoutTheServerPath(string? englishSource)
    {
        if (englishSource is not null) WriteStrings("content", englishSource);

        var result = Rules(null, "en");

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(problem.ProblemDetails.Title, Is.EqualTo("Rule catalogue unavailable"));
            Assert.That(problem.ProblemDetails.Detail, Does.Not.Contain(_root));
            Assert.That(problem.ProblemDetails.Detail, Does.Not.Contain("_sR.yaml"));
        });
    }

    // What reading the string resources can throw besides a missing or
    // malformed file: each is logged and answered with the same path-free 500.
    static IEnumerable<TestCaseData> UnreadableSources()
    {
        const string path = "/toolkit/reports/Validation-Report/content/_sR.yaml";
        yield return new TestCaseData(new UnauthorizedAccessException($"Access to the path '{path}' is denied."))
            .SetName("TryReadRuleCatalogue_WhenTheSourceCannotBeOpened_Is500WithoutTheServerPath");
        yield return new TestCaseData(new IOException($"Input/output error : '{path}'"))
            .SetName("TryReadRuleCatalogue_WhenReadingTheSourceFails_Is500WithoutTheServerPath");
        yield return new TestCaseData(new DirectoryNotFoundException($"Could not find a part of the path '{path}'."))
            .SetName("TryReadRuleCatalogue_WhenTheSourceDirectoryIsGone_Is500WithoutTheServerPath");
    }

    [TestCaseSource(nameof(UnreadableSources))]
    public void TryReadRuleCatalogue_WhenTheSourceCannotBeRead_Is500WithoutTheServerPath(Exception failure)
    {
        var logger = new RecordingLogger();

        var read = ReportConfigEndpoints.TryReadRuleCatalogue<int>(() => throw failure, logger, out _, out var result);

        Assert.That(read, Is.False);
        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result!;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(problem.ProblemDetails.Title, Is.EqualTo("Rule catalogue unavailable"));
            Assert.That(problem.ProblemDetails.Detail, Does.Not.Contain("/toolkit"));
            Assert.That(logger.Errors, Is.EqualTo(new[] { failure }), "the exception, and its path, go to the log");
        });
    }

    [Test]
    public void ValidationRules_WithoutALocale_IsEnglish_AndWithOne_IsThatTranslation()
    {
        WriteStrings("content", """
            problems:
              "25":
                summary: An enrolment without an admission form.
            """);
        WriteStrings("content.de", """
            problems:
              "25":
                summary: Eine Aufnahme ohne Aufnahmeformular.
            """);

        Assert.Multiple(() =>
        {
            Assert.That(Summaries(Rules(null, "en", "de")),
                Is.EqualTo(new[] { "An enrolment without an admission form." }));
            Assert.That(Summaries(Rules("de", "en", "de")),
                Is.EqualTo(new[] { "Eine Aufnahme ohne Aufnahmeformular." }));
        });
    }

    [Test]
    public void ValidationRules_ForALocaleTheReportDoesNotServe_Is400WithItsCode()
    {
        WriteStrings("content", """
            problems:
              "25":
                summary: An enrolment without an admission form.
            """);

        var result = Rules("it", "en");

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.UnsupportedLocale));
        });
    }

    /// <summary>Records the exceptions logged at error level.</summary>
    sealed class RecordingLogger : ILogger
    {
        public List<Exception?> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) Errors.Add(exception);
        }
    }
}
