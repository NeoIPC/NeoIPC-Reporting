using System.Security.Claims;
using System.Text.Json;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Minimal-API handlers for <c>/admin/validation-exceptions</c> — a
/// single admin-managed resource (there is one validation-exception
/// file at a time, auto-applied to every report render). No public-tier
/// endpoint exists; partners never select this file.
/// </summary>
/// <remarks>
/// The resource is a singleton, so the API has no id segment:
/// <list type="bullet">
///   <item><description><b>GET</b> — the current file's metadata, or 404
///   when none is uploaded.</description></item>
///   <item><description><b>PUT</b> — upload = idempotent create-or-replace
///   of the one file (the previous file, if any, is overwritten).</description></item>
///   <item><description><b>DELETE</b> — remove the file.</description></item>
/// </list>
/// Uploads accept any Content-Type and record it on the sidecar so a
/// future download could serve the original type. Files are stored on
/// disk with the <c>.csv</c> extension regardless (the validation
/// pipeline only consumes CSV today).
/// </remarks>
public static class ValidationExceptionEndpoints
{
    public static IResult AdminGet(ValidationExceptionStorage storage)
    {
        if (!storage.Exists())
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Not found",
                detail: "No validation-exception file has been uploaded.");
        var sidecar = ReadSidecar(storage);
        if (sidecar is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        return Results.Ok(AdminValidationExceptionMetadata.From(ValidationExceptionStorage.SingletonId, sidecar));
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
            // Idempotent replace: CommitAsync moves into place with
            // overwrite, so re-uploading swaps the single stored file.
            await storage.CommitAsync(ValidationExceptionStorage.SingletonId, stagedPath, sidecarJson, ct);
            return Results.Ok(AdminValidationExceptionMetadata.From(ValidationExceptionStorage.SingletonId, sidecar));
        }
        catch
        {
            storage.Discard(stagedPath);
            throw;
        }
    }

    public static IResult AdminDelete(ValidationExceptionStorage storage)
    {
        if (!storage.Exists())
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        storage.Delete();
        return Results.NoContent();
    }

    static ValidationExceptionSidecar? ReadSidecar(ValidationExceptionStorage storage)
    {
        try
        {
            using var fs = File.OpenRead(storage.MetaPath());
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
