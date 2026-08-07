namespace NeoIPC.Reporting;

/// <summary>
/// Stable, machine-readable identifiers attached to every
/// <c>application/problem+json</c> response as the <c>code</c> extension
/// member. Unlike <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails.Title"/>
/// (English prose that may be reworded at any time), a code is a contract:
/// the app maps it to a localized, user-domain message. Add a constant here
/// when a new failure mode is introduced and map it in the app's
/// <c>problemDetails</c> helper. Uncoded errors (bare 406/415/500) fall back
/// to a generic message on the app side.
/// </summary>
public static class ProblemCodes
{
    // Report render — Partner + Reference
    public const string MissingUnitCodes = "missing-unit-codes";
    public const string MissingPartnerDataBody = "missing-partner-data-body";
    public const string InvalidConfidenceIntervals = "invalid-confidence-intervals";
    public const string InvalidReferenceDataFile = "invalid-reference-data-file";
    public const string InvalidReferenceDataId = "invalid-reference-data-id";
    public const string ReferenceDatasetNotFound = "reference-dataset-not-found";
    public const string UnsupportedLocale = "unsupported-locale";
    public const string MixedModeNotAllowed = "mixed-mode-not-allowed";
    /// <summary>
    /// An uploaded partner dataset was combined with parameters only a live fetch
    /// can honour. Distinct from <see cref="MixedModeNotAllowed"/>, which is about a
    /// stored <em>reference</em> dataset: a consumer maps each code to its own
    /// message, so one code covering both would name the wrong cause in the app.
    /// </summary>
    public const string UploadedDataFixesScope = "uploaded-data-fixes-scope";
    public const string InvalidParameterValue = "invalid-parameter-value";
    public const string NoAcceptableOutput = "no-acceptable-output";

    // Authorization
    public const string InsufficientAuthority = "insufficient-authority";

    // Admin resources (reference-data, validation-exceptions)
    public const string InvalidId = "invalid-id";
    public const string InvalidReferenceData = "invalid-reference-data";
    public const string DuplicateReferenceData = "duplicate-reference-data";
    public const string ResourceNotFound = "resource-not-found";
    public const string UnsupportedMediaType = "unsupported-media-type";
}
