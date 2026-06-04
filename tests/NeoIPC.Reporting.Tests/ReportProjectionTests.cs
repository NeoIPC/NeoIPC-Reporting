using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class ReportProjectionTests
{
    [TestCase(ReferenceReportElement.BirthWeightFigure)]
    [TestCase(ReferenceReportElement.IncidenceDensityTable)]
    [TestCase(ReferenceReportElement.SecondaryBsiRateTable)]
    public void ReferenceProjection_ToggleRoundtrip(ReferenceReportElement element)
    {
        var enabled = ReferenceReportProjection.Apply(
            new ReferenceReportRenderParameters(), element, include: true);
        var disabled = ReferenceReportProjection.Apply(enabled, element, include: false);

        Assert.That(enabled, Is.Not.EqualTo(new ReferenceReportRenderParameters()),
            "applying include=true must change at least one property");
        Assert.That(disabled, Is.Not.EqualTo(enabled),
            "applying include=false must produce a different state from include=true");
    }

    [TestCase(ReferenceReportSectionText.PatientPopulation)]
    [TestCase(ReferenceReportSectionText.Surgery)]
    public void ReferenceProjection_SectionText_ToggleRoundtrip(ReferenceReportSectionText section)
    {
        var enabled = ReferenceReportProjection.Apply(
            new ReferenceReportRenderParameters(), section, include: true);
        var disabled = ReferenceReportProjection.Apply(enabled, section, include: false);

        Assert.That(enabled, Is.Not.EqualTo(new ReferenceReportRenderParameters()));
        Assert.That(disabled, Is.Not.EqualTo(enabled));
    }

    [TestCase(PartnerReportElement.BirthWeightFigure)]
    [TestCase(PartnerReportElement.SurgicalProcedureRateTable)]
    [TestCase(PartnerReportElement.SecondaryBsiRateTable)]
    public void PartnerProjection_ToggleRoundtrip(PartnerReportElement element)
    {
        var enabled = PartnerReportProjection.Apply(
            new PartnerReportRenderParameters(), element, include: true);
        var disabled = PartnerReportProjection.Apply(enabled, element, include: false);

        Assert.That(enabled, Is.Not.EqualTo(new PartnerReportRenderParameters()));
        Assert.That(disabled, Is.Not.EqualTo(enabled));
    }

    [Test]
    public void ReferenceProjection_AllElements_ChangeSomething()
    {
        // Catches any future enum value that's missing from the switch.
        var baseline = new ReferenceReportRenderParameters();
        foreach (ReferenceReportElement element in Enum.GetValues<ReferenceReportElement>())
        {
            var applied = ReferenceReportProjection.Apply(baseline, element, include: false);
            Assert.That(applied, Is.Not.EqualTo(baseline),
                $"element {element} did not change any property — likely missing from Apply switch");
        }
    }

    [Test]
    public void PartnerProjection_AllElements_ChangeSomething()
    {
        var baseline = new PartnerReportRenderParameters();
        foreach (PartnerReportElement element in Enum.GetValues<PartnerReportElement>())
        {
            var applied = PartnerReportProjection.Apply(baseline, element, include: false);
            Assert.That(applied, Is.Not.EqualTo(baseline),
                $"element {element} did not change any property — likely missing from Apply switch");
        }
    }
}
