using System.Text.RegularExpressions;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using SjolystInnovation.AppKit.Pdf;
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
    [InlineData("no", "vilkar-AID-000042-1A2B3C4D.pdf")]
    [InlineData("en", "terms-AID-000042-1A2B3C4D.pdf")]
    public void TermsFileName_FollowsTheBuyersLanguage(string locale, string terms)
    {
        Assert.Equal(terms, OrderDocuments.FileNameOfTerms(NewOrder(locale)));
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
    public void Terms_CarryAContentSecurityPolicy_ThatAllowsOnlyInlineStylesAndDataImages()
    {
        const string csp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">";

        Assert.Contains(csp, OrderDocuments.TermsHtml(NewOrder(), Seller)!);
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
    public void WithdrawalForm_IsFilledIn_WithTheSeller_TheItem_AndTheOrderNumber()
    {
        var seller = new AppSettings
        {
            CompanyName = "Altinnendata",
            CompanyLegalName = "Altinnendata Sjølyst",
            OrgNumber = "123 456 789",
            StreetAddress = "Mårvegen 21a",
            PostalCode = "4347",
            AddressLocality = "Lye",
            PublicEmail = "altinnendata@gmail.com",
        };

        var form = OrderDocuments.WithdrawalFormRequest(NewOrder(), seller);

        Assert.Equal("Altinnendata", form.Seller.Name);
        Assert.Equal("Altinnendata Sjølyst", form.Seller.LegalName);
        Assert.Equal("123 456 789", form.Seller.OrganizationNumber);
        Assert.Equal(["Mårvegen 21a, 4347 Lye"], form.Seller.AddressLines);
        Assert.Equal("altinnendata@gmail.com", form.Seller.Email);
        Assert.Equal("AID-000042-1A2B3C4D", form.Order.Reference);
        Assert.Equal(["Gaming PC <RTX>"], form.Order.Items);
        Assert.Null(form.Order.ReceivedOn);
    }

    [Theory]
    [InlineData("no", WithdrawalFormLanguage.Norwegian)]
    [InlineData("nb", WithdrawalFormLanguage.Norwegian)]
    [InlineData("en", WithdrawalFormLanguage.English)]
    [InlineData("EN", WithdrawalFormLanguage.English)]
    public void WithdrawalForm_FollowsTheBuyersLanguage(string locale, WithdrawalFormLanguage language)
    {
        Assert.Equal(language, OrderDocuments.WithdrawalFormRequest(NewOrder(locale), Seller).Language);
    }

    [Fact]
    public void WithdrawalForm_DatesTheAgreement_ByWhenVippsConfirmedThePayment_InOslo()
    {
        // Paid through a payment link days after checkout. 23:15 UTC on the 23rd is the 24th in Oslo.
        var order = NewOrder();
        order.AuthorizedAt = new DateTime(2026, 10, 23, 23, 15, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 24), OrderDocuments.WithdrawalFormRequest(order, Seller).Order.OrderedOn);
        Assert.Contains("Vilkårene ble godtatt: 19.10.2026", Text(OrderDocuments.TermsHtml(order, Seller)!));
    }

    [Fact]
    public void WithdrawalForm_WithoutAConfirmedPayment_DatesTheAgreementByTheTermsAccept_InOslo()
    {
        // 22:30 UTC on the 18th is the 19th in Oslo.
        var order = NewOrder();
        order.AuthorizedAt = null;

        Assert.Equal(new DateOnly(2026, 10, 19), OrderDocuments.WithdrawalFormRequest(order, Seller).Order.OrderedOn);
    }

    [Fact]
    public void Terms_LoadNothingFromTheNetwork()
    {
        var html = OrderDocuments.TermsHtml(NewOrder(), Seller)!;

        Assert.DoesNotMatch(@"(src|href)\s*=\s*""?https?:", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("url(", html);
    }

    /// <summary>The text of the HTML, with tags dropped and entities decoded, for comparing the wording.</summary>
    private static string Text(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<style>.*?</style>", "", RegexOptions.Singleline), "<[^>]+>", ""));
}
