using System.Text.RegularExpressions;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderDocumentsTests
{
    private static readonly AppSettings Seller = new()
    {
        CompanyName = "Altinnendata",
        CompanyLegalName = "Altinnendata Sjølyst",
        StreetAddress = "Mårvegen 21a",
        PostalCode = "4347",
        AddressLocality = "Lye",
        PublicEmail = "altinnendata@gmail.com",
    };

    private static Order NewOrder(string locale = "no", string? markdown = "## 1. Kjøp\n\nDu kjøper **én** maskin.") => new()
    {
        Id = 42,
        Reference = "AID-000042-1A2B3C4D", VippsReference = "AID-000042-1A2B3C4D",
        PublicToken = "tok",
        Status = OrderStatus.Reserved,
        Locale = locale,
        BuyerName = "Ola",
        BuyerEmail = "ola@kunde.no",
        BuyerPhone = "4799999999",
        DeliveryMethod = DeliveryMethod.Pickup,
        DeliveryLabel = "Henting",
        BuildTitle = "Gaming PC <RTX>",
        BuildSlug = "gaming-pc",
        BuildPriceInOre = 1_299_000,
        TotalInOre = 1_299_000,
        // 22:30 UTC on the 18th is the 19th in Oslo.
        TermsAcceptedAt = new DateTime(2026, 10, 18, 22, 30, 0, DateTimeKind.Utc),
        TermsTitle = locale == "en" ? "Terms of Service" : "Vilkår",
        TermsMarkdown = markdown,
        TermsLocale = locale,
        TermsVersion = "2026-10-01T08:00:00.000000Z",
    };

    [Theory]
    [InlineData("no", "vilkar-AID-000042-1A2B3C4D.pdf", "angrerettskjema.pdf")]
    [InlineData("en", "terms-AID-000042-1A2B3C4D.pdf", "withdrawal-form.pdf")]
    public void FileNames_FollowTheBuyersLanguage(string locale, string terms, string form)
    {
        Assert.Equal(terms, OrderDocuments.FileNameOfTerms(NewOrder(locale)));
        Assert.Equal(form, OrderDocuments.FileNameOfWithdrawalForm(NewOrder(locale)));
    }

    [Theory]
    [InlineData("no", "Ordrenummer", "Vilkårene ble godtatt", "Vilkår", "nb")]
    [InlineData("en", "Order number", "Terms accepted on", "Terms of Service", "en")]
    public void Terms_HaveTheCompany_TheOrderNumber_AndTheAcceptDate(string locale, string number, string accepted, string title, string lang)
    {
        var html = OrderDocuments.TermsHtml(NewOrder(locale), Seller)!;

        Assert.StartsWith($"<!doctype html><html lang=\"{lang}\">", html);
        var text = Text(html);
        Assert.Contains("Altinnendata Sjølyst", text);
        Assert.Contains($"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>", html);
        Assert.Contains($"{number}: AID-000042-1A2B3C4D", text);
        Assert.Contains($"{accepted}: 19.10.2026", text);
        Assert.Contains("@page { size: A4; margin: 20mm 18mm; }", html);
    }

    [Fact]
    public void Terms_RenderTheMarkdown_AndShowRawHtmlAsText()
    {
        var html = OrderDocuments.TermsHtml(NewOrder(markdown: "## 1. Kjøp\n\nDu kjøper **én** maskin.\n\n<img src=\"http://10.0.0.1/x\"><script>alert(1)</script>"), Seller)!;

        Assert.Contains("<h2>1. Kjøp</h2>", html);
        Assert.Contains("<strong>én</strong>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Terms_DropImages_AndShowLinksAsTextWithTheirAddress()
    {
        var html = OrderDocuments.TermsHtml(NewOrder(markdown:
            "Se ![logo](http://10.0.0.1/logo.png) og [personvern](/no/privacy), [her](https://example.com/vilkar) eller <https://example.com/a>.\n\n![](http://169.254.169.254/x)"), Seller)!;

        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("10.0.0.1", html);
        Assert.DoesNotContain("169.254.169.254", html);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("personvern (/no/privacy)", html);
        Assert.Contains("her (https://example.com/vilkar)", html);
        Assert.Contains("eller https://example.com/a.", html);
    }

    [Fact]
    public void Documents_CarryAContentSecurityPolicy_ThatAllowsOnlyInlineStylesAndDataImages()
    {
        const string csp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">";

        Assert.Contains(csp, OrderDocuments.TermsHtml(NewOrder(), Seller)!);
        Assert.Contains(csp, OrderDocuments.WithdrawalFormHtml(NewOrder(), Seller));
    }

    [Fact]
    public void Terms_UseTheCompanyName_WhenThereIsNoLegalName()
    {
        var html = OrderDocuments.TermsHtml(NewOrder(), new AppSettings { CompanyName = "Altinnendata", CompanyLegalName = "" })!;

        Assert.Contains("<div class=\"company\">Altinnendata</div>", html);
        Assert.DoesNotContain("Sj", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Terms_AreNull_WithoutASnapshot(string? markdown)
    {
        Assert.Null(OrderDocuments.TermsHtml(NewOrder(markdown: markdown), Seller));
    }

    [Fact]
    public void WithdrawalForm_InNorwegian_UsesTheOfficialWording_FilledIn()
    {
        var html = OrderDocuments.WithdrawalFormHtml(NewOrder("no"), Seller);
        var text = Text(html);

        Assert.Contains("Standardskjema for bruk av angrerett", text);
        Assert.Contains("(Fyll ut og returner dette skjemaet dersom du ønsker å gå fra avtalen.)", text);
        Assert.Contains("Utfylt skjema sendes til: Altinnendata Sjølyst, Mårvegen 21a, 4347 Lye, altinnendata@gmail.com", text);
        Assert.Contains("Jeg underretter herved om at jeg ønsker å gå fra min avtale om kjøp av følgende varer: Gaming PC <RTX>", text);
        Assert.Contains("Ordrenummer: AID-000042-1A2B3C4D", text);
        Assert.Contains("Avtalen ble inngått den: 19.10.2026", text);
        foreach (var blank in new[] { "Varen ble mottatt den:", "Forbrukerens navn:", "Forbrukerens adresse:", "Dato:", "Underskrift (kun dersom skjemaet sendes på papir):" })
            Assert.Contains(blank, text);
        Assert.Equal(5, Regex.Count(html, "<span class=\"blank\"></span>"));
        Assert.Contains("Gaming PC &lt;RTX&gt;", html);
    }

    [Fact]
    public void WithdrawalForm_InEnglish_UsesTheModelFormWording_FilledIn()
    {
        var html = OrderDocuments.WithdrawalFormHtml(NewOrder("en"), Seller);
        var text = Text(html);

        Assert.Contains("Model withdrawal form", text);
        Assert.Contains("(Complete and return this form only if you wish to withdraw from the contract.)", text);
        Assert.Contains("To: Altinnendata Sjølyst, Mårvegen 21a, 4347 Lye, altinnendata@gmail.com", text);
        Assert.Contains("I hereby give notice that I withdraw from my contract of sale of the following goods: Gaming PC <RTX>", text);
        Assert.Contains("Order number: AID-000042-1A2B3C4D", text);
        Assert.Contains("Ordered on: 19.10.2026", text);
        foreach (var blank in new[] { "Received on:", "Name of consumer:", "Address of consumer:", "Date:", "Signature of consumer (only if this form is notified on paper):" })
            Assert.Contains(blank, text);
        Assert.Equal(5, Regex.Count(html, "<span class=\"blank\"></span>"));
    }

    [Fact]
    public void Documents_LoadNothingFromTheNetwork()
    {
        foreach (var html in new[] { OrderDocuments.TermsHtml(NewOrder(), Seller)!, OrderDocuments.WithdrawalFormHtml(NewOrder(), Seller) })
        {
            Assert.DoesNotMatch(@"(src|href)\s*=\s*""?https?:", html);
            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("url(", html);
        }
    }

    /// <summary>The text of the HTML, with tags dropped and entities decoded, for comparing the wording.</summary>
    private static string Text(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<style>.*?</style>", "", RegexOptions.Singleline), "<[^>]+>", ""));
}
