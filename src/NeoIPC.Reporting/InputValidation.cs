namespace NeoIPC.Reporting;

/// <summary>
/// API-boundary validation for string-typed query parameters that flow
/// into the rendering pipeline. The Quarto <c>-P</c> argument format
/// expresses values as single-line YAML scalars; embedded control
/// characters (newlines, NULs, etc.) either break the scalar or split
/// it into a fake second <c>-P</c> token. Rejecting them at the API
/// boundary is more honest than silently stripping or escaping at the
/// argv-build layer (which would surprise the caller about what
/// rendered, and creates the risk of partial mitigations missing
/// edge cases).
/// </summary>
/// <remarks>
/// <para>
/// Rejected characters:
/// </para>
/// <list type="bullet">
///   <item><description>Anything <see cref="char.IsControl(char)"/>
///   classifies as a control character — C0 (<c>U+0000–U+001F</c>),
///   <c>DEL</c> (<c>U+007F</c>), and the C1 set (<c>U+0080–U+009F</c>).</description></item>
///   <item><description>Unicode line and paragraph separators
///   (<c>U+2028</c>, <c>U+2029</c>) — not in <c>Cc</c> per the BCL but
///   treated as line breaks by some text processors and would split a
///   YAML scalar.</description></item>
/// </list>
/// <para>
/// Tab (<c>U+0009</c>) is allowed via explicit carve-out — it's
/// printable enough for legitimate file paths / display names and
/// YAML single-quoted scalars handle it. Out of scope (could be
/// added if a concrete attack surface emerges): zero-width
/// characters, bidirectional override marks (Trojan Source).
/// </para>
/// </remarks>
public static class InputValidation
{
    /// <summary>
    /// Returns a 400 <see cref="IResult"/> when any of the supplied
    /// <c>(name, value)</c> pairs contains an unsafe character; returns
    /// <c>null</c> when every value is safe (or null). The first
    /// offender wins so the caller gets one clear failure rather than
    /// a list to triage.
    /// </summary>
    public static IResult? RejectUnsafeStrings(params (string Name, string? Value)[] entries)
    {
        foreach (var (name, value) in entries)
        {
            if (value is null) continue;
            if (ContainsUnsafeChar(value))
                return Reject(name);
        }
        return null;
    }

    /// <summary>
    /// As <see cref="RejectUnsafeStrings"/> but validates each element
    /// of the array independently. Used for query parameters that bind
    /// to <c>string[]</c> (e.g. <c>countryFilter</c>, <c>unitCodes</c>).
    /// </summary>
    public static IResult? RejectUnsafeStringArray(string name, string[]? values)
    {
        if (values is null) return null;
        foreach (var v in values)
        {
            if (v is null) continue;
            if (ContainsUnsafeChar(v)) return Reject(name);
        }
        return null;
    }

    static bool ContainsUnsafeChar(string s)
    {
        foreach (var c in s)
        {
            if (c == '\t') continue;
            if (char.IsControl(c)) return true;
            // U+2028 (line separator) and U+2029 (paragraph separator)
            // are not in Unicode category Cc, so char.IsControl misses
            // them — but text processors treat them as line breaks and
            // they would split a YAML scalar.
            if (c == 0x2028 || c == 0x2029) return true;
        }
        return false;
    }

    static IResult Reject(string name) =>
        ProblemDetailsHelper.BadRequest(
            "Invalid parameter value",
            $"The '{name}' value contains a control character " +
            "(newline, carriage return, or other char outside the " +
            "printable range) that cannot be safely passed to the " +
            "rendering pipeline.");
}
