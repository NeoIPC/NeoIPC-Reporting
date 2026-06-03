namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Admin-tier listing entry for a validation-exception file. Returned
/// by <c>GET /admin/validation-exceptions</c>. There is no
/// public-tier equivalent — non-admin users have no selection use case
/// for these files.
/// </summary>
public sealed record AdminValidationExceptionMetadata(
    string Id,
    string DisplayName,
    long SizeBytes,
    string ContentType,
    string? UploaderUserId,
    DateTimeOffset CreatedAt)
{
    public static AdminValidationExceptionMetadata From(string id, ValidationExceptionSidecar sidecar) =>
        new(
            Id: id,
            DisplayName: sidecar.DisplayName,
            SizeBytes: sidecar.SizeBytes,
            ContentType: sidecar.ContentType,
            UploaderUserId: sidecar.UploaderUserId,
            CreatedAt: sidecar.CreatedAt);
}
