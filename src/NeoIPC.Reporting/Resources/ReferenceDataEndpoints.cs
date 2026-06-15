using System.Security.Claims;
using System.Text.Json;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Minimal-API handlers for <c>/reference-data</c> (public listing) and
/// <c>/admin/reference-data</c> (full CRUD). The handlers themselves
/// don't enforce authorization — the <c>NeoIpcAdmin</c> policy on the
/// <c>/admin</c> route group does that.
/// </summary>
/// <remarks>
/// Uploads use the staged-commit lifecycle on
/// <see cref="FileStorage"/>: stream the body to a staging file → run
/// the metadata extractor against it → build the sidecar → commit
/// (atomic move into place). On any failure the staged file is
/// discarded.
/// </remarks>
public static class ReferenceDataEndpoints
{
    /// <summary>Public listing — abstracted metadata only, no admin-only fields.</summary>
    public static IResult List(ReferenceDataStorage storage)
    {
        var items = new List<PublicReferenceDataMetadata>();
        foreach (var id in storage.EnumerateIds())
        {
            var sidecar = ReadSidecar(storage, id);
            if (sidecar is not null)
                items.Add(PublicReferenceDataMetadata.From(id, sidecar));
        }
        return Results.Ok(items);
    }

    /// <summary>Admin listing — public fields plus size, content type, uploader id.</summary>
    public static IResult AdminList(ReferenceDataStorage storage)
    {
        var items = new List<AdminReferenceDataMetadata>();
        foreach (var id in storage.EnumerateIds())
        {
            var sidecar = ReadSidecar(storage, id);
            if (sidecar is not null)
                items.Add(AdminReferenceDataMetadata.From(id, sidecar));
        }
        return Results.Ok(items);
    }

    /// <summary>Admin download — streams the raw stored JSON.</summary>
    public static IResult AdminDownload(string id, ReferenceDataStorage storage)
    {
        if (!FileStorage.IsValidId(id))
            return ProblemDetailsHelper.BadRequest("Invalid id", "The id must be 32 hex characters.");
        if (!storage.Exists(id))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        var sidecar = ReadSidecar(storage, id);
        var contentType = sidecar?.ContentType ?? "application/json";
        return Results.File(storage.DataPath(id), contentType: contentType);
    }

    /// <summary>
    /// Admin upload — stages the body, runs the metadata extractor, builds
    /// the sidecar from the extracted filter set, and commits. Returns
    /// 415 when the Content-Type isn't <c>application/json</c>; 400 when
    /// the body fails extraction (likely not a valid reference dataset).
    /// </summary>
    public static async Task<IResult> AdminUpload(
        string? displayName,
        HttpRequest request,
        ReferenceDataStorage storage,
        ReferenceDataMetadataExtractor extractor,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!IsJsonContentType(request.ContentType))
            return Results.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported media type",
                detail: "Reference-data upload requires Content-Type: application/json.");

        var stagedPath = await storage.StageAsync(request.Body, ct);
        try
        {
            var extraction = await extractor.ExtractAsync(stagedPath, ct);
            if (!extraction.Success || extraction.Metadata is null)
                return ProblemDetailsHelper.BadRequest(
                    "Invalid reference data",
                    extraction.ErrorMessage ?? "The uploaded file is not a valid reference dataset.");

            var id = FileStorage.GenerateId();
            var fileInfo = new FileInfo(stagedPath);
            var createdAt = DateTimeOffset.UtcNow;
            var sidecar = new ReferenceDataSidecar
            {
                DisplayName = displayName ?? DefaultDisplayName(createdAt),
                ContentType = "application/json",
                SizeBytes = fileInfo.Length,
                UploaderUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedAt = createdAt,
                ReportingPeriodFrom = extraction.Metadata.ReportingPeriodFrom,
                ReportingPeriodTo = extraction.Metadata.ReportingPeriodTo,
                BirthWeightFrom = extraction.Metadata.BirthWeightFrom,
                BirthWeightTo = extraction.Metadata.BirthWeightTo,
                GestationalAgeFrom = extraction.Metadata.GestationalAgeFrom,
                GestationalAgeTo = extraction.Metadata.GestationalAgeTo,
                Countries = extraction.Metadata.Countries,
                IncludeTestUnits = extraction.Metadata.IncludeTestUnits,
                IncludeNonCorePatients = extraction.Metadata.IncludeNonCorePatients,
            };

            var sidecarJson = JsonSerializer.Serialize(sidecar);
            await storage.CommitAsync(id, stagedPath, sidecarJson, ct);
            return Results.Created($"/admin/reference-data/{id}",
                AdminReferenceDataMetadata.From(id, sidecar));
        }
        catch
        {
            storage.Discard(stagedPath);
            throw;
        }
    }

    /// <summary>Admin delete — removes both the data file and the sidecar.</summary>
    public static IResult AdminDelete(string id, ReferenceDataStorage storage)
    {
        if (!FileStorage.IsValidId(id))
            return ProblemDetailsHelper.BadRequest("Invalid id", "The id must be 32 hex characters.");
        if (!storage.Exists(id))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        storage.Delete(id);
        return Results.NoContent();
    }

    static ReferenceDataSidecar? ReadSidecar(ReferenceDataStorage storage, string id)
    {
        try
        {
            using var fs = File.OpenRead(storage.MetaPath(id));
            return JsonSerializer.Deserialize<ReferenceDataSidecar>(fs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static bool IsJsonContentType(string? contentType) =>
        !string.IsNullOrEmpty(contentType)
        && contentType.Split(';', 2)[0].Trim().Equals("application/json",
            StringComparison.OrdinalIgnoreCase);

    static string DefaultDisplayName(DateTimeOffset createdAt) =>
        $"Reference data {createdAt:yyyy-MM-dd HH:mm} UTC";
}
