using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class ReportLoggingTests
{
    const string RenderCategory = "NeoIPC.Reporting.Render.Reference-Report";

    readonly List<string> _tempFiles = [];

    [TearDown]
    public void Cleanup()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f))
                File.Delete(f);
        _tempFiles.Clear();
    }

    // ---------------------------------------------------------------- mappings

    [TestCase(LogLevel.Trace, "debug")]
    [TestCase(LogLevel.Debug, "verbose")]
    [TestCase(LogLevel.Information, "normal")]
    [TestCase(LogLevel.Warning, "quiet")]
    [TestCase(LogLevel.Error, "quiet")]
    [TestCase(LogLevel.Critical, "quiet")]
    [TestCase(LogLevel.None, "quiet")]
    public void ToNeoIpcLogLevel_MapsTheDial(LogLevel level, string expected)
        => Assert.That(ReportLogging.ToNeoIpcLogLevel(level), Is.EqualTo(expected));

    [TestCase(LogLevel.Trace, "debug")]
    [TestCase(LogLevel.Debug, "debug")]
    [TestCase(LogLevel.Information, "info")]
    [TestCase(LogLevel.Warning, "info")]   // floored at info: Pandoc severity is only
    [TestCase(LogLevel.Error, "info")]     // recoverable from info-level Quarto records
    [TestCase(LogLevel.Critical, "info")]
    public void ToQuartoLogLevel_FlooredAtInfo(LogLevel level, string expected)
        => Assert.That(ReportLogging.ToQuartoLogLevel(level), Is.EqualTo(expected));

    [TestCase("TRACE", LogLevel.Trace)]
    [TestCase("DEBUG", LogLevel.Debug)]
    [TestCase("INFO", LogLevel.Information)]
    [TestCase("SUCCESS", LogLevel.Information)]
    [TestCase("WARN", LogLevel.Warning)]
    [TestCase("ERROR", LogLevel.Error)]
    [TestCase("FATAL", LogLevel.Critical)]
    [TestCase("something-else", LogLevel.Debug)]
    public void FromRLevel_MapsLoggerLevelNames(string level, LogLevel expected)
        => Assert.That(ReportLogging.FromRLevel(level), Is.EqualTo(expected));

    [TestCase("INFO", LogLevel.Information)]
    [TestCase("WARN", LogLevel.Warning)]      // the real Quarto/@std/log token
    [TestCase("WARNING", LogLevel.Warning)]   // accepted defensive alias
    [TestCase("ERROR", LogLevel.Error)]
    [TestCase("CRITICAL", LogLevel.Critical)]
    [TestCase("DEBUG", LogLevel.Debug)]
    [TestCase(null, LogLevel.Debug)]
    public void FromQuartoLevel_MapsLevelNames(string? levelName, LogLevel expected)
        => Assert.That(ReportLogging.FromQuartoLevel(levelName), Is.EqualTo(expected));

    [TestCase("ERROR", LogLevel.Error)]
    [TestCase("WARNING", LogLevel.Warning)]
    [TestCase("INFO", LogLevel.Information)]
    public void FromPandocLevel_MapsPrefixes(string pandocLevel, LogLevel expected)
        => Assert.That(ReportLogging.FromPandocLevel(pandocLevel), Is.EqualTo(expected));

    [Test]
    public void EffectiveMinLevel_ReturnsLowestEnabledLevel()
    {
        var (info, _) = BuildFactory(LogLevel.Information);
        var (warn, _) = BuildFactory(LogLevel.Warning);
        using (info) using (warn)
            Assert.Multiple(() =>
            {
                Assert.That(ReportLogging.EffectiveMinLevel(info.CreateLogger("c")), Is.EqualTo(LogLevel.Information));
                Assert.That(ReportLogging.EffectiveMinLevel(warn.CreateLogger("c")), Is.EqualTo(LogLevel.Warning));
            });
    }

    // ------------------------------------------------------------- R log drain

    [Test]
    public async Task DrainRLog_RoutesByNamespaceMapsLevelAndPreservesMessage()
    {
        var (factory, entries) = BuildFactory(LogLevel.Trace);
        using var _ = factory;
        var file = WriteLines(
            """{"time":"2026-06-28 10:00:00","level":"DEBUG","ns":"neoipcr","msg":"DHIS2 me: status=200 rows=42"}""",
            """{"time":"2026-06-28 10:00:01","level":"INFO","ns":"report-common","msg":"loaded glossary"}""",
            """{"time":"2026-06-28 10:00:02","level":"WARN","ns":"reference-report","msg":"a report warning"}""");

        await ReportLogDrain.DrainRLogAsync(file, factory, RenderCategory, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(3));

            var neoipcr = Single(entries, $"{RenderCategory}.R.neoipcr");
            Assert.That(neoipcr.Level, Is.EqualTo(LogLevel.Debug));
            // The R message is carried verbatim (the drain passes it as a log
            // argument, never as a format template), and the original wall-clock
            // is preserved alongside it.
            Assert.That(neoipcr.Message, Is.EqualTo("[2026-06-28 10:00:00] DHIS2 me: status=200 rows=42"));

            Assert.That(Single(entries, $"{RenderCategory}.R.common").Level, Is.EqualTo(LogLevel.Information));
            // Any namespace that is neither neoipcr nor report-common is the
            // report's own slug → the report channel.
            Assert.That(Single(entries, $"{RenderCategory}.R.report").Level, Is.EqualTo(LogLevel.Warning));
        });
    }

    [Test]
    public async Task DrainRLog_AbsentFile_IsNoOp()
    {
        var (factory, entries) = BuildFactory(LogLevel.Trace);
        using var _ = factory;
        await ReportLogDrain.DrainRLogAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"),
            factory, RenderCategory, CancellationToken.None);
        Assert.That(entries, Is.Empty);
    }

    // --------------------------------------------------------- Quarto log drain

    [Test]
    public async Task DrainQuartoLog_AtWarning_SuppressesQuartoInfoButKeepsRecoveredPandocSeverity()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        // Real Quarto/@std/log tokens: levelName "WARN" (not "WARNING") and numeric
        // levels INFO=20/WARN=30/ERROR=40. Pandoc's own prefix is the full word
        // "[WARNING]"/"[ERROR]" (Haskell `show Verbosity`), distinct from Quarto's
        // levelName — both are exercised here.
        var file = WriteLines(
            """{"levelName":"INFO","level":20,"msg":"render progress chatter","loggerName":"quarto"}""",
            """{"levelName":"INFO","level":20,"msg":"[WARNING] citation not found","loggerName":"quarto"}""",
            """{"levelName":"INFO","level":20,"msg":"[ERROR] pandoc could not convert","loggerName":"quarto"}""",
            """{"levelName":"WARN","level":30,"msg":"a real quarto warning","loggerName":"quarto"}""",
            """{"levelName":"ERROR","level":40,"msg":"a real quarto error","loggerName":"quarto"}""");

        var result = await ReportLogDrain.DrainQuartoLogAsync(
            file, factory, RenderCategory, exitCode: 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // Every line is captured raw for the failure payload, independent of
            // the emit-time level filtering.
            Assert.That(result.RawRecords, Has.Count.EqualTo(5));
            Assert.That(result.Issue13394Success, Is.False);

            // Quarto's own INFO chatter is suppressed at Warning (its structured
            // levelName drives the per-category filter — no content guessing).
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Information, "render progress chatter"), Is.False);
            // ...but Quarto's real WARNING/ERROR survive.
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Warning, "a real quarto warning"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Error, "a real quarto error"), Is.True);

            // Pandoc severity is recovered from the [LEVEL] prefix and routed to
            // .Pandoc at the recovered level, so it survives the Warning floor.
            Assert.That(HasEntry(entries, $"{RenderCategory}.Pandoc", LogLevel.Error, "[ERROR] pandoc could not convert"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.Pandoc", LogLevel.Warning, "[WARNING] citation not found"), Is.True);
        });
    }

    [Test]
    public async Task DrainQuartoLog_AtInformation_EmitsQuartoInfoChatter()
    {
        var (factory, entries) = BuildFactory(LogLevel.Information);
        using var _ = factory;
        var file = WriteLines(
            """{"levelName":"INFO","level":2,"msg":"render progress chatter","loggerName":"quarto"}""");

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 0, CancellationToken.None);

        Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Information, "render progress chatter"), Is.True);
    }

    [Test]
    public async Task DrainQuartoLog_RecognisesWellKnownIssue13394OnExit1()
    {
        var (factory, _) = BuildFactory(LogLevel.Trace);
        using var f = factory;
        var file = WriteLines(
            """{"levelName":"ERROR","level":8,"msg":"NotFound: No such file or directory (os error 2): rename '/work/-' -> '/work/_output/-'"}""");

        var result = await ReportLogDrain.DrainQuartoLogAsync(
            file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.That(result.Issue13394Success, Is.True);
    }

    [Test]
    public async Task DrainQuartoLog_ToleratesGarbledLine()
    {
        var (factory, entries) = BuildFactory(LogLevel.Trace);
        using var _ = factory;
        var file = WriteLines(
            "this is not json",
            """{"levelName":"ERROR","level":8,"msg":"a real quarto error","loggerName":"quarto"}""");

        var result = await ReportLogDrain.DrainQuartoLogAsync(
            file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The unparseable line is skipped; the valid record still emits.
            Assert.That(result.RawRecords, Has.Count.EqualTo(1));
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Error, "a real quarto error"), Is.True);
        });
    }

    [Test]
    public async Task DrainQuartoLog_MultiLinePandocRecord_RecoversTheMostSevereLevel()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        // Quarto batches Pandoc stderr per chunk, so one INFO record can carry
        // several [LEVEL] lines. A leading [WARNING] must not downgrade a later
        // [ERROR]: the whole record routes to .Pandoc at Error and survives the
        // Warning floor.
        var file = WriteLines(
            """{"levelName":"INFO","level":20,"msg":"[WARNING] citation not found\n[ERROR] pandoc could not convert","loggerName":"quarto"}""");

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(HasEntry(entries, $"{RenderCategory}.Pandoc", LogLevel.Error, "pandoc could not convert"), Is.True);
            // Not downgraded to Warning, and not left on the Quarto channel.
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.Pandoc" && e.Level == LogLevel.Warning), Is.False);
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.Quarto"), Is.False);
        });
    }

    [Test]
    public async Task DrainRLog_PassesMessageVerbatim_NoFormatStringInterpretationOrExtraFields()
    {
        // GDPR/no-body invariant on the .NET side: the drain must pass the R
        // msg strictly as a log argument (never as a message template) and emit
        // nothing beyond the record's own fields. neoipcr guarantees the msg is
        // URL+status+row-count only; here a brace-bearing, surveillance-shaped
        // token proves the .NET side neither widens it nor evaluates it.
        var (factory, entries) = BuildFactory(LogLevel.Trace);
        using var _ = factory;
        const string surveillanceLikeMsg = "DHIS2 events: status=200 {patientId} rows=7 {0} {birthWeight}";
        var file = WriteLines(
            $$"""{"time":"2026-06-28 10:00:00","level":"DEBUG","ns":"neoipcr","msg":{{System.Text.Json.JsonSerializer.Serialize(surveillanceLikeMsg)}}}""");

        await ReportLogDrain.DrainRLogAsync(file, factory, RenderCategory, CancellationToken.None);

        var entry = Single(entries, $"{RenderCategory}.R.neoipcr");
        Assert.Multiple(() =>
        {
            // The braces are emitted literally — not interpreted as a format
            // placeholder (which would throw or substitute) — and the message
            // is exactly the file's msg with only the time prefix added.
            Assert.That(entry.Message, Is.EqualTo($"[2026-06-28 10:00:00] {surveillanceLikeMsg}"));
            Assert.That(entries, Has.Count.EqualTo(1));
        });
    }

    // ------------------------------------------------------------------ helpers

    (ILoggerFactory Factory, List<LogEntry> Entries) BuildFactory(LogLevel minLevel)
    {
        var entries = new List<LogEntry>();
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(minLevel);
            b.AddProvider(new CapturingLoggerProvider(entries));
        });
        return (factory, entries);
    }

    string WriteLines(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"neoipc-drain-test-{Guid.NewGuid():N}.json");
        File.WriteAllLines(path, lines);
        _tempFiles.Add(path);
        return path;
    }

    static LogEntry Single(List<LogEntry> entries, string category)
        => entries.Single(e => e.Category == category);

    static bool HasEntry(List<LogEntry> entries, string category, LogLevel level, string messageSubstring)
        => entries.Any(e => e.Category == category && e.Level == level && e.Message.Contains(messageSubstring));

    sealed record LogEntry(string Category, LogLevel Level, string Message);

    sealed class CapturingLoggerProvider(List<LogEntry> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);
        public void Dispose() { }

        sealed class CapturingLogger(string category, List<LogEntry> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
