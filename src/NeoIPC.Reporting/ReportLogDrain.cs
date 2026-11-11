using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace NeoIPC.Reporting;

/// <summary>
/// Replays the structured log files a report render leaves behind — the R
/// <c>logger</c> <c>layout_json</c> file and (for Quarto renders) Quarto's
/// json-stream log — into <see cref="ILogger"/> under a per-render,
/// per-source category tree. A pure file → logger transformation: the
/// producers own the file paths and the category root; this owns the
/// parsing, the source routing, and the level mapping (see
/// <see cref="ReportLogging"/>).
/// </summary>
static partial class ReportLogDrain
{
    /// <summary>Outcome of draining a Quarto json-stream log.</summary>
    /// <param name="Issue13394Success">
    /// True when the log carried the well-known Quarto #13394 rename error,
    /// which actually produced valid output — the caller treats the non-zero
    /// exit as a success.
    /// </param>
    /// <param name="RawRecords">
    /// Every parsed record, verbatim, for the failure-path ProblemDetails
    /// payload.
    /// </param>
    public readonly record struct QuartoDrainResult(bool Issue13394Success, JsonArray RawRecords);

    /// <summary>
    /// Replay an R <c>layout_json</c> file (one JSON object per line) into
    /// per-source sub-loggers under <paramref name="renderCategory"/>,
    /// routing by the record's <c>ns</c> field and mapping its <c>level</c> to
    /// a <see cref="LogLevel"/>. The R message text is passed as a log
    /// argument (never as the template) so it cannot be read as a format
    /// string. No-ops when the file is absent. Per-source level filtering is
    /// each sub-logger's own <see cref="ILogger.IsEnabled"/>.
    /// </summary>
    public static async ValueTask DrainRLogAsync(
        string? rLogFilePath, ILoggerFactory loggerFactory, string renderCategory,
        CancellationToken cancellationToken)
    {
        if (rLogFilePath is null || !File.Exists(rLogFilePath))
            return;

        ILogger? reportLogger = null, commonLogger = null, neoipcrLogger = null;

        await foreach (var line in File.ReadLinesAsync(rLogFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = TryParseObject(line);
            if (record is null)
                continue;

            var message = ValueOf(record, "msg");
            if (string.IsNullOrWhiteSpace(message))
                continue;

            // neoipcr and the shared common/ layer are named explicitly; any
            // other namespace is the report's own slug → the report channel.
            var logger = ValueOf(record, "ns") switch
            {
                "neoipcr" => neoipcrLogger ??= loggerFactory.CreateLogger($"{renderCategory}.R.neoipcr"),
                "report-common" => commonLogger ??= loggerFactory.CreateLogger($"{renderCategory}.R.common"),
                _ => reportLogger ??= loggerFactory.CreateLogger($"{renderCategory}.R.report"),
            };

            logger.Log(ReportLogging.FromRLevel(ValueOf(record, "level")),
                "[{RTimestamp}] {Message}", ValueOf(record, "time"), message);
        }
    }

    /// <summary>
    /// Replay Quarto's json-stream log into per-source sub-loggers, one entry per
    /// record: Quarto's own records to <c>.Quarto</c>, and the child-engine output
    /// Quarto flattens onto its stream recovered to <c>.Pandoc</c> (by its
    /// <c>[LEVEL]</c> prefix, and a failed conversion at Error), <c>.LaTeX</c>
    /// (the writeError block, at Error), and <c>.R.report</c> (red-colourized
    /// knitr errors on a failed render, at Error).
    /// Returns the raw records (for the failure-path payload) and whether the
    /// well-known #13394 success case was seen. Per-source level filtering is each
    /// sub-logger's own <see cref="ILogger.IsEnabled"/>.
    /// </summary>
    public static async ValueTask<QuartoDrainResult> DrainQuartoLogAsync(
        string quartoLogFilePath, ILoggerFactory loggerFactory, string renderCategory,
        int exitCode, CancellationToken cancellationToken)
    {
        var rawRecords = new JsonArray();
        var issue13394Success = false;
        if (!File.Exists(quartoLogFilePath))
            return new QuartoDrainResult(issue13394Success, rawRecords);

        // Every record is read before any is emitted: a failed Pandoc conversion is
        // recognized only by the records that follow Pandoc's output.
        var records = new List<QuartoRecord>();
        await foreach (var line in File.ReadLinesAsync(quartoLogFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { continue; }
            if (node is null)
                continue;
            rawRecords.Add(node); // raw, for the failure ProblemDetails payload

            if (node is JsonObject record && ValueOf(record, "msg") is { } msg)
                records.Add(new QuartoRecord(msg, ValueOf(record, "levelName")));
        }

        // Gated like the LaTeX anchor below: a successful render has no failed conversion.
        var pandocFailureParts = exitCode != 0
            ? MarkPandocFailures(records)
            : new PandocFailurePart[records.Count];

        var quartoLogger = loggerFactory.CreateLogger($"{renderCategory}.Quarto");
        var pandocLogger = loggerFactory.CreateLogger($"{renderCategory}.Pandoc");
        var latexLogger = loggerFactory.CreateLogger($"{renderCategory}.LaTeX");
        var rLogger = loggerFactory.CreateLogger($"{renderCategory}.R.report");

        // True while inside a Quarto writeError block — an `error("compilation
        // failed- …")` record and the `info(detail)` / `info("see …log")` records
        // that follow it — so the extracted detail is elevated to .LaTeX whatever
        // its content.
        var latexErrorActive = false;

        for (var i = 0; i < records.Count; i++)
        {
            var (message, levelName) = records[i];
            if (string.IsNullOrWhiteSpace(message))
                continue;

            // Detection runs on the raw message (ANSI intact — the R red gate
            // needs it); the emitted text has the SGR colour codes stripped so
            // the log stays clean.
            var display = StripSgrRegex().Replace(message, "");

            // Well-known Quarto bug (#13394): a specific rename error that
            // actually produced valid output. Treat the render as a success
            // and downgrade the noise to a trace.
            if (exitCode == 1 &&
                ReportLogging.FromQuartoLevel(levelName) == LogLevel.Error &&
                QuartoIssue13394DetectionRegex().IsMatch(message))
            {
                quartoLogger.LogTrace(
                    "Hit well-known Quarto bug (https://github.com/quarto-dev/quarto-cli/issues/13394): {Message}",
                    display);
                issue13394Success = true;
                continue;
            }

            // A failed Pandoc conversion (see MarkPandocFailures) is reported by the
            // ERROR record of Pandoc's stderr and Quarto's closing record, both on
            // .Pandoc at Error; a [WARNING] line in that stderr goes with it, as a
            // batched record below takes the level of its most severe line. The INFO
            // records that streamed the same stderr are a copy cut at arbitrary
            // points, a [LEVEL] prefix included, so they go to Trace rather than
            // reporting the failure a second time.
            switch (pandocFailureParts[i])
            {
                case PandocFailurePart.Report:
                    pandocLogger.LogError("{Message}", display);
                    continue;
                case PandocFailurePart.LiveCopy:
                    pandocLogger.LogTrace("{Message}", display);
                    continue;
            }

            // Structural LaTeX recovery. Quarto's writeError emits
            // error("\ncompilation failed- <primary>") then info(<findLatexError
            // detail>) then info("see <log> for more information."). Anchor on the
            // "compilation failed-" record and elevate the whole block to .LaTeX
            // at Error, so ANY findLatexError shape is recovered without
            // enumerating them — the l.<n> line-context, the fixed "No pages of
            // output", an emergency-stop / output-routine <*>/<output> context, or
            // the luaotfload-fallback guidance. Keying on writeError's structure
            // rather than on the detail's content, whose shapes differ between
            // Quarto releases, is what lets the recovery catch shapes it does not
            // enumerate. Self-gating: writeError only runs on a failed compile,
            // and the anchor is exitCode-guarded for belt and braces.
            if (latexErrorActive)
            {
                if (levelName == "INFO")
                {
                    var isSeeLog = LatexSeeLogRegex().IsMatch(message);
                    latexLogger.Log(isSeeLog ? LogLevel.Information : LogLevel.Error, "{Message}", display);
                    if (isSeeLog) // the "see …log" pointer closes the block
                        latexErrorActive = false;
                    continue;
                }
                latexErrorActive = false; // nothing in the block is non-INFO; such a record ends it
            }
            if (exitCode != 0 && message.StartsWith("\ncompilation failed- ", StringComparison.Ordinal))
            {
                latexLogger.LogError("{Message}", display);
                latexErrorActive = true;
                continue;
            }

            // Pandoc severity recovery: Quarto re-logs Pandoc's stderr as its own
            // INFO records, flattening the severity; Pandoc's own [LEVEL] prefix
            // carries the truth. Quarto batches stderr per chunk, so one INFO
            // record can carry several [LEVEL]-prefixed lines — route the whole
            // record to the Pandoc channel at the MOST severe of them, so an
            // embedded [ERROR] is never downgraded by a leading [WARNING]. A chunk
            // ends wherever a read from Pandoc's pipe did, inside a prefix too ("["
            // and "WARNING] …" as two records), and a prefix so split is currently
            // not recovered. Pandoc severity is explicit, so this recovery is not
            // exitCode-gated.
            if (levelName == "INFO")
            {
                var pandocMatches = PandocLevelPrefixRegex().Matches(message);
                if (pandocMatches.Count > 0)
                {
                    var pandocLevel = LogLevel.Information;
                    foreach (Match m in pandocMatches)
                    {
                        var lineLevel = ReportLogging.FromPandocLevel(m.Groups[1].Value);
                        if (lineLevel > pandocLevel)
                            pandocLevel = lineLevel;
                    }
                    pandocLogger.Log(pandocLevel, "{Message}", display);
                    continue;
                }
            }

            // R/knitr fatal recovery — only on a failed render, so benign red
            // warnings on a successful render are not elevated. Quarto colours the
            // stderr of the Rscript process that runs knitr red (ESC[31m); a red
            // record carrying an R error signal is the failure. The check is
            // level-AGNOSTIC on purpose, because the level depends on how Quarto
            // runs knitr (callR in quarto-dev/quarto-cli's src/execute/rmd.ts,
            // v1.11.9): with the render's own quiet flag set, a failed run's
            // output arrives as one ERROR record; QuartoReportProducer's --quiet is
            // a log option (logOptions in src/core/log.ts) that leaves that flag
            // unset, so the output streams as INFO records. R-exclusive terminal
            // signals ("Execution halted", "Quitting from", a native "caught
            // segfault" / "R is aborting now" crash) match even without colour, so
            // they survive NO_COLOR and do not depend on the colour Quarto applies;
            // the colour gate covers the otherwise-ambiguous "Error:" / "! ". The
            // structured DHIS2/neoipcr trace arrives separately via the layout_json
            // file, drained with its true namespace by DrainRLogAsync.
            if (exitCode != 0 &&
                (RExclusiveFatalRegex().IsMatch(message)
                 || (message.Contains(KnitrColorCode, StringComparison.Ordinal)
                     && RAmbiguousFatalRegex().IsMatch(message))))
            {
                rLogger.LogError("{Message}", display);
                continue;
            }

            // Generate-only PDF/A: the reports set `pdf-standard` but the runtime image ships
            // no veraPDF (conformance validation is a design-time/CI concern, not a per-render
            // one). Quarto then emits a benign "verapdf is not installed" WARNING on every
            // render; drop it to Debug so it does not recur at Warning on .Quarto.
            // Generation (LuaLaTeX) is unaffected — the standard-compliant PDF is still produced.
            if (message.Contains("verapdf is not installed", StringComparison.Ordinal))
            {
                quartoLogger.LogDebug("{Message}", display);
                continue;
            }

            // KOMA-Script cannot produce tagged PDF, so the reports declare PDF/A-4 only and
            // never activate tagging. Should tagging become active anyway — someone re-adding a
            // PDF/UA standard, or a LaTeX release auto-activating it under \DocumentMetadata —
            // the class emits this warning and silently degrades every heading to ordinary
            // paragraph text, while the file may still assert conformance. Nothing else catches
            // it: Quarto's PDF/UA linter matches `Package tagpdf Warning:` and the unset-language
            // warning, not `Package scrartcl Warning:`, and no veraPDF runs here. Raise it to
            // Error so the regression is loud rather than buried in Quarto's INFO stream.
            if (message.Contains("Activated tagging detected but not supported", StringComparison.Ordinal))
            {
                latexLogger.LogError("{Message}", display);
                continue;
            }

            quartoLogger.Log(ReportLogging.FromQuartoLevel(levelName), "{Message}", display);
        }

        return new QuartoDrainResult(issue13394Success, rawRecords);
    }

    static JsonObject? TryParseObject(string line)
    {
        try { return JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>The string value of a JSON object property, or <c>null</c> when absent.</summary>
    /// <remarks>
    /// <c>ToString()</c> is relied on intentionally: the R <c>logger::layout_json()</c>
    /// fields the drain reads (<c>time</c>/<c>level</c>/<c>ns</c>/<c>msg</c>) are always
    /// emitted as JSON strings, so this returns their text directly. A non-string node
    /// would merely stringify to its JSON text and then fall through the level/namespace
    /// switches to a safe default, so no type-guarding is needed.
    /// </remarks>
    static string? ValueOf(JsonObject record, string key)
        => record.TryGetPropertyValue(key, out var node) ? node?.ToString() : null;

    /// <summary>The message and level name of a Quarto json-stream record.</summary>
    readonly record struct QuartoRecord(string Message, string? LevelName);

    /// <summary>A record's part in a failed Pandoc conversion.</summary>
    enum PandocFailurePart : byte
    {
        /// <summary>Outside a failed conversion's block, or a streamed copy that does not match its stderr.</summary>
        None,
        /// <summary>Reports it: the ERROR record of Pandoc's stderr, or Quarto's closing record.</summary>
        Report,
        /// <summary>Streamed, as INFO, part of the stderr that the ERROR record repeats.</summary>
        LiveCopy,
    }

    /// <summary>
    /// Marks the records of each failed Pandoc conversion in <paramref name="records"/>.
    /// </summary>
    /// <remarks>
    /// While Pandoc runs, Quarto streams its stderr as INFO records, one per chunk read
    /// from the pipe (processOutput in quarto-dev/quarto-cli's src/core/process.ts).
    /// When Pandoc fails, Quarto logs that stderr again, trimmed, as one ERROR record,
    /// and the render ends with the ERROR record "Pandoc conversion failed (exit code
    /// N)" (runPandoc in src/command/render/pandoc.ts, logged by logError in
    /// src/core/log.ts; v1.11.9). That closing record anchors the block; the ERROR
    /// record before it, if any, is the stderr, and the INFO records before that whose
    /// text, joined and trimmed, is the stderr's are its live copy. DEBUG records, which
    /// a debug-level log puts between them, are passed over. A copy that does not match,
    /// such as one missing a chunk that began with a carriage return, which Quarto's log
    /// file leaves out, stays unmarked, so nothing is lost.
    /// </remarks>
    static PandocFailurePart[] MarkPandocFailures(IReadOnlyList<QuartoRecord> records)
    {
        var parts = new PandocFailurePart[records.Count];
        for (var closing = 0; closing < records.Count; closing++)
        {
            if (records[closing].LevelName != "ERROR"
                || !PandocConversionFailedRegex().IsMatch(records[closing].Message))
                continue;
            parts[closing] = PandocFailurePart.Report;

            var stderr = PreviousNonDebug(records, closing);
            if (stderr < 0 || records[stderr].LevelName != "ERROR")
                continue; // Pandoc's stderr was empty, so Quarto logged no record of it
            parts[stderr] = PandocFailurePart.Report;

            // Walk back over the INFO records, joining them once they are long enough
            // to hold the stderr; a trimmed join as long as the stderr that differs
            // from it ends the walk, since going further back only lengthens it.
            var stderrText = records[stderr].Message;
            var copy = new List<int>();
            var length = 0;
            for (var j = PreviousNonDebug(records, stderr);
                 j >= 0 && records[j].LevelName == "INFO";
                 j = PreviousNonDebug(records, j))
            {
                copy.Add(j);
                length += records[j].Message.Length;
                if (length < stderrText.Length)
                    continue;
                var joined = new StringBuilder(length);
                for (var k = copy.Count - 1; k >= 0; k--)
                    joined.Append(records[copy[k]].Message);
                var text = joined.ToString().Trim();
                if (text == stderrText)
                {
                    foreach (var k in copy)
                        parts[k] = PandocFailurePart.LiveCopy;
                    break;
                }
                if (text.Length >= stderrText.Length)
                    break;
            }
        }
        return parts;
    }

    /// <summary>The index of the last record before <paramref name="index"/> that is not DEBUG, or -1.</summary>
    static int PreviousNonDebug(IReadOnlyList<QuartoRecord> records, int index)
    {
        var i = index - 1;
        while (i >= 0 && records[i].LevelName == "DEBUG")
            i--;
        return i;
    }

    [GeneratedRegex(@"NotFound: No such file or directory \(os error 2\): rename '.+?(/|\\)-' -> '.+?(/|\\)_output(/|\\)-'")]
    private static partial Regex QuartoIssue13394DetectionRegex();

    // Pandoc prefixes the first line of every message with its verbosity, e.g.
    // "[WARNING] …" (logOutput in jgm/pandoc's src/Text/Pandoc/Class/IO.hs;
    // continuation lines are indented, not re-prefixed). Multiline so ^ matches
    // each line start — one re-logged Quarto INFO record can batch several
    // Pandoc messages. Pandoc emits only ERROR / WARNING / INFO.
    [GeneratedRegex(@"^\[(ERROR|WARNING|INFO)\]", RegexOptions.Multiline)]
    private static partial Regex PandocLevelPrefixRegex();

    // Quarto's closing record for a failed Pandoc conversion (runPandoc in
    // quarto-dev/quarto-cli's src/command/render/pandoc.ts, v1.11.9). Anchored at the
    // start only, since logError appends a stack trace when QUARTO_DEBUG or
    // QUARTO_PRINT_STACK is set.
    [GeneratedRegex(@"^Pandoc conversion failed \(exit code -?\d+\)")]
    private static partial Regex PandocConversionFailedRegex();

    // Quarto colours the stderr of the Rscript process that runs knitr red (SGR
    // "ESC[31m", Deno colors.red — callR in quarto-dev/quarto-cli's
    // src/execute/rmd.ts, v1.11.9), so the escape marks a record as R-origin
    // regardless of its level. The ESC is built from its code point (0x1B) so the
    // source carries no raw control byte and no greedy C# \x escape. Reliable
    // only because GetProcessStartInfo scrubs NO_COLOR from the child env —
    // Deno's red is gated on !Deno.noColor.
    private static readonly string KnitrColorCode = (char)0x1b + "[31m";

    // Strip ANSI SGR colour sequences (the red ESC[31m…ESC[39m Quarto puts on the
    // Rscript stderr, Quarto's blue progress) from a record before it is logged, so the emitted text is clean.
    // \e is the .NET-regex escape for ESC (U+001B) — no C# \x/\u needed.
    [GeneratedRegex(@"\e\[[0-9;]*m")]
    private static partial Regex StripSgrRegex();

    // R-exclusive terminal/fatal signals: knitr's per-chunk "Quitting from …"
    // frame, Rscript's terminal "Execution halted", and base R's native-crash
    // handler ("*** caught segfault ***", "… R is aborting now …"). These cannot
    // be confused with Quarto/Pandoc/LaTeX output, so they are matched WITHOUT the
    // colour gate — recovery survives NO_COLOR and does not depend on the colour.
    [GeneratedRegex(@"Execution halted|Quitting from|caught segfault|R is aborting now")]
    private static partial Regex RExclusiveFatalRegex();

    // R error signals that ARE ambiguous with other engines' output — a base/rlang
    // error header ("Error:" / "Error in …") and the rlang "! " bullet — so they
    // are only trusted inside a record already known to be R-origin (red).
    // Multiline so ^ matches the "! " line within the batched record.
    [GeneratedRegex(@"Error:|Error in |^! ", RegexOptions.Multiline)]
    private static partial Regex RAmbiguousFatalRegex();

    // The final line of Quarto's writeError block, "see <log> for more
    // information." — closes the LaTeX-error window opened by "compilation failed-".
    [GeneratedRegex(@"see .+ for more information\.")]
    private static partial Regex LatexSeeLogRegex();
}
