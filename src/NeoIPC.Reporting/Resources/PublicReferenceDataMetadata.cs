namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Public-tier listing entry for a reference dataset. Returned by
/// <c>GET /reference-data</c> to any authenticated user, so partners
/// can pick a dataset by the filter set that shaped it without seeing
/// admin-only fields (size, content type, uploader id).
/// </summary>
public sealed record PublicReferenceDataMetadata(
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
    DateTimeOffset CreatedAt)
{
    public static PublicReferenceDataMetadata From(string id, ReferenceDataSidecar sidecar) =>
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
            CreatedAt: sidecar.CreatedAt);
}
