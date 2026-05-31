using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace NeoIPC.Reporting;

sealed partial class QuartoReferenceReportGenerator :
    QuartoReportGenerator
{
    readonly ReferenceReportParameters _referenceReportParameters;

    public QuartoReferenceReportGenerator(string mediaType, string language, ReferenceReportParameters referenceReportParameters, IWebHostEnvironment environment, ILogger logger)
        : base(ReportsSourceDirConst, mediaType, language, referenceReportParameters.SessionId, environment, logger)
    {
        _referenceReportParameters = referenceReportParameters;
        ReportFileName = SupportedLanguageDictionary[language];
    }

    const string DirName = "Reference-Report";
    const string ReportsSourceDirConst = ReportsSourceDir + "/" + DirName;
    protected override string ReportFileDownloadName => "Reference-Report";
    protected override string ReportFileName { get; }

    protected override IEnumerable<string> GetReportParameters()
    {
        if (_referenceReportParameters.ReportingPeriodFrom.HasValue)
            yield return $"reportingPeriodFrom:{_referenceReportParameters.ReportingPeriodFrom:o}";

        if (_referenceReportParameters.ReportingPeriodTo.HasValue)
            yield return $"reportingPeriodTo:{_referenceReportParameters.ReportingPeriodTo:o}";

        if (_referenceReportParameters.BirthWeightFrom.HasValue)
            yield return $"birthWeightFrom:{_referenceReportParameters.BirthWeightFrom}";

        if (_referenceReportParameters.BirthWeightTo.HasValue)
            yield return $"birthWeightTo:{_referenceReportParameters.BirthWeightTo}";

        if (_referenceReportParameters.GestationalAgeFrom.HasValue)
            yield return $"gestationWeeksFrom:{_referenceReportParameters.GestationalAgeFrom}";

        if (_referenceReportParameters.GestationalAgeTo.HasValue)
            yield return $"gestationWeeksTo:{_referenceReportParameters.GestationalAgeTo}";

        if (!_referenceReportParameters.CountryFilter.IsDefaultOrEmpty)
            yield return $"reportingCountries:[{string.Join(",", _referenceReportParameters.CountryFilter)}]";

        if (!_referenceReportParameters.HospitalFilter.IsDefaultOrEmpty)
            yield return $"hospitalFilter:[{string.Join(",", _referenceReportParameters.HospitalFilter)}]";

        if (_referenceReportParameters.TestUnitFilter != null)
            yield return $"testUnitFilter:{_referenceReportParameters.TestUnitFilter}";

        if (_referenceReportParameters.DefaultPatientFilter != null)
            yield return $"defaultPatientFilter:{_referenceReportParameters.DefaultPatientFilter}";
    }

    public static readonly FrozenDictionary<string, string> SupportedLanguageDictionary;
    static QuartoReferenceReportGenerator()
    {
        var reportSourceDir = new DirectoryInfo(ReportsSourceDirConst);
        if (!reportSourceDir.Exists)
            throw new DirectoryNotFoundException($"Report directory '{reportSourceDir.FullName}' not found.");

        var t = new Dictionary<string, string> { { "en", "Reference-Report.qmd" }, { "en-GB", "Reference-Report.qmd" } };
        foreach (var file in reportSourceDir.EnumerateFiles("Reference-Report.*.qmd", SearchOption.TopDirectoryOnly))
        {
            var locale = GetReferenceReportTranslationFileRegex().Replace(file.Name, "$1");
            t.Add(locale, file.Name);
        }
        SupportedLanguageDictionary = t.ToFrozenDictionary(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"Reference-Report\.(.+)\.qmd")]
    private static partial Regex GetReferenceReportTranslationFileRegex();
}