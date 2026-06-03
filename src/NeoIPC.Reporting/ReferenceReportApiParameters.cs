namespace NeoIPC.Reporting;

/// <summary>
/// Reference-Report API surface. Hand-written, paired with the
/// source-generator-emitted <see cref="ReferenceReportRenderParameters"/>
/// (the QMD reflection) and the generator-emitted
/// <c>ReferenceReportQuartoArgumentBuilder</c> (the per-format CLI
/// emission). The pairing is enforced at compile time by
/// <c>ParameterRecordGenerator</c>: every <c>[RenderParameter("name")]</c>
/// must reference a real param in the QMD.
/// </summary>
/// <remarks>
/// API-only fields (<c>[ApiParameter]</c>) carry concerns the QMD
/// doesn't see directly: the opaque ID for an admin-uploaded reference
/// dataset, the locale override, the Quarto profile, the opaque ID for
/// a validation-exception file, and the element / section-text
/// projections. The handler resolves opaque IDs to filesystem paths,
/// applies the projections, and folds the results into
/// <see cref="ReferenceReportRenderParameters"/> via <c>with</c> before
/// invoking the generator.
/// </remarks>
public sealed partial record ReferenceReportApiParameters : ReportRequestBase
{
    [ApiParameter]
    public string? ReferenceDataId { get; init; }

    [ApiParameter]
    public string? Profile { get; init; }

    [ApiParameter]
    public string? ValidationExceptionFile { get; init; }

    [ApiParameter]
    public ReferenceReportElement[]? EnabledElements { get; init; }

    [ApiParameter]
    public ReferenceReportElement[]? DisabledElements { get; init; }

    [ApiParameter]
    public ReferenceReportSectionText[]? EnabledSectionTexts { get; init; }

    [ApiParameter]
    public ReferenceReportSectionText[]? DisabledSectionTexts { get; init; }

    [RenderParameter("reportingPeriodFrom")]
    public DateOnly? ReportingPeriodFrom { get; init; }

    [RenderParameter("reportingPeriodTo")]
    public DateOnly? ReportingPeriodTo { get; init; }

    [RenderParameter("birthWeightFrom")]
    public ushort? BirthWeightFrom { get; init; }

    [RenderParameter("birthWeightTo")]
    public ushort? BirthWeightTo { get; init; }

    [RenderParameter("gestationWeeksFrom")]
    public ushort? GestationalAgeFrom { get; init; }

    [RenderParameter("gestationWeeksTo")]
    public ushort? GestationalAgeTo { get; init; }

    [RenderParameter("reportingCountries")]
    public string[]? CountryFilter { get; init; }

    [RenderParameter("hospitalFilter")]
    public string[]? HospitalFilter { get; init; }

    [RenderParameter("testUnitFilter")]
    public bool? TestUnitFilter { get; init; }

    [RenderParameter("defaultPatientFilter")]
    public bool? DefaultPatientFilter { get; init; }

    [RenderParameter("sparseDataThreshold")]
    public ushort? SparseDataThreshold { get; init; }

    [RenderParameter("includeConfidenceIntervals", Converter = typeof(ConfidenceIntervalConverter))]
    public ConfidenceIntervalMode? ConfidenceIntervals { get; init; }

    [RenderParameter("includeIntroductionTexts")]
    public bool? IncludeIntroductionTexts { get; init; }

    [RenderParameter("includeMethodsTexts")]
    public bool? IncludeMethodsTexts { get; init; }
}
