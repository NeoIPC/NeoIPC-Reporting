using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The fonts the built image installs for the reports, read through fontconfig
/// inside the running container: exactly the four static EB Garamond OTFs the
/// Dockerfile pins, each carrying the subscript digits, ≥, and − that the
/// Partner and Reference Reports' bold table headers set; and Noto Sans, which
/// their PDF figures set, resolving to the four pinned CFF-flavoured OTFs, so
/// that a figure the Cairo device draws embeds no TrueType CID font.
/// </summary>
/// <remarks>
/// Debian's <c>fonts-ebgaramond</c> registers its files under the family
/// "EB Garamond" as well, so a revert to that package, or an installation of
/// it beside the pinned faces, changes the file set this test compares. Its
/// bold face lacks those glyphs, which the charset query tells apart.
/// Cairo embeds a TrueType font's glyphs outside WinAnsi as a CID TrueType
/// font without the CIDToGIDMap entry PDF/A-4 requires, and a CFF-flavoured
/// font's as CFF, so the figure test fails when fontconfig hands Cairo the
/// TrueType Noto Sans of <c>fonts-noto-core</c> again.
/// Runs the image <see cref="SmokeTestImage"/> resolves, as
/// <see cref="ParametersEndpointTests"/> does.
/// </remarks>
[TestFixture]
[Category("Container")]
public class ImageFontTests
{
    const string FontDirectory = "/usr/share/fonts/opentype/ebgaramond/";

    static readonly string[] PinnedFaces =
    [
        FontDirectory + "EBGaramond-Bold.otf",
        FontDirectory + "EBGaramond-BoldItalic.otf",
        FontDirectory + "EBGaramond-Italic.otf",
        FontDirectory + "EBGaramond-Regular.otf",
    ];

    IContainer? _container;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        var imageTag = await SmokeTestImage.ResolveAsync();

        // Skip rather than fail when there is no Docker to talk to; see
        // NegativePathTests.StartContainer for why the try spans Build().
        try
        {
            _container = new ContainerBuilder(imageTag).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            Assert.Ignore($"Category=Container tests need a running Docker daemon. {ex.Message}");
        }
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    async Task<string[]> FontFiles(string pattern)
    {
        var result = await _container!.ExecAsync(["fc-list", pattern, "file"]);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        // fc-list prints one "<path>: " line per matching face.
        return [.. result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd(':'))
            .Order(StringComparer.Ordinal)];
    }

    [Test]
    public async Task EbGaramond_IsExactlyThePinnedFaces()
    {
        Assert.That(await FontFiles("EB Garamond"), Is.EqualTo(PinnedFaces));
    }

    [Test]
    public async Task EbGaramond_EveryFaceCarriesTheGlyphsTheTableHeadersSet()
    {
        // U+2081–U+2083 subscript one to three, U+2212 minus sign, U+2265
        // greater-than or equal to.
        Assert.That(await FontFiles("EB Garamond:charset=2081-2083 2212 2265"), Is.EqualTo(PinnedFaces));
    }

    [TestCase("Regular", "NotoSans-Regular.otf")]
    [TestCase("Bold", "NotoSans-Bold.otf")]
    [TestCase("Italic", "NotoSans-Italic.otf")]
    [TestCase("Bold Italic", "NotoSans-BoldItalic.otf")]
    public async Task NotoSans_EachFaceResolvesToThePinnedOtf(string style, string file)
    {
        var result = await _container!.ExecAsync(["fc-match", "-f", "%{file}", $"Noto Sans:style={style}"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        Assert.That(result.Stdout, Is.EqualTo("/usr/share/fonts/opentype/notosans/" + file));
    }

    [Test]
    public async Task NotoSans_AFigureTheCairoDeviceDrawsEmbedsNoTrueTypeCidFont()
    {
        // A bold title and a regular axis label, each with a minus sign and
        // Greek letters, which lie outside WinAnsi and so reach Cairo's CID
        // fonts; the counts are of the font dictionaries in the written PDF.
        const string script = """
            out <- tempfile(fileext = ".pdf")
            grDevices::cairo_pdf(out, family = "Noto Sans")
            plot(1:3, main = "Title − αβ", xlab = "Label − γδ")
            invisible(grDevices::dev.off())
            bytes <- readBin(out, "raw", file.info(out)$size)
            count <- function(s) length(grepRaw(s, bytes, fixed = TRUE, all = TRUE))
            cat(count("/CIDFontType2"), count("/FontFile2"), count("/CIDFontType0"))
            """;

        var result = await _container!.ExecAsync(["env", "LC_ALL=C.UTF-8", "Rscript", "-e", script]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        var counts = result.Stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(counts[0], Is.Zero, "CID TrueType fonts (/CIDFontType2)");
            Assert.That(counts[1], Is.Zero, "embedded TrueType programs (/FontFile2)");
            Assert.That(counts[2], Is.Positive, "CFF CID fonts (/CIDFontType0), so the CID path was exercised");
        });
    }
}
