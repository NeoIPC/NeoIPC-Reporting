using System.Text.Json.Serialization;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// On-disk shape of the <c>{id}.meta.json</c> sidecar for a reference
/// dataset. Property names are pinned by <c>JsonPropertyName</c> so the
/// disk format stays stable across .NET property renames.
/// </summary>
/// <remarks>
/// The filter fields (<c>ReportingPeriod*</c>, <c>BirthWeight*</c>,
/// <c>GestationalAge*</c>, <c>Countries</c>, <c>IncludeTestUnits</c>,
/// <c>IncludeNonCorePatients</c>) are populated at upload time by
/// <see cref="ReferenceDataMetadataExtractor"/> from the dataset's own
/// metadata block. Operators / partner clients pick a dataset by these
/// fields when filling in <c>?referenceDataId=…</c> on the report
/// endpoints.
/// </remarks>
public sealed record ReferenceDataSidecar
{
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("contentType")]
    public required string ContentType { get; init; }

    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("uploaderUserId")]
    public string? UploaderUserId { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("reportingPeriodFrom")]
    public DateOnly? ReportingPeriodFrom { get; init; }

    [JsonPropertyName("reportingPeriodTo")]
    public DateOnly? ReportingPeriodTo { get; init; }

    [JsonPropertyName("birthWeightFrom")]
    public int? BirthWeightFrom { get; init; }

    [JsonPropertyName("birthWeightTo")]
    public int? BirthWeightTo { get; init; }

    [JsonPropertyName("gestationalAgeFrom")]
    public int? GestationalAgeFrom { get; init; }

    [JsonPropertyName("gestationalAgeTo")]
    public int? GestationalAgeTo { get; init; }

    [JsonPropertyName("countries")]
    public string[]? Countries { get; init; }

    [JsonPropertyName("includeTestUnits")]
    public bool IncludeTestUnits { get; init; }

    [JsonPropertyName("includeNonCorePatients")]
    public bool IncludeNonCorePatients { get; init; }
}
