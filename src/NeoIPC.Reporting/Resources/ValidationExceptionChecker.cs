using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Checks an uploaded validation-exception file before it is stored, so that
/// a list the reports cannot apply is refused at upload rather than found at
/// render time.
/// </summary>
public interface IValidationExceptionChecker
{
    /// <summary>
    /// The reason the file at <paramref name="path"/> is refused, naming it
    /// <paramref name="displayName"/> rather than by its path, or <c>null</c>
    /// when the reports can apply it. Throws when the check itself fails, a
    /// refusal without a reason included.
    /// </summary>
    Task<string?> CheckAsync(string path, string displayName, CancellationToken ct);
}

/// <summary>
/// Reads an uploaded validation-exception file with neoipcr's own reader, by
/// running <c>scripts/check-validation-exceptions.R</c>, which loads neoipcr
/// as the renders do; see that script for what it refuses.
/// </summary>
/// <remarks>
/// The reader is neoipcr's because the renders read the list with it: a
/// check of its own here could pass a file every render refuses, or refuse
/// one they apply.
/// </remarks>
public sealed class ValidationExceptionChecker(
    IOptions<ReportingOptions> options,
    IHostEnvironment env,
    ILogger<ValidationExceptionChecker> logger) : IValidationExceptionChecker
{
    // The script's exit status for a file it refuses, its reason on stdout.
    // R exits with 1 on an uncaught error and with 2 on a fatal error of its
    // own start-up (R_Suicide, as for a script it cannot open), so neither
    // can pass for a refusal.
    internal const int RefusedExitCode = 3;

    /// <summary>
    /// What the script's exit status says: <c>true</c> with a <c>null</c>
    /// reason when it accepts the file, <c>true</c> with the reason when it
    /// refuses it, and <c>false</c> when the check itself failed. A refusal
    /// without a reason is a failure too, since the script always gives one.
    /// </summary>
    internal static bool TryReadOutcome(int exitCode, string stdout, out string? reason)
    {
        reason = null;
        if (exitCode == 0)
            return true;
        if (exitCode != RefusedExitCode)
            return false;
        reason = stdout.ReplaceLineEndings("\n").Trim();
        return reason.Length > 0;
    }

    public async Task<string?> CheckAsync(string path, string displayName, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "Rscript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--vanilla");
        psi.ArgumentList.Add(Path.Combine(env.ContentRootPath, "scripts", "check-validation-exceptions.R"));
        psi.ArgumentList.Add("--in");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(displayName);
        // The reason goes to an administrator as neoipcr words it, in English
        // and in a UTF-8 locale the image generates; neoipcr comes from the
        // editable checkout in workspace mode, as for a render.
        var locale = RScriptReportProducer.DefaultLocale;
        psi.Environment["LANGUAGE"] = locale.Language;
        psi.Environment["LANG"] = locale.LcAll;
        psi.Environment["LC_ALL"] = locale.LcAll;
        if (options.Value.BuildMode == BuildMode.Workspace)
            psi.Environment["NEOIPCR_DEV_PATH"] = options.Value.NeoIpcrDevPath;
        else
            psi.Environment.Remove("NEOIPCR_DEV_PATH");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Rscript.");
        // Both streams are read while the process runs, so neither fills its
        // pipe and stalls the script.
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            // Disposing the Process does not end the OS process, so a check the
            // request timeout cancels would otherwise keep running.
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception killError)
            {
                logger.LogWarning(killError, "Failed to kill the cancelled validation-exception check.");
            }
            throw;
        }

        if (TryReadOutcome(proc.ExitCode, await stdout, out var reason))
            return reason;

        // Both streams: R writes a fatal error that precedes its console's
        // set-up, such as a script it cannot open, to stdout.
        logger.LogError(
            "The validation-exception check failed (exit {ExitCode}): {Stdout} {Stderr}",
            proc.ExitCode, await stdout, await stderr);
        throw new InvalidOperationException(
            $"The validation-exception check failed with exit status {proc.ExitCode}.");
    }
}
