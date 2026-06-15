using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// <see cref="FileStorage"/> for the single admin-uploaded
/// validation-exception file (a CSV whitelist of known false-positive
/// validation findings, auto-applied to every report render). Mounted
/// at <see cref="ReportingOptions.ValidationExceptionsDir"/>.
/// </summary>
/// <remarks>
/// There is exactly one validation-exception file at a time: uploading
/// replaces it, and every report render folds it in automatically. The
/// file is stored under the fixed <see cref="SingletonId"/> rather than
/// a generated id, so the no-argument helpers below address it without
/// the caller tracking an id. The base <see cref="FileStorage"/>
/// id-keyed API still works (the singleton id is a valid 32-hex id) but
/// is not used outside these helpers.
/// </remarks>
public sealed class ValidationExceptionStorage : FileStorage
{
    /// <summary>
    /// The fixed id under which the one validation-exception file is
    /// stored (all-zero — a valid 32-hex id that <see cref="FileStorage.IsValidId"/>
    /// accepts but <see cref="FileStorage.GenerateId"/> never produces).
    /// </summary>
    public const string SingletonId = "00000000000000000000000000000000";

    public ValidationExceptionStorage(IOptions<ReportingOptions> options)
        : base(options.Value.ValidationExceptionsDir, "csv")
    {
    }

    /// <summary>True iff the validation-exception file is present.</summary>
    public bool Exists() => Exists(SingletonId);

    /// <summary>Resolves the data-file path of the validation-exception file.</summary>
    public string DataPath() => DataPath(SingletonId);

    /// <summary>Resolves the metadata-sidecar path of the validation-exception file.</summary>
    public string MetaPath() => MetaPath(SingletonId);

    /// <summary>Removes the validation-exception file (and its sidecar). No-op when absent.</summary>
    public void Delete() => Delete(SingletonId);
}
