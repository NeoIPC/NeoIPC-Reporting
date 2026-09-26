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
/// the three QMD params a caller may set. The QMD's
/// <c>validationExceptionFile</c> and <c>dhis2*</c> params are server-side:
/// the handler folds the single admin-uploaded exception file and the
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
}
