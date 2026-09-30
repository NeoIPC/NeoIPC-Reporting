using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The workdir a Quarto render gets over a fixture toolkit tree: the report's
/// own files are private copies, not links into the report sources.
/// </summary>
/// <remarks>
/// An HTML render copies the resources its document references, such as a
/// solution's screenshot, into Quarto's output directory and sets their
/// timestamps. Quarto copies a symlink as a symlink and sets the timestamps
/// through it, which reaches the source file; on the image's read-only
/// <c>/toolkit</c> that fails the render. A regular file in the workdir takes
/// Quarto's plain file copy instead.
/// </remarks>
[TestFixture]
[Category("Unit")]
public class RenderWorkdirTests
{
    const string Report = QuartoValidationReportProducer.ReportName;

    string _root = null!;
    string _reportSource = null!;
    string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "neoipc-render-workdir-" + Guid.NewGuid().ToString("N"));
        _reportSource = Path.Combine(_root, "toolkit", "reports", Report);
        _tempDir = Path.Combine(_root, "renders");
        Directory.CreateDirectory(Path.Combine(_reportSource, "img"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_reportSource, $"{Report}.qmd"), "---\ntitle: fixture\n---\n");
        File.WriteAllBytes(Path.Combine(_reportSource, "img", "fig-screenshot.png"), [0x89, 0x50, 0x4e, 0x47]);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    QuartoValidationReportProducer Producer()
    {
        var registry = new ReportLanguageRegistry();
        registry.Set(Report, new Dictionary<string, string> { ["en"] = $"{Report}.qmd" });
        var apiParameters = new ValidationReportApiParameters
        {
            SessionId = "test-session",
            AcceptHeaders = [],
            AcceptLanguageHeaders = [],
        };
        return new QuartoValidationReportProducer("text/html", new ResolvedLocale("en", "GB"),
            apiParameters, apiParameters.MapTo(),
            Options.Create(new ReportingOptions
            {
                ReportsSourceDir = Path.Combine(_root, "toolkit", "reports"),
                ReportsTempDir = _tempDir,
            }),
            registry, new ProductionEnvironment(), NullLoggerFactory.Instance);
    }

    string WorkdirPath(params string[] relative) =>
        Path.Combine([Directory.GetDirectories(_tempDir, "render_*").Single(), "reports", Report, .. relative]);

    [Test]
    public async Task TheReportsOwnFiles_AreCopies_ThatLeaveTheSourcesUntouched()
    {
        var source = Path.Combine(_reportSource, "img", "fig-screenshot.png");
        await using var producer = Producer();
        var copy = WorkdirPath("img", "fig-screenshot.png");

        Assert.Multiple(() =>
        {
            // Quarto takes its symlink copy for a link, and its file copy
            // otherwise.
            Assert.That(new FileInfo(copy).LinkTarget, Is.Null, "the workdir's file is not a link");
            Assert.That(File.ReadAllBytes(copy), Is.EqualTo(File.ReadAllBytes(source)));
        });

        File.WriteAllBytes(copy, [0x00]);

        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4e, 0x47 }),
            "a change made through the workdir does not reach the report sources");
    }

    [Test]
    public async Task TheWorkdir_IsRemovedOnDispose_AndTheSourcesStay()
    {
        var producer = Producer();
        var renderRoot = Directory.GetDirectories(_tempDir, "render_*").Single();

        await producer.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(renderRoot), Is.False);
            Assert.That(File.Exists(Path.Combine(_reportSource, "img", "fig-screenshot.png")), Is.True);
        });
    }

    sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "NeoIPC.Reporting.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
