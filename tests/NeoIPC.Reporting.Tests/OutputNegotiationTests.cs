using System.Collections.Immutable;
using Microsoft.Net.Http.Headers;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class OutputNegotiationTests
{
    static ImmutableArray<MediaTypeHeaderValue> Accept(params string[] mediaTypes)
        => [.. mediaTypes.Select(m => MediaTypeHeaderValue.Parse(m))];

    [Test]
    public void OnlyRenderedOutputsAreAcceptable_RenderedOnly_IsTrue()
    {
        // A locale is mandatory for these, so with none available the request
        // must be refused (406) up front.
        Assert.Multiple(() =>
        {
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("application/pdf")), Is.True);
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("text/html")), Is.True);
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("text/html", "application/pdf")), Is.True);
        });
    }

    [Test]
    public void OnlyRenderedOutputsAreAcceptable_DataOutputAcceptable_IsFalse()
    {
        Assert.Multiple(() =>
        {
            // Pure locale-independent data output — no locale needed.
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("application/json")), Is.False);
            // A rendered type is offered, but so is the locale-independent JSON:
            // the request is serviceable without a locale, so it is not rendered-only.
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("application/pdf", "application/json")), Is.False);
            // Wildcards accept the JSON data output too.
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("*/*")), Is.False);
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("application/*")), Is.False);
        });
    }

    [Test]
    public void OnlyRenderedOutputsAreAcceptable_NoSupportedOutput_IsFalse()
    {
        // An unsupported Accept type is neither a rendered nor a data output: not
        // a locale problem (that is decided in producer selection, which refuses
        // with 406 + a code), so this must not report a rendered-only request.
        Assert.Multiple(() =>
        {
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(Accept("application/xml")), Is.False);
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable([]), Is.False);
        });
    }

    [Test]
    public void OnlyRenderedOutputsAreAcceptable_DataOutputUnavailable_FallsBackToRenderedOnly()
    {
        // Both handlers skip the JSON producer when the dataset is supplied rather
        // than fetched (a stored referenceDataId, an uploaded partner-data body).
        // The JSON output therefore cannot serve those requests, so a caller who
        // accepts it and offers no locale must still be refused — otherwise the
        // request slips past this gate and lands on producer selection, which
        // refuses it for a reason that is not the real one.
        Assert.Multiple(() =>
        {
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(
                Accept("*/*"), dataOutputAvailable: false), Is.True);
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(
                Accept("application/pdf", "application/json"), dataOutputAvailable: false), Is.True);
            // JSON alone still names no producible output, so this is a
            // media-type refusal rather than a locale one.
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(
                Accept("application/json"), dataOutputAvailable: false), Is.False);
        });
    }

    [Test]
    public void SortAccept_DropsZeroQualityEntries()
    {
        // RFC 9110 §12.4.2: q=0 means "not acceptable", so the type must be dropped —
        // otherwise it slips past the locale gate or is served as a fallback.
        var kept = OutputNegotiation.SortAccept(
        [
            MediaTypeHeaderValue.Parse("application/json;q=0"),
            MediaTypeHeaderValue.Parse("text/html"),
        ]).Select(h => h.MediaType.ToString()).ToList();
        Assert.That(kept, Is.EqualTo(new[] { "text/html" }));
    }

    [Test]
    public void SortAcceptLanguage_DropsZeroQualityEntries()
    {
        var kept = OutputNegotiation.SortAcceptLanguage(
        [
            StringWithQualityHeaderValue.Parse("de;q=0"),
            StringWithQualityHeaderValue.Parse("en"),
        ]).Select(h => h.Value.ToString()).ToList();
        Assert.That(kept, Is.EqualTo(new[] { "en" }));
    }

    [Test]
    public void OnlyRenderedOutputsAreAcceptable_IgnoresZeroQuality()
    {
        // q=0 = "not acceptable" (RFC 9110), so even given a raw (unsorted) array
        // the helper must not treat a q=0 data type as acceptable: json;q=0 + a
        // rendered type + no locale is still a rendered-only (406) request.
        Assert.Multiple(() =>
        {
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(
                Accept("application/json;q=0", "text/html")), Is.True);
            // With the only rendered type at q=0, nothing rendered remains → not rendered-only.
            Assert.That(OutputNegotiation.OnlyRenderedOutputsAreAcceptable(
                Accept("application/pdf;q=0", "application/json")), Is.False);
        });
    }
}
