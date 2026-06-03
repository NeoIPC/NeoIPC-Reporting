namespace NeoIPC.Reporting;

/// <summary>
/// Marks a property on a <c>&lt;Report&gt;ApiParameters</c> record as part of the
/// public API surface for that report.
/// </summary>
/// <remarks>
/// Use on its own for parameters that drive .NET-side behaviour but do not
/// pass through to the QMD (e.g. <c>locale</c>, <c>profile</c>,
/// <c>referenceDataId</c>). For parameters that map to a QMD param, prefer
/// <see cref="RenderParameterAttribute"/> instead — it implies API-surface
/// inclusion and additionally drives the generated <c>MapTo()</c> mapper.
///
/// Properties carrying neither attribute are excluded from the
/// source-generator-emitted <c>Schema</c> and from <c>MapTo()</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ApiParameterAttribute : Attribute
{
}
