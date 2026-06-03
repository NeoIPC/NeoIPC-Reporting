using System.Globalization;

namespace NeoIPC.Reporting;

/// <summary>
/// Builds the CLI args for <c>Generate-ReferenceData.R</c>. Used by the
/// JSON-output path (<see cref="RScriptReferenceReportGenerator"/>) which
/// returns the raw reference dataset without rendering Quarto.
/// </summary>
/// <remarks>
/// Flag names match the script's <c>long_map</c> / <c>short_map</c>
/// (camelCase: <c>--reportingPeriodFrom</c>, <c>--birthWeightFrom</c>, …).
/// Hospital filtering is intentionally not forwarded — it is a Quarto
/// render-time concern, and the data-fetch script does not accept it.
/// The QMD's <c>testUnitFilter</c> / <c>defaultPatientFilter</c> map to
/// the script's <c>--includeTestUnits</c> / <c>--includeNonCorePatients</c>
/// presence flags with inverted semantics: the QMD bool is "apply the
/// filter" (default true → exclude); the script flag is "include in
/// dataset" (default absent → exclude). They agree only at the defaults,
/// so the flag is emitted iff the QMD bool is explicitly false.
/// </remarks>
public static class ReferenceReportRScriptArgumentBuilder
{
    public static IEnumerable<string> Build(ReferenceReportRenderParameters p)
    {
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

        if (p.ReportingCountries is { Length: > 0 })
        {
            yield return "--reportingCountries";
            yield return string.Join(",", p.ReportingCountries);
        }

        if (p.TestUnitFilter == false)
            yield return "--includeTestUnits";

        if (p.DefaultPatientFilter == false)
            yield return "--includeNonCorePatients";

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
