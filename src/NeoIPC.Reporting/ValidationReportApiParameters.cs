namespace NeoIPC.Reporting;

/// <summary>
/// Validation-Report API surface. Hand-written, paired with the
/// source-generator-emitted <see cref="ValidationReportRenderParameters"/>
/// (the QMD reflection) and the generator-emitted
/// <c>ValidationReportQuartoArgumentBuilder</c> (the per-format CLI
/// emission); <c>ParameterRecordGenerator</c> enforces at compile time that
/// every <c>[RenderParameter("name")]</c> names a real param in the QMD.
/// </summary>
/// <remarks>
/// The report validates live DHIS2 data and renders to HTML or PDF only; it
/// has no stored-dataset mode and no JSON data output, so the surface is
/// the QMD params a caller may set. The QMD's
/// <c>validationExceptionFile</c>, <c>validationExceptionFileUploadedAt</c>,
/// and <c>dhis2*</c> params are server-side: the handler folds the single
/// admin-uploaded exception file, the day it was uploaded, and the
/// configured DHIS2 endpoint into the render parameters via <c>with</c>.
/// The locale override lives on <see cref="ReportRequestBase"/>.
/// </remarks>
public sealed partial record ValidationReportApiParameters : ReportRequestBase
{
    /// <summary>
    /// Department codes (NeoIPC site codes) whose records are validated;
    /// absent = every department the DHIS2 session can see.
    /// </summary>
    [RenderParameter("departmentFilter")]
    public string[]? DepartmentFilter { get; init; }

    /// <summary>
    /// Ids of the validation rules to apply; absent = every rule. The handler
    /// refuses an id the report's rule catalogue does not know.
    /// </summary>
    [RenderParameter("rules")]
    public int[]? Rules { get; init; }

    /// <summary>Include the departments flagged as test units.</summary>
    [RenderParameter("includeTestData")]
    public bool? IncludeTestData { get; init; }

    /// <summary>
    /// Apply the stored validation-exception file; absent = applied. The
    /// handler refuses <c>false</c> from a caller without the F_NEOIPC_ADMIN
    /// authority.
    /// </summary>
    [RenderParameter("applyValidationExceptions")]
    public bool? ApplyValidationExceptions { get; init; }

    /// <summary>
    /// Add the appendix of the exception records for the departments in
    /// scope that match no record or exempt nothing; absent = not added. The
    /// appendix names patients, and the handler refuses <c>true</c> from a
    /// caller without the F_NEOIPC_ADMIN authority.
    /// </summary>
    [RenderParameter("includeUnusedValidationExceptions")]
    public bool? IncludeUnusedValidationExceptions { get; init; }
}
