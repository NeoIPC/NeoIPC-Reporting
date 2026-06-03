using Microsoft.AspNetCore.Mvc;

namespace NeoIPC.Reporting;

/// <summary>
/// Convenience helpers for producing RFC 7807 <c>application/problem+json</c>
/// responses from minimal-API handlers without each call site having to
/// instantiate <see cref="ProblemDetails"/> by hand.
/// </summary>
public static class ProblemDetailsHelper
{
    public static IResult BadRequest(string title, string detail) =>
        Results.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status400BadRequest,
        });

    public static IResult Forbidden(string title, string detail) =>
        Results.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status403Forbidden,
        });

    public static IResult NotFound(string title, string detail) =>
        Results.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status404NotFound,
        });

    public static IResult UnsupportedMediaType(string detail) =>
        Results.Problem(new ProblemDetails
        {
            Title = "Unsupported media type",
            Detail = detail,
            Status = StatusCodes.Status415UnsupportedMediaType,
        });
}
