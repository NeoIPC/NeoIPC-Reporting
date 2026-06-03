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
/// </remarks>
public sealed partial record PartnerReportApiParameters : ReportRequestBase
{
    [ApiParameter]
    public string? ReferenceDataFile { get; init; }

    [ApiParameter]
    public string? Profile { get; init; }

    [ApiParameter]
    public string? ValidationExceptionFile { get; init; }

    [ApiParameter]
    public PartnerReportElement[]? EnabledElements { get; init; }

    [ApiParameter]
    public PartnerReportElement[]? DisabledElements { get; init; }

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
}
