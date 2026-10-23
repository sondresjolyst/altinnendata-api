using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SjolystInnovation.AppKit.Email;
using SjolystInnovation.AppKit.Pdf;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class OrderConfirmationTests(PostgresDatabase postgres) : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://www.altinnendata.no" })
        .Build();

    private readonly List<EmailMessage> _sent = [];
    private readonly FakePdf _pdf = new();
    private readonly ListLogger<OrderConfirmation> _log = new();
    private Func<EmailMessage, EmailResult> _brevo = _ => new EmailResult(true, "id");

    private sealed class FakePdf : IPdfRenderer
    {
        public List<string> Html { get; } = [];
        public Exception? Fail { get; set; }

        public Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken = default)
        {
            lock (Html) Html.Add(html);
            if (Fail != null) throw Fail;
            return Task.FromResult(Encoding.UTF8.GetBytes("%PDF-1.7 " + html.Length));
        }
    }

    private Mock<IEmailSender> Email()
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                lock (_sent) _sent.Add(m);
                return Task.FromResult(_brevo(m));
            });
        return email;
    }

    private OrderConfirmation Confirmation(ApplicationDbContext db) => new(db, Email().Object, _pdf, Config, _log);

    private OrderNotifier Notifier(ApplicationDbContext db) =>
        new(db, Email().Object, Confirmation(db), Config, NullLogger<OrderNotifier>.Instance);

    private static async Task<int> SeedAsync(ApplicationDbContext db, OrderStatus status = OrderStatus.Reserved, string locale = "no",
        bool withTerms = true, Action<Order>? edit = null)
    {
        if (!await db.AppSettings.AnyAsync(Ct))
            db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" });
        var reference = $"AID-{Random.Shared.Next(1, 999999):D6}-ABCDEF12";
        var order = new Order
        {
            Reference = reference, VippsReference = reference,
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = status,
            Locale = locale,
            BuyerName = "Ola Nordmann",
            BuyerEmail = "ola@kunde.no",
            BuyerPhone = "4791234567",
            DeliveryMethod = DeliveryMethod.Pickup,
            DeliveryLabel = "Henting",
            BuildTitle = "Gaming-PC",
            BuildSlug = "gaming-pc",
            BuildPriceInOre = 1_299_000,
            TotalInOre = 1_299_000,
            TermsAcceptedAt = DateTime.UtcNow,
            TermsTitle = withTerms ? "Vilkår" : null,
            TermsMarkdown = withTerms ? "## Kjøp\n\nTeksten." : null,
            TermsLocale = withTerms ? locale : null,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
        };
        edit?.Invoke(order);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        return order.Id;
    }

    private async Task<Order> OrderAsync(ApplicationDbContext existing, int id)
    {
        await using var fresh = postgres.NewContextOn(existing);
        return await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    private List<EmailMessage> ToBuyer => _sent.Where(m => m.To == "ola@kunde.no").ToList();
    private List<EmailMessage> ToOwner => _sent.Where(m => m.To == "shop@altinnendata.no").ToList();

    [Theory]
    [InlineData("no", "vilkar-", "angrerettskjema.pdf")]
    [InlineData("en", "terms-", "withdrawal-form.pdf")]
    public async Task Reserved_SendsTheConfirmationWithTwoPdfs_RecordsIt_AndTellsTheOwner(string locale, string termsPrefix, string form)
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db, locale: locale);

        await Notifier(db).OrderReservedAsync(id, Ct);

        var order = await OrderAsync(db, id);
        var buyer = Assert.Single(ToBuyer);
        Assert.Equal([$"{termsPrefix}{order.Reference}.pdf", form], buyer.Attachments.Select(a => a.Name));
        Assert.All(buyer.Attachments, a => Assert.Equal("%PDF", Encoding.ASCII.GetString(a.Content, 0, 4)));
        Assert.Single(ToOwner);
        Assert.Equal(2, _pdf.Html.Count);
        Assert.Contains("Kjøp", _pdf.Html[0]);
        Assert.Contains(order.Reference, _pdf.Html[1]);
        Assert.NotNull(order.ConfirmationSentAt);
        Assert.Equal(1, order.ConfirmationAttempts);
        Assert.NotNull(order.ConfirmationAttemptedAt);
    }

    [Fact]
    public async Task Reserved_WithoutATermsSnapshot_AttachesOnlyTheForm()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db, withTerms: false);

        await Notifier(db).OrderReservedAsync(id, Ct);

        Assert.Equal(["angrerettskjema.pdf"], Assert.Single(ToBuyer).Attachments.Select(a => a.Name));
        Assert.NotNull((await OrderAsync(db, id)).ConfirmationSentAt);
    }

    [Fact]
    public async Task Reserved_WhenThePdfsFail_StillTellsTheOwner_AndLeavesTheConfirmationForTheRetry()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db);
        _pdf.Fail = new TimeoutException("chromium hung");

        await Notifier(db).OrderReservedAsync(id, Ct);

        Assert.Empty(ToBuyer);
        Assert.Single(ToOwner);
        var order = await OrderAsync(db, id);
        Assert.Null(order.ConfirmationSentAt);
        Assert.Equal(1, order.ConfirmationAttempts);
        Assert.Contains(_log.Entries, e => e.Message.Contains("rendering the PDFs failed") && e.Message.Contains("retried later"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserved_WhenBrevoRefusesOrFails_LeavesTheConfirmationForTheRetry(bool throws)
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db);
        _brevo = m => m.To == "ola@kunde.no"
            ? (throws ? throw new HttpRequestException("down") : new EmailResult(false, Error: "blocked"))
            : new EmailResult(true);

        await Notifier(db).OrderReservedAsync(id, Ct);

        Assert.Single(ToOwner);
        var order = await OrderAsync(db, id);
        Assert.Null(order.ConfirmationSentAt);
        Assert.Equal(1, order.ConfirmationAttempts);
    }

    [Fact]
    public async Task TrySend_SkipsAnOrderAlreadySent_OrClaimedByAnotherTry()
    {
        await using var db = await postgres.NewContextAsync();
        var sentId = await SeedAsync(db, edit: o => o.ConfirmationSentAt = DateTime.UtcNow);
        var claimedId = await SeedAsync(db, edit: o => o.ConfirmationAttempts = 2);

        Assert.False(await Confirmation(db).TrySendAsync(sentId, 0, Ct));
        Assert.False(await Confirmation(db).TrySendAsync(claimedId, 1, Ct));

        Assert.Empty(_sent);
        Assert.Equal(0, (await OrderAsync(db, sentId)).ConfirmationAttempts);
        Assert.Equal(2, (await OrderAsync(db, claimedId)).ConfirmationAttempts);
    }

    [Fact]
    public async Task TrySend_FromTwoInstancesAtOnce_SendsOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db);
        await using var other = postgres.NewContextOn(db);

        var results = await Task.WhenAll(Confirmation(db).TrySendAsync(id, 0, Ct), Confirmation(other).TrySendAsync(id, 0, Ct));

        Assert.Single(results, r => r);
        Assert.Single(ToBuyer);
        Assert.Equal(1, (await OrderAsync(db, id)).ConfirmationAttempts);
    }

    [Fact]
    public async Task TrySend_OnTheLastTry_SaysToSendItByHand()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedAsync(db, edit: o => o.ConfirmationAttempts = OrderConfirmation.MaxAttempts - 1);
        _pdf.Fail = new InvalidOperationException("no chrome");

        Assert.False(await Confirmation(db).TrySendAsync(id, OrderConfirmation.MaxAttempts - 1, Ct));

        Assert.Contains(_log.Entries, e => e.Message.Contains("Send it by hand"));
    }

    [Fact]
    public void Backoff_DoublesFromFiveMinutes()
    {
        Assert.Equal(
            [5, 10, 20, 40, 80, 160, 320],
            Enumerable.Range(1, 7).Select(n => OrderConfirmationRetry.Backoff(n).TotalMinutes));
    }

    [Fact]
    public async Task Retry_ResendsReservedAndPaidOrders_WithTheirPdfs_ButNeverTheOwnerEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var now = DateTime.UtcNow;
        var reserved = await SeedAsync(db, OrderStatus.Reserved, edit: o => { o.ConfirmationAttempts = 1; o.ConfirmationAttemptedAt = now.AddMinutes(-6); });
        var paid = await SeedAsync(db, OrderStatus.Paid, edit: o => { o.ConfirmationAttempts = 1; o.ConfirmationAttemptedAt = now.AddMinutes(-6); });
        var others = new List<int>();
        foreach (var status in new[] { OrderStatus.Pending, OrderStatus.Failed, OrderStatus.Cancelled, OrderStatus.Refunded })
            others.Add(await SeedAsync(db, status, edit: o => { o.ConfirmationAttempts = 1; o.ConfirmationAttemptedAt = now.AddHours(-1); }));

        var sent = await new OrderConfirmationRetry(db, Confirmation(db)).ResendMissingAsync(now, Ct);

        Assert.Equal(2, sent);
        Assert.Equal(2, ToBuyer.Count);
        Assert.Empty(ToOwner);
        Assert.All(ToBuyer, m => Assert.Equal(2, m.Attachments.Count));
        Assert.NotNull((await OrderAsync(db, reserved)).ConfirmationSentAt);
        Assert.NotNull((await OrderAsync(db, paid)).ConfirmationSentAt);
        foreach (var id in others) Assert.Null((await OrderAsync(db, id)).ConfirmationSentAt);

        Assert.Equal(0, await new OrderConfirmationRetry(db, Confirmation(db)).ResendMissingAsync(now.AddDays(1), Ct));
        Assert.Equal(2, ToBuyer.Count);
    }

    [Fact]
    public async Task Retry_WaitsForTheBackoff_AndTheFirstDelay_AndStopsAtTheCap()
    {
        await using var db = await postgres.NewContextAsync();
        var now = DateTime.UtcNow;
        // Tried twice, the last try 9 minutes ago: the wait after two tries is 10 minutes.
        var backingOff = await SeedAsync(db, edit: o => { o.ConfirmationAttempts = 2; o.ConfirmationAttemptedAt = now.AddMinutes(-9); });
        // Never tried, made 4 minutes ago: the first send may still be on its way.
        var fresh = await SeedAsync(db, edit: o => o.CreatedAt = now.AddMinutes(-4));
        var givenUp = await SeedAsync(db, edit: o => { o.ConfirmationAttempts = OrderConfirmation.MaxAttempts; o.ConfirmationAttemptedAt = now.AddDays(-2); });

        Assert.Equal(0, await new OrderConfirmationRetry(db, Confirmation(db)).ResendMissingAsync(now, Ct));
        Assert.Empty(_sent);

        Assert.Equal(2, await new OrderConfirmationRetry(db, Confirmation(db)).ResendMissingAsync(now.AddMinutes(2), Ct));
        Assert.NotNull((await OrderAsync(db, backingOff)).ConfirmationSentAt);
        Assert.Equal(3, (await OrderAsync(db, backingOff)).ConfirmationAttempts);
        Assert.NotNull((await OrderAsync(db, fresh)).ConfirmationSentAt);
        Assert.Null((await OrderAsync(db, givenUp)).ConfirmationSentAt);
        Assert.Equal(OrderConfirmation.MaxAttempts, (await OrderAsync(db, givenUp)).ConfirmationAttempts);
    }
}
