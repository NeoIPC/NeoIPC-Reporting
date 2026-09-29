using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The EB Garamond faces the built image installs, read through fontconfig
/// inside the running container: exactly the four static OTFs the Dockerfile
/// pins, each carrying the subscript digits, ≥ and − that the Partner and
/// Reference Reports' bold table headers set.
/// </summary>
/// <remarks>
/// Debian's <c>fonts-ebgaramond</c> registers its files under the family
/// "EB Garamond" as well, so a revert to that package, or an installation of
/// it beside the pinned faces, changes the file set this test compares. Its
/// bold face lacks those glyphs, which the charset query tells apart.
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
}
