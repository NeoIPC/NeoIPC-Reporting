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
    /// Replay Quarto's json-stream log into the <c>.Quarto</c> sub-logger (and
    /// the recovered <c>.Pandoc</c> sub-logger), one entry per record. Returns
    /// the raw records (for the failure-path payload) and whether the
    /// well-known #13394 success case was seen. Per-source level filtering is
    /// each sub-logger's own <see cref="ILogger.IsEnabled"/>.
    /// </summary>
    public static async ValueTask<QuartoDrainResult> DrainQuartoLogAsync(
        string quartoLogFilePath, ILoggerFactory loggerFactory, string renderCategory,
        int exitCode, CancellationToken cancellationToken)
    {
        var rawRecords = new JsonArray();
        var issue13394Success = false;
        if (!File.Exists(quartoLogFilePath))
            return new QuartoDrainResult(issue13394Success, rawRecords);

        var quartoLogger = loggerFactory.CreateLogger($"{renderCategory}.Quarto");
        var pandocLogger = loggerFactory.CreateLogger($"{renderCategory}.Pandoc");
        var latexLogger = loggerFactory.CreateLogger($"{renderCategory}.LaTeX");
        var rLogger = loggerFactory.CreateLogger($"{renderCategory}.R.report");

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

            if (node is not JsonObject record)
                continue;
            var message = ValueOf(record, "msg");
            if (string.IsNullOrWhiteSpace(message))
                continue;
            var levelName = ValueOf(record, "levelName");

            // Well-known Quarto bug (#13394): a specific rename error that
            // actually produced valid output. Treat the render as a success
            // and downgrade the noise to a trace.
            if (exitCode == 1 &&
                ReportLogging.FromQuartoLevel(levelName) == LogLevel.Error &&
                QuartoIssue13394DetectionRegex().IsMatch(message))
            {
                quartoLogger.LogTrace(
                    "Hit well-known Quarto bug (https://github.com/quarto-dev/quarto-cli/issues/13394): {Message}",
                    message);
                issue13394Success = true;
                continue;
            }

            // Pandoc severity recovery: Quarto re-logs Pandoc's stderr as its
            // own INFO records, flattening the severity; Pandoc's own [LEVEL]
            // prefix carries the truth. Quarto batches stderr per chunk, so one
            // INFO record can carry several [LEVEL]-prefixed lines — route the
            // whole record to the Pandoc channel at the MOST severe of them, so
            // an embedded [ERROR] is never downgraded by a leading [WARNING]
            // and survives even when the service runs quiet. Quarto's own INFO
            // records (no [LEVEL] prefix) stay on the Quarto channel.
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
                    pandocLogger.Log(pandocLevel, "{Message}", message);
                    continue;
                }

                // R/knitr fatal recovery: under the service's (quiet) render,
                // Quarto pipes the knitr child's stderr and re-emits it as its
                // own INFO records, colourised red (ESC[31m) by the engine. The
                // red progress lines are benign, but an R chunk error — an
                // "Error:"/"Error in" message, the rlang "! " bullet, "Quitting
                // from", "Execution halted" — is a render failure logged at INFO.
                // Route those to .R.report at Error. (The structured DHIS2/neoipcr
                // trace arrives separately via the layout_json file, drained with
                // its true namespace by DrainRLogAsync; this path only recovers R
                // output Quarto captured onto its own stream.)
                if (message.Contains(KnitrColorCode) && RFatalContentRegex().IsMatch(message))
                {
                    rLogger.LogError("{Message}", message);
                    continue;
                }

                // LaTeX fatal recovery: Quarto extracts the PDF engine's error
                // from the .log and re-emits it as its own INFO record, having
                // stripped the leading TeX "! " — so the surviving marker is
                // TeX's "l.<n>" line-context, which R output never uses (R reports
                // errors as "file:line"). Route it to .LaTeX at Error so the cause
                // survives a Warning threshold alongside Quarto's own generic
                // (already Error) "compilation failed-" record. Benign engine
                // output (the LuaHBTeX banner) carries no marker and stays on the
                // Quarto channel.
                if (LatexFatalErrorRegex().IsMatch(message))
                {
                    latexLogger.LogError("{Message}", message);
                    continue;
                }
            }

            quartoLogger.Log(ReportLogging.FromQuartoLevel(levelName), "{Message}", message);
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

    [GeneratedRegex(@"NotFound: No such file or directory \(os error 2\): rename '.+?(/|\\)-' -> '.+?(/|\\)_output(/|\\)-'")]
    private static partial Regex QuartoIssue13394DetectionRegex();

    // Pandoc prefixes the first line of every message with its verbosity, e.g.
    // "[WARNING] …" (refs/pandoc/src/Text/Pandoc/Class/IO.hs; continuation
    // lines are indented, not re-prefixed). Multiline so ^ matches each line
    // start — one re-logged Quarto INFO record can batch several Pandoc
    // messages. Pandoc emits only ERROR / WARNING / INFO.
    [GeneratedRegex(@"^\[(ERROR|WARNING|INFO)\]", RegexOptions.Multiline)]
    private static partial Regex PandocLevelPrefixRegex();

    // knitr colourises the R stderr it streams to Quarto red (SGR "ESC[31m",
    // Deno colors.red — refs/quarto-cli/src/execute/rmd.ts), so the escape marks
    // a record as R-origin regardless of its (flattened) INFO level. The ESC is
    // built from its code point (0x1B) so the source carries no raw control byte
    // and no greedy C# \x escape.
    private static readonly string KnitrColorCode = (char)0x1b + "[31m";

    // Fatal signals inside an R-origin (red) record: a base/rlang error header
    // ("Error:"/"Error in …"), the rlang "! " bullet, knitr's "Quitting from …"
    // frame, and Rscript's terminal "Execution halted". Only applied to records
    // already known to be R-origin (red), so "Error"/"! " cannot be confused
    // with Quarto/Pandoc output. Multiline so ^ matches the "! " line within the
    // batched record.
    [GeneratedRegex(@"Execution halted|Quitting from|Error:|Error in |^! ", RegexOptions.Multiline)]
    private static partial Regex RFatalContentRegex();

    // Quarto extracts the PDF engine's error from the .log and re-emits it as its
    // own INFO record, having stripped the leading TeX "! " (its findLatexError
    // captures the text after "! "; refs/quarto-cli/src/command/render/latexmk).
    // The surviving unambiguous marker is TeX's "l.<n>" line-context — which R
    // never emits (R reports "file:line"), so it will not steal an R record.
    // Line-anchored (it begins a line); Multiline so ^ matches within a batched
    // record. The benign LuaHBTeX banner carries no such marker.
    [GeneratedRegex(@"^l\.\d+ ", RegexOptions.Multiline)]
    private static partial Regex LatexFatalErrorRegex();
}
