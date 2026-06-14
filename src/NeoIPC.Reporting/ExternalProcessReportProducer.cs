using System.Diagnostics;
using static NeoIPC.Reporting.Helpers;

namespace NeoIPC.Reporting;

/// <summary>
/// Common machinery for any generator that produces a report by
/// shelling out to an external process and streaming its stdout to the
/// caller. Subclasses provide the <see cref="ProcessStartInfo"/>, the
/// download filename, and the error-mapping logic.
/// </summary>
/// <remarks>
/// Subclasses today: <see cref="QuartoReportProducer"/> (Quarto +
/// LaTeX) and <see cref="RScriptReportProducer"/> (raw Rscript JSON
/// output). The base class buffers stdout into a memory stream so the
/// subprocess can finish before the response starts flowing — this
/// lets the error-handling path replace the response with a 5xx
/// ProblemDetails when the subprocess fails after partial output.
/// </remarks>
abstract partial class ExternalProcessReportProducer : IDataProducer
{
    public string MediaType { get; }
    protected IWebHostEnvironment Environment { get; }
    public ILogger Logger { get; }

    protected ExternalProcessReportProducer(string mediaType, IWebHostEnvironment environment, ILogger logger)
    {
        MediaType = mediaType;
        Environment = environment;
        Logger = logger;
    }

    protected abstract ProcessStartInfo GetProcessStartInfo();

    protected abstract ValueTask<DataResult> HandleError(int processId, int exitCode, Stream stdOutBuffer, string stdErrString, CancellationToken cancellationToken);

    protected abstract string? ReportFileDownloadName { get; }

    public async Task<DataResult> Generate(CancellationToken cancellationToken)
    {
        try
        {
            var bufferStream = new MemoryStream();
            var startInfo = GetProcessStartInfo();
            LogStartingProcess(Logger, startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory);
            using var reportGenerationProcess = Process.Start(startInfo);

            if (reportGenerationProcess == null)
                return new DataResult(detail: "The external process failed to start", statusCode: 500, title: "Internal Server Error", showMessage: Environment.IsDevelopment());

            var stdOut = reportGenerationProcess.StandardOutput.BaseStream.CopyToAsync(bufferStream, cancellationToken);
            var stdErr = reportGenerationProcess.StandardError.ReadToEndAsync(cancellationToken);

            await reportGenerationProcess.WaitForExitAsync(cancellationToken);

            if (reportGenerationProcess.ExitCode != 0)
            {
                await Task.WhenAll(stdOut, stdErr);
                var returnValue = await HandleError(reportGenerationProcess.Id, reportGenerationProcess.ExitCode, bufferStream, stdErr.Result, cancellationToken);
                if (!returnValue.Success)
                    return returnValue;
            }

            await stdOut;
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
            return new DataResult(e, showMessage: Environment.IsDevelopment());
        }
    }

    public abstract ValueTask DisposeAsync();
    [LoggerMessage(LogLevel.Debug, "Starting process for {process} with arguments {arguments} in directory {workingDirectory}.")]
    static partial void LogStartingProcess(ILogger logger, string process, string arguments, string workingDirectory);
}
