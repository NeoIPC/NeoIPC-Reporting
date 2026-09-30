using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
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
/// and a rule added to the report needs no change to this service or to the
/// app; a released image picks it up with the reports release it pins.
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
/// quoted summary is never coerced. A plain (unquoted) summary the report's
/// own YAML reader takes for a null, a logical or a number, which fails the
/// render, makes the file malformed here too; see
/// <see cref="ReadsAsNonString"/>. The parsed catalogue is cached per language
/// and refreshed when either file's write time changes, which keeps a
/// workspace launch against an edited toolkit current while costing a
/// production instance, whose toolkit tree is read-only, one stat of each
/// file per request: one for English, two for any other language.
/// </para>
/// </remarks>
public sealed partial class ValidationRuleCatalogue
{
    const string ReportName = QuartoValidationReportProducer.ReportName;
    const string EnglishLanguage = "en";

    readonly IOptions<ReportingOptions> _options;
    readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    sealed record CacheEntry(DateTime BaseStamp, DateTime OverlayStamp, ImmutableArray<ValidationRule> Rules);

    /// <summary>
    /// Creates the catalogue over the toolkit tree at
    /// <see cref="ReportingOptions.ReportsSourceDir"/>. Nothing is read until
    /// <see cref="Rules"/> or <see cref="Ids"/> is first asked for.
    /// </summary>
    public ValidationRuleCatalogue(IOptions<ReportingOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// The rules in ascending id order, with each summary in
    /// <paramref name="language"/> where the toolkit carries that
    /// translation and in English otherwise.
    /// </summary>
    /// <exception cref="IOException">The report's string resources are not where the toolkit tree should hold them (<see cref="FileNotFoundException"/>), or cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The report's string resources cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">A string resource file does not have the shape the report itself depends on.</exception>
    public ImmutableArray<ValidationRule> Rules(string language)
    {
        // A FileInfo reads its file's state once, on first access, and answers
        // Exists and LastWriteTimeUtc from that one snapshot.
        var baseFile = new FileInfo(BasePath);
        if (!baseFile.Exists)
            throw new FileNotFoundException(
                "The Validation Report's string resources were not found in the toolkit tree.", baseFile.FullName);
        var overlayFile = string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : new FileInfo(OverlayPath(language));
        var overlayStamp = overlayFile is { Exists: true } ? overlayFile.LastWriteTimeUtc : NoOverlay;

        if (_cache.TryGetValue(language, out var cached)
            && cached.BaseStamp == baseFile.LastWriteTimeUtc && cached.OverlayStamp == overlayStamp)
            return cached.Rules;

        var summaries = ReadSummaries(baseFile.FullName, complete: true);
        if (overlayStamp != NoOverlay)
        {
            // An overlay gone between the snapshot and the read, or a link to
            // nothing, is read as absent: the language falls back to English.
            try
            {
                foreach (var (id, summary) in ReadSummaries(overlayFile!.FullName, complete: false))
                {
                    if (summaries.ContainsKey(id)) summaries[id] = summary;
                }
            }
            catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                overlayStamp = NoOverlay;
            }
        }

        ImmutableArray<ValidationRule> rules = [.. summaries.Select(kv => new ValidationRule(kv.Key, kv.Value))];
        _cache[language] = new CacheEntry(baseFile.LastWriteTimeUtc, overlayStamp, rules);
        return rules;
    }

    /// <summary>The ids of every rule the catalogue knows, ascending.</summary>
    public ImmutableArray<int> Ids => [.. Rules(EnglishLanguage).Select(r => r.Id)];

    static readonly DateTime NoOverlay = DateTime.MinValue;

    string BasePath => Path.Combine(
        _options.Value.ReportsSourceDir, ReportName, "content", "_sR.yaml");

    string OverlayPath(string language) => Path.Combine(
        _options.Value.ReportsSourceDir, ReportName, $"content.{language.ToLowerInvariant()}", "_sR.yaml");

    /// <summary>
    /// Reads <c>problems.&lt;id&gt;.summary</c> from one string resource file. The
    /// English source is <paramref name="complete"/>: every rule must carry a
    /// non-empty summary, since the report itself refuses to render otherwise.
    /// A translation may lack keys, whole rules or the <c>problems</c> mapping
    /// altogether, or carry a blank summary; what it lacks falls back to
    /// English in <see cref="Rules"/>. In either file, a summary the report's
    /// YAML reader does not read as one non-empty string fails the render, so
    /// it makes the file malformed: an empty one, one that is not a scalar, or
    /// a plain one read as a null, a logical or a number.
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
            if (summaryNode is YamlScalarNode { Style: ScalarStyle.Plain, Tag.IsEmpty: true } plain
                && ReadsAsNonString(plain.Value ?? string.Empty))
                throw Malformed(path,
                    $"rule {id} has an unquoted 'summary' the report reads as a null, a logical or a number; quote it");
            // The report's cascade takes a translation's summary over the
            // English one whatever it holds, and refuses to render unless it is
            // one non-empty string; blank text it renders as it stands.
            if (summaryNode is not YamlScalarNode { Value: { } summary })
                throw Malformed(path, $"rule {id} has a 'summary' that is not text");
            if (summary.Length == 0)
                throw Malformed(path, $"rule {id} has an empty 'summary'");
            if (string.IsNullOrWhiteSpace(summary))
            {
                if (complete) throw Malformed(path, $"rule {id} has a blank 'summary'");
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

    /// <summary>
    /// Whether the report's string-resource reader reads <paramref name="plainScalar"/>,
    /// the whole text of an untagged plain scalar, as something other than a
    /// string: a null, a logical, an integer or a double.
    /// </summary>
    /// <remarks>
    /// The reader is R's yaml package (2.3.12) with the handlers of
    /// <c>string_resource_handlers()</c> in the reports' <c>common/helpers.R</c>,
    /// which keep every YAML 1.1 boolean word as text except <c>true</c> and
    /// <c>false</c> in their three spellings. The package's implicit typing
    /// leaves, each as the whole scalar: the null spellings (empty, <c>~</c>,
    /// <c>null</c>, <c>Null</c>, <c>NULL</c>); those six boolean words and
    /// <c>.na</c>; integers in hexadecimal after a lower-case <c>0x</c>, in
    /// octal after a leading <c>0</c>, and in decimal, commas allowed among the
    /// digits, and <c>.na.integer</c>; doubles with a point, commas allowed
    /// before and after it, or with a point and an exponent that carries its
    /// sign, <c>.inf</c>, <c>-.inf</c> and <c>.nan</c> in three spellings each,
    /// and <c>.na.real</c>. A comma or an overflow yields <c>NA</c> of the
    /// same type, which fails the render all the same. Timestamps, sexagesimal
    /// numbers, underscores in numbers and the <c>0o</c>, <c>0b</c> and
    /// <c>0X</c> prefixes stay strings. So do <c>&lt;&lt;</c>, which the reader
    /// turns into <c>_yaml.merge_</c>, and <c>.na.character</c>, a missing
    /// string; neither fails the render.
    /// The set is measured: the reader's classification of
    /// <c>summary: &lt;token&gt;</c>, for every token of up to four characters
    /// over <c>0 1 7 8 9 . , - + e E x a F _ :</c> and for those spellings and
    /// their case variants, agrees with this expression on all 61,511 tokens
    /// the reader parses as either an unchanged string or one of those types.
    /// </remarks>
    internal static bool ReadsAsNonString(string plainScalar) => NonStringPlainScalar().IsMatch(plainScalar);

    [GeneratedRegex(
        @"\A(?:~|null|Null|NULL|true|True|TRUE|false|False|FALSE|\.na|\.na\.integer|\.na\.real"
        + @"|[-+]?0x[0-9a-fA-F,]+|[-+]?0[0-7,]+|[-+]?(?:0|[1-9][0-9,]*)"
        + @"|[-+]?(?:[0-9][0-9,]*)?\.[0-9,]*|[-+]?(?:[0-9][0-9,]*)?\.[0-9.]*[eE][-+][0-9]+"
        + @"|\+?\.(?:inf|Inf|INF)|-\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex NonStringPlainScalar();
}
