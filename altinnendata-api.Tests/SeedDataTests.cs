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

    private static string Page(string key, string locale) =>
        OneLine(LegalSeedText.All.Single(p => p.Key == key && p.Locale == locale).BodyMarkdown);

    private static string Privacy(string locale) => Page("privacy", locale);

    private static string Cookies(string locale) => Page("cookies", locale);

    private static readonly Dictionary<int, (string No, string En)> Words = new() { [5] = ("fem", "five") };

    /// <summary>Days a whole number of months stands for in the text. 365 days is said as 12 months.</summary>
    private static readonly Dictionary<int, int> MonthsOfDays = new() { [365] = 12 };

    [Theory]
    [InlineData("no", "## Behandlingsansvarlig Altinnendata AS er behandlingsansvarlig for personopplysningene vi samler inn gjennom denne nettsiden. Organisasjonsnummeret er 938 433 240, og adressen er Mårvegen 21a, 4347 Lye.",
        "Send henvendelser til altinnendata@gmail.com.")]
    [InlineData("en", "## Who is responsible Altinnendata AS is the data controller for the personal data we collect through this website. Our organisation number is 938 433 240, and our address is Mårvegen 21a, 4347 Lye, Norway.",
        "Send requests to altinnendata@gmail.com.")]
    public void LegalSeedText_PrivacyNamesTheController(string locale, string identity, string contact)
    {
        var body = Privacy(locale);

        Assert.StartsWith(identity, body);
        Assert.Contains(contact, body);
    }

    [Theory]
    [InlineData("no", "Fiken er databehandler og holder til i Norge.", "Google er databehandler for e-posten vår i Google Workspace.",
        "Brevo er databehandler og holder til i Frankrike", "Vipps MobilePay holder til i Norge og er selvstendig behandlingsansvarlig.",
        "uten noen skyleverandør", "Vi får ingen profilopplysninger fra Vipps.", "sende ordrebekreftelse og faktura")]
    [InlineData("en", "Fiken is a data processor based in Norway.", "Google is the data processor for our email in Google Workspace.",
        "Brevo is a data processor based in France", "Vipps MobilePay is based in Norway and is an independent controller.",
        "with no cloud provider behind it", "We get no profile data from Vipps.", "send the order confirmation and invoice")]
    public void LegalSeedText_PrivacyKeepsTheLiveFacts(string locale, params string[] phrases)
    {
        var body = Privacy(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
    }

    [Theory]
    [InlineData("no", "Meta Pixel, bare etter samtykke og bare når vi har slått den på:", "Vi sender ikke navn, e-postadresse eller mobilnummer til Meta.",
        "Vi bruker opplysningene til markedsføring og måling av annonser hos Meta bare når du har samtykket. Vi selger dem ikke.",
        "Samtykke (art. 6 nr. 1 bokstav a og ekomloven § 3-15).", "felles behandlingsansvarlige med Meta etter personvernforordningen art. 26",
        "Overføringen bygger på EU-U.S. Data Privacy Framework.", "Bruk lenken «Innstillinger for informasjonskapsler» nederst på siden. Det er like enkelt som å gi samtykket.",
        "Med samtykket ditt setter Meta Pixel også informasjonskapsler til markedsføring. Uten samtykke settes de ikke.")]
    [InlineData("en", "Meta Pixel, only after consent and only when we have turned it on:", "We do not send your name, email address or mobile number to Meta.",
        "We use your data for marketing and ad measurement at Meta only when you have given consent. We do not sell it.",
        "Consent (Art. 6(1)(a) and section 3-15 of the Norwegian Electronic Communications Act).", "joint controllers with Meta under GDPR Art. 26",
        "The transfer is based on the EU-U.S. Data Privacy Framework.", "Use the \"Cookie settings\" link at the bottom of the page. It is as easy as giving consent.",
        "With your consent, the Meta Pixel also sets cookies for marketing. Without consent they are not set.")]
    public void LegalSeedText_PrivacyDescribesTheMetaPixel(string locale, params string[] phrases)
    {
        var body = Privacy(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Contains("[" + (locale == "no" ? "personvernerklæring" : "Privacy Policy") + "](https://www.facebook.com/privacy/policy)", body);
        Assert.Contains("](https://www.facebook.com/policies/cookies/)", body);
        Assert.DoesNotContain(locale == "no" ? "Vi bruker ikke opplysningene til markedsføring" : "We do not use your data for marketing", body);
        Assert.DoesNotContain(locale == "no" ? "Nettbutikken setter ingen informasjonskapsler" : "The online shop sets no cookies", body);
    }

    [Theory]
    [InlineData("no", "Samtykkelogg: hvert valg du tar om informasjonskapsler, med en tilfeldig samtykke-ID", "bruker-ID-en din bare hvis du er innlogget",
        "Oppføringen inneholder ikke IP-adressen din eller opplysninger om nettleseren.",
        "Vi fører en logg over samtykkene fordi vi må kunne dokumentere dem, slik art. 7 nr. 1 krever.")]
    [InlineData("en", "Consent log: each cookie choice you make, with a random consent ID", "your user ID only when you are signed in",
        "The entry holds no IP address or browser data.",
        "We keep a log of consent choices because we must be able to document them, as Art. 7(1) requires.")]
    public void LegalSeedText_PrivacyDescribesTheConsentLog(string locale, params string[] phrases)
    {
        var body = Privacy(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        var legitimate = body[body.IndexOf(locale == "no" ? "Berettiget interesse (art. 6 nr. 1 bokstav f)." : "Legitimate interests (Art. 6(1)(f)).")..];
        Assert.Contains(locale == "no" ? "Vi fører en logg over samtykkene" : "We keep a log of consent choices", legitimate[..legitimate.IndexOf(locale == "no" ? "- Samtykke" : "- Consent")]);
    }

    /// <summary>The retention numbers in the privacy text follow the constants, so a change to either fails here.</summary>
    [Fact]
    public void LegalSeedText_RetentionNumbers_MatchTheConstants()
    {
        var orders = (int)OrderRetention.KeepFor.TotalDays;
        var years = Words[OrderAnonymisation.KeepYears];
        var months = MonthsOfDays[(int)EnquiryRetention.KeepFor.TotalDays];

        var no = Privacy("no");
        Assert.Contains($"Bestillinger: {years.No} år etter slutten av regnskapsåret", no);
        Assert.Contains($"eller bestillingen ble laget i testmodus, sletter vi bestillingen etter {orders} dager.", no);
        Assert.Contains("Deretter fjerner vi navn, e-postadresse, mobilnummer, adresse, merknader og IP-adressen. Selve salget beholdes uten opplysninger om deg.", no);
        Assert.DoesNotContain("Deretter sletter vi dem.", no);
        Assert.Contains($"Henvendelser: {months} måneder etter at vi mottok dem.", no);

        var en = Privacy("en");
        Assert.Contains($"Orders: {years.En} years after the end of the financial year", en);
        Assert.Contains($"or the order was made in test mode, we delete the order after {orders} days.", en);
        Assert.Contains("Then we remove the name, email address, mobile number, address, notes and IP address. The sale itself is kept without details about you.", en);
        Assert.DoesNotContain("Then we delete them.", en);
        Assert.Contains($"Enquiries: {months} months after we receive them.", en);
    }

    [Theory]
    [InlineData("no", "Samtykkelogg: 3 år. Deretter sletter vi oppføringene automatisk.", "Informasjonskapslene fra Meta: se siden om informasjonskapsler.")]
    [InlineData("en", "Consent log: 3 years. Then we delete the entries automatically.", "Meta cookies: see our Cookie Policy.")]
    public void LegalSeedText_PrivacyRetentionCoversTheConsentLogAndMetaCookies(string locale, params string[] phrases)
    {
        var body = Privacy(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
    }

    [Theory]
    [InlineData("no", "| `__Secure-next-auth.session-token` | Holder deg innlogget. Settes bare ved innlogging | 7 dager |",
        "| `__Host-next-auth.csrf-token` | Sikkerhet. Hindrer forfalskning av forespørsler på tvers av nettsteder. Settes ved hvert besøk | Økt |",
        "| `__Secure-next-auth.callback-url` | Husker hvor du skal sendes etter innlogging. Settes ved hvert besøk | Økt |",
        "De er nødvendige og inneholder ingen personopplysninger.", "`__Secure-next-auth.session-token` settes bare når noen logger inn.")]
    [InlineData("en", "| `__Secure-next-auth.session-token` | Keeps you signed in. Set only on sign-in | 7 days |",
        "| `__Host-next-auth.csrf-token` | Security. Prevents cross-site request forgery. Set on every visit | Session |",
        "| `__Secure-next-auth.callback-url` | Remembers where to redirect after login. Set on every visit | Session |",
        "They are necessary and hold no personal data.", "`__Secure-next-auth.session-token` is set only when someone signs in.")]
    public void LegalSeedText_NamesTheSignInCookiesWithTheirPrefixes(string locale, params string[] phrases)
    {
        var body = Cookies(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.DoesNotMatch(@"next-auth\.(session-token|csrf-token|callback-url)", body.Replace("__Secure-next-auth", "").Replace("__Host-next-auth", ""));
        Assert.DoesNotContain(locale == "no" ? "Kapslene under settes bare når noen logger inn" : "The cookies below are only set when someone signs in", body);
        Assert.DoesNotContain("30", body);
    }

    [Theory]
    [InlineData("no", "| `consent` (lokal lagring) | Nødvendig. Husker valget ditt om markedsføring, slik at vi ikke spør på nytt ved hvert besøk | Til du velger på nytt eller sletter nettstedsdata. Vi spør på nytt etter 12 måneder |",
        "en tilfeldig samtykke-ID", "for å dokumentere samtykket", "Den inneholder ikke IP-adressen din eller opplysninger om nettleseren.", "Loggen slettes etter 3 år.")]
    [InlineData("en", "| `consent` (local storage) | Necessary. Remembers your marketing choice so we do not ask again on every visit | Until you choose again or clear site data. We ask again after 12 months |",
        "a random consent ID", "to document the consent", "It holds no IP address or browser data.", "The log is deleted after 3 years.")]
    public void LegalSeedText_CookiesDescribeTheConsentRecord(string locale, params string[] phrases)
    {
        var body = Cookies(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
    }

    [Theory]
    [InlineData("no", "Når vi har lagt inn en Meta Pixel-ID", "Kapslene under settes bare etter at du har sagt ja til markedsføring.",
        "ekomloven § 3-15 og personvernforordningen art. 6 nr. 1 bokstav a", "felles behandlingsansvarlige med Meta etter personvernforordningen art. 26",
        "«Innstillinger for informasjonskapsler» nederst på siden", "Da sletter vi `_fbp` og `_fbc`. `fr` ligger på Metas eget domene",
        "Du kan også slette eller blokkere informasjonskapsler i nettleserinnstillingene.")]
    [InlineData("en", "When we have set a Meta Pixel ID", "The cookies below are set only after you have accepted marketing.",
        "section 3-15 of the Norwegian Electronic Communications Act (ekomloven) and GDPR Art. 6(1)(a)", "joint controllers with Meta under GDPR Art. 26",
        "\"Cookie settings\" link at the bottom of the page", "We then delete `_fbp` and `_fbc`. `fr` sits on Meta's own domain",
        "You can also clear or block cookies through your browser settings.", "Payment happens at Vipps, which has its own cookie rules.")]
    public void LegalSeedText_CookiesDescribeTheMetaPixel(string locale, params string[] phrases)
    {
        var body = Cookies(locale);

        foreach (var phrase in phrases) Assert.Contains(phrase, body);
        Assert.Matches(@"\| `_fbp` \| Meta \| [^|]+ \| 90 ", body);
        Assert.Matches(@"\| `_fbc` \| Meta \| [^|]+ \| 90 ", body);
        Assert.Matches(@"\| `fr` \| Meta \| [^|]+ \| 90 ", body);
        Assert.Contains("](https://www.facebook.com/policies/cookies/)", body);
        Assert.DoesNotContain(locale == "no" ? "ingen kapsler fra tredjeparter" : "do not use any third-party", body);
    }
}
