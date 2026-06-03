namespace NeoIPC.Reporting;

/// <summary>
/// Display modes for confidence intervals in rate tables.
/// </summary>
public enum ConfidenceIntervalMode
{
    /// <summary>Show CIs on every metric.</summary>
    All,

    /// <summary>Show CIs on rates only (per-1000-patient-day style); plain counts get no CI.</summary>
    Rate,

    /// <summary>Suppress CI columns entirely.</summary>
    None,
}

/// <summary>
/// Translates the C# enum to the lowercase strings the QMD's
/// <c>includeConfidenceIntervals</c> param expects (<c>"all"</c>,
/// <c>"rate"</c>, <c>"none"</c>). Wired up via
/// <c>[RenderParameter("includeConfidenceIntervals", Converter = typeof(ConfidenceIntervalConverter))]</c>;
/// the source generator emits the conversion call into <c>MapTo()</c>.
/// </summary>
public sealed class ConfidenceIntervalConverter : IQmdValueConverter<ConfidenceIntervalMode?, string?>
{
    public static string? Convert(ConfidenceIntervalMode? input) =>
        input switch
        {
            ConfidenceIntervalMode.All => "all",
            ConfidenceIntervalMode.Rate => "rate",
            ConfidenceIntervalMode.None => "none",
            _ => null,
        };
}
