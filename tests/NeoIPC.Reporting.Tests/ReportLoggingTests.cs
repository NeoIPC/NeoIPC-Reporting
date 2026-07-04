using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
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
    public async Task DrainQuartoLog_AtInformation_RecoversTheLatexWriteErrorBlockToTheLatexChannel()
    {
        var (factory, entries) = BuildFactory(LogLevel.Information);
        using var _ = factory;
        // Quarto's writeError block for a failed PDF compile (real record shapes):
        // the generic "compilation failed-" at ERROR, then the extracted
        // findLatexError detail at INFO, then the "see …log" pointer at INFO. The
        // whole block is re-attributed to .LaTeX (the detail at Error), keyed on
        // the stable writeError structure rather than the detail's content.
        var file = WriteLines(
            """{"levelName":"INFO","level":20,"msg":"This is LuaHBTeX, Version 1.24.0 (TeX Live 2026)\n","loggerName":"default"}""",
            """{"levelName":"ERROR","level":40,"msg":"\ncompilation failed- error","loggerName":"default"}""",
            """{"levelName":"INFO","level":20,"msg":"Undefined control sequence.\nl.172 \\undefinedControlSequenceProbe\n","loggerName":"default"}""",
            """{"levelName":"INFO","level":20,"msg":"see probe.log for more information.","loggerName":"default"}""");

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The "compilation failed-" primary and the extracted detail both land
            // on .LaTeX at Error; the "see …log" pointer closes the block at Information.
            Assert.That(HasEntry(entries, $"{RenderCategory}.LaTeX", LogLevel.Error, "compilation failed"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.LaTeX", LogLevel.Error, "Undefined control sequence"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.LaTeX", LogLevel.Information, "see probe.log"), Is.True);
            // The benign engine banner (before the block) stays on .Quarto at INFO.
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Information, "LuaHBTeX"), Is.True);
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.LaTeX" && e.Message.Contains("LuaHBTeX")), Is.False);
        });
    }

    [Test]
    public async Task DrainQuartoLog_AtWarning_RecoversALatexDetailThatHasNoLineContext()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        // A LaTeX failure whose findLatexError detail carries NO "l.<n>" marker —
        // Quarto's fixed "No pages of output" alternate, an emergency-stop/output
        // context, or the 1.10 luaotfload-fallback guidance string. Structural
        // detection recovers it anyway, so the cause survives a Warning threshold
        // instead of vanishing at INFO on .Quarto. (A content marker misses these.)
        var file = WriteLines(
            """{"levelName":"ERROR","level":40,"msg":"\ncompilation failed- error","loggerName":"default"}""",
            """{"levelName":"INFO","level":20,"msg":"No pages of output - the document appears to have produced no output.","loggerName":"default"}""",
            """{"levelName":"INFO","level":20,"msg":"see probe.log for more information.","loggerName":"default"}""");

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(HasEntry(entries, $"{RenderCategory}.LaTeX", LogLevel.Error, "No pages of output"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.LaTeX", LogLevel.Error, "compilation failed"), Is.True);
        });
    }

    [Test]
    public async Task DrainQuartoLog_RoutesRedKnitrErrorsToRReportAtErrorButLeavesBenignProgressOnQuarto()
    {
        var (factory, entries) = BuildFactory(LogLevel.Information);
        using var _ = factory;
        // Records as Quarto writes them for a failing R chunk under the service's
        // (quiet) render (captured from a real render): knitr's stderr is piped
        // and re-emitted as red-colourised (ESC[31m) INFO records. The "processing
        // file" progress is benign; the "Error:"/"! ", "Quitting from" and
        // "Execution halted" records are the failure, logged at INFO. esc is built
        // from its code point and JSON-escaped by the serialiser so the source
        // carries no raw control byte.
        var esc = (char)0x1b;
        var file = WriteLines(
            QuartoInfo($"{esc}[31m\n\nprocessing file: report.qmd\n{esc}[39m"),
            QuartoInfo($"{esc}[31mError:\n! object 'foo' not found\n{esc}[39m"),
            QuartoInfo($"{esc}[31m\nQuitting from report.qmd:12-20 [setup]\n{esc}[39m"),
            QuartoInfo($"{esc}[31mExecution halted\n{esc}[39m"));

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // Benign red R progress carries no error signal — stays on Quarto at INFO.
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Information, "processing file"), Is.True);
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.R.report" && e.Message.Contains("processing file")), Is.False);
            // Each R-error record is recovered to .R.report at Error.
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "object 'foo' not found"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "Quitting from"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "Execution halted"), Is.True);
            // The R rlang "! " bullet must NOT be read as a TeX error — nothing on .LaTeX.
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.LaTeX"), Is.False);
            // The SGR colour codes are stripped from every emitted message (the red
            // input carries ESC[31m…ESC[39m; none of it may reach ILogger output).
            Assert.That(
                entries.All(e => !e.Message.Contains((char)0x1b) && !e.Message.Contains("[31m") && !e.Message.Contains("[39m")),
                Is.True, "raw ANSI/SGR codes leaked into an emitted message");
        });
    }

    [Test]
    public async Task DrainQuartoLog_AtWarning_RedKnitrErrorSurvivesTheFloorOnRReport()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        var esc = (char)0x1b;
        var file = WriteLines(
            QuartoInfo($"{esc}[31m\n\nprocessing file: report.qmd\n{esc}[39m"),
            QuartoInfo($"{esc}[31mError:\n! object 'foo' not found\n{esc}[39m"));

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "object 'foo' not found"), Is.True);
            // The benign progress (INFO) is filtered by the Warning floor.
            Assert.That(entries.Any(e => e.Message.Contains("processing file")), Is.False);
        });
    }

    [Test]
    public async Task DrainQuartoLog_OnSuccessfulRender_DoesNotElevateBenignRedRWarnings()
    {
        var (factory, entries) = BuildFactory(LogLevel.Information);
        using var _ = factory;
        // DrainDiagnostics runs on the success path too. A successful render's red R
        // output is only warnings/progress, so recovery is exitCode-gated: a benign
        // line that happens to contain "Error:" (e.g. a warning quoting a handled
        // upstream failure) is NOT promoted to Error on .R.report.
        var esc = (char)0x1b;
        var file = WriteLines(
            QuartoInfo($"{esc}[31mWarning: server returned Error: 503, retrying\n{esc}[39m"));

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(entries.Any(e => e.Category == $"{RenderCategory}.R.report"), Is.False);
            Assert.That(HasEntry(entries, $"{RenderCategory}.Quarto", LogLevel.Information, "retrying"), Is.True);
        });
    }

    [Test]
    public async Task DrainQuartoLog_RecoversAnRErrorAlreadyPromotedToErrorLevel_Issue12799()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        // Future-proofing for quarto-dev#12799: when Quarto promotes knitr errors to
        // ERROR at source, the record arrives as levelName ERROR (not INFO). The
        // level-agnostic R detection keeps re-attributing it to .R.report instead of
        // letting it fall through to .Quarto.
        var esc = (char)0x1b;
        var file = WriteLines(
            QuartoRecord("ERROR", $"{esc}[31mError:\n! object 'foo' not found\n{esc}[39m"));

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "object 'foo' not found"), Is.True);
    }

    [Test]
    public async Task DrainQuartoLog_RecoversRFatalsWithoutColour_NoColorAndNativeCrash()
    {
        var (factory, entries) = BuildFactory(LogLevel.Warning);
        using var _ = factory;
        // Colour-independent net: R-exclusive terminal signals are recovered even
        // when the red gate is absent — an inherited NO_COLOR that disables Quarto's
        // colourising, or a native crash that bypasses R's error machinery. Both
        // must still reach .R.report at Error.
        var file = WriteLines(
            QuartoInfo("Execution halted"),
            QuartoInfo("\n *** caught segfault ***\naddress 0x0, cause 'memory not mapped'"));

        await ReportLogDrain.DrainQuartoLogAsync(file, factory, RenderCategory, exitCode: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "Execution halted"), Is.True);
            Assert.That(HasEntry(entries, $"{RenderCategory}.R.report", LogLevel.Error, "caught segfault"), Is.True);
        });
    }

    // ------------------------------------------------------- cancellation / kill

    [Test]
    public async Task Generate_WhenCancelledMidRender_KillsTheChildProcessTreeAndSurfacesTheCancellation()
    {
        var (factory, _) = BuildFactory(LogLevel.Warning);
        using var _f = factory;
        var pidFile = Path.Combine(Path.GetTempPath(), $"neoipc-kill-test-{Guid.NewGuid():N}.pid");
        _tempFiles.Add(pidFile);

        await using var producer = new LongRunningProducer(factory, pidFile);
        using var cts = new CancellationTokenSource();

        var generate = producer.Generate(cts.Token);

        // Wait until the GRANDCHILD has started and recorded its PID, then cancel.
        // Asserting the grandchild (not the direct child) dies is what proves the
        // tree kill: a single-child kill would orphan it and leave it running.
        var grandchildPid = await ReadPidWhenAvailable(pidFile, TimeSpan.FromSeconds(20));
        Assert.That(ProcessIsAlive(grandchildPid), Is.True, "precondition: the grandchild should be running before the cancel");

        await cts.CancelAsync();

        // The cancellation must surface to the caller (WaitForExitAsync throws a
        // TaskCanceledException, a subclass — CatchAsync accepts derived types)...
        Assert.CatchAsync<OperationCanceledException>(() => generate);
        // ...and the whole tree — including the grandchild, which only
        // Kill(entireProcessTree) reaches — must be gone once it has unwound (the
        // producer waits for the killed root before returning, so there is no race).
        Assert.That(ProcessIsAlive(grandchildPid), Is.False, "the cancelled render's process tree (grandchild) was not killed");
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

    // A Quarto json-stream record carrying an arbitrary level name and msg. The
    // serialiser JSON-escapes both, so control chars like ESC are emitted as their
    // escape sequence rather than a raw byte the parser would reject.
    static string QuartoRecord(string levelName, string msg)
        => $$"""{"levelName":{{System.Text.Json.JsonSerializer.Serialize(levelName)}},"level":20,"msg":{{System.Text.Json.JsonSerializer.Serialize(msg)}},"loggerName":"default"}""";

    static string QuartoInfo(string msg) => QuartoRecord("INFO", msg);

    static LogEntry Single(List<LogEntry> entries, string category)
        => entries.Single(e => e.Category == category);

    static bool HasEntry(List<LogEntry> entries, string category, LogLevel level, string messageSubstring)
        => entries.Any(e => e.Category == category && e.Level == level && e.Message.Contains(messageSubstring));

    static async Task<int> ReadPidWhenAvailable(string pidFile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout; // wall clock is fine in a test
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) &&
                int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out var pid))
                return pid;
            await Task.Delay(50);
        }
        throw new TimeoutException($"The child did not record its PID to {pidFile} within {timeout}.");
    }

    static bool ProcessIsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no process with that id — it exited (and was reaped)
        }
    }

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

    // A minimal producer whose direct child spawns a long-lived GRANDCHILD that
    // records its own PID, so the cancellation test asserts the whole tree — not
    // just the direct child — is killed. A single-child kill would orphan the
    // grandchild (failing the test); only Kill(entireProcessTree) reaps it.
    sealed class LongRunningProducer(ILoggerFactory factory, string pidFile)
        : ExternalProcessReportProducer("text/plain", "Kill-Test", new FakeWebHostEnvironment(), factory)
    {
        protected override ProcessStartInfo GetProcessStartInfo()
        {
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "powershell.exe";
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(
                    "$g = Start-Process powershell -WindowStyle Hidden -PassThru " +
                    "-ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30'; " +
                    $"$g.Id | Set-Content -LiteralPath '{pidFile}'; Start-Sleep -Seconds 30");
            }
            else
            {
                psi.FileName = "/bin/sh";
                // Background a grandchild `sleep`; $! is its PID; `wait` keeps the shell alive.
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add($"sleep 30 & echo $! > '{pidFile}'; wait");
            }
            return psi;
        }

        protected override ValueTask<DataResult> HandleError(
            int processId, int exitCode, Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken)
            => ValueTask.FromResult(new DataResult()); // never reached on the cancellation path

        protected override string? ReportFileDownloadName => "kill-test";

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "NeoIPC.Reporting.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
