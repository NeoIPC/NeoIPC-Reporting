using Microsoft.Extensions.Logging;

namespace NeoIPC.Reporting;

/// <summary>
/// Bridges the .NET log level to the verbosity controls of the child
/// report processes and back again.
/// </summary>
/// <remarks>
/// <para>
/// A render shells out to Rscript and/or Quarto, which in turn run the
/// report's R code and neoipcr. All of those log through one dial driven
/// by the service's effective <see cref="LogLevel"/>: the R side reads
/// <c>NEOIPC_LOG_LEVEL</c> (see <see cref="ToNeoIpcLogLevel"/>) and Quarto
/// takes <c>--log-level</c> (see <see cref="ToQuartoLogLevel"/>). When the
/// child finishes, its structured log file is drained back into
/// <see cref="ILogger"/>; the <c>From*</c> helpers map each record's own
/// level string onto <see cref="LogLevel"/> so the re-emitted entry keeps
/// its original severity.
/// </para>
/// </remarks>
static class ReportLogging
{
    /// <summary>
    /// The lowest <see cref="LogLevel"/> the logger actually emits — its
    /// effective floor — or <see cref="LogLevel.None"/> when nothing is
    /// enabled. This is the single value the child-process verbosity is
    /// derived from.
    /// </summary>
    public static LogLevel EffectiveMinLevel(ILogger logger)
    {
        for (var level = LogLevel.Trace; level < LogLevel.None; level++)
            if (logger.IsEnabled(level))
                return level;
        return LogLevel.None;
    }

    /// <summary>
    /// The lowest <see cref="LogLevel"/> enabled across a render's whole
    /// per-source category sub-tree (the root render category plus the given
    /// drain-channel suffixes). The child-process verbosity is derived from
    /// this minimum so that raising any single source above the render root
    /// (e.g. <c>…Render.&lt;report&gt;.R.neoipcr = Trace</c>) actually makes the
    /// child write that source's finer records — per-source tuning works
    /// upward, not only downward.
    /// </summary>
    public static LogLevel EffectiveMinLevel(
        ILoggerFactory loggerFactory, string renderCategory, IEnumerable<string> categorySuffixes)
    {
        var min = LogLevel.None;
        foreach (var suffix in categorySuffixes)
        {
            var level = EffectiveMinLevel(loggerFactory.CreateLogger(renderCategory + suffix));
            if (level < min)
                min = level;
        }
        return min;
    }

    /// <summary>
    /// The <c>NEOIPC_LOG_LEVEL</c> verbosity word the report pipeline reads
    /// (the same dial the PowerShell wrappers drive from
    /// <c>-Quiet</c>/<c>-Verbose</c>/<c>-Debug</c>). Governs the R
    /// <c>logger</c> namespaces — including neoipcr's DHIS2 query trace,
    /// which only appears at <c>verbose</c>/<c>debug</c>.
    /// </summary>
    public static string ToNeoIpcLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "debug",
        LogLevel.Debug => "verbose",
        LogLevel.Information => "normal",
        _ => "quiet", // Warning / Error / Critical / None
    };

    /// <summary>
    /// Quarto's <c>--log-level</c> for the render, <b>floored at
    /// <c>info</c></b> regardless of the service level. Quarto re-logs every
    /// Pandoc line — whatever its real severity — as an <c>INFO</c> record
    /// (it captures Pandoc's stderr and re-emits it through its own
    /// <c>info()</c>), so a coarser level would drop Pandoc warnings and
    /// errors before they are ever written to the log file. Keeping the file
    /// at <c>info</c> guarantees the Pandoc lines are captured; the drain
    /// then re-imposes the service level per source (so Quarto's own INFO
    /// chatter is suppressed when the service runs quiet) and recovers
    /// Pandoc's true severity from its <c>[LEVEL]</c> prefix.
    /// </summary>
    public static string ToQuartoLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        _ => "info",
    };

    /// <summary>
    /// Maps an R <c>logger</c> level name (the <c>level</c> field of a
    /// <c>layout_json</c> record) onto <see cref="LogLevel"/>. Unknown values
    /// fall back to <see cref="LogLevel.Debug"/>.
    /// </summary>
    public static LogLevel FromRLevel(string? level) => level switch
    {
        "TRACE" => LogLevel.Trace,
        "DEBUG" => LogLevel.Debug,
        "INFO" or "SUCCESS" => LogLevel.Information,
        "WARN" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        "FATAL" => LogLevel.Critical,
        _ => LogLevel.Debug,
    };

    /// <summary>
    /// Maps a Quarto json-stream <c>levelName</c> onto <see cref="LogLevel"/>.
    /// Quarto's canonical warning token is <c>WARN</c> (the Deno
    /// <c>@std/log</c> level name; <c>refs/quarto-cli/src/core/log.ts</c>
    /// declares <c>LogLevel = "DEBUG"|"INFO"|"WARN"|"ERROR"|"CRITICAL"</c>);
    /// <c>WARNING</c> is accepted as a defensive alias. Unknown values fall
    /// back to <see cref="LogLevel.Debug"/>.
    /// </summary>
    public static LogLevel FromQuartoLevel(string? levelName) => levelName switch
    {
        "INFO" => LogLevel.Information,
        "WARN" or "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        "CRITICAL" => LogLevel.Critical,
        _ => LogLevel.Debug,
    };

    /// <summary>
    /// Maps a Pandoc verbosity prefix — the <c>[LEVEL]</c> token Pandoc
    /// prints before every message — onto <see cref="LogLevel"/>. Pandoc
    /// emits only <c>ERROR</c>, <c>WARNING</c> and <c>INFO</c>.
    /// </summary>
    public static LogLevel FromPandocLevel(string pandocLevel) => pandocLevel switch
    {
        "ERROR" => LogLevel.Error,
        "WARNING" => LogLevel.Warning,
        _ => LogLevel.Information, // INFO
    };
}
