using System.Globalization;

namespace NeoIPC.Reporting;

/// <summary>
/// Builds the CLI args for <c>Generate-PartnerData.R</c>. Used by the
/// JSON-output path (<see cref="RScriptPartnerReportGenerator"/>) which
/// returns the raw partner dataset without rendering Quarto.
/// </summary>
/// <remarks>
/// The Quarto online-mode flow does its own DHIS2 import inline from
/// <c>_setup.qmd</c> and does not invoke this builder.
/// </remarks>
public static class PartnerReportRScriptArgumentBuilder
{
    /// <summary>
    /// Yields the CLI args. When <paramref name="outputFilePath"/> is
    /// non-null, <c>--file</c> + path are emitted first so the R
    /// script writes JSON there instead of to stdout.
    /// </summary>
    public static IEnumerable<string> Build(PartnerReportRenderParameters p, string? outputFilePath)
    {
        if (outputFilePath is not null)
        {
            yield return "--file";
            yield return outputFilePath;
        }

        if (p.UnitCodes is { Length: > 0 })
        {
            yield return "--unitCodes";
            yield return string.Join(",", p.UnitCodes);
        }

        if (p.ReferenceDataFile is not null)
        {
            yield return "--referenceDataFile";
            yield return p.ReferenceDataFile;
        }

        if (p.ReportingPeriodFrom.HasValue)
        {
            yield return "--reportingPeriodFrom";
            yield return p.ReportingPeriodFrom.Value.ToString("o", CultureInfo.InvariantCulture);
        }
        if (p.ReportingPeriodTo.HasValue)
        {
            yield return "--reportingPeriodTo";
            yield return p.ReportingPeriodTo.Value.ToString("o", CultureInfo.InvariantCulture);
        }
        if (p.BirthWeightFrom.HasValue)
        {
            yield return "--birthWeightFrom";
            yield return p.BirthWeightFrom.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (p.BirthWeightTo.HasValue)
        {
            yield return "--birthWeightTo";
            yield return p.BirthWeightTo.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (p.GestationWeeksFrom.HasValue)
        {
            yield return "--gestationWeeksFrom";
            yield return p.GestationWeeksFrom.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (p.GestationWeeksTo.HasValue)
        {
            yield return "--gestationWeeksTo";
            yield return p.GestationWeeksTo.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (p.IncludeNonCorePatients == true)
            yield return "--includeNonCorePatients";
        if (p.IncludeTestData == true)
            yield return "--includeTestData";
        if (p.ValidationExceptionFile is not null)
        {
            yield return "--validationExceptionFile";
            yield return p.ValidationExceptionFile;
        }
        if (p.Dhis2Scheme is not null)
        {
            yield return "--scheme";
            yield return p.Dhis2Scheme;
        }
        if (p.Dhis2Hostname is not null)
        {
            yield return "--host";
            yield return p.Dhis2Hostname;
        }
        if (p.Dhis2Port.HasValue)
        {
            yield return "--port";
            yield return p.Dhis2Port.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (p.Dhis2Path is not null)
        {
            yield return "--path";
            yield return p.Dhis2Path;
        }
    }
}
