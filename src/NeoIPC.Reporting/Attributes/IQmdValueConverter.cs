namespace NeoIPC.Reporting;

/// <summary>
/// Advisory marker for a value-converter type referenced from
/// <c>[RenderParameter(Converter = typeof(...))]</c>. Documents the
/// expected shape: a static <c>Convert(TFrom) → TTo</c> method.
/// </summary>
/// <remarks>
/// The source generator emits <c>ConverterType.Convert(value)</c> at the
/// call site, so the C# compiler enforces the signature regardless of
/// whether the type formally implements this interface — but
/// implementing it makes the contract discoverable and keeps the door
/// open for the generator to start enforcing it explicitly later.
/// </remarks>
public interface IQmdValueConverter<in TFrom, out TTo>
{
    static abstract TTo Convert(TFrom input);
}
