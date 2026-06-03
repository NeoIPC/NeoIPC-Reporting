namespace NeoIPC.Reporting;

/// <summary>
/// Marks a property on a <c>&lt;Report&gt;ApiParameters</c> record as both
/// part of the public API surface AND a mapping to a Quarto-side <c>params:</c>
/// entry in the report's master <c>.qmd</c>.
/// </summary>
/// <remarks>
/// The <see cref="Name"/> argument is the QMD param name (camelCase, as
/// declared in the QMD's YAML <c>params:</c> block). The source generator
/// validates at compile time that the named param exists in the
/// corresponding generated <c>&lt;Report&gt;RenderParameters</c> record;
/// a typo or QMD-side rename produces a build error
/// (<c>NRP002 — RenderParameter references unknown param</c>).
///
/// <para>
/// The optional <see cref="Converter"/> is a type whose static
/// <c>Convert(TFrom) → TTo</c> method translates the API-surface value
/// to the QMD-native value (e.g. enum → string). The interface
/// <see cref="IQmdValueConverter{TFrom, TTo}"/> documents the expected
/// shape; the call site is what the C# compiler actually verifies.
/// </para>
///
/// <para>
/// Implies <see cref="ApiParameterAttribute"/> — there is no need to
/// place both attributes on the same property.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RenderParameterAttribute : Attribute
{
    public RenderParameterAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The QMD <c>params:</c> entry name this property maps to.</summary>
    public string Name { get; }

    /// <summary>
    /// Optional converter type. Its static <c>Convert</c> method translates
    /// the API value to the QMD-native value at <c>MapTo()</c> time.
    /// </summary>
    public Type? Converter { get; init; }
}
