using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class ShopWebhookHandlerTests(PostgresDatabase postgres) : TestBase
{
    private const int Total = 1_899_000;
    private const string Reference = "order-ref-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IVippsClient> _vipps = VippsWith(captured: Total);
    private readonly Mock<IOrderNotifier> _notifier = new();
    private readonly ListLogger<ShopWebhookHandler> _log = new();

    private static readonly DateTimeOffset EventTime = new(2026, 10, 18, 12, 30, 0, TimeSpan.Zero);

    private static VippsWebhookDelivery<VippsPaymentEvent> Delivery(
        string name,
        int? amount = Total,
        bool isTest = true,
        bool success = true,
        string reference = Reference) => new()
    {
        Event = new VippsPaymentEvent
        {
            Msn = "123456",
            Reference = reference,
            PspReference = Guid.NewGuid().ToString("N"),
            Name = name,
            Amount = amount is { } value ? new VippsWebhookAmount { Value = value, Currency = "NOK" } : null,
            Timestamp = EventTime,
            Success = success
        },
        IsTest = isTest,
        EventKey = Guid.NewGuid().ToString("N"),
        Slot = ShopVipps.Slot
    };

    /// <summary>A build held by a test order in <paramref name="status"/>.</summary>
    private static async Task<(int BuildId, int OrderId)> SeedAsync(ApplicationDbContext db, OrderStatus status = OrderStatus.Pending)
    {
        var build = new PcBuild { Slug = "gaming-pc", Published = true, PriceNok = 18990, Availability = BuildAvailability.Available, Components = [PartWithCondition()] };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);

        var order = new Order
        {
            Reference = Reference,
            PublicToken = "token-1",
            Status = status,
            Locale = "no",
            BuyerName = "Ola Nordmann",
            BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567",
            DeliveryMethod = DeliveryMethod.Pickup,
            DeliveryLabel = "Henting",
            PcBuildId = build.Id,
            BuildTitle = "Gaming-PC",
            BuildSlug = "gaming-pc",
            BuildPriceInOre = Total,
            TotalInOre = Total,
            IsTest = true,
            TermsAcceptedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        Assert.True(await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct));
        return (build.Id, order.Id);
    }

    /// <summary>
    /// Delivers the event the way the library endpoint does: on a context of its own, saving an applied
    /// event's changes with the processed marker, then running the side effects.
    /// </summary>
    private async Task<VippsWebhookOutcome> DeliverAsync(ApplicationDbContext existing, VippsWebhookDelivery<VippsPaymentEvent> delivery)
    {
        await using var db = postgres.NewContextOn(existing);
        var outcome = await new ShopWebhookHandler(db, _vipps.Object, _notifier.Object, _log).HandleAsync(delivery, Ct);
        if (outcome.Kind == VippsWebhookOutcomeKind.Retry) return outcome;
        if (outcome.Kind == VippsWebhookOutcomeKind.Rejected) db.ChangeTracker.Clear();

        db.ProcessedWebhookEvents.Add(new VippsProcessedWebhookEvent { Id = delivery.EventKey, Source = delivery.Slot, ProcessedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);
        if (outcome.AfterCommit is { } afterCommit) await afterCommit(Ct);
        return outcome;
    }

    private async Task<(PcBuild Build, Order Order)> ReloadAsync(ApplicationDbContext existing, int buildId, int orderId)
    {
        await using var fresh = postgres.NewContextOn(existing);
        return (await fresh.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == buildId, Ct),
            await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct));
    }

    private void VerifyNoEmails() => _notifier.VerifyNoOtherCalls();

    private static VippsPayment Payment(int captured, int refunded) =>
        new() { Reference = Reference, State = "AUTHORIZED", CapturedAmountInOre = captured, RefundedAmountInOre = refunded };

    /// <summary>A Vipps whose payment has captured and refunded these totals. Only the payment lookup is allowed.</summary>
    private static Mock<IVippsClient> VippsWith(int captured = 0, int refunded = 0)
    {
        var vipps = new Mock<IVippsClient>(MockBehavior.Strict);
        vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>())).ReturnsAsync(Payment(captured, refunded));
        return vipps;
    }

    private void VippsHas(int captured = Total, int refunded = 0) =>
        _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>())).ReturnsAsync(Payment(captured, refunded));

    [Fact]
    public async Task Authorized_ReservesThePendingOrder_AndNotifiesOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        var first = await DeliverAsync(db, Delivery("AUTHORIZED"));
        var again = await DeliverAsync(db, Delivery("AUTHORIZED"));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, first.Kind);
        Assert.Equal(VippsWebhookOutcomeKind.Applied, again.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(EventTime.UtcDateTime, order.AuthorizedAt);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        _notifier.Verify(n => n.OrderReservedAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Authorized_WithoutATimestamp_UsesTheTimeItArrived()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        var delivery = Delivery("AUTHORIZED");
        delivery = delivery with { Event = delivery.Event with { Timestamp = null } };
        var before = DateTime.UtcNow;

        await DeliverAsync(db, delivery);

        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.InRange(order.AuthorizedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Authorized_AfterAdminCancel_ChangesNothing()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Cancelled);

        var outcome = await DeliverAsync(db, Delivery("AUTHORIZED"));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.AfterCommit);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(order.AuthorizedAt);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Authorized_FromTheOtherEnvironment_IsRejected()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        var outcome = await DeliverAsync(db, Delivery("AUTHORIZED", isTest: false));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("production"));
        VerifyNoEmails();
    }

    [Theory]
    [InlineData(Total - 100)]
    [InlineData(Total + 100)]
    [InlineData(null)]
    public async Task Authorized_WithAnotherAmount_IsRejected(int? amount)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        var outcome = await DeliverAsync(db, Delivery("AUTHORIZED", amount));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.AuthorizedAt);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("amount"));
        VerifyNoEmails();
    }

    [Theory]
    [InlineData("CAPTURED", 0)]
    [InlineData("CAPTURED", Total + 1)]
    [InlineData("CAPTURED", null)]
    [InlineData("REFUNDED", 0)]
    [InlineData("REFUNDED", Total + 1)]
    public async Task CapturedOrRefunded_OutsideTheTotal_IsRejected(string name, int? amount)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);

        var outcome = await DeliverAsync(db, Delivery(name, amount));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(0, order.CapturedInOre);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("amount"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task UnknownReference_IsRejected()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);

        var outcome = await DeliverAsync(db, Delivery("AUTHORIZED", reference: "someone-else"));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("someone-else"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task AnEventThatDidNotSucceed_IsRejected()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);

        var outcome = await DeliverAsync(db, Delivery("CANCELLED", success: false));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("did not succeed"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task AFailedEventWithAnAmountThatDoesNotFit_LogsThatItDidNotSucceed()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, OrderStatus.Reserved);

        var outcome = await DeliverAsync(db, Delivery("CAPTURED", 0, success: false));

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, outcome.Kind);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("did not succeed"));
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Authorized_WhenTheAdminCancelledMeanwhile_SavesNothing_AndSendsNoEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        var delivery = Delivery("AUTHORIZED");

        // The handler reads the Pending order, then an admin cancels it before the endpoint saves.
        await using (var handlerDb = postgres.NewContextOn(db))
        {
            var outcome = await new ShopWebhookHandler(handlerDb, _vipps.Object, _notifier.Object, _log).HandleAsync(delivery, Ct);
            Assert.NotNull(outcome.AfterCommit);

            await using (var admin = postgres.NewContextOn(db))
            {
                var adminOrder = await admin.Orders.SingleAsync(o => o.Id == orderId, Ct);
                adminOrder.Status = OrderStatus.Cancelled;
                await admin.SaveChangesAsync(Ct);
            }

            handlerDb.ProcessedWebhookEvents.Add(new VippsProcessedWebhookEvent { Id = delivery.EventKey, Source = delivery.Slot, ProcessedAt = DateTime.UtcNow });
            // The library endpoint answers 500 to a failed save and skips the side effects, so Vipps sends the event again.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => handlerDb.SaveChangesAsync(Ct));
        }

        var redelivery = await DeliverAsync(db, delivery);

        Assert.Null(redelivery.AfterCommit);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(order.AuthorizedAt);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Captured_InFull_MarksPaidAndTheBuildSoldToday()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);

        var outcome = await DeliverAsync(db, Delivery("CAPTURED"));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Total, order.CapturedInOre);
        Assert.Equal(EventTime.UtcDateTime, order.CapturedAt);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), build.SoldOn);
        Assert.Null(build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Total - 1)]
    public async Task Captured_ThatVippsDoesNotShowYet_AsksForARetry_AndLeavesTheOrderUnpaid(int vippsCaptured)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        VippsHas(captured: vippsCaptured);

        var outcome = await DeliverAsync(db, Delivery("CAPTURED"));

        Assert.Equal(VippsWebhookOutcomeKind.Retry, outcome.Kind);
        _vipps.Verify(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>()), Times.Once);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(0, order.CapturedInOre);
        Assert.Null(order.CapturedAt);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("does not show"));
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Refunded_ThatVippsDoesNotShowYet_AsksForARetry_AndRecordsNothing()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await DeliverAsync(db, Delivery("CAPTURED"));
        VippsHas(refunded: 200_000);

        var outcome = await DeliverAsync(db, Delivery("REFUNDED", 300_000));

        Assert.Equal(VippsWebhookOutcomeKind.Retry, outcome.Kind);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(0, order.RefundedInOre);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("does not show"));
        VerifyNoEmails();

        // Vipps shows it on the redelivery.
        VippsHas(refunded: 300_000);
        var again = await DeliverAsync(db, Delivery("REFUNDED", 300_000));

        Assert.NotNull(again.AfterCommit);
        Assert.Equal(300_000, (await ReloadAsync(db, buildId, orderId)).Order.RefundedInOre);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refunded_ClearsTheAdminsPendingRefund()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await DeliverAsync(db, Delivery("CAPTURED"));
        await using (var admin = postgres.NewContextOn(db))
            await admin.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PendingRefundKey, $"refund-{Reference}-0")
                .SetProperty(o => o.PendingRefundInOre, 300_000), Ct);
        VippsHas(refunded: 300_000);

        await DeliverAsync(db, Delivery("REFUNDED", 300_000));

        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(300_000, order.RefundedInOre);
        Assert.Null(order.PendingRefundKey);
        Assert.Null(order.PendingRefundInOre);
    }

    [Fact]
    public async Task Captured_Partly_StaysReserved_AndRecordsTheVippsTotal()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VippsPayment { Reference = Reference, State = "AUTHORIZED", CapturedAmountInOre = 600_000 });

        var outcome = await DeliverAsync(db, Delivery("CAPTURED", 500_000));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(600_000, order.CapturedInOre);
        Assert.Null(order.CapturedAt);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("partial capture"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task Captured_PartlyUntilVippsHasItAll_MarksPaid()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VippsPayment { Reference = Reference, State = "AUTHORIZED", CapturedAmountInOre = Total });

        await DeliverAsync(db, Delivery("CAPTURED", 500_000));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Total, order.CapturedInOre);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
    }

    [Theory]
    [InlineData("CAPTURED", "http")]
    [InlineData("CAPTURED", "timeout")]
    [InlineData("REFUNDED", "http")]
    [InlineData("REFUNDED", "timeout")]
    public async Task CapturedOrRefunded_WhenVippsCannotBeRead_AsksForARetry(string name, string failure)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, name == "CAPTURED" ? OrderStatus.Reserved : OrderStatus.Paid);
        if (name == "REFUNDED")
        {
            await using var fresh = postgres.NewContextOn(db);
            await fresh.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CapturedInOre, Total), Ct);
        }
        // HttpClient reports its own timeout as a TaskCanceledException around a TimeoutException.
        Exception error = failure == "http"
            ? new HttpRequestException("Vipps is down")
            : new TaskCanceledException("The request was canceled.", new TimeoutException());
        _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>())).ThrowsAsync(error);

        var outcome = await DeliverAsync(db, Delivery(name));

        Assert.Equal(VippsWebhookOutcomeKind.Retry, outcome.Kind);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(name == "CAPTURED" ? OrderStatus.Reserved : OrderStatus.Paid, order.Status);
        Assert.Equal(name == "CAPTURED" ? 0 : Total, order.CapturedInOre);
        Assert.Equal(0, order.RefundedInOre);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Captured_WhenTheBuildChangedMeanwhile_SavesNothing_AndTheRedeliveryKeepsTheAdminsChange()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        var delivery = Delivery("CAPTURED");

        // The handler loads the build, then an admin edit clears the hold before the endpoint saves.
        await using (var handlerDb = postgres.NewContextOn(db))
        {
            var outcome = await new ShopWebhookHandler(handlerDb, _vipps.Object, _notifier.Object, _log).HandleAsync(delivery, Ct);
            Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);

            await using (var admin = postgres.NewContextOn(db))
            {
                await admin.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Availability, BuildAvailability.Reserved)
                    .SetProperty(b => b.HeldByOrderId, (int?)null), Ct);
            }

            handlerDb.ProcessedWebhookEvents.Add(new VippsProcessedWebhookEvent { Id = delivery.EventKey, Source = delivery.Slot, ProcessedAt = DateTime.UtcNow });
            // The library endpoint answers 500 to a failed save, so Vipps sends the event again.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => handlerDb.SaveChangesAsync(Ct));
        }

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        await using (var check = postgres.NewContextOn(db))
            Assert.False(await check.ProcessedWebhookEvents.AnyAsync(Ct));

        var redelivery = await DeliverAsync(db, delivery);

        Assert.Equal(VippsWebhookOutcomeKind.Applied, redelivery.Kind);
        (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Null(build.HeldByOrderId);
        Assert.Null(build.SoldOn);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not marked sold"));
    }

    [Theory]
    [InlineData("ABORTED")]
    [InlineData("EXPIRED")]
    [InlineData("TERMINATED")]
    public async Task Failed_FromPending_ReleasesTheBuild_WithoutAnEmail(string name)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        var outcome = await DeliverAsync(db, Delivery(name, amount: null));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Failed_FromReserved_ReleasesTheBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);

        await DeliverAsync(db, Delivery("EXPIRED", amount: null));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Failed_DoesNotReleaseABuildTheAdminReservedByHand()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        await using (var admin = postgres.NewContextOn(db))
        {
            await admin.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Availability, BuildAvailability.Reserved)
                .SetProperty(b => b.HeldByOrderId, (int?)null), Ct);
        }

        await DeliverAsync(db, Delivery("EXPIRED", amount: null));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Null(build.HeldByOrderId);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Failed_AfterTheOrderEnded_ChangesNothing(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, status);

        var outcome = await DeliverAsync(db, Delivery("ABORTED", amount: null));

        Assert.Null(outcome.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(status, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Cancelled_FromVipps_ReleasesAndNotifies_AndAfterAdminCancelDoesNothing()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);

        var first = await DeliverAsync(db, Delivery("CANCELLED"));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, first.Kind);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(EventTime.UtcDateTime, order.CancelledAt);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);
        _notifier.Verify(n => n.OrderCancelledAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);

        // The admin already cancelled. The CANCELLED event that follows changes nothing and sends nothing.
        var afterAdmin = await DeliverAsync(db, Delivery("CANCELLED"));

        Assert.Null(afterAdmin.AfterCommit);
        _notifier.Verify(n => n.OrderCancelledAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Cancelled_OnAPendingOrder_FailsAndReleasesTheBuild_WithoutAnEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        var outcome = await DeliverAsync(db, Delivery("CANCELLED"));

        Assert.Null(outcome.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Null(order.CancelledAt);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Cancelled_OnAPendingOrder_DoesNotReleaseABuildTheAdminReservedByHand()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        await using (var admin = postgres.NewContextOn(db))
        {
            await admin.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Availability, BuildAvailability.Reserved)
                .SetProperty(b => b.HeldByOrderId, (int?)null), Ct);
        }

        await DeliverAsync(db, Delivery("CANCELLED"));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Null(build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Cancelled_OnAPendingOrderAfterAPartialCapture_StaysPending_AndKeepsTheBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        await using (var fresh = postgres.NewContextOn(db))
            await fresh.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CapturedInOre, 500_000), Ct);

        await DeliverAsync(db, Delivery("CANCELLED"));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("manual handling"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task Cancelled_AfterAPartialCapture_StaysReserved_AndKeepsTheBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await using (var fresh = postgres.NewContextOn(db))
            await fresh.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CapturedInOre, 500_000), Ct);

        await DeliverAsync(db, Delivery("CANCELLED"));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("manual handling"));
        VerifyNoEmails();
    }

    [Fact]
    public async Task Refunded_KeepsTheBuildSold_AndNotifies()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await DeliverAsync(db, Delivery("CAPTURED"));
        VippsHas(refunded: Total);

        var first = await DeliverAsync(db, Delivery("REFUNDED"));
        var again = await DeliverAsync(db, Delivery("REFUNDED"));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, first.Kind);
        Assert.Null(again.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(Total, order.RefundedInOre);
        Assert.Equal(EventTime.UtcDateTime, order.RefundedAt);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();
    }

    [Fact]
    public async Task Refunded_InParts_EmailsEachPartOnce_AndRefundsAtTheCapturedTotal()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await DeliverAsync(db, Delivery("CAPTURED"));
        VippsHas(refunded: 300_000);

        var first = await DeliverAsync(db, Delivery("REFUNDED", 300_000));
        var again = await DeliverAsync(db, Delivery("REFUNDED", 300_000));

        Assert.NotNull(first.AfterCommit);
        Assert.Null(again.AfterCommit);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(300_000, order.RefundedInOre);
        Assert.Null(order.RefundedAt);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);

        VippsHas(refunded: Total);
        await DeliverAsync(db, Delivery("REFUNDED", Total - 300_000));

        (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(Total, order.RefundedInOre);
        Assert.Equal(EventTime.UtcDateTime, order.RefundedAt);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total - 300_000, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();

        await using var fresh = postgres.NewContextOn(db);
        var rows = await fresh.OrderRefunds.AsNoTracking().Where(r => r.OrderId == orderId).OrderBy(r => r.Id).ToListAsync(Ct);
        Assert.Equal([300_000, Total - 300_000], rows.Select(r => r.AmountInOre));
        Assert.All(rows, r => Assert.Equal(RefundSource.Webhook, r.Source));
        Assert.All(rows, r => Assert.Equal(EventTime.UtcDateTime, r.RefundedAt));
        Assert.All(rows, r => Assert.NotNull(r.IdempotencyKey));
    }

    [Fact]
    public async Task Captured_AfterRefunded_DoesNotMoveBack()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await DeliverAsync(db, Delivery("CAPTURED"));
        VippsHas(refunded: Total);
        await DeliverAsync(db, Delivery("REFUNDED"));

        var outcome = await DeliverAsync(db, Delivery("CAPTURED"));
        var authorized = await DeliverAsync(db, Delivery("AUTHORIZED"));
        var cancelled = await DeliverAsync(db, Delivery("CANCELLED"));

        Assert.Null(outcome.AfterCommit);
        Assert.Null(authorized.AfterCommit);
        Assert.Null(cancelled.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();
    }

    [Theory]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Captured_AfterTheOrderEnded_DoesNotMoveBack(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, status);

        await DeliverAsync(db, Delivery("CAPTURED"));
        await DeliverAsync(db, Delivery("REFUNDED"));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(status, order.Status);
        Assert.Equal(0, order.CapturedInOre);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        VerifyNoEmails();
    }

    [Fact]
    public async Task AnyEvent_ForAnOrderWhoseBuildWasDeleted_StillMovesTheOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await using (var admin = postgres.NewContextOn(db))
            await admin.PcBuilds.Where(b => b.Id == buildId).ExecuteDeleteAsync(Ct);

        await DeliverAsync(db, Delivery("CAPTURED"));

        await using var fresh = postgres.NewContextOn(db);
        var order = await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.PcBuildId);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Cancelled_AfterTheOrderEnded_ChangesNothing(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, status);

        var outcome = await DeliverAsync(db, Delivery("CANCELLED"));

        Assert.Null(outcome.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(status, order.Status);
        Assert.Null(order.CancelledAt);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        VerifyNoEmails();
    }

    [Theory]
    [InlineData("ABORTED")]
    [InlineData("EXPIRED")]
    [InlineData("TERMINATED")]
    public async Task Failed_AfterAPartialCapture_StaysReserved_AndKeepsTheBuild(string name)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await using (var fresh = postgres.NewContextOn(db))
            await fresh.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CapturedInOre, 500_000), Ct);

        var outcome = await DeliverAsync(db, Delivery(name, amount: null));

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.AfterCommit);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("manual handling") && e.Message.Contains(name));
        VerifyNoEmails();
    }

    [Fact]
    public async Task Captured_OnAPendingOrder_MarksPaid_AndSendsTheReservedEmailsOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);

        await DeliverAsync(db, Delivery("CAPTURED"));
        await DeliverAsync(db, Delivery("CAPTURED"));

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        _notifier.Verify(n => n.OrderReservedAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoEmails();
    }
}
