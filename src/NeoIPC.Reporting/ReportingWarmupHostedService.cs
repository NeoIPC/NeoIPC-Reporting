using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Startup service that prepares the filesystem layout the rendering
/// pipeline expects: the per-render temp root, the per-resource storage
/// directories, and the per-report language registry. The actual
/// per-render symlink layout is built lazily by
/// <see cref="QuartoReportProducer"/> on each request.
/// </summary>
/// <remarks>
/// <para>
/// Constructor-injecting <see cref="Dhis2Endpoint"/> forces DI to
/// build it at host startup; any validation failure aborts startup
/// rather than surfacing on the first request.
/// </para>
///
/// <para>
/// The service walks each report directory for
/// <c>{Report}.&lt;lang&gt;.qmd</c> filenames and registers what it
/// finds in <see cref="ReportLanguageRegistry"/>.
/// </para>
/// </remarks>
public sealed class ReportingWarmupHostedService : IHostedService
{
    readonly IOptions<ReportingOptions> _options;
    readonly ReportLanguageRegistry _registry;

    public ReportingWarmupHostedService(
        IOptions<ReportingOptions> options,
        ReportLanguageRegistry registry,
        Dhis2Endpoint dhis2Endpoint)
    {
        _ = dhis2Endpoint;
        _options = options;
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var sourceDir = new DirectoryInfo(opts.ReportsSourceDir);
        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException(
                $"Reports source directory '{sourceDir.FullName}' not found.");

        Directory.CreateDirectory(opts.ReportsTempDir);
        Directory.CreateDirectory(opts.ReferenceDataDir);
        Directory.CreateDirectory(opts.ValidationExceptionsDir);

        // A subdirectory under <src> is a "report" iff it contains a
        // <name>.qmd file at its top level. Anything else (common/,
        // filters/, logos/, …) is a shared resource and exposed by the
        // per-render layout, not the language registry.
        foreach (var reportSubdir in sourceDir.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Join(reportSubdir.FullName, $"{reportSubdir.Name}.qmd")))
                continue;
            RegisterReportLanguages(reportSubdir);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    void RegisterReportLanguages(DirectoryInfo reportDir)
    {
        var reportName = reportDir.Name;
        var baseQmd = $"{reportName}.qmd";
        var languages = new Dictionary<string, string>(StringComparer.Ordinal);

        if (File.Exists(Path.Join(reportDir.FullName, baseQmd)))
        {
            languages["en"] = baseQmd;
            languages["en-GB"] = baseQmd;
        }

        foreach (var file in reportDir.EnumerateFiles($"{reportName}.*.qmd",
                     SearchOption.TopDirectoryOnly))
        {
            var prefix = $"{reportName}.";
            var name = file.Name;
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".qmd", StringComparison.Ordinal)) continue;
            var locale = name[prefix.Length..^4];
            if (locale.Length == 0) continue;
            languages[locale] = name;
        }

        if (languages.Count > 0)
            _registry.Set(reportName, languages);
    }
}
