namespace NeoIPC.Reporting;

/// <summary>
/// Partner-Report element flags — the same 13 names as
/// <see cref="ReferenceReportElement"/> by design (Phase C plan
/// constraint). Kept distinct from the Reference enum so the two
/// reports can evolve independently if needed.
/// </summary>
public enum PartnerReportElement
{
    BirthWeightFigure,
    GestationalAgeFigure,
    IncidenceDensityTable,
    DeviceAssociatedIncidenceDensityTable,
    AgentPerInfectionRateTable,
    InfectiousAgentDetectionRateTable,
    RiskDensityRateTable,
    AntibioticUtilisationTable,
    SurgicalProcedureRateTable,
    ResistantPathogenInfectionRateTable,
    OrganismResistanceRateTable,
    AntibioticResistanceTestRateTable,
    SecondaryBsiRateTable,
}

/// <summary>
/// Projects <see cref="PartnerReportElement"/> values onto the typed
/// boolean flags on <see cref="PartnerReportRenderParameters"/>.
/// Partner-Report has no section-text-projection counterpart — its
/// QMD only exposes intro / methods / outlier-interpretation toggles,
/// which are mapped via direct <c>[RenderParameter]</c> annotations.
/// </summary>
public static class PartnerReportProjection
{
    public static PartnerReportRenderParameters Apply(
        PartnerReportRenderParameters p, PartnerReportElement element, bool include) =>
        element switch
        {
            PartnerReportElement.BirthWeightFigure => p with { IncludeBirthWeightFigure = include },
            PartnerReportElement.GestationalAgeFigure => p with { IncludeGestationalAgeFigure = include },
            PartnerReportElement.IncidenceDensityTable => p with { IncludeIncidenceDensityTable = include },
            PartnerReportElement.DeviceAssociatedIncidenceDensityTable => p with { IncludeDeviceAssociatedIncidenceDensityTable = include },
            PartnerReportElement.AgentPerInfectionRateTable => p with { IncludeAgentPerInfectionRateTable = include },
            PartnerReportElement.InfectiousAgentDetectionRateTable => p with { IncludeInfectiousAgentDetectionRateTable = include },
            PartnerReportElement.RiskDensityRateTable => p with { IncludeRiskDensityRateTable = include },
            PartnerReportElement.AntibioticUtilisationTable => p with { IncludeAntibioticUtilisationTable = include },
            PartnerReportElement.SurgicalProcedureRateTable => p with { IncludeSurgicalProcedureRateTable = include },
            PartnerReportElement.ResistantPathogenInfectionRateTable => p with { IncludeResistantPathogenInfectionRateTable = include },
            PartnerReportElement.OrganismResistanceRateTable => p with { IncludeOrganismResistanceRateTable = include },
            PartnerReportElement.AntibioticResistanceTestRateTable => p with { IncludeAntibioticResistanceTestRateTable = include },
            PartnerReportElement.SecondaryBsiRateTable => p with { IncludeSecondaryBsiRateTable = include },
            _ => p,
        };
}
