using System.Security.Claims;
using System.Text.Json;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Minimal-API handlers for <c>/admin/validation-exceptions</c> — a
/// single admin-managed resource (there is one validation-exception
/// file at a time, applied to every report render unless an administrator
/// renders the Validation Report without it). No public-tier endpoint
/// exists; partners never select this file.
/// </summary>
/// <remarks>
/// The resource is a singleton, so the API has no id segment:
/// <list type="bullet">
///   <item><description><b>GET</b> — the current file's metadata, or 404
///   when none is uploaded.</description></item>
///   <item><description><b>PUT</b> — upload = idempotent create-or-replace
///   of the one file (the previous file, if any, is overwritten), after
///   <see cref="IValidationExceptionChecker"/> has accepted it: a file it
///   refuses is a 400 with <see cref="ProblemCodes.InvalidValidationExceptions"/>
///   and the reason, and the stored file stays as it was.</description></item>
///   <item><description><b>DELETE</b> — remove the file.</description></item>
/// </list>
/// Uploads accept any Content-Type, which the sidecar records. The check
/// reads the content as CSV whatever the type says, and the file is stored
/// with the <c>.csv</c> extension.
/// </remarks>
public static class ValidationExceptionEndpoints
{
    public static IResult AdminGet(ValidationExceptionStorage storage) =>
        storage.ReadMetadata() is { } metadata
            ? Results.Ok(AdminValidationExceptionMetadata.From(ValidationExceptionStorage.SingletonId, metadata))
            : ProblemDetailsHelper.NotFound(
                ProblemCodes.ResourceNotFound, "Not found",
                "No validation-exception file has been uploaded.");

    public static async Task<IResult> AdminUpload(
        string? displayName,
        HttpRequest request,
        ValidationExceptionStorage storage,
        IValidationExceptionChecker checker,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var contentType = string.IsNullOrEmpty(request.ContentType)
            ? "application/octet-stream"
            : request.ContentType;
        var createdAt = DateTimeOffset.UtcNow;
        var name = displayName ?? ValidationExceptionStorage.DefaultDisplayName(createdAt);

        var stagedPath = await storage.StageAsync(request.Body, ct);
        var committed = false;
        try
        {
            var refusal = await checker.CheckAsync(stagedPath, name, ct);
            if (refusal is not null)
                return ProblemDetailsHelper.BadRequest(
                    ProblemCodes.InvalidValidationExceptions,
                    "Invalid validation exceptions",
                    refusal);

            var sidecar = new ValidationExceptionSidecar
            {
                DisplayName = name,
                ContentType = contentType,
                SizeBytes = new FileInfo(stagedPath).Length,
                UploaderUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedAt = createdAt,
            };
            var sidecarJson = JsonSerializer.Serialize(sidecar);
            // Idempotent replace: CommitAsync moves into place with
            // overwrite, so re-uploading swaps the single stored file.
            await storage.CommitAsync(ValidationExceptionStorage.SingletonId, stagedPath, sidecarJson, ct);
            committed = true;
            return Results.Ok(AdminValidationExceptionMetadata.From(ValidationExceptionStorage.SingletonId, sidecar));
        }
        finally
        {
            // Any exit that did not commit, a refusal included, leaves the
            // staged file behind, so it is discarded here.
            if (!committed) storage.Discard(stagedPath);
        }
    }

    public static IResult AdminDelete(ValidationExceptionStorage storage)
    {
        if (!storage.Exists())
            return ProblemDetailsHelper.NotFound(
                ProblemCodes.ResourceNotFound, "Not found",
                "No validation-exception file has been uploaded.");
        storage.Delete();
        return Results.NoContent();
    }
}
