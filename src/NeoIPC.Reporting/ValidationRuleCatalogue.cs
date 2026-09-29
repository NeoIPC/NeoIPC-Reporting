using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Options;
using YamlDotNet.RepresentationModel;

namespace NeoIPC.Reporting;

/// <summary>One validation rule as the Validation Report describes it: its id and a placeholder-free summary of what it checks.</summary>
public sealed record ValidationRule(int Id, string Summary);

/// <summary>
/// The Validation Report's rule catalogue, read from the report's own string
/// resources rather than declared here: <c>problems.&lt;id&gt;.summary</c> in
/// <c>{ReportsSourceDir}/Validation-Report/content/_sR.yaml</c>, overlaid per
/// key by <c>content.&lt;language&gt;/_sR.yaml</c> where that translation
/// exists. This is the same cascade the report applies when it renders, so
/// the list a form shows and the rules the render knows come from one place,
/// and a rule added upstream reaches the app without a service release.
/// </summary>
/// <remarks>
/// <para>
/// A key the translation lacks keeps its English text, which is what the
/// report's own cascade does (<c>modifyList()</c> semantics). The territory
/// overlay the report also supports (<c>content.&lt;language&gt;_&lt;TERRITORY&gt;/</c>)
/// is not read: the service serves one territory per language, so a
/// territory-specific catalogue would first have to be introduced upstream.
/// </para>
/// <para>
/// The files are parsed with YamlDotNet's node API and read as scalars, so a
/// summary is never coerced (YAML 1.1 would otherwise turn a summary reading
/// <c>no</c> into a boolean). The parsed catalogue is cached per language
/// and refreshed when either file's write time changes, which keeps a
/// workspace launch against an edited toolkit current while costing a
/// production instance, whose toolkit tree is read-only, two stat calls per
/// request.
/// </para>
/// </remarks>
public sealed class ValidationRuleCatalogue
{
    public const string ReportName = QuartoValidationReportProducer.ReportName;
    const string EnglishLanguage = "en";

    readonly IOptions<ReportingOptions> _options;
    readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    sealed record CacheEntry(DateTime BaseStamp, DateTime OverlayStamp, ImmutableArray<ValidationRule> Rules);

    public ValidationRuleCatalogue(IOptions<ReportingOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// The rules in ascending id order, with each summary in
    /// <paramref name="language"/> where the toolkit carries that
    /// translation and in English otherwise.
    /// </summary>
    /// <exception cref="FileNotFoundException">The report's string resources are not where the toolkit tree should hold them.</exception>
    /// <exception cref="InvalidOperationException">A string resource file does not have the shape the report itself depends on.</exception>
    public ImmutableArray<ValidationRule> Rules(string language)
    {
        var basePath = BasePath;
        var overlayPath = string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : OverlayPath(language);

        var baseStamp = File.GetLastWriteTimeUtc(basePath);
        var overlayStamp = overlayPath is not null && File.Exists(overlayPath)
            ? File.GetLastWriteTimeUtc(overlayPath)
            : DateTime.MinValue;

        if (_cache.TryGetValue(language, out var cached)
            && cached.BaseStamp == baseStamp && cached.OverlayStamp == overlayStamp)
            return cached.Rules;

        var rules = Load(basePath, overlayStamp == DateTime.MinValue ? null : overlayPath);
        _cache[language] = new CacheEntry(baseStamp, overlayStamp, rules);
        return rules;
    }

    /// <summary>The ids of every rule the catalogue knows, ascending.</summary>
    public ImmutableArray<int> Ids => [.. Rules(EnglishLanguage).Select(r => r.Id)];

    string BasePath => Path.Combine(
        _options.Value.ReportsSourceDir, ReportName, "content", "_sR.yaml");

    string OverlayPath(string language) => Path.Combine(
        _options.Value.ReportsSourceDir, ReportName, $"content.{language.ToLowerInvariant()}", "_sR.yaml");

    static ImmutableArray<ValidationRule> Load(string basePath, string? overlayPath)
    {
        if (!File.Exists(basePath))
            throw new FileNotFoundException(
                "The Validation Report's string resources were not found in the toolkit tree.", basePath);

        var summaries = ReadSummaries(basePath, complete: true);
        if (overlayPath is not null)
        {
            foreach (var (id, summary) in ReadSummaries(overlayPath, complete: false))
            {
                if (summaries.ContainsKey(id)) summaries[id] = summary;
            }
        }

        return [.. summaries.Select(kv => new ValidationRule(kv.Key, kv.Value))];
    }

    /// <summary>
    /// Reads <c>problems.&lt;id&gt;.summary</c> from one string resource file. The
    /// English source is <paramref name="complete"/>: every rule must carry a
    /// non-empty summary, since the report itself refuses to render otherwise.
    /// A translation may lack keys, whole rules or the <c>problems</c> mapping
    /// altogether; what it lacks falls back to English in <see cref="Load"/>.
    /// </summary>
    static SortedDictionary<int, string> ReadSummaries(string path, bool complete)
    {
        var yaml = new YamlStream();
        try
        {
            using var reader = new StreamReader(path);
            yaml.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw Malformed(path, $"it is not valid YAML ({e.Message})");
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw Malformed(path, "the document is not a mapping");

        var result = new SortedDictionary<int, string>();
        if (!TryGet(root, "problems", out var problemsNode))
        {
            if (complete) throw Malformed(path, "it has no 'problems' mapping");
            return result;
        }
        if (problemsNode is not YamlMappingNode problems)
            throw Malformed(path, "'problems' is not a mapping");

        foreach (var (keyNode, valueNode) in problems.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } keyText }
                || !int.TryParse(keyText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                throw Malformed(path, $"the rule key '{keyNode}' is not a rule id");
            if (valueNode is not YamlMappingNode entry)
                throw Malformed(path, $"rule {id} is not a mapping");

            if (!TryGet(entry, "summary", out var summaryNode))
            {
                if (complete) throw Malformed(path, $"rule {id} has no 'summary'");
                continue;
            }
            if (summaryNode is not YamlScalarNode { Value: { } summary } || string.IsNullOrWhiteSpace(summary))
            {
                if (complete) throw Malformed(path, $"rule {id} has an empty 'summary'");
                continue;
            }
            result[id] = summary.Trim();
        }

        return result;
    }

    static bool TryGet(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var (k, v) in mapping.Children)
        {
            if (k is YamlScalarNode { Value: var text } && string.Equals(text, key, StringComparison.Ordinal))
            {
                value = v;
                return true;
            }
        }
        value = null!;
        return false;
    }

    static InvalidOperationException Malformed(string path, string why) =>
        new($"The Validation Report's string resources at '{path}' are malformed: {why}.");
}
