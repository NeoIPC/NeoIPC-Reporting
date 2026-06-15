namespace NeoIPC.Reporting;

/// <summary>
/// Partner-Report API surface. Same two-tier pattern as
/// <see cref="ReferenceReportApiParameters"/> — hand-written here,
/// paired with the source-generator-emitted
/// <see cref="PartnerReportRenderParameters"/>.
/// </summary>
/// <remarks>
/// Note that <c>partnerDataFile</c> is NOT exposed as an API
/// parameter: the dataset is either generated transiently on the fly
/// in online mode (GET — handler runs <c>Generate-PartnerData.R</c>
/// against the per-render workdir) or supplied as the request body in
/// dataFile mode (POST — handler streams body into the workdir). In
/// both cases the path is set by the handler, never by the caller.
/// <see cref="ReferenceDataFile"/> is the opaque ID of an admin-uploaded
/// reference benchmark; same resolution mechanism as Reference-Report.
/// The handler resolves the single admin-uploaded validation-exception
/// file (if any) and folds its path into the render parameters. Each
/// content figure/table is an explicit <c>includeX</c> render flag;
/// the app maps presets onto them client-side. The Quarto profile is
/// derived server-side from locale + output format and is likewise
/// not part of the API surface.
/// </remarks>
public sealed partial record PartnerReportApiParameters : ReportRequestBase
{
    [ApiParameter]
    public string? ReferenceDataFile { get; init; }

    [RenderParameter("unitCodes")]
    public string[]? UnitCodes { get; init; }

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

    [RenderParameter("includeNonCorePatients")]
    public bool? IncludeNonCorePatients { get; init; }

    [RenderParameter("includeTestData")]
    public bool? IncludeTestData { get; init; }

    [RenderParameter("sparseDataThreshold")]
    public ushort? SparseDataThreshold { get; init; }

    [RenderParameter("includeConfidenceIntervals", Converter = typeof(ConfidenceIntervalConverter))]
    public ConfidenceIntervalMode? ConfidenceIntervals { get; init; }

    [RenderParameter("includeIntroductionTexts")]
    public bool? IncludeIntroductionTexts { get; init; }

    [RenderParameter("includeMethodsTexts")]
    public bool? IncludeMethodsTexts { get; init; }

    [RenderParameter("includeOutlierInterpretation")]
    public bool? IncludeOutlierInterpretation { get; init; }

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

    [RenderParameter("includeInfectiousAgentDetectionRateTable")]
    public bool? IncludeInfectiousAgentDetectionRateTable { get; init; }

    [RenderParameter("includeRiskDensityRateTable")]
    public bool? IncludeRiskDensityRateTable { get; init; }

    [RenderParameter("includeAntibioticUtilisationTable")]
    public bool? IncludeAntibioticUtilisationTable { get; init; }

    [RenderParameter("includeSurgicalProcedureRateTable")]
    public bool? IncludeSurgicalProcedureRateTable { get; init; }

    [RenderParameter("includeResistantPathogenInfectionRateTable")]
    public bool? IncludeResistantPathogenInfectionRateTable { get; init; }

    [RenderParameter("includeOrganismResistanceRateTable")]
    public bool? IncludeOrganismResistanceRateTable { get; init; }

    [RenderParameter("includeAntibioticResistanceTestRateTable")]
    public bool? IncludeAntibioticResistanceTestRateTable { get; init; }

    [RenderParameter("includeSecondaryBsiRateTable")]
    public bool? IncludeSecondaryBsiRateTable { get; init; }
}
