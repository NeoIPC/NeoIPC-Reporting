using Microsoft.Net.Http.Headers;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

[TestFixture]
[Category("Unit")]
public class LocaleResolverTests
{
    static readonly IReadOnlyCollection<string> SupportedLanguages = new[] { "en", "de", "el" };

    static IEnumerable<StringWithQualityHeaderValue> Accept(params string[] values)
        => values.Select(v => StringWithQualityHeaderValue.Parse(v));

    [Test]
    public void Parse_BareLanguage_AppliesDefaultTerritory()
    {
        var locale = LocaleResolver.Parse("de");
        Assert.Multiple(() =>
        {
            Assert.That(locale.Language, Is.EqualTo("de"));
            Assert.That(locale.Territory, Is.EqualTo("DE"));
            Assert.That(locale.LcAll, Is.EqualTo("de_DE.UTF-8"));
        });
    }

    [TestCase("de-DE", "de", "DE")]
    [TestCase("de_DE", "de", "DE")]
    [TestCase("EN-gb", "en", "GB")]
    public void Parse_LanguageTerritoryForms_NormalizeCase(string input, string expectedLang, string expectedTerritory)
    {
        var locale = LocaleResolver.Parse(input);
        Assert.Multiple(() =>
        {
            Assert.That(locale.Language, Is.EqualTo(expectedLang));
            Assert.That(locale.Territory, Is.EqualTo(expectedTerritory));
        });
    }

    [Test]
    public void Resolve_ExplicitSupported_Wins()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: "de",
            acceptLanguageHeaders: Accept("en"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale, Is.Not.Null);
            Assert.That(result.Locale!.Language, Is.EqualTo("de"));
        });
    }

    [Test]
    public void Resolve_ExplicitUnsupported_DoesNotFallBack()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: "fr",
            acceptLanguageHeaders: Accept("en"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.ExplicitUnsupported));
            Assert.That(result.Locale, Is.Null);
        });
    }

    [Test]
    public void Resolve_NoExplicit_FallsBackToFirstMatchingAcceptLanguage()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("fr", "de", "en"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale!.Language, Is.EqualTo("de"));
        });
    }

    [Test]
    public void Resolve_NoSupportedMatch_ReturnsNoMatch()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("fr", "ja"),
            supportedLanguages: SupportedLanguages);

        Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.NoMatch));
        Assert.That(result.Locale, Is.Null);
    }

    // --- exact-tag matching: LC_ALL must stay a locale the container generates ---

    [Test]
    public void Resolve_AcceptLanguage_UnservedTerritoryOnly_DoesNotMatch()
    {
        // en-US is a territory we don't generate; with no bare `en` offered we
        // refuse rather than composing the ungenerated en_US.UTF-8 (which made
        // lualatex/tlmgr hard-exit and Quarto misreport "missing packages").
        var result = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("en-US"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.NoMatch));
            Assert.That(result.Locale, Is.Null);
        });
    }

    [Test]
    public void Resolve_AcceptLanguage_TypicalBrowser_ResolvesViaBareLanguage()
    {
        // en-US,en;q=0.9 (the common browser header, q-sorted to en-US, en): the
        // unserved en-US is skipped and the client's own `en` resolves to en_GB.
        var result = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("en-US", "en"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale!.Language, Is.EqualTo("en"));
            Assert.That(result.Locale!.Territory, Is.EqualTo("GB"));
            Assert.That(result.Locale!.LcAll, Is.EqualTo("en_GB.UTF-8"));
        });
    }

    [Test]
    public void Resolve_AcceptLanguage_ExactServedTerritory_Resolves()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("en-GB"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale!.LcAll, Is.EqualTo("en_GB.UTF-8"));
        });
    }

    [Test]
    public void Resolve_ExplicitUnservedTerritory_ReturnsExplicitUnsupported()
    {
        // An explicit ?locale=en-US names a locale we don't serve; per the
        // explicit-wins-or-fails rule it is refused, not silently downgraded —
        // and it must not consult the Accept-Language `en` behind it.
        var result = LocaleResolver.Resolve(
            explicitLocale: "en-US",
            acceptLanguageHeaders: Accept("en"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.ExplicitUnsupported));
            Assert.That(result.Locale, Is.Null);
        });
    }

    [Test]
    public void Resolve_SupportedLanguageDifferentCase_StillMatches()
    {
        // supportedLanguages carries the registry keys verbatim (from the qmd
        // filenames), so a key whose case differs from the normalized
        // (lower-cased) request tag must still match — the membership check is
        // OrdinalIgnoreCase, matching DefaultTerritories.
        var mixedCase = new[] { "EN", "DE" };
        var result = LocaleResolver.Resolve(
            explicitLocale: "de",
            acceptLanguageHeaders: Accept("en"),
            supportedLanguages: mixedCase);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale!.Language, Is.EqualTo("de"));
        });
    }

    [Test]
    public void Resolve_ServedLanguageWithoutGeneratedLocale_Refuses()
    {
        // A language the report has content for but the container generates no
        // OS locale for (absent from DefaultTerritories, e.g. fr/af/ne) must be
        // refused rather than resolved to an ungenerated fr_FR.UTF-8 that would
        // crash lualatex — a distinct branch from territory-mismatch and from
        // plain not-supported.
        var supported = new[] { "en", "de", "fr" };

        var explicitResult = LocaleResolver.Resolve(
            explicitLocale: "fr",
            acceptLanguageHeaders: Accept("en"),
            supportedLanguages: supported);
        var acceptResult = LocaleResolver.Resolve(
            explicitLocale: null,
            acceptLanguageHeaders: Accept("fr"),
            supportedLanguages: supported);

        Assert.Multiple(() =>
        {
            Assert.That(explicitResult.Status, Is.EqualTo(LocaleResolver.Status.ExplicitUnsupported));
            Assert.That(explicitResult.Locale, Is.Null);
            Assert.That(acceptResult.Status, Is.EqualTo(LocaleResolver.Status.NoMatch));
            Assert.That(acceptResult.Locale, Is.Null);
        });
    }

    [Test]
    public void Resolve_ExplicitBareLanguage_ResolvesToServedLocale()
    {
        var result = LocaleResolver.Resolve(
            explicitLocale: "en",
            acceptLanguageHeaders: Accept("de"),
            supportedLanguages: SupportedLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LocaleResolver.Status.Resolved));
            Assert.That(result.Locale!.LcAll, Is.EqualTo("en_GB.UTF-8"));
        });
    }
}
