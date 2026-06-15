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
/// The two <c>[ApiParameter]</c> fields carry concerns the QMD doesn't
/// see directly: the opaque ID for an admin-uploaded reference dataset
/// and the locale override (the latter on <see cref="ReportRequestBase"/>).
/// The handler resolves the opaque ID to a filesystem path and folds it
/// into <see cref="ReferenceReportRenderParameters"/> via <c>with</c>
/// before invoking the generator. The handler resolves the single
/// admin-uploaded validation-exception file (if any). Each content
/// figure/table is an explicit <c>includeX</c> render flag; the app maps
/// presets onto them client-side. Section-text inclusion is governed solely
/// by <see cref="IncludeIntroductionTexts"/> / <see cref="IncludeMethodsTexts"/>.
/// The Quarto profile is derived server-side from locale + output
/// format and is not part of the API surface.
/// </remarks>
public sealed partial record ReferenceReportApiParameters : ReportRequestBase
{
    [ApiParameter]
    public string? ReferenceDataId { get; init; }

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

    [RenderParameter("includeBirthWeightFigure")]
    public bool? IncludeBirthWeightFigure { get; init; }

    [RenderParameter("includeGestationalAgeFigure")]
    public bool? IncludeGestationalAgeFigure { get; init; }

    [RenderParameter("includeIncidenceDensityTable")]
    public bool? IncludeIncidenceDensityTable { get; init; }

    [RenderParameter("includeDeviceAssociatedIncidenceDensityTable")]
    public bool? IncludeDeviceAssociatedIncidenceDensityTable { get; init; }

    [RenderParameter("includeAgentPerInfectionRateTable")]
    public bool? IncludeAgentPerInfectionRateTable { get; init; }

    [RenderParameter("includeResistantPathogenInfectionRateTable")]
    public bool? IncludeResistantPathogenInfectionRateTable { get; init; }

    [RenderParameter("includeOrganismResistanceRateTable")]
    public bool? IncludeOrganismResistanceRateTable { get; init; }

    [RenderParameter("includeInfectiousAgentDetectionRateTable")]
    public bool? IncludeInfectiousAgentDetectionRateTable { get; init; }

    [RenderParameter("includeAntibioticResistanceTestRateTable")]
    public bool? IncludeAntibioticResistanceTestRateTable { get; init; }

    [RenderParameter("includeRiskDensityRateTable")]
    public bool? IncludeRiskDensityRateTable { get; init; }

    [RenderParameter("includeAntibioticUtilisationTable")]
    public bool? IncludeAntibioticUtilisationTable { get; init; }

    [RenderParameter("includeSurgicalProcedureRateTable")]
    public bool? IncludeSurgicalProcedureRateTable { get; init; }

    [RenderParameter("includeSecondaryBsiRateTable")]
    public bool? IncludeSecondaryBsiRateTable { get; init; }
}
