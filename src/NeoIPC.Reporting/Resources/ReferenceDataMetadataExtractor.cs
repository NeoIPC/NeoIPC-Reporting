using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Reads the metadata block out of an admin-uploaded reference dataset
/// by shelling out to <c>scripts/extract-reference-data-metadata.R</c>,
/// which uses <c>jsonlite::unserializeJSON</c> to unwrap R's
/// <c>serializeJSON</c> format and <c>neoipcr::write_json</c> to emit a
/// plain-JSON projection that .NET can parse with
/// <see cref="JsonSerializer"/>.
/// </summary>
/// <remarks>
/// Why R-side extraction rather than .NET-side parsing: the dataset is
/// produced by <c>jsonlite::serializeJSON</c>, which wraps every value
/// in an R type descriptor (<c>{"type":...,"attributes":...,"value":...}</c>)
/// and only round-trips back through <c>jsonlite::unserializeJSON</c>.
/// Reimplementing that decoder in C# would be substantial work and
/// would couple the .NET service to an R-internal contract that can
/// shift; routing through neoipcr keeps the format coupling on one
/// side. See <c>tasks/neoipcr-plain-json-serialization.md</c> for the
/// follow-up that will eventually let datasets travel as plain JSON
/// directly, eliminating this extraction step.
/// </remarks>
public sealed class ReferenceDataMetadataExtractor
{
    readonly IOptions<ReportingOptions> _options;
    readonly IHostEnvironment _env;
    readonly ILogger<ReferenceDataMetadataExtractor> _logger;

    public ReferenceDataMetadataExtractor(
        IOptions<ReportingOptions> options,
        IHostEnvironment env,
        ILogger<ReferenceDataMetadataExtractor> logger)
    {
        _options = options;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Spawns the R extractor against <paramref name="datasetPath"/>,
    /// reads the resulting plain-JSON metadata back, and projects the
    /// neoipcr <c>dataset_options</c> shape (snake_case R names) to the
    /// .NET-side shape exposed by <see cref="ExtractedReferenceDataMetadata"/>.
    /// </summary>
    /// <remarks>
    /// On any non-zero exit the stderr is propagated as the result's
    /// error message so callers can return it as a 400 ProblemDetails.
    /// The temp output file is always cleaned up.
    /// </remarks>
    public async Task<ReferenceDataExtractionResult> ExtractAsync(
        string datasetPath, CancellationToken ct)
    {
        var scriptPath = ResolveScriptPath();
        var outPath = Path.Combine(
            _options.Value.ReportsTempDir,
            $"refdata-meta-{Guid.NewGuid():n}.json");

        var psi = new ProcessStartInfo
        {
            FileName = "Rscript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("--vanilla");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--in");
        psi.ArgumentList.Add(datasetPath);
        psi.ArgumentList.Add("--out");
        psi.ArgumentList.Add(outPath);

        if (_options.Value.BuildMode == BuildMode.Workspace)
            psi.Environment["NEOIPCR_DEV_PATH"] = "/neoipcr";

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Rscript.");
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Reference-data metadata extraction failed (exit {ExitCode}): {Stderr}",
                    proc.ExitCode, stderr);
                return ReferenceDataExtractionResult.Failed(stderr);
            }

            await using var fs = File.OpenRead(outPath);
            var extracted = await JsonSerializer.DeserializeAsync<ExtractedRoot>(fs, cancellationToken: ct);
            if (extracted is null)
                return ReferenceDataExtractionResult.Failed("Empty metadata output.");

            var ds = extracted.DatasetOptions;
            return ReferenceDataExtractionResult.Ok(new ExtractedReferenceDataMetadata(
                ReportingPeriodFrom: ds?.SurveillanceEndFrom,
                ReportingPeriodTo: ds?.SurveillanceEndTo,
                BirthWeightFrom: ds?.BirthWeightFrom,
                BirthWeightTo: ds?.BirthWeightTo,
                GestationalAgeFrom: ds?.GestationalAgeFrom,
                GestationalAgeTo: ds?.GestationalAgeTo,
                Countries: ds?.CountryFilter,
                IncludeTestUnits: ds?.IncludeTestData ?? false,
                IncludeNonCorePatients: ds?.IncludeIneligiblePatients ?? false,
                Calculated: extracted.Calculated));
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    string ResolveScriptPath() =>
        Path.Combine(_env.ContentRootPath, "scripts", "extract-reference-data-metadata.R");

    sealed record ExtractedRoot(
        [property: JsonPropertyName("calculated")] DateTimeOffset? Calculated,
        [property: JsonPropertyName("dataset_options")] ExtractedDatasetOptions? DatasetOptions);

    sealed record ExtractedDatasetOptions(
        [property: JsonPropertyName("surveillance_end_from")] DateOnly? SurveillanceEndFrom,
        [property: JsonPropertyName("surveillance_end_to")] DateOnly? SurveillanceEndTo,
        [property: JsonPropertyName("birth_weight_from")] int? BirthWeightFrom,
        [property: JsonPropertyName("birth_weight_to")] int? BirthWeightTo,
        [property: JsonPropertyName("gestational_age_from")] int? GestationalAgeFrom,
        [property: JsonPropertyName("gestational_age_to")] int? GestationalAgeTo,
        [property: JsonPropertyName("country_filter")] string[]? CountryFilter,
        [property: JsonPropertyName("include_test_data")] bool? IncludeTestData,
        [property: JsonPropertyName("include_ineligible_patients")] bool? IncludeIneligiblePatients);
}

/// <summary>
/// Filter-set values extracted from a reference dataset's metadata
/// block (with field names already projected to the .NET-side
/// vocabulary — neoipcr's <c>surveillance_end_*</c> becomes
/// <c>ReportingPeriod*</c>, <c>include_ineligible_patients</c> becomes
/// <c>IncludeNonCorePatients</c>, etc.).
/// </summary>
public sealed record ExtractedReferenceDataMetadata(
    DateOnly? ReportingPeriodFrom,
    DateOnly? ReportingPeriodTo,
    int? BirthWeightFrom,
    int? BirthWeightTo,
    int? GestationalAgeFrom,
    int? GestationalAgeTo,
    string[]? Countries,
    bool IncludeTestUnits,
    bool IncludeNonCorePatients,
    DateTimeOffset? Calculated);

/// <summary>
/// Outcome of <see cref="ReferenceDataMetadataExtractor.ExtractAsync"/>.
/// On <c>Success = false</c>, <see cref="ErrorMessage"/> carries the
/// stderr from the R subprocess (intended for surfacing in a
/// ProblemDetails response).
/// </summary>
public sealed record ReferenceDataExtractionResult(
    bool Success,
    ExtractedReferenceDataMetadata? Metadata,
    string? ErrorMessage)
{
    public static ReferenceDataExtractionResult Ok(ExtractedReferenceDataMetadata metadata) =>
        new(true, metadata, null);
    public static ReferenceDataExtractionResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
