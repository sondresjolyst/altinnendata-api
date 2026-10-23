using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class ShopPaymentPollingTests(PostgresDatabase postgres) : TestBase
{
    private const int Total = 1_899_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<IVippsClient> _vipps = new(MockBehavior.Strict);
    private readonly Mock<IOrderNotifier> _notifier = new();
    private readonly ListLogger<ShopPaymentPolling> _log = new();

    private static string Ref(int n) => $"AID-{n:D6}-POLL0000";

    /// <summary>A build held by an order in <paramref name="status"/>, created <paramref name="age"/> ago.</summary>
    private static async Task<(int BuildId, int OrderId)> SeedAsync(ApplicationDbContext db, int n, OrderStatus status, TimeSpan? age = null)
    {
        var build = new PcBuild { Slug = $"pc-{n}", Published = true, PriceNok = 18990, Availability = BuildAvailability.Available, Components = [PartWithCondition()] };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);

        var order = new Order
        {
            Reference = Ref(n), VippsReference = Ref(n),
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = status,
            Locale = "no",
            BuyerName = "Ola Nordmann",
            BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567",
            DeliveryMethod = DeliveryMethod.Pickup,
            DeliveryLabel = "Henting",
            PcBuildId = build.Id,
            BuildTitle = "Gaming-PC",
            BuildSlug = build.Slug,
            BuildPriceInOre = Total,
            TotalInOre = Total,
            CapturedInOre = status is OrderStatus.Paid or OrderStatus.Refunded ? Total : 0,
            IsTest = true,
            TermsAcceptedAt = DateTime.UtcNow,
            CreatedAt = Now - (age ?? TimeSpan.FromMinutes(20))
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        if (status is OrderStatus.Pending or OrderStatus.Reserved)
            Assert.True(await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct));
        db.ChangeTracker.Clear();
        return (build.Id, order.Id);
    }

    private void VippsShows(int n, string state, int captured = 0, int refunded = 0, int cancelled = 0, DateTimeOffset? guarantee = null) =>
        _vipps.Setup(v => v.GetPaymentAsync(Ref(n), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VippsPayment
            {
                Reference = Ref(n), State = state, AuthorizedAmountInOre = Total,
                CapturedAmountInOre = captured, RefundedAmountInOre = refunded, CancelledAmountInOre = cancelled,
                CaptureGuaranteedUntil = guarantee
            });

    private async Task<int> RunAsync(ApplicationDbContext existing, int afterId = 0, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(existing.Database.GetConnectionString());
        if (interceptor != null) options.AddInterceptors(interceptor);
        await using var db = new ApplicationDbContext(options.Options);
        var handler = new ShopWebhookHandler(db, _vipps.Object, _notifier.Object, new ListLogger<ShopWebhookHandler>());
        return await new ShopPaymentPolling(db, _vipps.Object, handler, _notifier.Object, _log).RunAsync(afterId, Now, Ct);
    }

    private async Task<(PcBuild Build, Order Order)> ReloadAsync(ApplicationDbContext existing, int buildId, int orderId)
    {
        await using var fresh = postgres.NewContextOn(existing);
        return (await fresh.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == buildId, Ct),
            await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct));
    }

    [Fact]
    public async Task LostAuthorized_ReservesThePendingOrder_AndSendsTheEmailsOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "AUTHORIZED");

        await RunAsync(db);
        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.NotNull(order.AuthorizedAt);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        _notifier.Verify(n => n.OrderReservedAsync(orderId, CancellationToken.None), Times.Once);
        _notifier.VerifyNoOtherCalls();
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("AUTHORIZED"));
    }

    [Fact]
    public async Task LostAuthorized_StoresTheGuaranteeFromThePayment_WithoutAskingVippsAgain()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        var guarantee = new DateTimeOffset(2026, 11, 17, 12, 30, 0, TimeSpan.Zero);
        VippsShows(1, "AUTHORIZED", guarantee: guarantee);

        await RunAsync(db);

        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(guarantee, order.CaptureGuaranteedUntil);
        _vipps.Verify(v => v.GetPaymentAsync(Ref(1), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LostAuthorized_WithoutAGuarantee_LeavesItEmpty_WithoutAskingVippsAgain()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "AUTHORIZED");

        await RunAsync(db);

        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Null(order.CaptureGuaranteedUntil);
        _vipps.Verify(v => v.GetPaymentAsync(Ref(1), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task APendingOrderYoungerThan15Minutes_IsLeftAlone()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, 1, OrderStatus.Pending, TimeSpan.FromMinutes(14));

        Assert.Equal(0, await RunAsync(db));

        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task AnOrderThatIsNotOpen_IsLeftAlone(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, 1, status, TimeSpan.FromDays(2));

        await RunAsync(db);

        _vipps.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("EXPIRED")]
    [InlineData("ABORTED")]
    [InlineData("TERMINATED")]
    public async Task LostEnd_OfAReservedOrder_FailsIt_KeepsTheBuildHeld_AndTellsTheOwnerOnce(string state)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Reserved);
        VippsShows(1, state);

        await RunAsync(db);
        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.NotNull(order.ReservationEndedAt);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        _notifier.Verify(n => n.ReservationEndedAsync(orderId, state, CancellationToken.None), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostExpired_OfAPendingOrder_FailsIt_AndReleasesTheBuild_WithoutAnEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "EXPIRED");

        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Null(order.ReservationEndedAt);
        Assert.Null(build.HeldByOrderId);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Created_ChangesNothing()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "CREATED");

        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(orderId, build.HeldByOrderId);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostCapture_OfAReservedOrder_PaysIt_AndSellsTheBuild_WithoutAnEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Reserved);
        VippsShows(1, "AUTHORIZED", captured: Total);

        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Total, order.CapturedInOre);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        Assert.Null(build.HeldByOrderId);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostCaptureAndRefund_OfAPendingOrder_SendsTheReservedAndRefundEmails_AndRecordsAPollingRefund()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "AUTHORIZED", captured: Total, refunded: 300_000);

        await RunAsync(db);

        var order = (await ReloadAsync(db, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct)).PcBuildId!.Value, orderId)).Order;
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(300_000, order.RefundedInOre);
        var refund = Assert.Single(await db.OrderRefunds.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync(Ct));
        Assert.Equal(300_000, refund.AmountInOre);
        Assert.Equal(RefundSource.Polling, refund.Source);
        Assert.StartsWith("poll:", refund.IdempotencyKey);
        _notifier.Verify(n => n.OrderReservedAsync(orderId, CancellationToken.None), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, CancellationToken.None), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostCancel_ByVippsOnItsOwn_KeepsTheBuildHeld_AndTellsTheOwner()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Reserved);
        VippsShows(1, "AUTHORIZED", cancelled: Total);

        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(orderId, build.HeldByOrderId);
        _notifier.Verify(n => n.ReservationEndedAsync(orderId, "CANCELLED", CancellationToken.None), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostCancel_TheAdminAskedFor_ReleasesTheBuild_AndEmailsTheBuyer()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Reserved);
        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.AdminCancelRequestedAt, DateTime.UtcNow), Ct);
        VippsShows(1, "AUTHORIZED", cancelled: Total);

        await RunAsync(db);

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(build.HeldByOrderId);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        _notifier.Verify(n => n.OrderCancelledAsync(orderId, CancellationToken.None), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWebhookThatSavesTheSameChangeFirst_WinsTheRace_AndThePollerSendsNoEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, 1, OrderStatus.Pending);
        VippsShows(1, "AUTHORIZED");
        var webhook = new BeforeFirstSave(async () =>
        {
            await using var other = postgres.NewContextOn(db);
            var order = await other.Orders.SingleAsync(o => o.Id == orderId, Ct);
            order.Status = OrderStatus.Reserved;
            order.AuthorizedAt = Now;
            await other.SaveChangesAsync(Ct);
        });

        await RunAsync(db, interceptor: webhook);
        await RunAsync(db);

        Assert.True(webhook.Fired);
        var (_, saved) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, saved.Status);
        _notifier.VerifyNoOtherCalls();
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("changed while"));
    }

    [Fact]
    public async Task AVippsOutage_StopsTheRun_AndTheNextRunStartsAtTheSameOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, first) = await SeedAsync(db, 1, OrderStatus.Reserved);
        await SeedAsync(db, 2, OrderStatus.Reserved);
        _vipps.Setup(v => v.GetPaymentAsync(Ref(1), true, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        var next = await RunAsync(db);

        Assert.Equal(first - 1, next);
        _vipps.Verify(v => v.GetPaymentAsync(Ref(2), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("run stops"));
    }

    [Fact]
    public async Task AVippsTimeout_StopsTheRunToo()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, first) = await SeedAsync(db, 1, OrderStatus.Reserved);
        _vipps.Setup(v => v.GetPaymentAsync(Ref(1), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout", new TimeoutException()));

        Assert.Equal(first - 1, await RunAsync(db));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task APaymentVippsRefusesToShow_IsSkipped_AndTheRunGoesOn(HttpStatusCode status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, first) = await SeedAsync(db, 1, OrderStatus.Reserved);
        var (_, second) = await SeedAsync(db, 2, OrderStatus.Pending);
        _vipps.Setup(v => v.GetPaymentAsync(Ref(1), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("no", null, status));
        VippsShows(2, "AUTHORIZED");

        Assert.Equal(0, await RunAsync(db));

        Assert.Equal(OrderStatus.Reserved, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == second, Ct)).Status);
        Assert.Equal(OrderStatus.Reserved, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == first, Ct)).Status);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("refused"));
    }

    [Fact]
    public async Task ARun_ChecksOneBatch_AndTheNextRunGoesOnFromThere()
    {
        await using var db = await postgres.NewContextAsync();
        var ids = new List<int>();
        for (var n = 1; n <= ShopPaymentPolling.BatchSize + 3; n++)
        {
            ids.Add((await SeedAsync(db, n, OrderStatus.Reserved)).OrderId);
            VippsShows(n, "AUTHORIZED");
        }

        var next = await RunAsync(db);

        Assert.Equal(ids[ShopPaymentPolling.BatchSize - 1], next);
        _vipps.Verify(v => v.GetPaymentAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Exactly(ShopPaymentPolling.BatchSize));
        _vipps.Verify(v => v.GetPaymentAsync(Ref(ShopPaymentPolling.BatchSize + 1), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(0, await RunAsync(db, next));
        _vipps.Verify(v => v.GetPaymentAsync(Ref(ShopPaymentPolling.BatchSize + 3), true, It.IsAny<CancellationToken>()), Times.Once);
        _vipps.Verify(v => v.GetPaymentAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Exactly(ShopPaymentPolling.BatchSize + 3));
    }

    [Fact]
    public async Task AnOrderThatEndedAfterTheBatchWasListed_IsNotReadFromVipps()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, first) = await SeedAsync(db, 1, OrderStatus.Pending);
        var (_, second) = await SeedAsync(db, 2, OrderStatus.Reserved);
        VippsShows(1, "AUTHORIZED");
        // The second order is paid by the admin while the first one is handled.
        _notifier.Setup(n => n.OrderReservedAsync(first, It.IsAny<CancellationToken>()))
            .Returns(() => db.Orders.Where(o => o.Id == second).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), Ct));

        Assert.Equal(0, await RunAsync(db));

        _vipps.Verify(v => v.GetPaymentAsync(Ref(2), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailedEmail_IsLogged_AndTheRunGoesOn()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, first) = await SeedAsync(db, 1, OrderStatus.Pending);
        var (_, second) = await SeedAsync(db, 2, OrderStatus.Pending);
        VippsShows(1, "AUTHORIZED");
        VippsShows(2, "AUTHORIZED");
        _notifier.Setup(n => n.OrderReservedAsync(first, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("smtp"));

        await RunAsync(db);

        Assert.Equal(OrderStatus.Reserved, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == second, Ct)).Status);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("emails"));
    }

    public static TheoryData<string, int, int, int, string[]> Payments => new()
    {
        { "CREATED", 0, 0, 0, [] },
        { "AUTHORIZED", 0, 0, 0, ["AUTHORIZED:1899000"] },
        { "AUTHORIZED", 0, 0, Total, ["CANCELLED:1899000"] },
        { "AUTHORIZED", Total, 0, 0, ["CAPTURED:1899000"] },
        { "AUTHORIZED", 500, 0, Total - 500, ["CAPTURED:500"] },
        { "AUTHORIZED", Total, 1000, 0, ["CAPTURED:1899000", "REFUNDED:1000"] },
        { "ABORTED", 0, 0, 0, ["ABORTED:"] },
        { "EXPIRED", 0, 0, 0, ["EXPIRED:"] },
        { "TERMINATED", 0, 0, 0, ["TERMINATED:"] },
        { "SOMETHING_NEW", 0, 0, 0, [] },
    };

    [Theory]
    [MemberData(nameof(Payments))]
    public void ChangesOf_ReadsThePaymentInTheOrderVippsMakesTheChanges(string state, int captured, int refunded, int cancelled, string[] expected)
    {
        var order = new Order
        {
            Reference = "AID-000001-ABCDEF12", VippsReference = "AID-000001-ABCDEF12", PublicToken = "t", Locale = "no", BuyerName = "a", BuyerEmail = "b", BuyerPhone = "c",
            DeliveryLabel = "d", BuildTitle = "e", BuildSlug = "f", TotalInOre = Total
        };
        var payment = new VippsPayment
        {
            Reference = order.Reference, State = state, AuthorizedAmountInOre = Total,
            CapturedAmountInOre = captured, RefundedAmountInOre = refunded, CancelledAmountInOre = cancelled
        };

        var changes = ShopPaymentPolling.ChangesOf(order, payment, Now);

        Assert.Equal(expected, changes.Select(c => $"{c.Name}:{c.Amount}"));
        Assert.All(changes, c => Assert.Equal(Now, c.At));
        Assert.All(changes, c => Assert.Equal(RefundSource.Polling, c.Source));
        Assert.All(changes, c => Assert.Equal($"poll:{order.Reference}:{c.Name}:{c.Amount}", c.Key));
    }

    [Fact]
    public async Task Service_RunOnce_LogsAFailure_AndDoesNotThrow()
    {
        var scopes = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("no db"));
        var log = new ListLogger<ShopPaymentPollingService>();

        await new ShopPaymentPollingService(scopes.Object, TimeProvider.System, log).RunOnceAsync(Ct);

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Payment polling failed"));
        Assert.Equal(TimeSpan.FromMinutes(3), ShopPaymentPollingService.Interval);
        Assert.Equal(TimeSpan.FromMinutes(15), ShopPaymentPolling.PendingAfter);
    }

    /// <summary>Runs <paramref name="before"/> once, just before the first save, so another writer can get in first.</summary>
    private sealed class BeforeFirstSave(Func<Task> before) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await before();
            }
            return result;
        }
    }
}
