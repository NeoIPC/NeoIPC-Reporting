using System.Text;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class HtmlFragmentTransformerTests
{
    [Test]
    public async Task Strips_wrappers_and_hoists_style()
    {
        const string html = """
            <!doctype html>
            <html><head>
              <title>Report</title>
              <style>.foo { color: red; }</style>
            </head><body>
              <h1>Hello</h1>
              <p class="foo">World</p>
            </body></html>
            """;

        var result = await TransformToString(html);

        Assert.That(result, Does.Contain("<style>"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
        Assert.That(result, Does.Contain("<h1>Hello</h1>"));
        Assert.That(result, Does.Contain("class=\"foo\""));
        Assert.That(result, Does.Not.Contain("<html"));
        Assert.That(result, Does.Not.Contain("<head"));
        Assert.That(result, Does.Not.Contain("<body"));
        Assert.That(result, Does.Not.Contain("<title>"));
    }

    [Test]
    public async Task Handles_empty_body()
    {
        const string html = "<!doctype html><html><head></head><body></body></html>";
        var result = await TransformToString(html);
        Assert.That(result.Trim(), Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task Hoists_multiple_style_blocks()
    {
        const string html = """
            <html><head>
              <style>.a { color: red; }</style>
              <style>.b { color: blue; }</style>
            </head><body><p class="a b">x</p></body></html>
            """;
        var result = await TransformToString(html);

        Assert.That(result, Does.Contain("#neoipc-rendered-report .a"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .b"));
    }

    static async Task<string> TransformToString(string html)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(html));
        using var fragment = await HtmlFragmentTransformer.TransformAsync(
            input, TestContext.CurrentContext.CancellationToken);
        return Encoding.UTF8.GetString(fragment.ToArray());
    }
}
