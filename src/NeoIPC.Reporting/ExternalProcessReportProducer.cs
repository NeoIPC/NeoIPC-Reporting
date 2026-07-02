using System.Diagnostics;
using Microsoft.Extensions.Logging;
using static NeoIPC.Reporting.Helpers;

namespace NeoIPC.Reporting;

/// <summary>
/// Common machinery for any generator that produces a report by
/// shelling out to an external process and streaming its stdout to the
/// caller. Subclasses provide the <see cref="ProcessStartInfo"/>, the
/// download filename, and the error-mapping logic.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses today: <see cref="QuartoReportProducer"/> (Quarto +
/// LaTeX) and <see cref="RScriptReportProducer"/> (raw Rscript JSON
/// output). The base class buffers stdout into a memory stream so the
/// subprocess can finish before the response starts flowing — this
/// lets the error-handling path replace the response with a 5xx
/// ProblemDetails when the subprocess fails after partial output.
/// </para>
/// <para>
/// Logging: the child processes log through one verbosity dial driven by
/// the service's effective <see cref="LogLevel"/> (see
/// <see cref="ReportLogging"/>), writing their diagnostics to structured
/// files. After the child exits, <see cref="DrainDiagnostics"/> re-emits
/// those records into <see cref="ILogger"/> — on the success path as well
/// as on failure — under a per-render category tree rooted at
/// <c>NeoIPC.Reporting.Render.&lt;report&gt;</c>, so a successful render's
/// DHIS2 query trace is no longer discarded. Each render carries a
/// <c>RenderId</c> scope (and a nested <c>ProcessId</c> scope once the
/// child starts) so concurrent renders' drained entries stay correlated.
/// </para>
/// </remarks>
abstract partial class ExternalProcessReportProducer : IDataProducer
{
    public string MediaType { get; }
    protected IWebHostEnvironment Environment { get; }
    protected ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Root category of this render's logger tree —
    /// <c>NeoIPC.Reporting.Render.&lt;report&gt;</c>. The service's own
    /// render messages log here; the drained child diagnostics log under
    /// its <c>.Quarto</c>/<c>.Pandoc</c>/<c>.R.*</c> children.
    /// </summary>
    protected string RenderCategory { get; }

    /// <summary>Logger for the service's own render messages (the root category).</summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Correlation id for this render, attached as a <c>RenderId</c> log
    /// scope around <see cref="Generate"/>. Defaults to a synthesized value;
    /// <see cref="QuartoReportProducer"/> overrides it with its per-render
    /// workdir name.
    /// </summary>
    public string RenderId { get; protected set; }

    /// <summary>
    /// Set once the render has failed (a non-zero exit that mapped to a failure
    /// result, or an exception before output). Lets a subclass's
    /// <see cref="DisposeAsync"/> decide whether to preserve scratch state for
    /// inspection rather than delete it.
    /// </summary>
    protected bool RenderFailed { get; private set; }

    /// <summary>
    /// Path the child writes its R <c>logger</c> <c>layout_json</c> records
    /// to (passed as <c>NEOIPC_LOG_FILE</c>), or <c>null</c> when the
    /// producer does not drain an R log. Drained by
    /// <see cref="ReportLogDrain.DrainRLogAsync"/>.
    /// </summary>
    protected string? RLogFilePath { get; set; }

    /// <summary>
    /// The render's per-source log category suffixes — the root render
    /// category (<c>""</c>) plus the drain channels this producer emits to.
    /// The child-process verbosity is derived from the minimum effective level
    /// across these (see
    /// <see cref="ReportLogging.EffectiveMinLevel(ILoggerFactory,string,System.Collections.Generic.IEnumerable{string})"/>),
    /// so raising any one source above the render root makes the child write
    /// that source's finer records. <see cref="QuartoReportProducer"/> adds its
    /// Quarto/Pandoc channels.
    /// </summary>
    protected virtual IReadOnlyList<string> DrainCategorySuffixes { get; } =
        ["", ".R.report", ".R.common", ".R.neoipcr"];

    protected ExternalProcessReportProducer(
        string mediaType, string reportName, IWebHostEnvironment environment, ILoggerFactory loggerFactory)
    {
        MediaType = mediaType;
        Environment = environment;
        LoggerFactory = loggerFactory;
        RenderCategory = $"NeoIPC.Reporting.Render.{reportName}";
        Logger = loggerFactory.CreateLogger(RenderCategory);
        RenderId = $"render-{Guid.NewGuid():N}";
    }

    protected abstract ProcessStartInfo GetProcessStartInfo();

    protected abstract ValueTask<DataResult> HandleError(int processId, int exitCode, Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken);

    protected abstract string? ReportFileDownloadName { get; }

    public async Task<DataResult> Generate(CancellationToken cancellationToken)
    {
        using var renderScope = Logger.BeginScope(new Dictionary<string, object> { ["RenderId"] = RenderId });
        try
        {
            var bufferStream = new MemoryStream();
            var startInfo = GetProcessStartInfo();
            LogStartingProcess(Logger, startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory);
            using var reportGenerationProcess = Process.Start(startInfo);

            if (reportGenerationProcess == null)
                return new DataResult(detail: "The external process failed to start", statusCode: 500, title: "Internal Server Error", showMessage: Environment.IsDevelopment());

            using var processScope = Logger.BeginScope(
                new Dictionary<string, object> { ["ProcessId"] = reportGenerationProcess.Id });

            var stdOut = reportGenerationProcess.StandardOutput.BaseStream.CopyToAsync(bufferStream, cancellationToken);
            var stdErr = reportGenerationProcess.StandardError.ReadToEndAsync(cancellationToken);

            await reportGenerationProcess.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdOut, stdErr);

            // The child's structured log files are complete only now that it
            // has exited, so this is inherently a post-process drain. Run it on
            // both exit paths: on success it surfaces the render's R/Quarto
            // diagnostics (incl. the DHIS2 query trace) that were previously
            // discarded; on failure it precedes the error-result mapping. The
            // drain is a best-effort observability side-channel: an IO/parse
            // failure reading the log files must NEVER fail an otherwise-
            // successful render (the output is already fully buffered), so
            // swallow and log any non-cancellation drain error.
            try
            {
                await DrainDiagnostics(reportGenerationProcess.ExitCode, cancellationToken);
            }
            catch (Exception drainError) when (drainError is not OperationCanceledException)
            {
                Logger.LogError(drainError,
                    "Draining the render's diagnostic logs failed; the render result is unaffected.");
            }

            if (reportGenerationProcess.ExitCode != 0)
            {
                var returnValue = await HandleError(reportGenerationProcess.Id, reportGenerationProcess.ExitCode, bufferStream, stdErr.Result, cancellationToken);
                if (!returnValue.Success)
                {
                    RenderFailed = true;
                    return returnValue;
                }
            }

            return new DataResult(bufferStream, MediaType, GetFileDownloadName());

            string GetFileDownloadName() =>
                string.Concat(
                    "NeoIPC-Surveillance-",
                    ReportFileDownloadName,
                    "_",
                    DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss"),
                    FileExtensionFromMediaType(MediaType));
        }
        catch (Exception e)
        {
            // A client disconnect mid-render surfaces as a cancellation on the request token. Renders are
            // slow, so that is a routine, non-error event: let it propagate (the framework handles the
            // aborted request) rather than logging it as a failure or turning it into a 500.
            if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            RenderFailed = true;
            // Any other throw before output otherwise vanishes: the DataResult only surfaces the message
            // in Development, so in Production the caller sees a bare 500 with no trace of the cause. Log
            // it so production render failures are diagnosable.
            Logger.LogError(e, "Report generation process threw before producing output.");
            return new DataResult(e, showMessage: Environment.IsDevelopment());
        }
    }

    /// <summary>
    /// Re-emit the child process's structured diagnostics into
    /// <see cref="ILogger"/> after it exits. The base drains the R
    /// <c>layout_json</c> file when the producer set
    /// <see cref="RLogFilePath"/>; <see cref="QuartoReportProducer"/> also
    /// drains Quarto's json-stream log. Runs on both the success and the
    /// failure path.
    /// </summary>
    protected virtual async ValueTask DrainDiagnostics(int exitCode, CancellationToken cancellationToken)
        => await ReportLogDrain.DrainRLogAsync(RLogFilePath, LoggerFactory, RenderCategory, cancellationToken);

    public abstract ValueTask DisposeAsync();

    [LoggerMessage(LogLevel.Debug, "Starting process for {process} with arguments {arguments} in directory {workingDirectory}.")]
    static partial void LogStartingProcess(ILogger logger, string process, string arguments, string workingDirectory);
}
