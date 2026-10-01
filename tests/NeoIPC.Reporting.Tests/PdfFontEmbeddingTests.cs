using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// <see cref="PdfFontEmbedding"/> against PDFs that R wrote in the reporting image: one that conforms,
/// and one for each defect it must find.
/// </summary>
/// <remarks>
/// The fixtures under <c>Fixtures/</c> each draw one line of text on a 2 × 1 inch page:
/// <c>cff-embedded.pdf</c> with <c>grDevices::cairo_pdf(family = "Noto Sans")</c>, whose fallback
/// draws ≥ from Noto Sans Math; <c>helvetica-not-embedded.pdf</c> with <c>grDevices::pdf()</c>; and
/// <c>truetype-cid-without-cidtogidmap.pdf</c> with <c>grDevices::cairo_pdf(family = "Noto Serif")</c>,
/// a TrueType font, drawing Greek letters, which lie outside WinAnsi. The reports' PDFs come from
/// LuaLaTeX, which draws a figure as a form XObject and can keep its objects in compressed object
/// streams; <c>helvetica-not-embedded-in-object-stream.pdf</c> has that shape: LuaLaTeX with
/// <c>\pdfvariable objcompresslevel=2</c> draws the Helvetica fixture through <c>pdfpages</c>, so its
/// font dictionary exists only inside an object stream.
/// </remarks>
[TestFixture]
[Category("Unit")]
public class PdfFontEmbeddingTests
{
    static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

    [Test]
    public void FindProblems_EmbeddedCffFonts_FindsNone()
    {
        Assert.That(PdfFontEmbedding.FindProblems(Fixture("cff-embedded.pdf")), Is.Empty);
    }

    [Test]
    public void FindProblems_StandardFontNotEmbedded_NamesIt()
    {
        Assert.That(PdfFontEmbedding.FindProblems(Fixture("helvetica-not-embedded.pdf")),
            Has.Some.EqualTo("Helvetica (Type1) is not embedded"));
    }

    [Test]
    public void FindProblems_FontInAnObjectStreamDrawnThroughAFormXObject_NamesIt()
    {
        Assert.That(PdfFontEmbedding.FindProblems(Fixture("helvetica-not-embedded-in-object-stream.pdf")),
            Has.Some.EqualTo("Helvetica (Type1) is not embedded"));
    }

    [Test]
    public void FindProblems_TrueTypeCidFontWithoutCidToGidMap_NamesIt()
    {
        var problems = PdfFontEmbedding.FindProblems(Fixture("truetype-cid-without-cidtogidmap.pdf"));

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.EndWith("NotoSerif-Regular (CIDFontType2) has no CIDToGIDMap"));
    }
}
