using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// <see cref="FileStorage"/> for admin-uploaded validation-exception
/// files (CSV whitelists of known false-positive validation findings,
/// referenced from report renders). Mounted at
/// <see cref="ReportingOptions.ValidationExceptionsDir"/>.
/// </summary>
public sealed class ValidationExceptionStorage : FileStorage
{
    public ValidationExceptionStorage(IOptions<ReportingOptions> options)
        : base(options.Value.ValidationExceptionsDir, "csv")
    {
    }
}
