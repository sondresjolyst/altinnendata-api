using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SjolystInnovation.AppKit.Email;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderAnonymisationTests : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 1 January 2032 00:00 in Oslo is 31 December 2031 23:00 UTC.
    private static readonly DateTime FirstDue = new(2031, 12, 31, 23, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime March2026 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private static Order Sale(DateTime createdAt, DateTime? capturedAt = null, OrderStatus status = OrderStatus.Paid)
    {
        var order = OrderRetentionTests.NewOrder(status, createdAt);
        order.CapturedAt = capturedAt;
        order.StreetAddress = "Gata 1";
        order.PostalCode = "0150";
        order.City = "Oslo";
        order.PickupNote = "Ring ved door";
        order.ShippingNote = "Sendt med Posten";
        order.TermsAcceptedIp = "192.0.2.0";
        order.InvoiceNumber = "2026-001";
        order.TermsMarkdown = "Vilkar";
        order.TotalInOre = 100_000;
        order.CapturedInOre = 100_000;
        return order;
    }

    private static async Task<bool> IsAnonymisedAsync(ApplicationDbContext db, Order order) =>
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, Ct)).AnonymisedAt != null;

    [Fact]
    public async Task Due_FromOneJanuaryOsloTime_FiveFullYearsAfterTheYearOfTheSale()
    {
        await using var db = CreateDbContext();
        var sale = Sale(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        db.Orders.Add(sale);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, await OrderAnonymisation.AnonymiseAsync(db, FirstDue.AddSeconds(-1), Ct));
        Assert.False(await IsAnonymisedAsync(db, sale));
        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.True(await IsAnonymisedAsync(db, sale));
    }

    [Fact]
    public async Task ASaleAt2026Dec31_2330Utc_BelongsTo2027InOslo()
    {
        await using var db = CreateDbContext();
        var lateUtc = new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        var early = Sale(lateUtc.AddHours(-1), lateUtc.AddHours(-1));
        var late = Sale(lateUtc, lateUtc);
        db.Orders.AddRange(early, late);
        await db.SaveChangesAsync(Ct);

        // 2026 ends 31 December 2026 23:00 UTC, so the first is due on 1 January 2032 Oslo and the second a year later.
        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.True(await IsAnonymisedAsync(db, early));
        Assert.False(await IsAnonymisedAsync(db, late));
        Assert.Equal(0, await OrderAnonymisation.AnonymiseAsync(db, FirstDue.AddYears(1).AddSeconds(-1), Ct));
        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue.AddYears(1), Ct));
    }

    [Fact]
    public async Task TheCaptureDateDecides_AndTheCreationDateWhenThereIsNone()
    {
        await using var db = CreateDbContext();
        var createdOldCapturedNew = Sale(new DateTime(2026, 12, 30, 12, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 2, 12, 0, 0, DateTimeKind.Utc));
        var uncaptured = Sale(March2026, status: OrderStatus.Cancelled);
        db.Orders.AddRange(createdOldCapturedNew, uncaptured);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.False(await IsAnonymisedAsync(db, createdOldCapturedNew));
        Assert.True(await IsAnonymisedAsync(db, uncaptured));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Reserved)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task EveryStatusStillInTheTableIsAnonymised(OrderStatus status)
    {
        await using var db = CreateDbContext();
        db.Orders.Add(Sale(March2026, status: status));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
    }

    [Fact]
    public async Task ClearsTheBuyerFields_AndKeepsTheRest()
    {
        await using var db = CreateDbContext();
        var order = Sale(March2026, March2026.AddDays(1));
        order.Refunds.Add(new OrderRefund { AmountInOre = 100, RefundedAt = March2026.AddDays(2), Source = RefundSource.Admin });
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        var now = FirstDue.AddHours(5);

        await OrderAnonymisation.AnonymiseAsync(db, now, Ct);

        var o = await db.Orders.AsNoTracking().Include(x => x.Refunds).SingleAsync(Ct);
        Assert.Equal("Anonymisert", o.BuyerName);
        Assert.Equal("", o.BuyerEmail);
        Assert.Equal("", o.BuyerPhone);
        Assert.Null(o.StreetAddress);
        Assert.Null(o.PostalCode);
        Assert.Null(o.City);
        Assert.Null(o.PickupNote);
        Assert.Null(o.ShippingNote);
        Assert.Null(o.TermsAcceptedIp);
        Assert.Equal(now, o.AnonymisedAt);
        Assert.Equal(now, o.UpdatedAt);
        Assert.Equal(order.Reference, o.Reference);
        Assert.Equal(order.VippsReference, o.VippsReference);
        Assert.Equal("2026-001", o.InvoiceNumber);
        Assert.Equal("Vilkar", o.TermsMarkdown);
        Assert.Equal(100_000, o.TotalInOre);
        Assert.Equal(100_000, o.CapturedInOre);
        Assert.Equal(March2026, o.CreatedAt);
        Assert.Equal(March2026.AddDays(1), o.CapturedAt);
        Assert.Equal("PC", o.BuildTitle);
        Assert.Equal("Henting", o.DeliveryLabel);
        Assert.Equal(OrderStatus.Paid, o.Status);
        Assert.Single(o.Refunds);
    }

    [Fact]
    public async Task ASecondRunChangesNothing()
    {
        await using var db = CreateDbContext();
        db.Orders.Add(Sale(March2026));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.Equal(0, await OrderAnonymisation.AnonymiseAsync(db, FirstDue.AddDays(30), Ct));

        var o = await db.Orders.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(FirstDue, o.AnonymisedAt);
        Assert.Equal(FirstDue, o.UpdatedAt);
    }

    [Fact]
    public async Task AnonymisesOneBatchPerRun()
    {
        await using var db = CreateDbContext();
        db.Orders.AddRange(Enumerable.Range(0, OrderAnonymisation.BatchSize + 2).Select(_ => Sale(March2026)));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(OrderAnonymisation.BatchSize, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.Equal(2, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.Equal(0, await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct));
        Assert.Equal(500, OrderAnonymisation.BatchSize);
        Assert.Equal(5, OrderAnonymisation.KeepYears);
    }

    [Fact]
    public async Task TheServiceRunsIt_AndLogsTheCount()
    {
        var db = CreateDbContext();
        db.Orders.Add(Sale(March2026));
        await db.SaveChangesAsync(Ct);
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var log = new ListLogger<OrderRetentionService>();

        await new OrderRetentionService(services.GetRequiredService<IServiceScopeFactory>(), new FixedTime(FirstDue), log).RunOnceAsync(Ct);

        Assert.NotNull((await db.Orders.AsNoTracking().SingleAsync(Ct)).AnonymisedAt);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Anonymised the buyer details of 1 orders", entry.Message);
    }

    [Fact]
    public async Task TheServiceStillAnonymises_WhenThePurgeStepFails()
    {
        var db = CreateDbContext();
        db.Orders.Add(Sale(March2026));
        await db.SaveChangesAsync(Ct);
        // The first scope fails to give a context, so the purge fails and the second scope works.
        var scopes = new Mock<IServiceScopeFactory>();
        var calls = 0;
        scopes.Setup(f => f.CreateScope()).Returns(() =>
        {
            var scope = new Mock<IServiceScope>();
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(ApplicationDbContext)))
                .Returns(() => ++calls == 1 ? throw new InvalidOperationException("boom") : db);
            scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
            return scope.Object;
        });
        var log = new ListLogger<OrderRetentionService>();

        await new OrderRetentionService(scopes.Object, new FixedTime(FirstDue), log).RunOnceAsync(Ct);

        Assert.NotNull((await db.Orders.AsNoTracking().SingleAsync(Ct)).AnonymisedAt);
        Assert.Equal([LogLevel.Error, LogLevel.Information], log.Entries.Select(e => e.Level));
    }

    [Fact]
    public async Task AnAnonymisedOrderIsRefusedByTheAdminActions_AndNoVippsCallIsMade()
    {
        await using var db = CreateDbContext();
        var reserved = Sale(March2026, status: OrderStatus.Reserved);
        reserved.CapturedInOre = 0;
        var paid = Sale(March2026);
        db.Orders.AddRange(reserved, paid);
        await db.SaveChangesAsync(Ct);
        await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct);
        var vipps = new Mock<IVippsClient>(MockBehavior.Strict);
        var notifier = new Mock<IOrderNotifier>(MockBehavior.Strict);
        var log = new ListLogger<AdminOrderDto>();

        AssertAnonymised(await AdminOrders.Capture(reserved.Id, db, vipps.Object, log, Ct));
        AssertAnonymised(await AdminOrders.Cancel(reserved.Id, db, vipps.Object, notifier.Object, log, Ct));
        AssertAnonymised(await AdminOrders.Refund(paid.Id, new RefundRequest(null, 0), db, vipps.Object, notifier.Object, log, Ct));
        AssertAnonymised(await AdminOrders.MarkShipped(paid.Id, null, db, notifier.Object, log, Ct));
    }

    [Fact]
    public void AnAnonymisedOrderCannotGetAPaymentLink()
    {
        var order = Sale(DateTime.UtcNow, status: OrderStatus.Cancelled);
        order.ReservationEndedAt = DateTime.UtcNow;
        Assert.Null(PaymentLink.BlockedReasonOf(order, holdsBuild: true));

        order.AnonymisedAt = DateTime.UtcNow;

        Assert.Equal("wrongStatus", PaymentLink.BlockedReasonOf(order, holdsBuild: true));
    }

    [Fact]
    public async Task NoEmailGoesToAnEmptyAddress()
    {
        await using var db = CreateDbContext();
        var order = Sale(March2026);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        await OrderAnonymisation.AnonymiseAsync(db, FirstDue, Ct);
        var email = new Mock<IEmailSender>(MockBehavior.Strict);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var notifier = new OrderNotifier(db, email.Object, null!, config, NullLogger<OrderNotifier>.Instance);

        await notifier.OrderCancelledAsync(order.Id, Ct);
        await notifier.OrderRefundedAsync(order.Id, 100, Ct);
        await notifier.OrderShippedAsync(order.Id, Ct);

        email.VerifyNoOtherCalls();
    }

    private static void AssertAnonymised(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("anonymised", problem.ProblemDetails.Extensions["code"]);
    }
}

[Collection("postgres")]
public class OrderAnonymisationPostgresTests(PostgresDatabase postgres)
{
    [Fact]
    public async Task RunsOnPostgres_AndRereadsWhenAWebhookChangesAnOrderBetweenReadAndSave()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTime(2031, 12, 31, 23, 0, 0, DateTimeKind.Utc);
        await using var db = await postgres.NewContextAsync();
        var order = OrderRetentionTests.NewOrder(OrderStatus.Paid, new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        order.ShippingNote = "Sendt";
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await OrderAnonymisation.AnonymiseAsync(db, now, ct));

        await using var read = postgres.NewContextOn(db);
        var saved = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
        Assert.Equal("Anonymisert", saved.BuyerName);
        Assert.Null(saved.ShippingNote);
        Assert.Equal(now, saved.AnonymisedAt);
        Assert.Equal(0, await OrderAnonymisation.AnonymiseAsync(db, now, ct));
    }
}
