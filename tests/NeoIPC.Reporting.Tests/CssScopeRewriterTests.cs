using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class CssScopeRewriterTests
{
    const string Container = "#neoipc-rendered-report";

    [Test]
    public void Prefixes_simple_selector()
    {
        var css = ".foo { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }

    [Test]
    public void Prefixes_each_selector_in_comma_list()
    {
        var css = ".foo, .bar, #baz { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .bar"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report #baz"));
    }

    [Test]
    public void Rewrites_root_html_body_to_container()
    {
        var css = ":root { --var: 1; } html { font: a; } body { margin: 0; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("#neoipc-rendered-report {"));
        Assert.That(result, Does.Not.Contain(":root"));
        Assert.That(result, Does.Not.Match(@"\bhtml\s*\{"));
        Assert.That(result, Does.Not.Match(@"\bbody\s*\{"));
    }

    [Test]
    public void Preserves_descendants_of_body()
    {
        var css = "body .foo { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }

    [Test]
    public void Recurses_into_media_query()
    {
        var css = "@media (max-width: 600px) { .foo { color: red; } }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("@media (max-width: 600px)"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }

    [Test]
    public void Recurses_into_supports_query()
    {
        var css = "@supports (display: grid) { .foo { display: grid; } }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("@supports (display: grid)"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }

    [Test]
    public void Passes_keyframes_through_verbatim()
    {
        var css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("@keyframes fade"));
        Assert.That(
            result,
            Does.Not.Contain("#neoipc-rendered-report from"),
            "Keyframe stops must not be selector-prefixed");
        Assert.That(
            result,
            Does.Not.Contain("#neoipc-rendered-report to"),
            "Keyframe stops must not be selector-prefixed");
    }

    [Test]
    public void Passes_font_face_through_verbatim()
    {
        var css = "@font-face { font-family: Foo; src: url(x); }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("@font-face"));
        Assert.That(result, Does.Not.Contain("#neoipc-rendered-report"));
    }

    [Test]
    public void Preserves_import_at_rule()
    {
        var css = "@import url(\"foo.css\");\n.foo { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("@import"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }

    [Test]
    public void Handles_attribute_selectors_with_commas()
    {
        var css = "[data-key=\"a,b\"] { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(
            result,
            Does.Contain("#neoipc-rendered-report [data-key=\"a,b\"]"));
    }

    [Test]
    public void Handles_pseudo_class_with_parens()
    {
        var css = ".foo:is(.bar, .baz) { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(
            result,
            Does.Contain("#neoipc-rendered-report .foo:is(.bar, .baz)"));
    }

    [Test]
    public void Preserves_body_pseudo_element()
    {
        var css = "body::before { content: \"\"; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("#neoipc-rendered-report::before"));
    }

    [Test]
    public void Preserves_comments()
    {
        var css = "/* hello */\n.foo { color: red; }";
        var result = CssScopeRewriter.Rewrite(css, Container);
        Assert.That(result, Does.Contain("/* hello */"));
        Assert.That(result, Does.Contain("#neoipc-rendered-report .foo"));
    }
}
