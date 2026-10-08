using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// <see cref="FileStorage"/> for the single admin-uploaded
/// validation-exception file (a CSV list of reviewed validation findings to
/// keep despite the rule that flags them). Mounted at
/// <see cref="ReportingOptions.ValidationExceptionsDir"/>.
/// </summary>
/// <remarks>
/// There is exactly one validation-exception file at a time: uploading
/// replaces it, and every report render applies it, except a Validation
/// Report an administrator renders without it. The file is stored under the
/// fixed <see cref="SingletonId"/> rather than a generated id, so the
/// no-argument helpers below address it without the caller tracking an id.
/// The base <see cref="FileStorage"/> id-keyed API still works (the
/// singleton id is a valid 32-hex id) but is not used outside these helpers.
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

    /// <summary>
    /// The stored file's metadata, from its sidecar, or, where the sidecar
    /// cannot be read (one written by hand, say), from the data file itself:
    /// its size, and the time it was last written as the time of its upload.
    /// <c>null</c> when no file is stored, or when neither can be read.
    /// </summary>
    /// <remarks>
    /// A file counts as stored while its sidecar exists, and the renders
    /// apply it whether or not the sidecar can be read, so the admin listing
    /// reports it in both cases rather than as absent.
    /// </remarks>
    public ValidationExceptionSidecar? ReadMetadata()
    {
        if (!Exists()) return null;
        try
        {
            using var fs = File.OpenRead(MetaPath());
            if (JsonSerializer.Deserialize<ValidationExceptionSidecar>(fs) is { } sidecar)
                return sidecar;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        var data = new FileInfo(DataPath());
        if (!data.Exists) return null;
        var writtenAt = new DateTimeOffset(data.LastWriteTimeUtc, TimeSpan.Zero);
        return new ValidationExceptionSidecar
        {
            DisplayName = DefaultDisplayName(writtenAt),
            ContentType = "text/csv",
            SizeBytes = data.Length,
            CreatedAt = writtenAt,
        };
    }

    /// <summary>The display name of a file uploaded at <paramref name="createdAt"/> without one.</summary>
    public static string DefaultDisplayName(DateTimeOffset createdAt) =>
        $"Validation exceptions {createdAt:yyyy-MM-dd HH:mm} UTC";
}
