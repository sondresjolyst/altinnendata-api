using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using Xunit;

namespace altinnendata_api.Tests;

public class SeedDataTests : TestBase
{
    [Fact]
    public async Task ComponentCategories_AreSeededOnceWithBothLocales()
    {
        await using var db = CreateDbContext();

        await SeedData.EnsureComponentCategoriesAsync(db);
        var afterFirst = await db.ComponentCategories.CountAsync();

        await SeedData.EnsureComponentCategoriesAsync(db);

        Assert.Equal(afterFirst, await db.ComponentCategories.CountAsync());
        Assert.NotEqual(0, afterFirst);

        var cpu = await db.ComponentCategories.Include(c => c.Translations).FirstAsync(c => c.Key == "cpu");
        Assert.Equal("Prosessor", cpu.Translations.Single(t => t.Locale == "no").Name);
        Assert.Equal("Processor", cpu.Translations.Single(t => t.Locale == "en").Name);
    }

    [Fact]
    public void LegalSeedText_CoversEveryPageInEveryLocale()
    {
        foreach (var key in new[] { "terms", "privacy", "cookies" })
        {
            foreach (var locale in Locales.Supported)
            {
                var page = LegalSeedText.All.SingleOrDefault(p => p.Key == key && p.Locale == locale);
                Assert.NotNull(page);
                Assert.NotEmpty(page!.BodyMarkdown);
                Assert.NotEmpty(page.Title);
            }
        }
    }

    private static string OneLine(string markdown) => System.Text.RegularExpressions.Regex.Replace(markdown, @"\s+", " ");

    [Theory]
    [InlineData("no", "personvernforordningen art. 26", "Data Privacy Framework", "Innstillinger for", "ekomloven § 3-15", "Datatilsynet")]
    [InlineData("en", "GDPR Art. 26", "Data Privacy Framework", "\"Cookie settings\"", "section 3-15", "Datatilsynet")]
    public void LegalSeedText_CookiesDescribeTheMetaPixel(string locale, params string[] phrases)
    {
        var body = OneLine(LegalSeedText.All.Single(p => p.Key == "cookies" && p.Locale == locale).BodyMarkdown);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Contains("Meta Platforms Ireland Ltd", body);
        Assert.Contains("https://www.facebook.com/privacy/policy", body);
        Assert.Contains("https://www.facebook.com/policies/cookies/", body);
        Assert.Matches(@"\| `_fbp` \| Meta \| [^|]+ \| 90 ", body);
        Assert.Matches(@"\| `fr` \| Meta \| [^|]+ \| 90 ", body);
        Assert.DoesNotContain("ingen kapsler fra tredjeparter", body);
        Assert.DoesNotContain("do not use any third-party", body);
    }

    [Theory]
    [InlineData("no", "art. 6 nr. 1 bokstav a", "ekomloven § 3-15", "art. 26", "Data Privacy Framework", "Innstillinger for")]
    [InlineData("en", "Art. 6(1)(a)", "section 3-15", "Art. 26", "Data Privacy Framework", "\"Cookie settings\"")]
    public void LegalSeedText_PrivacyDescribesTheMetaPixel(string locale, params string[] phrases)
    {
        var body = OneLine(LegalSeedText.All.Single(p => p.Key == "privacy" && p.Locale == locale).BodyMarkdown);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Contains("Meta Platforms Ireland Ltd", body);
        Assert.Contains("https://www.facebook.com/privacy/policy", body);
        Assert.Contains("https://www.facebook.com/policies/cookies/", body);
        Assert.Contains("Datatilsynet", body);
        Assert.DoesNotContain("Vi bruker ikke opplysningene til markedsføring, og", body);
        Assert.DoesNotContain("We do not use your data for advertising and", body);
    }
}
