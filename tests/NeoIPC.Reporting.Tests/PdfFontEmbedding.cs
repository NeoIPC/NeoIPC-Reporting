using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// Finds the fonts that keep a PDF from conforming to the PDF/A-4 the reports declare: a font whose
/// program is not embedded, and a CID-keyed TrueType font (<c>CIDFontType2</c>) without the
/// <c>CIDToGIDMap</c> entry ISO 32000-2, 9.7.4, Table 115 requires of an embedded one.
/// </summary>
/// <remarks>
/// Reads every object the cross-reference table lists, those inside object streams included, rather
/// than walking the pages, so the fonts of a figure that a page draws as a form XObject are found too.
/// A Type 3 font draws its glyphs with content streams and has no program to embed.
/// </remarks>
internal static class PdfFontEmbedding
{
    /// <summary>One message per offending font, naming it; empty when every font conforms.</summary>
    public static IReadOnlyList<string> FindProblems(byte[] pdf)
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        using var document = PdfDocument.Open(pdf);
        var structure = document.Structure;

        DictionaryToken? Resolve(IToken? token) => token switch
        {
            DictionaryToken dictionary => dictionary,
            IndirectReferenceToken reference => structure.GetObject(reference.Data).Data as DictionaryToken,
            _ => null,
        };

        void Check(DictionaryToken font)
        {
            var subtype = font.TryGet(NameToken.Subtype, out NameToken s) ? s.Data : "";
            var name = font.TryGet(NameToken.BaseFont, out NameToken b) ? b.Data : "(no BaseFont)";
            if (subtype is "Type0" or "Type3")
                return;
            var descriptor = Resolve(font.TryGet(NameToken.FontDescriptor, out var d) ? d : null);
            if (descriptor is null
                || !(descriptor.ContainsKey(NameToken.FontFile)
                     || descriptor.ContainsKey(NameToken.FontFile2)
                     || descriptor.ContainsKey(NameToken.FontFile3)))
                problems.Add($"{name} ({subtype}) is not embedded");
            else if (subtype == "CIDFontType2" && !font.ContainsKey(NameToken.CidToGidMap))
                problems.Add($"{name} (CIDFontType2) has no CIDToGIDMap");
        }

        foreach (var reference in structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            if (structure.GetObject(reference).Data is not DictionaryToken font
                || !font.TryGet(NameToken.Type, out NameToken type) || type.Data != "Font")
                continue;
            Check(font);
            // A descendant written directly into the Type 0 font's array is no object of its own.
            if (font.TryGet(NameToken.DescendantFonts, out ArrayToken descendants))
                foreach (var descendant in descendants.Data.OfType<DictionaryToken>())
                    Check(descendant);
        }
        return [.. problems];
    }
}
