using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// <see cref="FileStorage"/> for admin-uploaded reference datasets
/// (the JSON output of <c>Generate-ReferenceData.R</c>). Mounted at
/// <see cref="ReportingOptions.ReferenceDataDir"/>; data files keep
/// the <c>.json</c> extension.
/// </summary>
public sealed class ReferenceDataStorage : FileStorage
{
    public ReferenceDataStorage(IOptions<ReportingOptions> options)
        : base(options.Value.ReferenceDataDir, "json")
    {
    }
}
