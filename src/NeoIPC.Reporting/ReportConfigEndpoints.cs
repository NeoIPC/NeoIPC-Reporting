using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NeoIPC.Reporting;

/// <summary>
/// Minimal-API handlers for the two report-configuration endpoints the
/// app reads to drive its forms: the content <b>presets</b> and the
/// supported <b>locales</b>. Both derive from the report layer (the
/// Surveillance-Toolkit tree mounted at
/// <see cref="ReportingOptions.ReportsSourceDir"/>) rather than from the
/// .NET API surface, so they change with the report without an app or
/// backend release.
/// </summary>
public static class ReportConfigEndpoints
{
    /// <summary>
    /// Returns the named content presets for <paramref name="reportName"/>,
    /// read at request time from <c>{ReportsSourceDir}/{reportName}/presets.json</c>.
    /// The response body is the file's <c>presets</c> object verbatim — a
    /// map of preset name → the render-param overrides it sets (each
    /// preset lists only the params that differ from the QMD defaults).
    /// </summary>
    /// <remarks>
    /// The file is the single source of truth for the preset feature; the
    /// app applies the chosen preset client-side as <c>includeX</c> /
    /// confidence-interval / section-text render params. It is NOT a
    /// Quarto profile (profiles cannot set document params), so it is read
    /// as plain JSON here with no Quarto involvement.
    /// </remarks>
    public static IResult Presets(string reportName, IOptions<ReportingOptions> options)
    {
        // reportName is a fixed compile-time constant (the producer's
        // ReportName), never user input — no path-traversal surface.
        var path = Path.Combine(options.Value.ReportsSourceDir, reportName, "presets.json");
        if (!File.Exists(path))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "No presets",
                detail: $"No presets.json is present for report '{reportName}'.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("presets", out var presets))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Malformed presets",
                detail: $"presets.json for report '{reportName}' has no 'presets' object.");

        return Results.Text(presets.GetRawText(), "application/json");
    }

    /// <summary>
    /// Returns the language tags <paramref name="reportName"/> supports —
    /// the locales for which a <c>{Report}.&lt;lang&gt;.qmd</c> wrapper was
    /// found at warmup (the master English QMD's bare tag included),
    /// sorted for determinism. The app maps each tag to a human-readable
    /// language name client-side; the wire value stays the tag.
    /// </summary>
    public static IResult Locales(string reportName, ReportLanguageRegistry registry) =>
        Results.Ok(registry.ForReport(reportName).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
}
