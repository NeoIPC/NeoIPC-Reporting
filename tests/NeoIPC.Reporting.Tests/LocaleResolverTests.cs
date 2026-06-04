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
}
