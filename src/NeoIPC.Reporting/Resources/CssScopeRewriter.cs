using System.Text;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Tiny CSS rewriter that prefix-scopes every selector in a
/// stylesheet to a given container selector (e.g.
/// <c>#neoipc-rendered-report</c>). Used by
/// <see cref="HtmlFragmentTransformer"/> to keep a report's inline
/// CSS from leaking into the containing app's DOM.
/// </summary>
/// <remarks>
/// <para>
/// Block-level walker (track brace depth, never parses declarations).
/// Decisions:
/// </para>
///
/// <list type="bullet">
///   <item><description><b>Top-level style rule</b>: prefix every
///   comma-separated selector. <c>:root</c> / <c>html</c> /
///   <c>body</c> at the head of a selector are rewritten to the
///   container instead of being prepended to — declarations targeted
///   at the document root (custom properties, page-level resets)
///   should land on the fragment's root.</description></item>
///   <item><description><b>Nested at-rules</b> (<c>@media</c>,
///   <c>@supports</c>, <c>@container</c>, <c>@layer</c>): emit the
///   prelude verbatim, then recurse into the inner block.</description></item>
///   <item><description><b>Self-contained at-rules</b>
///   (<c>@keyframes</c>, <c>@font-face</c>, <c>@page</c>,
///   <c>@property</c>, <c>@import</c>, <c>@charset</c>,
///   <c>@namespace</c>): pass through verbatim — their interior is
///   either keyframe lists, declarations, or a single statement,
///   none of which carry document-scoped selectors.</description></item>
/// </list>
///
/// <para>
/// Hand-written rather than using AngleSharp.Css to avoid the
/// dependency footprint and the round-trip risk on Quarto's CSS
/// output. Scope is intentionally narrow: this is not a general CSS
/// parser, only enough surface to walk Quarto-minimal output safely.
/// </para>
/// </remarks>
static class CssScopeRewriter
{
    public static string Rewrite(string css, string container)
    {
        var sb = new StringBuilder();
        var pos = 0;
        RewriteRules(css, ref pos, sb, container, isTopLevel: true);
        return sb.ToString();
    }

    static void RewriteRules(
        string css, ref int pos, StringBuilder sb, string container, bool isTopLevel)
    {
        while (pos < css.Length)
        {
            if (CopyWhitespace(css, ref pos, sb)) continue;
            if (pos >= css.Length) return;

            var c = css[pos];

            if (c == '}')
            {
                sb.Append('}');
                pos++;
                if (!isTopLevel) return;
                continue;
            }

            if (c == '/' && pos + 1 < css.Length && css[pos + 1] == '*')
            {
                CopyComment(css, ref pos, sb);
                continue;
            }

            if (c == '@')
            {
                EmitAtRule(css, ref pos, sb, container);
                continue;
            }

            EmitStyleRule(css, ref pos, sb, container);
        }
    }

    static bool CopyWhitespace(string css, ref int pos, StringBuilder sb)
    {
        var start = pos;
        while (pos < css.Length && char.IsWhiteSpace(css[pos])) pos++;
        if (pos == start) return false;
        sb.Append(css, start, pos - start);
        return true;
    }

    static void CopyComment(string css, ref int pos, StringBuilder sb)
    {
        var start = pos;
        var end = css.IndexOf("*/", pos + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            sb.Append(css, start, css.Length - start);
            pos = css.Length;
            return;
        }
        sb.Append(css, start, end + 2 - start);
        pos = end + 2;
    }

    static void EmitAtRule(string css, ref int pos, StringBuilder sb, string container)
    {
        var preludeStart = pos;
        while (pos < css.Length && css[pos] != '{' && css[pos] != ';')
        {
            if (css[pos] == '"' || css[pos] == '\'')
                SkipString(css, ref pos);
            else
                pos++;
        }

        var prelude = css.AsSpan(preludeStart, pos - preludeStart).ToString();
        sb.Append(prelude);

        if (pos >= css.Length) return;

        if (css[pos] == ';')
        {
            sb.Append(';');
            pos++;
            return;
        }

        sb.Append('{');
        pos++;

        if (IsNestedAtRule(prelude))
        {
            RewriteRules(css, ref pos, sb, container, isTopLevel: false);
        }
        else
        {
            CopyToMatchingBrace(css, ref pos, sb);
        }
    }

    static bool IsNestedAtRule(string prelude)
    {
        var trimmed = prelude.AsSpan().TrimStart();
        return trimmed.StartsWith("@media", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("@supports", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("@container", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("@layer", StringComparison.OrdinalIgnoreCase);
    }

    static void EmitStyleRule(string css, ref int pos, StringBuilder sb, string container)
    {
        var selectorStart = pos;
        var depth = 0;
        while (pos < css.Length)
        {
            var c = css[pos];
            if (c == '"' || c == '\'')
            {
                SkipString(css, ref pos);
                continue;
            }
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == '{' && depth == 0) break;
            else if (c == '}' && depth == 0)
            {
                sb.Append(css, selectorStart, pos - selectorStart);
                return;
            }
            pos++;
        }

        if (pos >= css.Length)
        {
            sb.Append(css, selectorStart, css.Length - selectorStart);
            return;
        }

        var selectorList = css.AsSpan(selectorStart, pos - selectorStart).ToString();
        sb.Append(PrefixSelectors(selectorList, container));
        sb.Append('{');
        pos++;
        CopyToMatchingBrace(css, ref pos, sb);
    }

    static void CopyToMatchingBrace(string css, ref int pos, StringBuilder sb)
    {
        var start = pos;
        var depth = 1;
        while (pos < css.Length && depth > 0)
        {
            var c = css[pos];
            if (c == '"' || c == '\'')
            {
                SkipString(css, ref pos);
                continue;
            }
            if (c == '/' && pos + 1 < css.Length && css[pos + 1] == '*')
            {
                var end = css.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                pos = end < 0 ? css.Length : end + 2;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth--;
            pos++;
        }
        sb.Append(css, start, pos - start);
    }

    static void SkipString(string css, ref int pos)
    {
        var quote = css[pos];
        pos++;
        while (pos < css.Length)
        {
            var c = css[pos];
            if (c == '\\' && pos + 1 < css.Length)
            {
                pos += 2;
                continue;
            }
            pos++;
            if (c == quote) return;
        }
    }

    static string PrefixSelectors(string selectorList, string container)
    {
        var sb = new StringBuilder(selectorList.Length + 32);
        var first = true;
        foreach (var selector in SplitTopLevel(selectorList, ','))
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(PrefixOne(selector, container));
        }
        return sb.ToString();
    }

    static IEnumerable<string> SplitTopLevel(string s, char sep)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"' || c == '\'')
            {
                var pos = i;
                SkipString(s, ref pos);
                i = pos - 1;
                continue;
            }
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == sep && depth == 0)
            {
                yield return s.Substring(start, i - start);
                start = i + 1;
            }
        }
        yield return s[start..];
    }

    static string PrefixOne(string selector, string container)
    {
        var trimmed = selector.TrimStart();
        var leading = selector[..(selector.Length - trimmed.Length)];
        if (trimmed.Length == 0) return selector;

        if (StartsWithRootTarget(trimmed, out var consumed))
        {
            // :root / html / body at the head — replace with the container.
            // Anything after (pseudo-class, combinator, …) is preserved.
            return leading + container + trimmed[consumed..];
        }

        return leading + container + " " + trimmed;
    }

    static bool StartsWithRootTarget(string s, out int consumed)
    {
        if (StartsWithIdent(s, ":root", out consumed)) return true;
        if (StartsWithIdent(s, "html", out consumed)) return true;
        if (StartsWithIdent(s, "body", out consumed)) return true;
        return false;
    }

    static bool StartsWithIdent(string s, string ident, out int consumed)
    {
        consumed = 0;
        if (!s.StartsWith(ident, StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Length == ident.Length)
        {
            consumed = ident.Length;
            return true;
        }
        var next = s[ident.Length];
        if (char.IsLetterOrDigit(next) || next == '-' || next == '_') return false;
        consumed = ident.Length;
        return true;
    }
}
