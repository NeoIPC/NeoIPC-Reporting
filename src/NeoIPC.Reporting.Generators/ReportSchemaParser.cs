using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace NeoIPC.Reporting.Generators;

/// <summary>
/// One report's parameter schema, parsed from a JSON snapshot of the master
/// QMD's <c>params:</c> block. <see cref="RecordName"/> is derived from the
/// snapshot filename (e.g. <c>Reference-Report.qmd-schema.json</c> →
/// <c>ReferenceReportRenderParameters</c>).
/// </summary>
internal sealed record ReportSchema(
    string RecordName,
    string SourceFileName,
    ImmutableArray<ReportParameter> Parameters);

/// <summary>
/// One parameter from a report schema snapshot. Carries the QMD-side name
/// and annotations that were in the master <c>.qmd</c> at extraction time:
/// <c>@type</c> drives the C# type mapping, <c>@range</c> constrains the
/// API surface, <c>@values</c> enumerates the legal set, and the
/// description text becomes the generated property's
/// <c>&lt;summary&gt;</c>.
/// </summary>
internal sealed record ReportParameter(
    string QmdName,
    string PropertyName,
    string CSharpType,
    string RType,
    string? DefaultValue,
    string Description,
    string? Range,
    ImmutableArray<string> Values);

/// <summary>
/// Reads a report's parameter-schema snapshot from
/// <c>&lt;AdditionalFiles&gt;</c> and parses it into a
/// <see cref="ReportSchema"/>. The snapshots are committed into
/// NeoIPC.Reporting so the source generator is self-contained.
/// </summary>
internal static class ReportSchemaParser
{
    public static ReportSchema? Parse(AdditionalText file, CancellationToken cancellationToken)
    {
        var sourceText = file.GetText(cancellationToken);
        if (sourceText is null) return null;
        var content = sourceText.ToString();
        if (string.IsNullOrWhiteSpace(content)) return null;

        Dictionary<string, object?>? root;
        try
        {
            root = MiniJson.Parse(content) as Dictionary<string, object?>;
        }
        catch (System.FormatException)
        {
            return null;
        }
        if (root is null) return null;
        if (!root.TryGetValue("params", out var paramsValue) ||
            paramsValue is not List<object?> paramsArr) return null;

        var parameters = ImmutableArray.CreateBuilder<ReportParameter>();
        foreach (var item in paramsArr)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not Dictionary<string, object?> p) continue;

            var qmdName = GetString(p, "qmdName");
            if (string.IsNullOrEmpty(qmdName)) continue;

            var rType = GetString(p, "rType");
            var defaultValue = GetString(p, "defaultValue");
            var range = GetString(p, "range");
            var description = GetString(p, "description") ?? string.Empty;
            var values = GetStringArray(p, "values");

            var effectiveRType = string.IsNullOrEmpty(rType)
                ? InferRType(defaultValue) ?? "character"
                : rType!;

            parameters.Add(new ReportParameter(
                QmdName: qmdName!,
                PropertyName: ToPascalCase(qmdName!),
                CSharpType: MapRTypeToCSharp(effectiveRType),
                RType: effectiveRType,
                DefaultValue: defaultValue,
                Description: description,
                Range: range,
                Values: values));
        }

        var recordName = GetRecordName(file.Path);
        var sourceFileName = System.IO.Path.GetFileName(file.Path);
        return new ReportSchema(recordName, sourceFileName, parameters.ToImmutable());
    }

    static string? GetString(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out var v)) return null;
        return v as string;
    }

    static ImmutableArray<string> GetStringArray(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out var v) || v is not List<object?> list) return ImmutableArray<string>.Empty;
        var builder = ImmutableArray.CreateBuilder<string>(list.Count);
        foreach (var item in list)
        {
            if (item is string s && s.Length > 0) builder.Add(s);
        }
        return builder.ToImmutable();
    }

    static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase.Substring(1);
    }

    static string GetRecordName(string path)
    {
        // Strip both `.qmd-schema.json` and any single trailing extension so
        // both `Reference-Report.qmd-schema.json` and `Reference-Report.qmd`
        // (legacy tests, plain filenames) yield `ReferenceReportRenderParameters`.
        var fileName = System.IO.Path.GetFileName(path);
        const string snapshotSuffix = ".qmd-schema.json";
        if (fileName.EndsWith(snapshotSuffix, System.StringComparison.OrdinalIgnoreCase))
            fileName = fileName.Substring(0, fileName.Length - snapshotSuffix.Length);
        else
            fileName = System.IO.Path.GetFileNameWithoutExtension(fileName);

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

    static string? InferRType(string? defaultValue)
    {
        if (string.IsNullOrEmpty(defaultValue) || defaultValue == "NULL") return null;
        if (defaultValue == "true" || defaultValue == "false") return "logical";
        if (Regex.IsMatch(defaultValue!, @"^-?\d+$")) return "integer";
        if (Regex.IsMatch(defaultValue!, @"^-?\d+\.\d+$")) return "numeric";
        return null;
    }

    // ----------------------------------------------------------------------
    // Tiny JSON reader. The generator targets netstandard2.0 and cannot
    // pull in System.Text.Json without bundling it into the analyzer DLL,
    // which is risky in Roslyn hosts. Since the schema files come from a
    // known, machine-generated source and have a fixed shape, a 100-line
    // recursive-descent reader is the lowest-risk option. Returns one of:
    // null, string, bool, double, List<object?>, Dictionary<string, object?>.
    // ----------------------------------------------------------------------
    static class MiniJson
    {
        public static object? Parse(string text)
        {
            int pos = 0;
            SkipWs(text, ref pos);
            var result = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos < text.Length)
                throw new System.FormatException($"Unexpected trailing content at offset {pos}");
            return result;
        }

        static object? ParseValue(string text, ref int pos)
        {
            SkipWs(text, ref pos);
            if (pos >= text.Length) throw new System.FormatException("Unexpected end of input");
            var c = text[pos];
            switch (c)
            {
                case '{': return ParseObject(text, ref pos);
                case '[': return ParseArray(text, ref pos);
                case '"': return ParseString(text, ref pos);
                case 't': Expect(text, ref pos, "true"); return true;
                case 'f': Expect(text, ref pos, "false"); return false;
                case 'n': Expect(text, ref pos, "null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(text, ref pos);
                    throw new System.FormatException($"Unexpected '{c}' at offset {pos}");
            }
        }

        static Dictionary<string, object?> ParseObject(string text, ref int pos)
        {
            var obj = new Dictionary<string, object?>(System.StringComparer.Ordinal);
            pos++; // consume '{'
            SkipWs(text, ref pos);
            if (Peek(text, pos) == '}') { pos++; return obj; }
            while (true)
            {
                SkipWs(text, ref pos);
                var key = ParseString(text, ref pos);
                SkipWs(text, ref pos);
                if (Peek(text, pos) != ':') throw new System.FormatException($"Expected ':' at offset {pos}");
                pos++;
                obj[key] = ParseValue(text, ref pos);
                SkipWs(text, ref pos);
                var next = Peek(text, pos);
                if (next == ',') { pos++; continue; }
                if (next == '}') { pos++; return obj; }
                throw new System.FormatException($"Expected ',' or '}}' at offset {pos}");
            }
        }

        static List<object?> ParseArray(string text, ref int pos)
        {
            var arr = new List<object?>();
            pos++; // consume '['
            SkipWs(text, ref pos);
            if (Peek(text, pos) == ']') { pos++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(text, ref pos));
                SkipWs(text, ref pos);
                var next = Peek(text, pos);
                if (next == ',') { pos++; continue; }
                if (next == ']') { pos++; return arr; }
                throw new System.FormatException($"Expected ',' or ']' at offset {pos}");
            }
        }

        static string ParseString(string text, ref int pos)
        {
            if (Peek(text, pos) != '"') throw new System.FormatException($"Expected '\"' at offset {pos}");
            pos++;
            var sb = new StringBuilder();
            while (pos < text.Length)
            {
                var c = text[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= text.Length) throw new System.FormatException("Unexpected end inside string");
                    var esc = text[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length) throw new System.FormatException("Truncated \\u escape");
                            var hex = text.Substring(pos, 4);
                            pos += 4;
                            sb.Append((char)System.Convert.ToInt32(hex, 16));
                            break;
                        default:
                            throw new System.FormatException($"Invalid escape '\\{esc}' at offset {pos - 1}");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new System.FormatException("Unterminated string");
        }

        static double ParseNumber(string text, ref int pos)
        {
            int start = pos;
            if (text[pos] == '-') pos++;
            while (pos < text.Length)
            {
                var c = text[pos];
                if (char.IsDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') pos++;
                else break;
            }
            return double.Parse(text.Substring(start, pos - start), System.Globalization.CultureInfo.InvariantCulture);
        }

        static void SkipWs(string text, ref int pos)
        {
            while (pos < text.Length)
            {
                var c = text[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                else break;
            }
        }

        static char Peek(string text, int pos) => pos < text.Length ? text[pos] : '\0';

        static void Expect(string text, ref int pos, string literal)
        {
            if (pos + literal.Length > text.Length)
                throw new System.FormatException($"Expected '{literal}' at offset {pos}");
            for (int i = 0; i < literal.Length; i++)
                if (text[pos + i] != literal[i])
                    throw new System.FormatException($"Expected '{literal}' at offset {pos}");
            pos += literal.Length;
        }
    }
}
