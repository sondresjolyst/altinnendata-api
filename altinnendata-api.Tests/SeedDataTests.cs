using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Features.Contact;
using altinnendata_api.Features.Shop;
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
        Assert.Matches(@"\| `_fbc` \| Meta \| [^|]+ \| 90 ", body);
        Assert.Matches(@"\| `consent` \([^)]+\) \| Altinnendata \| (Nødvendig|Necessary)\. [^|]+ \| [^|]+(site data|nettstedsdata)[^|]*\|", body);
        Assert.Matches("`_fbp` (og|and) `_fbc`", body);
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

    private static readonly Dictionary<int, (string No, string En)> Words = new() { [5] = ("fem", "five") };

    private static string Privacy(string locale) =>
        OneLine(LegalSeedText.All.Single(p => p.Key == "privacy" && p.Locale == locale).BodyMarkdown);

    [Theory]
    [InlineData("no", "## Kjøp i nettbutikken", "art. 6 nr. 1 bokstav b", "art. 6 nr. 1 bokstav c og bokføringsloven",
        "https://vipps.no/personvern-og-vilkar/personvern)", "/24 for IPv4 og /48 for IPv6", "e-post om bestillingen sendes gjennom Brevo",
        "når bestillingen ble opprettet hvis ingenting ble trukket", "siste endring av bestillingen hvis bare en del av beløpet ble trukket")]
    [InlineData("en", "## Shop purchases", "Art. 6(1)(b)", "Art. 6(1)(c) and the Norwegian Bookkeeping Act",
        "https://vipps.no/en/privacy-and-terms/privacy)", "/24 for IPv4 and /48 for IPv6", "emails about the order are sent through Brevo",
        "when the order was created if nothing was captured", "the last change to the order if only part of the amount was captured")]
    public void LegalSeedText_PrivacyDescribesShopOrders(string locale, params string[] phrases)
    {
        var body = Privacy(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Contains("Vipps MobilePay AS", body);
        Assert.DoesNotContain("We do not collect payment data through this site.", body);
        Assert.DoesNotContain("Vi samler ikke inn betalingsopplysninger gjennom denne siden.", body);
    }

    /// <summary>The retention numbers in the text follow the constants, so a change to either fails here.</summary>
    [Fact]
    public void LegalSeedText_RetentionNumbers_MatchTheConstants()
    {
        var orders = (int)OrderRetention.KeepFor.TotalDays;
        var enquiries = (int)EnquiryRetention.KeepFor.TotalDays;
        var years = Words[OrderAnonymisation.KeepYears];
        var example = 2026 + OrderAnonymisation.KeepYears + 1;

        var no = Privacy("no");
        Assert.Contains($"slettes {orders} dager etter at de ble opprettet", no);
        Assert.Contains($"fjernes {years.No} år etter slutten av regnskapsåret", no);
        Assert.Contains($"Et salg i 2026 anonymiseres fra 1. januar {example}.", no);
        Assert.Contains($"De slettes uansett {enquiries} dager etter at de kom inn.", no);

        var en = Privacy("en");
        Assert.Contains($"deleted {orders} days after they were created", en);
        Assert.Contains($"removed {years.En} years after the end of the financial year", en);
        Assert.Contains($"A sale in 2026 is anonymised from 1 January {example}.", en);
        Assert.Contains($"They are always deleted {enquiries} days after they arrive.", en);
    }

    [Theory]
    [InlineData("no", "privacy", "tilfeldig samtykke-ID", "Vi lagrer ikke IP-adressen din eller opplysninger om nettleseren", "art. 7 nr. 1", "art. 6 nr. 1 bokstav c")]
    [InlineData("no", "cookies", "tilfeldig samtykke-ID", "Vi lagrer ikke IP-adressen din eller opplysninger om nettleseren", "art. 7 nr. 1", "Loggen slettes etter 3 år")]
    [InlineData("en", "privacy", "random consent ID", "We do not store your IP address or any browser data", "Art. 7(1)", "Art. 6(1)(c)")]
    [InlineData("en", "cookies", "random consent ID", "We do not store your IP address or any browser data", "Art. 7(1)", "The log is deleted after 3 years")]
    public void LegalSeedText_DescribesTheConsentLog(string locale, string key, params string[] phrases)
    {
        var body = OneLine(LegalSeedText.All.Single(p => p.Key == key && p.Locale == locale).BodyMarkdown);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Matches(locale == "no" ? "bruker-ID-en ?(din)? bare hvis du er innlogget" : "user ID only when you are signed in", body);
        Assert.Matches(locale == "no" ? "på nytt etter 12 måneder" : "again after 12 months", body);
        Assert.Contains(locale == "no" ? "for å dokumentere samtykket" : "to document the consent", body);
        Assert.DoesNotContain(locale == "no" ? "vise at du" : "show that you", body);
    }

    [Theory]
    [InlineData("no", "privacy", "**Samtykkelogg:** 3 år")]
    [InlineData("en", "privacy", "**Consent log:** 3 years")]
    [InlineData("no", "cookies", "| `next-auth.session-token` | Altinnendata | Holder deg innlogget | 7 dager |")]
    [InlineData("en", "cookies", "| `next-auth.session-token` | Altinnendata | Keeps you signed in | 7 days |")]
    public void LegalSeedText_StatesTheDurations(string locale, string key, string phrase)
    {
        Assert.Contains(phrase, OneLine(LegalSeedText.All.Single(p => p.Key == key && p.Locale == locale).BodyMarkdown));
    }
}
