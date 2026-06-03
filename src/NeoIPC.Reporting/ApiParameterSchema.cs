namespace NeoIPC.Reporting;

/// <summary>
/// One row of the <c>GET /&lt;report&gt;/parameters</c> response — the
/// shape generated UIs (e.g. the future DHIS2 App Platform app) consume
/// to render dynamic forms per report.
/// </summary>
/// <remarks>
/// Emitted by the source generator from the
/// <c>[ApiParameter]</c> / <c>[RenderParameter]</c> annotations on
/// each report's API parameters record. <see cref="Type"/> uses the R
/// vocabulary (<c>character</c>, <c>integer</c>, <c>logical</c>,
/// <c>Date</c>, <c>character[]</c>) so consumers see the same naming
/// the QMDs themselves use.
/// </remarks>
public sealed record ApiParameterSchema(
    string Name,
    string Type,
    string? Default,
    string? Min,
    string? Max,
    string[]? Values,
    string? Description);
