using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace NeoIPC.Reporting.Generators;

/// <summary>
/// Roslyn-side projection of one <c>&lt;Report&gt;ApiParameters</c>
/// record: the namespace, the type name, and the list of properties
/// the source generator should emit code for. Built once per record
/// declaration syntax node by <see cref="ApiSchemaExtractor"/>.
/// </summary>
internal sealed record ApiSchema(
    string Namespace,
    string TypeName,
    ImmutableArray<ApiPropertyMapping> Properties);

/// <summary>
/// One property's worth of metadata the generator needs:
/// the C# property name and type-display string, the optional QMD
/// param name (for <c>[RenderParameter]</c>-decorated properties)
/// and converter type (FQN), and pre-computed array / enum facts
/// captured from the type symbol so later generation steps can avoid
/// the symbol-API round-trip.
/// </summary>
internal sealed record ApiPropertyMapping(
    string PropertyName,
    string CSharpType,
    string? QmdName,
    string? ConverterFqn,
    bool IsArray,
    bool IsEnum,
    ImmutableArray<string> EnumValues,
    Location Location);

/// <summary>
/// Walks a <c>partial record</c> declaration's properties, collects
/// the ones marked <c>[ApiParameter]</c> or <c>[RenderParameter]</c>,
/// and projects them to the <see cref="ApiSchema"/> shape the
/// generator consumes downstream.
/// </summary>
internal static class ApiSchemaExtractor
{
    const string ApiParameterAttributeFqn = "NeoIPC.Reporting.ApiParameterAttribute";
    const string RenderParameterAttributeFqn = "NeoIPC.Reporting.RenderParameterAttribute";

    public static ApiSchema? TryExtract(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var recordSyntax = (RecordDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(recordSyntax, ct);
        if (symbol is not INamedTypeSymbol typeSymbol) return null;

        var properties = ImmutableArray.CreateBuilder<ApiPropertyMapping>();
        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol prop) continue;

            string? qmdName = null;
            string? converterFqn = null;
            var hasApiParameter = false;
            var hasRenderParameter = false;

            foreach (var attr in prop.GetAttributes())
            {
                var attrFqn = attr.AttributeClass?.ToDisplayString();
                if (attrFqn == ApiParameterAttributeFqn)
                {
                    hasApiParameter = true;
                }
                else if (attrFqn == RenderParameterAttributeFqn)
                {
                    hasRenderParameter = true;
                    if (attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is string nameValue)
                    {
                        qmdName = nameValue;
                    }
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "Converter" &&
                            named.Value.Value is INamedTypeSymbol converterType)
                        {
                            converterFqn = converterType.ToDisplayString();
                        }
                    }
                }
            }

            if (!hasApiParameter && !hasRenderParameter) continue;

            var (isArray, isEnum, enumValues) = AnalyseType(prop.Type);
            var location = prop.Locations.FirstOrDefault() ?? Location.None;
            properties.Add(new ApiPropertyMapping(
                prop.Name,
                prop.Type.ToDisplayString(),
                qmdName,
                converterFqn,
                isArray,
                isEnum,
                enumValues,
                location));
        }

        if (properties.Count == 0) return null;
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        return new ApiSchema(ns, typeSymbol.Name, properties.ToImmutable());
    }

    /// <summary>
    /// Inspects the property type to discover whether it's an array,
    /// whether the (optionally array-element) type is an enum, and if
    /// so what its declared values are. Used by the schema emitter to
    /// produce <c>character[]</c> rather than <c>character</c> for
    /// arrays, and to fill in the <c>values</c> column of the schema
    /// row for enum-typed properties.
    /// </summary>
    static (bool IsArray, bool IsEnum, ImmutableArray<string> EnumValues) AnalyseType(ITypeSymbol type)
    {
        var current = UnwrapNullable(type);
        var isArray = false;
        if (current is IArrayTypeSymbol arr)
        {
            isArray = true;
            current = UnwrapNullable(arr.ElementType);
        }

        if (current.TypeKind != TypeKind.Enum)
            return (isArray, false, ImmutableArray<string>.Empty);

        var values = current.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .Select(f => f.Name)
            .ToImmutableArray();
        return (isArray, true, values);
    }

    static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
        {
            return named.TypeArguments[0];
        }
        return type;
    }
}
