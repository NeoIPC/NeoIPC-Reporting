using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NeoIPC.Reporting.Generators;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Generator")]
public class ParameterRecordGeneratorTests
{
    // Synthetic ApiParameters source. Defines the attribute types inline so
    // the test compilation doesn't need to reference NeoIPC.Reporting — the
    // generator matches attributes by FQN string, not by symbol identity.
    const string AttributeShim = """
        namespace NeoIPC.Reporting;
        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class ApiParameterAttribute : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class RenderParameterAttribute : System.Attribute
        {
            public RenderParameterAttribute(string name) { Name = name; }
            public string Name { get; }
            public System.Type? Converter { get; set; }
        }
        """;

    static GeneratorDriverRunResult Run(
        string apiSource,
        string? schemaSource,
        string schemaPath = "Reference-Report.qmd-schema.json")
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(AttributeShim),
                CSharpSyntaxTree.ParseText(apiSource),
            },
            references: GetRuntimeReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = (GeneratorDriver)CSharpGeneratorDriver.Create(new ParameterRecordGenerator());
        if (schemaSource is not null)
        {
            driver = driver.AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(schemaPath, schemaSource)));
        }

        return driver.RunGenerators(compilation).GetRunResult();
    }

    static IEnumerable<MetadataReference> GetRuntimeReferences()
    {
        // Reference everything currently loaded that has a file location;
        // covers System.Runtime, the BCL primitives, and our generator deps.
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
    }

    [Test]
    public void EmitsRenderParametersRecordFromSnapshot()
    {
        var schema = """
            {
              "params": [
                { "qmdName": "birthWeightFrom", "rType": "integer", "defaultValue": null, "range": null, "values": [], "description": "" },
                { "qmdName": "reportingCountries", "rType": "character[]", "defaultValue": null, "range": null, "values": [], "description": "" }
              ]
            }
            """;

        var result = Run(apiSource: "// no api types in this test", schemaSource: schema);

        Assert.That(result.Diagnostics, Is.Empty, "no diagnostics expected for valid input");
        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("ReferenceReportRenderParameters.g.cs"))
            .ToString();
        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("public sealed partial record ReferenceReportRenderParameters"));
            Assert.That(generated, Does.Contain("public int? BirthWeightFrom"));
            Assert.That(generated, Does.Contain("public string[]? ReportingCountries"));
        });
    }

    [Test]
    public void EmitsMapToWhenApiParametersPresent()
    {
        var schema = """
            {
              "params": [
                { "qmdName": "birthWeightFrom", "rType": "integer", "defaultValue": null, "range": null, "values": [], "description": "" }
              ]
            }
            """;
        var apiSource = """
            namespace TestNs;
            public sealed partial record ReferenceReportApiParameters
            {
                [NeoIPC.Reporting.RenderParameter("birthWeightFrom")]
                public int? BirthWeightFrom { get; init; }
            }
            """;

        var result = Run(apiSource, schema);

        Assert.That(result.Diagnostics, Is.Empty);
        var apiPartial = result.GeneratedTrees
            .Single(t => t.ToString().Contains("MapTo"))
            .ToString();
        Assert.That(apiPartial, Does.Contain("BirthWeightFrom = BirthWeightFrom"));
    }

    [Test]
    public void EmitsNrp002_WhenRenderParameterReferencesUnknownQmdName()
    {
        var schema = """
            {
              "params": [
                { "qmdName": "birthWeightFrom", "rType": "integer", "defaultValue": null, "range": null, "values": [], "description": "" }
              ]
            }
            """;
        var apiSource = """
            namespace TestNs;
            public sealed partial record ReferenceReportApiParameters
            {
                [NeoIPC.Reporting.RenderParameter("typoNotInQmd")]
                public int? BirthWeightFrom { get; init; }
            }
            """;

        var result = Run(apiSource, schema);
        var ids = result.Diagnostics.Select(d => d.Id).ToArray();
        Assert.That(ids, Contains.Item("NRP002"));
    }

    [Test]
    public void EmitsNrp001_WhenRenderParameterPropertyExistsButNoSchema()
    {
        var apiSource = """
            namespace TestNs;
            public sealed partial record ReferenceReportApiParameters
            {
                [NeoIPC.Reporting.RenderParameter("birthWeightFrom")]
                public int? BirthWeightFrom { get; init; }
            }
            """;

        // Note: no AdditionalText supplied.
        var result = Run(apiSource, schemaSource: null);
        var ids = result.Diagnostics.Select(d => d.Id).ToArray();
        Assert.That(ids, Contains.Item("NRP001"));
    }

    [Test]
    public void ApiParameterOnly_PropertyDoesNotAppearInMapTo()
    {
        var schema = """
            {
              "params": [
                { "qmdName": "birthWeightFrom", "rType": "integer", "defaultValue": null, "range": null, "values": [], "description": "" }
              ]
            }
            """;
        var apiSource = """
            namespace TestNs;
            public sealed partial record ReferenceReportApiParameters
            {
                [NeoIPC.Reporting.ApiParameter]
                public string? Locale { get; init; }

                [NeoIPC.Reporting.RenderParameter("birthWeightFrom")]
                public int? BirthWeightFrom { get; init; }
            }
            """;

        var result = Run(apiSource, schema);
        Assert.That(result.Diagnostics, Is.Empty);

        var apiPartial = result.GeneratedTrees
            .Single(t => t.ToString().Contains("MapTo"))
            .ToString();
        Assert.Multiple(() =>
        {
            Assert.That(apiPartial, Does.Contain("BirthWeightFrom"));
            Assert.That(apiPartial, Does.Not.Contain("Locale = Locale"),
                "Locale is API-only ([ApiParameter]); must not flow into MapTo()");
        });
    }

    [Test]
    public void NewSchemaParam_NotReferencedFromApi_StillFlowsThroughWithDefault()
    {
        // A schema param that no [RenderParameter] property maps to should
        // still appear on the generated RenderParameters record so
        // downstream code can fold it in via `with`. The build succeeds.
        var schema = """
            {
              "params": [
                { "qmdName": "birthWeightFrom", "rType": "integer", "defaultValue": null, "range": null, "values": [], "description": "" },
                { "qmdName": "someUnreferencedNewParam", "rType": "character", "defaultValue": null, "range": null, "values": [], "description": "" }
              ]
            }
            """;
        var apiSource = """
            namespace TestNs;
            public sealed partial record ReferenceReportApiParameters
            {
                [NeoIPC.Reporting.RenderParameter("birthWeightFrom")]
                public int? BirthWeightFrom { get; init; }
            }
            """;

        var result = Run(apiSource, schema);
        Assert.That(result.Diagnostics, Is.Empty,
            "an unreferenced schema param is fine — it flows through with its default");

        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("ReferenceReportRenderParameters.g.cs"))
            .ToString();
        Assert.That(generated, Does.Contain("SomeUnreferencedNewParam"));
    }
}

/// <summary>
/// Adapter exposing an in-memory string as Roslyn's
/// <see cref="AdditionalText"/>. Used to feed synthetic schema snapshots
/// to the source generator from tests without touching the filesystem.
/// </summary>
sealed class InMemoryAdditionalText : AdditionalText
{
    readonly SourceText _text;
    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text);
    }
    public override string Path { get; }
    public override SourceText? GetText(CancellationToken cancellationToken = default) => _text;
}
