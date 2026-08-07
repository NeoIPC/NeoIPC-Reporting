using Microsoft.AspNetCore.Mvc;

namespace NeoIPC.Reporting;

/// <summary>
/// Convenience helpers for producing RFC 7807 <c>application/problem+json</c>
/// responses from minimal-API handlers without each call site having to
/// instantiate <see cref="ProblemDetails"/> by hand. Every response carries a
/// stable <see cref="ProblemCodes">code</see> extension member so the app can
/// map the failure to a localized, user-domain message independently of the
/// English <c>Title</c>.
/// </summary>
public static class ProblemDetailsHelper
{
    public static IResult BadRequest(string code, string title, string detail) =>
        Problem(code, title, detail, StatusCodes.Status400BadRequest);

    public static IResult Forbidden(string code, string title, string detail) =>
        Problem(code, title, detail, StatusCodes.Status403Forbidden);

    public static IResult NotFound(string code, string title, string detail) =>
        Problem(code, title, detail, StatusCodes.Status404NotFound);

    public static IResult Conflict(string code, string title, string detail) =>
        Problem(code, title, detail, StatusCodes.Status409Conflict);

    public static IResult UnsupportedMediaType(string code, string detail) =>
        Problem(code, "Unsupported media type", detail, StatusCodes.Status415UnsupportedMediaType);

    /// <summary>
    /// Refuses a request whose proactive-negotiation headers (<c>Accept</c>,
    /// <c>Accept-Language</c>) admit no representation this resource can produce
    /// (RFC 9110 §15.5.7). Distinct from <see cref="UnsupportedMediaType"/>, which
    /// is about the media type of the <em>request</em> payload (§15.5.16).
    /// </summary>
    public static IResult NotAcceptable(string code, string detail) =>
        Problem(code, "Not acceptable", detail, StatusCodes.Status406NotAcceptable);

    static IResult Problem(string code, string title, string detail, int status) =>
        Results.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status,
            Extensions = { ["code"] = code },
        });
}
