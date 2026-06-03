using System.Security.Claims;
using System.Text.Json;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Minimal-API handlers for <c>/admin/validation-exceptions</c>.
/// No public-tier endpoint exists — non-admin users have no selection
/// use case for these files; partners receive their assigned exception
/// file by id from the operator.
/// </summary>
/// <remarks>
/// Uploads accept any Content-Type and record it on the sidecar so
/// downloads can serve the file with the original type. Files are
/// stored on disk with the <c>.csv</c> extension regardless (the
/// validation pipeline only consumes CSV today); the recorded
/// content-type drives the download Content-Type, not the on-disk
/// extension.
/// </remarks>
public static class ValidationExceptionEndpoints
{
    public static IResult AdminList(ValidationExceptionStorage storage)
    {
        var items = new List<AdminValidationExceptionMetadata>();
        foreach (var id in storage.EnumerateIds())
        {
            var sidecar = ReadSidecar(storage, id);
            if (sidecar is not null)
                items.Add(AdminValidationExceptionMetadata.From(id, sidecar));
        }
        return Results.Ok(items);
    }

    public static IResult AdminDownload(string id, ValidationExceptionStorage storage)
    {
        if (!FileStorage.IsValidId(id))
            return ProblemDetailsHelper.BadRequest("Invalid id", "The id must be 32 hex characters.");
        if (!storage.Exists(id))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        var sidecar = ReadSidecar(storage, id);
        var contentType = sidecar?.ContentType ?? "application/octet-stream";
        return Results.File(storage.DataPath(id), contentType: contentType);
    }

    public static async Task<IResult> AdminUpload(
        string? displayName,
        HttpRequest request,
        ValidationExceptionStorage storage,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var contentType = string.IsNullOrEmpty(request.ContentType)
            ? "application/octet-stream"
            : request.ContentType;

        var stagedPath = await storage.StageAsync(request.Body, ct);
        try
        {
            var id = FileStorage.GenerateId();
            var fileInfo = new FileInfo(stagedPath);
            var createdAt = DateTimeOffset.UtcNow;
            var sidecar = new ValidationExceptionSidecar
            {
                DisplayName = displayName ?? DefaultDisplayName(createdAt),
                ContentType = contentType,
                SizeBytes = fileInfo.Length,
                UploaderUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedAt = createdAt,
            };
            var sidecarJson = JsonSerializer.Serialize(sidecar);
            await storage.CommitAsync(id, stagedPath, sidecarJson, ct);
            return Results.Created($"/admin/validation-exceptions/{id}",
                AdminValidationExceptionMetadata.From(id, sidecar));
        }
        catch
        {
            storage.Discard(stagedPath);
            throw;
        }
    }

    public static IResult AdminDelete(string id, ValidationExceptionStorage storage)
    {
        if (!FileStorage.IsValidId(id))
            return ProblemDetailsHelper.BadRequest("Invalid id", "The id must be 32 hex characters.");
        if (!storage.Exists(id))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        storage.Delete(id);
        return Results.NoContent();
    }

    static ValidationExceptionSidecar? ReadSidecar(ValidationExceptionStorage storage, string id)
    {
        try
        {
            using var fs = File.OpenRead(storage.MetaPath(id));
            return JsonSerializer.Deserialize<ValidationExceptionSidecar>(fs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string DefaultDisplayName(DateTimeOffset createdAt) =>
        $"Validation exceptions {createdAt:yyyy-MM-dd HH:mm} UTC";
}
