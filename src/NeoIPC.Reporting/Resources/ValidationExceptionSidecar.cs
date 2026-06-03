using System.Text.Json.Serialization;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// On-disk shape of the <c>{id}.meta.json</c> sidecar for a
/// validation-exception file. Property names are pinned by
/// <c>JsonPropertyName</c> so the disk format stays stable across .NET
/// property renames.
/// </summary>
public sealed record ValidationExceptionSidecar
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
}
