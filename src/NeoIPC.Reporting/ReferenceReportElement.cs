namespace NeoIPC.Reporting;

/// <summary>
/// Stable user-facing names for the 13 fine-grained content elements
/// the Reference-Report can include or exclude. Projected onto the
/// QMD's <c>include*Figure</c> / <c>include*Table</c> flags by
/// <see cref="ReferenceReportProjection.Apply(ReferenceReportRenderParameters, ReferenceReportElement, bool)"/>.
/// </summary>
/// <remarks>
/// The PS-wrapper's grouped form (PatientPopulation, NosocomialInfections, …)
/// is deliberately not exposed here — the API speaks at the
/// fine-grained element level, and pre-configured groups (if useful)
/// become a UI convenience handled client-side.
/// </remarks>
public enum ReferenceReportElement
{
    BirthWeightFigure,
    GestationalAgeFigure,
    IncidenceDensityTable,
    DeviceAssociatedIncidenceDensityTable,
    AgentPerInfectionRateTable,
    ResistantPathogenInfectionRateTable,
    OrganismResistanceRateTable,
    InfectiousAgentDetectionRateTable,
    AntibioticResistanceTestRateTable,
    RiskDensityRateTable,
    AntibioticUtilisationTable,
    SurgicalProcedureRateTable,
    SecondaryBsiRateTable,
}

/// <summary>
/// Stable names for the 5 narrative section-text toggles. Projected
/// onto the QMD's <c>includeText*</c> flags by
/// <see cref="ReferenceReportProjection.Apply(ReferenceReportRenderParameters, ReferenceReportSectionText, bool)"/>.
/// </summary>
public enum ReferenceReportSectionText
{
    PatientPopulation,
    Nosocomial,
    InfectiousAgents,
    RiskFactors,
    Surgery,
}

/// <summary>
/// Projects element / section-text enum values onto the typed boolean
/// flags on <see cref="ReferenceReportRenderParameters"/>. Used by the
/// handler after <c>MapTo()</c> has populated the simple
/// <c>[RenderParameter]</c> mappings — the projections fold in the
/// many-to-many fan-out the source generator can't express.
/// </summary>
public static class ReferenceReportProjection
{
    public static ReferenceReportRenderParameters Apply(
        ReferenceReportRenderParameters p, ReferenceReportElement element, bool include) =>
        element switch
        {
            ReferenceReportElement.BirthWeightFigure => p with { IncludeBirthWeightFigure = include },
            ReferenceReportElement.GestationalAgeFigure => p with { IncludeGestationalAgeFigure = include },
            ReferenceReportElement.IncidenceDensityTable => p with { IncludeIncidenceDensityTable = include },
            ReferenceReportElement.DeviceAssociatedIncidenceDensityTable => p with { IncludeDeviceAssociatedIncidenceDensityTable = include },
            ReferenceReportElement.AgentPerInfectionRateTable => p with { IncludeAgentPerInfectionRateTable = include },
            ReferenceReportElement.ResistantPathogenInfectionRateTable => p with { IncludeResistantPathogenInfectionRateTable = include },
            ReferenceReportElement.OrganismResistanceRateTable => p with { IncludeOrganismResistanceRateTable = include },
            ReferenceReportElement.InfectiousAgentDetectionRateTable => p with { IncludeInfectiousAgentDetectionRateTable = include },
            ReferenceReportElement.AntibioticResistanceTestRateTable => p with { IncludeAntibioticResistanceTestRateTable = include },
            ReferenceReportElement.RiskDensityRateTable => p with { IncludeRiskDensityRateTable = include },
            ReferenceReportElement.AntibioticUtilisationTable => p with { IncludeAntibioticUtilisationTable = include },
            ReferenceReportElement.SurgicalProcedureRateTable => p with { IncludeSurgicalProcedureRateTable = include },
            ReferenceReportElement.SecondaryBsiRateTable => p with { IncludeSecondaryBsiRateTable = include },
            _ => p,
        };

    public static ReferenceReportRenderParameters Apply(
        ReferenceReportRenderParameters p, ReferenceReportSectionText section, bool include) =>
        section switch
        {
            ReferenceReportSectionText.PatientPopulation => p with { IncludeTextPatientPopulation = include },
            ReferenceReportSectionText.Nosocomial => p with { IncludeTextNosocomial = include },
            ReferenceReportSectionText.InfectiousAgents => p with { IncludeTextInfectiousAgents = include },
            ReferenceReportSectionText.RiskFactors => p with { IncludeTextRiskFactors = include },
            ReferenceReportSectionText.Surgery => p with { IncludeTextSurgery = include },
            _ => p,
        };
}
