namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Admin-tier listing entry for a reference dataset. Returned by
/// <c>GET /admin/reference-data</c>; carries the public fields plus
/// upload-bookkeeping (size, content type, uploader id) the operator
/// audience legitimately needs.
/// </summary>
public sealed record AdminReferenceDataMetadata(
    string Id,
    string DisplayName,
    DateOnly? ReportingPeriodFrom,
    DateOnly? ReportingPeriodTo,
    int? BirthWeightFrom,
    int? BirthWeightTo,
    int? GestationalAgeFrom,
    int? GestationalAgeTo,
    string[]? Countries,
    bool IncludeTestUnits,
    bool IncludeNonCorePatients,
    long SizeBytes,
    string ContentType,
    string? UploaderUserId,
    DateTimeOffset CreatedAt)
{
    public static AdminReferenceDataMetadata From(string id, ReferenceDataSidecar sidecar) =>
        new(
            Id: id,
            DisplayName: sidecar.DisplayName,
            ReportingPeriodFrom: sidecar.ReportingPeriodFrom,
            ReportingPeriodTo: sidecar.ReportingPeriodTo,
            BirthWeightFrom: sidecar.BirthWeightFrom,
            BirthWeightTo: sidecar.BirthWeightTo,
            GestationalAgeFrom: sidecar.GestationalAgeFrom,
            GestationalAgeTo: sidecar.GestationalAgeTo,
            Countries: sidecar.Countries,
            IncludeTestUnits: sidecar.IncludeTestUnits,
            IncludeNonCorePatients: sidecar.IncludeNonCorePatients,
            SizeBytes: sidecar.SizeBytes,
            ContentType: sidecar.ContentType,
            UploaderUserId: sidecar.UploaderUserId,
            CreatedAt: sidecar.CreatedAt);
}
