using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace NeoIPC.Reporting.Generators;

/// <summary>
/// One report's QMD <c>params:</c> block, parsed into the shape the
/// generator emits a <c>partial record &lt;Report&gt;RenderParameters</c>
/// from. <see cref="RecordName"/> is derived from the QMD filename
/// (e.g. <c>Reference-Report.qmd</c> → <c>ReferenceReportRenderParameters</c>).
/// </summary>
internal sealed record QmdSchema(
    string RecordName,
    string SourceFileName,
    ImmutableArray<QmdParameter> Parameters);

/// <summary>
/// One <c>params:</c> entry from a QMD file. Type information comes
/// from per-param annotation comments — <c># @type integer</c>,
/// <c># @range 0..65535</c>, <c># @values [a, b, c]</c> — falling back
/// to inference from the YAML default value when the annotation is
/// absent. Free-form description lines (any leading comment block
/// without an <c>@</c> tag) become the <c>&lt;summary&gt;</c> of the
/// generated property.
/// </summary>
internal sealed record QmdParameter(
    string QmdName,
    string PropertyName,
    string CSharpType,
    string RType,
    string? DefaultValue,
    string Description,
    string? Range,
    ImmutableArray<string> Values);

/// <summary>
/// Reads a QMD file from <c>&lt;AdditionalFiles&gt;</c> and parses its
/// YAML <c>params:</c> block plus the surrounding annotation comments
/// into a <see cref="QmdSchema"/>. Robust enough for the toolkit's
/// param syntax; deliberately not a general YAML parser.
/// </summary>
internal static class QmdParser
{
    public static QmdSchema? Parse(AdditionalText file, CancellationToken cancellationToken)
    {
        var sourceText = file.GetText(cancellationToken);
        if (sourceText is null) return null;
        var content = sourceText.ToString();

        var (paramsStart, paramsEnd) = FindParamsBlock(content);
        if (paramsStart < 0) return null;

        var lines = content.Split('\n');
        var parameters = ImmutableArray.CreateBuilder<QmdParameter>();
        var description = new List<string>();
        string? rType = null;
        string? range = null;
        var values = ImmutableArray<string>.Empty;

        for (int i = paramsStart; i < paramsEnd; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];
            var trimmedRight = line.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(trimmedRight))
            {
                description.Clear();
                rType = null;
                range = null;
                values = ImmutableArray<string>.Empty;
                continue;
            }

            if (!trimmedRight.StartsWith("  ")) break;
            var content2 = trimmedRight.Substring(2);

            if (content2.StartsWith("#"))
            {
                var commentText = content2.Substring(1).TrimStart();
                if (commentText.StartsWith("@type "))
                {
                    rType = commentText.Substring("@type ".Length).Trim();
                }
                else if (commentText.StartsWith("@range "))
                {
                    range = commentText.Substring("@range ".Length).Trim();
                }
                else if (commentText.StartsWith("@values "))
                {
                    values = ParseValuesList(commentText.Substring("@values ".Length).Trim());
                }
                else
                {
                    description.Add(commentText);
                }
                continue;
            }

            var match = ParamLineRegex.Match(content2);
            if (!match.Success)
            {
                description.Clear();
                rType = null;
                range = null;
                values = ImmutableArray<string>.Empty;
                continue;
            }

            var qmdName = match.Groups[1].Value;
            var defaultRaw = StripInlineComment(match.Groups[2].Value).Trim();
            var inferredRType = rType ?? InferRType(defaultRaw) ?? "character";
            var csharpType = MapRTypeToCSharp(inferredRType);
            var propertyName = ToPascalCase(qmdName);
            var defaultValue = NormalizeDefault(defaultRaw);

            parameters.Add(new QmdParameter(
                qmdName,
                propertyName,
                csharpType,
                inferredRType,
                defaultValue,
                string.Join("\n", description),
                range,
                values));

            description.Clear();
            rType = null;
            range = null;
            values = ImmutableArray<string>.Empty;
        }

        var recordName = GetRecordName(file.Path);
        var sourceFileName = System.IO.Path.GetFileName(file.Path);
        return new QmdSchema(recordName, sourceFileName, parameters.ToImmutable());
    }

    static (int start, int end) FindParamsBlock(string content)
    {
        var lines = content.Split('\n');
        int dashCount = 0;
        int frontmatterEnd = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r') == "---")
            {
                dashCount++;
                if (dashCount == 2)
                {
                    frontmatterEnd = i;
                    break;
                }
            }
        }
        if (frontmatterEnd < 0) return (-1, -1);

        for (int i = 0; i < frontmatterEnd; i++)
        {
            if (lines[i].TrimEnd('\r').StartsWith("params:"))
                return (i + 1, frontmatterEnd);
        }
        return (-1, -1);
    }

    static string StripInlineComment(string s)
    {
        var inSingle = false;
        var inDouble = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '#' && !inSingle && !inDouble) return s.Substring(0, i);
        }
        return s;
    }

    static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase.Substring(1);
    }

    static string GetRecordName(string path)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var sb = new StringBuilder();
        foreach (var part in fileName.Split('-'))
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part, 1, part.Length - 1);
        }
        sb.Append("RenderParameters");
        return sb.ToString();
    }

    static string MapRTypeToCSharp(string rType)
    {
        return rType switch
        {
            "character" => "string?",
            "character[]" => "string[]?",
            "integer" => "int?",
            "numeric" => "double?",
            "logical" => "bool?",
            "Date" => "DateOnly?",
            _ => "string?",
        };
    }

    static string? NormalizeDefault(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "NULL") return null;
        if ((raw.StartsWith("\"") && raw.EndsWith("\"")) ||
            (raw.StartsWith("'") && raw.EndsWith("'")))
        {
            if (raw.Length >= 2) return raw.Substring(1, raw.Length - 2);
        }
        return raw;
    }

    static string? InferRType(string defaultValue)
    {
        if (string.IsNullOrEmpty(defaultValue) || defaultValue == "NULL") return null;
        if (defaultValue == "true" || defaultValue == "false") return "logical";
        if (Regex.IsMatch(defaultValue, @"^-?\d+$")) return "integer";
        if (Regex.IsMatch(defaultValue, @"^-?\d+\.\d+$")) return "numeric";
        if ((defaultValue.StartsWith("\"") && defaultValue.EndsWith("\"")) ||
            (defaultValue.StartsWith("'") && defaultValue.EndsWith("'")))
            return "character";
        return null;
    }

    static ImmutableArray<string> ParseValuesList(string raw)
    {
        if (!raw.StartsWith("[") || !raw.EndsWith("]")) return ImmutableArray<string>.Empty;
        var inner = raw.Substring(1, raw.Length - 2);
        if (string.IsNullOrWhiteSpace(inner)) return ImmutableArray<string>.Empty;
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in inner.Split(','))
        {
            var trimmed = item.Trim().Trim('\'').Trim('"');
            if (trimmed.Length > 0) builder.Add(trimmed);
        }
        return builder.ToImmutable();
    }

    static readonly Regex ParamLineRegex = new(@"^(\w+):\s*(.*)$", RegexOptions.Compiled);
}
