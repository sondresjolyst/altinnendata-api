using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using SjolystInnovation.AppKit.Email;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderEmailsTests : TestBase
{
    private const string Reference = "ae-000001-abcdef12";

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://www.altinnendata.no" })
            .Build();

    private static Order NewOrder(string locale = "no", DeliveryMethod method = DeliveryMethod.Shipping, string buyerName = "Ola Nordmann") => new()
    {
        Id = 7,
        Reference = Reference,
        PublicToken = "tok",
        Status = OrderStatus.Reserved,
        Locale = locale,
        BuyerName = buyerName,
        BuyerEmail = "ola@kunde.no",
        BuyerPhone = "+4799999999",
        StreetAddress = method == DeliveryMethod.Pickup ? null : "Storgata 1",
        PostalCode = method == DeliveryMethod.Pickup ? null : "0155",
        City = method == DeliveryMethod.Pickup ? null : "Oslo",
        DeliveryMethod = method,
        DeliveryLabel = locale == "no" ? "Frakt" : "Shipping",
        DeliveryPriceInOre = 19900,
        PickupNote = method == DeliveryMethod.Pickup ? "Torsdag etter 16" : null,
        BuildTitle = "Gaming PC RTX 3060",
        BuildSlug = "gaming-pc",
        BuildPriceInOre = 1299000,
        TotalInOre = 1318900,
        CreatedAt = new DateTime(2026, 10, 18, 22, 30, 0, DateTimeKind.Utc),
        AuthorizedAt = new DateTime(2026, 10, 18, 22, 35, 0, DateTimeKind.Utc),
        TermsAcceptedAt = new DateTime(2026, 10, 18, 22, 30, 0, DateTimeKind.Utc),
    };

    [Theory]
    [InlineData("no", "Ordrebekreftelse ae-000001-abcdef12")]
    [InlineData("en", "Order confirmation ae-000001-abcdef12")]
    public void BuyerConfirmation_UsesTheBuyersLanguage(string locale, string subject)
    {
        var msg = OrderEmails.BuyerConfirmation(NewOrder(locale), new AppSettings(), Config());

        Assert.Equal(subject, msg.Subject);
        Assert.Equal("ola@kunde.no", msg.To);
        if (locale == "en")
        {
            Assert.Contains("Not VAT registered", msg.HtmlBody);
            Assert.Contains("right of withdrawal", msg.HtmlBody);
            Assert.Contains("13,189.00", msg.HtmlBody);
            Assert.Contains("https://www.altinnendata.no/en/terms", msg.HtmlBody);
        }
        else
        {
            Assert.Contains("angrerett", msg.HtmlBody);
        }
    }

    [Fact]
    public void BuyerConfirmation_ShowsLinesTotalAndNoVat()
    {
        var msg = OrderEmails.BuyerConfirmation(NewOrder(), new AppSettings(), Config());

        Assert.Contains("12 990,00", msg.HtmlBody);
        Assert.Contains("199,00", msg.HtmlBody);
        Assert.Contains("13 189,00", msg.HtmlBody);
        Assert.Contains("Gaming PC RTX 3060", msg.HtmlBody);
        Assert.Contains("Frakt", msg.HtmlBody);
        Assert.Contains("Storgata 1", msg.HtmlBody);
        Assert.Contains("Ikke merverdiavgiftsregistrert", msg.HtmlBody);
        Assert.Contains("https://www.altinnendata.no/no/terms", msg.HtmlBody);
        Assert.Contains("altinnendata@gmail.com", msg.HtmlBody);
    }

    [Fact]
    public void BuyerConfirmation_ForPickupShowsTheNoteInsteadOfAnAddress()
    {
        var msg = OrderEmails.BuyerConfirmation(NewOrder(method: DeliveryMethod.Pickup), new AppSettings(), Config());

        Assert.Contains("Torsdag etter 16", msg.HtmlBody);
        Assert.DoesNotContain("Storgata", msg.HtmlBody);
    }

    [Fact]
    public void BuyerConfirmation_HidesTheVatLineOnceRegistered()
    {
        var msg = OrderEmails.BuyerConfirmation(NewOrder(), new AppSettings { VatRegistered = true }, Config());

        Assert.DoesNotContain("Ikke merverdiavgiftsregistrert", msg.HtmlBody);
    }

    [Fact]
    public void OwnerNewOrder_LinksTheAdminOrderAndRepliesToTheBuyer()
    {
        var msg = OrderEmails.OwnerNewOrder(NewOrder(), new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" }, Config());

        Assert.Equal("Ny ordre ae-000001-abcdef12: Gaming PC RTX 3060", msg.Subject);
        Assert.Equal("shop@altinnendata.no", msg.To);
        Assert.Equal("ola@kunde.no", msg.ReplyTo);
        Assert.Contains("https://www.altinnendata.no/no/admin/shop/orders/7", msg.HtmlBody);
        Assert.Contains("Ola Nordmann", msg.HtmlBody);
        Assert.Contains("+4799999999", msg.HtmlBody);
        Assert.Contains("Storgata 1", msg.HtmlBody);
        Assert.Contains("0155", msg.HtmlBody);
        Assert.Contains("Oslo", msg.HtmlBody);
        Assert.Contains("12 990,00", msg.HtmlBody);
        Assert.Contains("199,00", msg.HtmlBody);
        Assert.Contains("13 189,00", msg.HtmlBody);
        Assert.Contains("19.10.2026", msg.HtmlBody); // 22:30 UTC is already the next day in Oslo
        Assert.Contains(Reference, msg.HtmlBody);
        Assert.Contains("Ikke merverdiavgiftsregistrert, ingen mva.", msg.HtmlBody);
    }

    [Fact]
    public void OwnerNewOrder_ForPickupLeavesOutTheAddressAndShowsTheNote()
    {
        var msg = OrderEmails.OwnerNewOrder(NewOrder(method: DeliveryMethod.Pickup), new AppSettings(), Config());

        Assert.Contains("Torsdag etter 16", msg.HtmlBody);
        Assert.DoesNotContain("Storgata", msg.HtmlBody);
    }

    [Fact]
    public void CancelAndRefundEmails_UseTheBuyersLanguage()
    {
        var no = NewOrder("no");
        var en = NewOrder("en");
        var s = new AppSettings();

        Assert.Equal("Ordre ae-000001-abcdef12 er kansellert", OrderEmails.BuyerCancelled(no, s).Subject);
        Assert.Equal("Order ae-000001-abcdef12 is cancelled", OrderEmails.BuyerCancelled(en, s).Subject);
        Assert.Contains("It can take a few days before the amount appears in your account.", OrderEmails.BuyerRefunded(Refunded(en), 1318900, s).HtmlBody);
    }

    /// <summary>The order after its refunds, with <paramref name="refunded"/> paid back of a full capture.</summary>
    private static Order Refunded(Order o, int? refunded = null)
    {
        o.CapturedInOre = o.TotalInOre;
        o.RefundedInOre = refunded ?? o.TotalInOre;
        o.Status = o.RefundedInOre >= o.CapturedInOre ? OrderStatus.Refunded : OrderStatus.Paid;
        return o;
    }

    [Theory]
    [InlineData("no", "Pengene for ordre ae-000001-abcdef12 er betalt tilbake", "Vi har betalt tilbake 13 189,00 kr til Vipps.")]
    [InlineData("en", "Order ae-000001-abcdef12 is refunded", "We have refunded 13,189.00 kr to Vipps.")]
    public void RefundEmail_ForTheWholeOrder_KeepsTheSubject(string locale, string subject, string sentence)
    {
        var msg = OrderEmails.BuyerRefunded(Refunded(NewOrder(locale)), 1318900, new AppSettings());

        Assert.Equal(subject, msg.Subject);
        Assert.Contains(sentence, msg.HtmlBody);
    }

    [Theory]
    [InlineData("no", "Vi har betalt tilbake 3 000,00 kr for ordre ae-000001-abcdef12", "Vi har betalt tilbake 3 000,00 kr til Vipps.")]
    [InlineData("en", "We have refunded 3,000.00 kr for order ae-000001-abcdef12", "We have refunded 3,000.00 kr to Vipps.")]
    public void RefundEmail_ForAPart_NamesTheAmountRefundedThisTime(string locale, string subject, string sentence)
    {
        var msg = OrderEmails.BuyerRefunded(Refunded(NewOrder(locale), refunded: 700_000), 300_000, new AppSettings());

        Assert.Equal(subject, msg.Subject);
        Assert.Contains(sentence, msg.HtmlBody);
        Assert.DoesNotContain("7 000,00", msg.HtmlBody);
        Assert.DoesNotContain("7,000.00", msg.HtmlBody);
    }

    [Fact]
    public void RefundEmail_ForTheLastPart_KeepsTheSubject_AndNamesTheLastAmount()
    {
        var msg = OrderEmails.BuyerRefunded(Refunded(NewOrder()), 318_900, new AppSettings());

        Assert.Equal("Pengene for ordre ae-000001-abcdef12 er betalt tilbake", msg.Subject);
        Assert.Contains("Vi har betalt tilbake 3 189,00 kr til Vipps.", msg.HtmlBody);
    }

    [Theory]
    [InlineData("no", "Beløpet er reservert i Vipps.")]
    [InlineData("en", "The amount is reserved in Vipps.")]
    public void BuyerConfirmation_SaysTheAmountIsReserved_WithoutSayingWhenItIsTaken(string locale, string sentence)
    {
        var body = OrderEmails.BuyerConfirmation(NewOrder(locale), new AppSettings(), Config()).HtmlBody;

        Assert.Contains(sentence, body);
        Assert.DoesNotContain("trekkes", body);
        Assert.DoesNotContain("utlevert", body);
        Assert.DoesNotContain("is taken", body);
        Assert.DoesNotContain("handed over", body);
    }

    [Fact]
    public void Emails_EncodeBuyerText()
    {
        var o = NewOrder(buyerName: "<script>x</script>");
        o.StreetAddress = "<script>x</script>";
        o.PickupNote = "<script>x</script>";
        var s = new AppSettings();
        var config = Config();

        var bodies = new[]
        {
            OrderEmails.BuyerConfirmation(o, s, config).HtmlBody,
            OrderEmails.OwnerNewOrder(o, s, config).HtmlBody,
            OrderEmails.BuyerCancelled(o, s).HtmlBody,
            OrderEmails.BuyerRefunded(o, 1318900, s).HtmlBody,
        };

        foreach (var body in bodies)
            Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", bodies[1]);
    }

    [Fact]
    public async Task Notifier_Reserved_SendsBuyerAndOwner_AndSurvivesAFailedSend()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" });
        var order = NewOrder();
        order.Id = 0;
        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sent = new List<EmailMessage>();
        var calls = 0;
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                if (++calls == 1) throw new InvalidOperationException("brevo down");
                sent.Add(m);
                return Task.FromResult(new EmailResult(true));
            });
        var notifier = new OrderNotifier(db, email.Object, Config(), NullLogger<OrderNotifier>.Instance);

        await notifier.OrderReservedAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        var owner = Assert.Single(sent);
        Assert.Equal("shop@altinnendata.no", owner.To);
        Assert.Equal("ola@kunde.no", owner.ReplyTo);
    }

    [Fact]
    public async Task Notifier_CancelledAndRefunded_EmailTheBuyer()
    {
        await using var db = CreateDbContext();
        var order = NewOrder();
        order.Id = 0;
        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m))
            .ReturnsAsync(new EmailResult(true));
        var notifier = new OrderNotifier(db, email.Object, Config(), NullLogger<OrderNotifier>.Instance);

        await notifier.OrderCancelledAsync(order.Id, TestContext.Current.CancellationToken);
        await notifier.OrderRefundedAsync(order.Id, 300_000, TestContext.Current.CancellationToken);

        Assert.Equal(["Ordre ae-000001-abcdef12 er kansellert", "Vi har betalt tilbake 3 000,00 kr for ordre ae-000001-abcdef12"], sent.Select(m => m.Subject));
        Assert.All(sent, m => Assert.Equal("ola@kunde.no", m.To));
    }
}
