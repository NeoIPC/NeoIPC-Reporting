using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The fonts the built image installs for the reports, read through fontconfig
/// inside the running container, and a figure its Cairo device draws: exactly
/// the four static EB Garamond OTFs the Dockerfile pins, each carrying the
/// subscript digits, ≥, and − that the Partner and Reference Reports' bold
/// table headers set; and Noto Sans, which their PDF figures set, together with
/// every Noto Sans family it falls back to, resolving to the Compact Font Format
/// (CFF) OTFs the image fetches, ≥ from Noto Sans Math in any language and each
/// script from its own family, so that a figure the Cairo device draws embeds
/// no CID-keyed TrueType font.
/// </summary>
/// <remarks>
/// Debian's <c>fonts-ebgaramond</c> registers its files under the family
/// "EB Garamond" as well, so a revert to that package, or an installation of
/// it beside the pinned faces, changes the file set this test compares. Its
/// bold face lacks those glyphs, which the charset query tells apart.
/// Cairo embeds a TrueType font's glyphs outside WinAnsi as a CID-keyed
/// TrueType font without the CIDToGIDMap entry PDF/A-4 requires, and a CFF
/// font's as CFF, so the figure test fails when fontconfig hands Cairo a
/// TrueType Noto Sans family of <c>fonts-noto-core</c> again. The language in
/// a script query stands for the one Pango requests for a run of that script:
/// the locale's own language when it covers the script, otherwise the script's
/// sample language, such as <c>hi</c> for Devanagari. A rule that names font
/// files by path glob stops matching for a process run from a parent
/// directory of the fonts, the root among them, so the tests that read which
/// fonts a process sees run from the root as well as from the service's own
/// directory.
/// Runs the image <see cref="SmokeTestImage"/> resolves, as
/// <see cref="ParametersEndpointTests"/> does.
/// </remarks>
[TestFixture]
[Category("Container")]
public class ImageFontTests
{
    const string FontDirectory = "/usr/share/fonts/opentype/ebgaramond/";
    const string NotoSansDirectory = "/usr/share/fonts/opentype/notosans/";

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
        Assert.That(result.Stdout, Is.EqualTo(NotoSansDirectory + file));
    }

    [Test]
    public async Task NotoSans_EveryFamilyIsOfferedOnlyAsAnOtf(
        [Values("/", "/usr/share/fonts", "/app")] string workingDirectory)
    {
        var result = await _container!.ExecAsync(
            ["env", "-C", workingDirectory, "fc-list", "-f", "%{family[0]}|%{file}\n"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        var files = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Noto Sans", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('|') + 1)..])
            .ToArray();
        Assert.That(files, Is.Not.Empty);
        Assert.That(files, Has.All.StartsWith(NotoSansDirectory).And.All.EndsWith(".otf"));
    }

    [Test]
    public async Task NotoSans_FallsBackOnlyToNotoFontsAndEbGaramond(
        [Values("/", "/app")] string workingDirectory)
    {
        // fontconfig's fallback list for Noto Sans, trimmed as a renderer sees
        // it: a font that adds no character to the fonts before it is left out.
        // EB Garamond, the reports' own font and also CFF, supplies the few
        // characters no installed Noto font carries.
        var result = await _container!.ExecAsync(
            ["env", "-C", workingDirectory, "fc-match", "-s", "-f", "%{family}\n", "Noto Sans"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        var families = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(families, Is.Not.Empty);
        Assert.That(families.Where(f => !f.StartsWith("Noto", StringComparison.Ordinal)
                                        && !f.StartsWith("EB Garamond", StringComparison.Ordinal)),
            Is.Empty);
    }

    [Test]
    public async Task NotoSans_ListsItsFallbackFamiliesBeforeTheGenericSansSerifFamilies()
    {
        // The configured family list, after the distribution's own rules have
        // added the families they prefer for sans-serif, DejaVu Sans among them.
        // A font of those lists that supports a run's language would otherwise
        // outrank the run's Noto Sans family wherever it is installed.
        var result = await _container!.ExecAsync(["fc-pattern", "-c", "-d", "-f", "%{family}", "Noto Sans"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        var families = result.Stdout.Split(',');
        var devanagari = Array.IndexOf(families, "Noto Sans Devanagari");
        Assert.That(devanagari, Is.GreaterThan(0));
        Assert.That(devanagari, Is.LessThan(Array.IndexOf(families, "DejaVu Sans")));
        Assert.That(devanagari, Is.LessThan(Array.IndexOf(families, "sans-serif")));
    }

    [TestCase("en")]
    [TestCase("de")]
    [TestCase("ne")]
    public async Task NotoSans_GreaterThanOrEqualToComesFromTheNotoSansMathOtf(string language)
    {
        // U+2265, which Noto Sans lacks. fontconfig's own order would take it
        // from DejaVu Sans, a TrueType font, and a language would otherwise let
        // a font supporting it win, such as Noto Sans Mono or EB Garamond.
        var result = await _container!.ExecAsync(
            ["fc-match", "-f", "%{file}", $"Noto Sans:lang={language}:charset=2265"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        Assert.That(result.Stdout, Is.EqualTo(NotoSansDirectory + "NotoSansMath-Regular.otf"));
    }

    // The danda, U+0964, is in Noto Sans Bengali as well, and Noto Sans Math
    // carries the Arabic alef, U+0627; the language decides between them. A
    // Nepali locale requests ne for the danda, every other locale hi.
    [TestCase("hi", "0964", "NotoSansDevanagari-Regular.otf")]
    [TestCase("ne", "0964", "NotoSansDevanagari-Regular.otf")]
    [TestCase("he", "05D0", "NotoSansHebrew-Regular.otf")]
    [TestCase("ar", "0627", "NotoSansArabic-Regular.otf")]
    [TestCase("th", "0E01", "NotoSansThai-Regular.otf")]
    [TestCase("bn", "0995", "NotoSansBengali-Regular.otf")]
    public async Task NotoSans_EachScriptComesFromTheOtfOfItsOwnFamily(string language, string codepoint, string file)
    {
        var result = await _container!.ExecAsync(
            ["fc-match", "-f", "%{file}", $"Noto Sans:lang={language}:charset={codepoint}"]);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Stderr);
        Assert.That(result.Stdout, Is.EqualTo(NotoSansDirectory + file));
    }

    [Test]
    public async Task NotoSans_AFigureTheCairoDeviceDrawsEmbedsNoTrueTypeCidFont(
        [Values("/", "/app")] string workingDirectory)
    {
        // A bold title and regular axis labels with a minus sign, Greek letters,
        // and ≥, which lie outside WinAnsi and so reach Cairo's CID fonts, and
        // with Nepali, Hebrew, and Arabic, all but the minus sign and the Greek
        // through fallback fonts; drawn in a locale the renders use, and counted
        // as the font dictionaries in the written PDF.
        const string script = """
            out <- tempfile(fileext = ".pdf")
            grDevices::cairo_pdf(out, family = "Noto Sans")
            plot(1:3, main = "Title ≥ − αβ नेपाली।", xlab = "Label ≥ − γδ עברית", ylab = "العربية")
            invisible(grDevices::dev.off())
            bytes <- readBin(out, "raw", file.info(out)$size)
            count <- function(s) length(grepRaw(s, bytes, fixed = TRUE, all = TRUE))
            cat(count("/CIDFontType2"), count("/FontFile2"), count("/CIDFontType0"))
            """;

        var result = await _container!.ExecAsync(
            ["env", "-C", workingDirectory, "LC_ALL=en_GB.UTF-8", "Rscript", "-e", script]);

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
