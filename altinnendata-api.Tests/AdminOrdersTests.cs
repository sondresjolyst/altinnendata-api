using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class AdminOrdersTests(PostgresDatabase postgres) : TestBase
{
    private const int Total = 1_299_000;
    private const string Reference = "ae-000001-abcdef12";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime WebhookTime = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IVippsClient> _vipps = new(MockBehavior.Strict);
    private readonly Mock<IOrderNotifier> _notifier = new();
    private readonly ListLogger<AdminOrderDto> _log = new();

    /// <summary>
    /// An order in <paramref name="status"/> for a build. A Pending or Reserved order holds the build,
    /// a Paid or Refunded one has sold it.
    /// </summary>
    private static async Task<(int BuildId, int OrderId)> SeedAsync(
        ApplicationDbContext db,
        OrderStatus status = OrderStatus.Reserved,
        string reference = Reference,
        string slug = "gaming-pc",
        DateTime? createdAt = null,
        Action<Order>? editOrder = null)
    {
        var build = new PcBuild { Slug = slug, Published = true, PriceNok = 12990, Availability = BuildAvailability.Available };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);

        var order = new Order
        {
            Reference = reference,
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
            BuildSlug = slug,
            BuildPriceInOre = Total,
            TotalInOre = Total,
            CapturedInOre = status is OrderStatus.Paid or OrderStatus.Refunded ? Total : 0,
            IsTest = true,
            TermsAcceptedAt = DateTime.UtcNow,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        editOrder?.Invoke(order);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        if (status is OrderStatus.Pending or OrderStatus.Reserved)
        {
            Assert.True(await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct));
        }
        else if (status is OrderStatus.Paid or OrderStatus.Refunded)
        {
            build.Availability = BuildAvailability.Sold;
            build.SoldOn = new DateOnly(2026, 10, 1);
            await db.SaveChangesAsync(Ct);
        }

        db.ChangeTracker.Clear();
        return (build.Id, order.Id);
    }

    private async Task<(PcBuild Build, Order Order)> ReloadAsync(ApplicationDbContext existing, int buildId, int orderId)
    {
        await using var fresh = postgres.NewContextOn(existing);
        return (await fresh.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == buildId, Ct),
            await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct));
    }

    /// <summary>Changes the order and its build on a context of its own, the way a webhook does while an admin action waits on Vipps.</summary>
    private async Task WebhookChangesAsync(ApplicationDbContext existing, int orderId, Action<Order, PcBuild> change)
    {
        await using var other = postgres.NewContextOn(existing);
        var order = await other.Orders.SingleAsync(o => o.Id == orderId, Ct);
        var build = await other.PcBuilds.SingleAsync(b => b.Id == order.PcBuildId, Ct);
        change(order, build);
        await other.SaveChangesAsync(Ct);
    }

    private static VippsPayment Payment(int captured = 0, int refunded = 0) =>
        new() { Reference = Reference, State = "AUTHORIZED", CapturedAmountInOre = captured, RefundedAmountInOre = refunded };

    private void SetupCapture(int captured = Total) =>
        _vipps.Setup(v => v.CapturePaymentAsync(Reference, Total, "capture-" + Reference, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment(captured: captured));

    private void SetupCancel() =>
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    private void SetupRefund(int amount = Total, int refundedBefore = 0, int? refunded = null) =>
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, amount, $"refund-{Reference}-{refundedBefore}", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment(captured: Total, refunded: refunded ?? refundedBefore + amount));

    private void SetupGetPayment(int captured = Total, int refunded = 0) =>
        _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment(captured: captured, refunded: refunded));

    private Task<IResult> Capture(ApplicationDbContext db, int id) => AdminOrders.Capture(id, db, _vipps.Object, _log, Ct);
    private Task<IResult> Cancel(ApplicationDbContext db, int id) => AdminOrders.Cancel(id, db, _vipps.Object, _notifier.Object, _log, Ct);
    /// <summary>A refund from an order page loaded now, unless <paramref name="expected"/> says what an older page showed.</summary>
    private async Task<IResult> Refund(ApplicationDbContext db, int id, int? amountInOre = null, int? expected = null)
    {
        expected ??= await db.Orders.AsNoTracking().Where(o => o.Id == id).Select(o => (int?)o.RefundedInOre).FirstOrDefaultAsync(Ct) ?? 0;
        return await AdminOrders.Refund(id, new RefundRequest(amountInOre, expected), db, _vipps.Object, _notifier.Object, _log, Ct);
    }

    private async Task<Order> OrderAsync(ApplicationDbContext existing, int orderId)
    {
        await using var fresh = postgres.NewContextOn(existing);
        return await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct);
    }

    private static AdminOrderDto AssertOk(IResult result) => Assert.IsType<Ok<AdminOrderDto>>(result).Value!;

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    private static VippsWebhookDelivery<VippsPaymentEvent> Delivery(string name, int amount = Total) => new()
    {
        Event = new VippsPaymentEvent
        {
            Msn = "123456",
            Reference = Reference,
            PspReference = Guid.NewGuid().ToString("N"),
            Name = name,
            Amount = new VippsWebhookAmount { Value = amount, Currency = "NOK" },
            Timestamp = new DateTimeOffset(WebhookTime),
            Success = true
        },
        IsTest = true,
        EventKey = Guid.NewGuid().ToString("N"),
        Slot = ShopVipps.Slot
    };

    [Fact]
    public async Task List_IsNewestFirst_WithTheGuaranteeAndInvoiceNumber()
    {
        await using var db = await postgres.NewContextAsync();
        var guarantee = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var (_, olderId) = await SeedAsync(db, OrderStatus.Paid, "ae-000001-older000", "older-pc", DateTime.UtcNow.AddDays(-2));
        var (newerBuildId, newerId) = await SeedAsync(db, OrderStatus.Reserved, "ae-000002-newer000", "newer-pc", DateTime.UtcNow,
            o =>
            {
                o.CaptureGuaranteedUntil = guarantee;
                o.InvoiceNumber = "10042";
                o.DeliveryMethod = DeliveryMethod.Shipping;
            });

        var result = await AdminOrders.List(db, Ct);

        var list = Assert.IsType<Ok<List<AdminOrderSummaryDto>>>(result).Value!;
        Assert.Equal([newerId, olderId], list.Select(o => o.Id));
        var newer = list[0];
        Assert.Equal("ae-000002-newer000", newer.Reference);
        Assert.Equal("Reserved", newer.Status);
        Assert.Equal("Ola Nordmann", newer.BuyerName);
        Assert.Equal("Gaming-PC", newer.BuildTitle);
        Assert.Equal(newerBuildId, newer.PcBuildId);
        Assert.Equal("Shipping", newer.DeliveryMethod);
        Assert.Equal(Total, newer.TotalInOre);
        Assert.True(newer.IsTest);
        Assert.Equal(guarantee, newer.CaptureGuaranteedUntil);
        Assert.Equal("10042", newer.InvoiceNumber);
        Assert.Null(list[1].CaptureGuaranteedUntil);
        Assert.Null(list[1].InvoiceNumber);
    }

    [Fact]
    public async Task Get_ReturnsTheWholeOrder_Or404()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, editOrder: o =>
        {
            o.DeliveryMethod = DeliveryMethod.HomeDelivery;
            o.StreetAddress = "Storgata 1";
            o.PostalCode = "0155";
            o.City = "Oslo";
            o.WithinDistanceConfirmed = true;
            o.DeliveryPriceInOre = 50000;
        });

        var dto = AssertOk(await AdminOrders.Get(orderId, db, Ct));

        Assert.Equal(orderId, dto.Id);
        Assert.Equal(Reference, dto.Reference);
        Assert.Equal("Reserved", dto.Status);
        Assert.Equal("no", dto.Locale);
        Assert.Equal("ola@example.com", dto.BuyerEmail);
        Assert.Equal("4791234567", dto.BuyerPhone);
        Assert.Equal("Storgata 1", dto.StreetAddress);
        Assert.Equal("0155", dto.PostalCode);
        Assert.Equal("Oslo", dto.City);
        Assert.Equal("HomeDelivery", dto.DeliveryMethod);
        Assert.Equal("Henting", dto.DeliveryLabel);
        Assert.Equal(50000, dto.DeliveryPriceInOre);
        Assert.True(dto.WithinDistanceConfirmed);
        Assert.Equal(buildId, dto.PcBuildId);
        Assert.Equal("gaming-pc", dto.BuildSlug);
        Assert.Equal(Total, dto.BuildPriceInOre);
        Assert.Equal(Total, dto.TotalInOre);
        Assert.IsType<NotFound>(await AdminOrders.Get(orderId + 100, db, Ct));
    }

    [Fact]
    public async Task Actions_OnAnUnknownOrder_Return404()
    {
        await using var db = await postgres.NewContextAsync();

        Assert.IsType<NotFound>(await Capture(db, 999));
        Assert.IsType<NotFound>(await Cancel(db, 999));
        Assert.IsType<NotFound>(await Refund(db, 999));
        Assert.IsType<NotFound>(await Refund(db, 999, 100));
        Assert.IsType<NotFound>(await AdminOrders.SetInvoiceNumber(999, new InvoiceNumberRequest("1"), db, Ct));
        _vipps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Capture_MarksPaidAndSold_WithAnIdempotencyKeyFromTheReference()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        SetupCapture();
        var before = DateTime.UtcNow;

        var dto = AssertOk(await Capture(db, orderId));

        _vipps.Verify(v => v.CapturePaymentAsync("ae-000001-abcdef12", 1299000, "capture-ae-000001-abcdef12", true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Paid", dto.Status);
        Assert.Equal(Total, dto.CapturedInOre);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Total, order.CapturedInOre);
        Assert.InRange(order.CapturedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.True(order.UpdatedAt >= before.AddSeconds(-1));
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), build.SoldOn);
        Assert.Null(build.HeldByOrderId);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Capture_Partial_Returns502_AndStaysReserved()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        SetupCapture(captured: 500_000);

        var result = await Capture(db, orderId);

        AssertProblem(result, StatusCodes.Status502BadGateway, "captureIncomplete");
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(500_000, order.CapturedInOre);
        Assert.Null(order.CapturedAt);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Capture_OnAnOrderThatIsNotReserved_Returns409(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, status);

        var result = await Capture(db, orderId);

        AssertProblem(result, StatusCodes.Status409Conflict, "wrongStatus");
        _vipps.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Failed)]
    public async Task Cancel_OnAnOrderThatIsNotReserved_Returns409(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, status);

        AssertProblem(await Cancel(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");
        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancel_OnAPartlyCapturedOrder_Returns409_AndKeepsTheHold()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, editOrder: o => o.CapturedInOre = 500_000);

        AssertProblem(await Cancel(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");

        _vipps.VerifyNoOtherCalls();
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(orderId, build.HeldByOrderId);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Reserved)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Refund_OnAnOrderThatIsNotPaid_Returns409(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, status);

        AssertProblem(await Refund(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");
        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_OnAPaidOrderWithNothingLeftToRefund_Returns409()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid, editOrder: o => o.RefundedInOre = Total);

        AssertProblem(await Refund(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");
        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Total - 300_000 + 1)]
    public async Task Refund_WithAnAmountOutsideWhatIsLeft_Returns400OnTheAmount(int amount)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Paid, editOrder: o => o.RefundedInOre = 300_000);

        var result = await Refund(db, orderId, amount);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(["amountInOre"], problem.ProblemDetails.Errors.Keys);
        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(300_000, order.RefundedInOre);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_ReleasesOnlyItsOwnHold_AndNotifies(bool adminTookTheBuild)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        if (adminTookTheBuild)
        {
            // An admin edit clears the hold and keeps the admin's availability.
            var held = await db.PcBuilds.SingleAsync(b => b.Id == buildId, Ct);
            held.HeldByOrderId = null;
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
        }
        SetupCancel();
        var before = DateTime.UtcNow;

        var dto = AssertOk(await Cancel(db, orderId));

        _vipps.Verify(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Cancelled", dto.Status);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.InRange(order.CancelledAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Null(build.HeldByOrderId);
        Assert.Equal(adminTookTheBuild ? BuildAvailability.Reserved : BuildAvailability.Available, build.Availability);
        _notifier.Verify(n => n.OrderCancelledAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_WithoutAnAmount_RefundsAll_KeepsTheBuildSold_AndNotifies()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund();
        var before = DateTime.UtcNow;

        var dto = AssertOk(await Refund(db, orderId));

        _vipps.Verify(v => v.RefundPaymentAsync(Reference, Total, "refund-ae-000001-abcdef12-0", true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Refunded", dto.Status);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(Total, order.RefundedInOre);
        Assert.InRange(order.RefundedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        Assert.Equal(new DateOnly(2026, 10, 1), build.SoldOn);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_TwoPartsThenTheRest_EachWithItsOwnKeyAndEmail_RefundsAtTheEnd()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund(amount: 300_000);
        SetupRefund(amount: 400_000, refundedBefore: 300_000);
        SetupRefund(amount: Total - 700_000, refundedBefore: 700_000);

        var first = AssertOk(await Refund(db, orderId, 300_000));
        Assert.Equal("Paid", first.Status);
        Assert.Equal(300_000, first.RefundedInOre);
        Assert.Null(first.RefundedAt);

        var second = AssertOk(await Refund(db, orderId, 400_000));
        Assert.Equal("Paid", second.Status);
        Assert.Equal(700_000, second.RefundedInOre);

        var rest = AssertOk(await Refund(db, orderId));
        Assert.Equal("Refunded", rest.Status);
        Assert.Equal(Total, rest.RefundedInOre);
        Assert.NotNull(rest.RefundedAt);

        _vipps.Verify(v => v.RefundPaymentAsync(Reference, 300_000, "refund-ae-000001-abcdef12-0", true, It.IsAny<CancellationToken>()), Times.Once);
        _vipps.Verify(v => v.RefundPaymentAsync(Reference, 400_000, "refund-ae-000001-abcdef12-300000", true, It.IsAny<CancellationToken>()), Times.Once);
        _vipps.Verify(v => v.RefundPaymentAsync(Reference, Total - 700_000, "refund-ae-000001-abcdef12-700000", true, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 400_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total - 700_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
    }

    [Fact]
    public async Task Refund_WhenVippsHasNotRecordedItYet_StaysPaid_AndLeavesTheEmailToTheWebhook()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund(amount: 300_000, refunded: 0);

        var dto = AssertOk(await Refund(db, orderId, 300_000));

        Assert.Equal("Paid", dto.Status);
        Assert.Equal(0, dto.RefundedInOre);
        _notifier.VerifyNoOtherCalls();
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(0, order.RefundedInOre);
    }

    public static TheoryData<string, string> VippsFailures => new()
    {
        { "capture", "http" }, { "capture", "timeout" },
        { "cancel", "http" }, { "cancel", "timeout" },
        { "refund", "http" }, { "refund", "timeout" }
    };

    [Theory]
    [MemberData(nameof(VippsFailures))]
    public async Task Action_WhenVippsFails_ChangesNothing(string action, string failure)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, action == "refund" ? OrderStatus.Paid : OrderStatus.Reserved);
        var (buildBefore, orderBefore) = await ReloadAsync(db, buildId, orderId);
        // HttpClient reports its own timeout as a TaskCanceledException around a TimeoutException.
        Exception error = failure == "http"
            ? new HttpRequestException("Vipps is down")
            : new TaskCanceledException("The request was canceled.", new TimeoutException());
        var vipps = new Mock<IVippsClient>();
        vipps.Setup(v => v.CapturePaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        vipps.Setup(v => v.CancelPaymentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        vipps.Setup(v => v.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);

        var result = action switch
        {
            "capture" => await AdminOrders.Capture(orderId, db, vipps.Object, _log, Ct),
            "cancel" => await AdminOrders.Cancel(orderId, db, vipps.Object, _notifier.Object, _log, Ct),
            _ => await AdminOrders.Refund(orderId, new RefundRequest(null, 0), db, vipps.Object, _notifier.Object, _log, Ct)
        };

        AssertProblem(result, StatusCodes.Status502BadGateway, "paymentProviderUnavailable");
        Assert.Equal("Vipps did not confirm the change. Check the payment in the Vipps portal before you try again.",
            Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("nothing was changed", StringComparison.OrdinalIgnoreCase));
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        if (action == "refund")
        {
            // The refund may have happened in Vipps, so the order remembers it until Vipps shows it.
            Assert.Equal(orderBefore.RefundedInOre, order.RefundedInOre);
            Assert.Equal($"refund-{Reference}-0", order.PendingRefundKey);
            Assert.Equal(Total, order.PendingRefundInOre);
        }
        else
        {
            Assert.Equal(orderBefore.Version, order.Version);
        }
        Assert.Equal(orderBefore.Status, order.Status);
        Assert.Equal(buildBefore.Version, build.Version);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_WithoutTheRefundedTotalItWasLoadedWith_Returns400()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);

        foreach (var req in new RefundRequest?[] { null, new(300_000, null) })
        {
            var problem = Assert.IsType<ValidationProblem>(await AdminOrders.Refund(orderId, req, db, _vipps.Object, _notifier.Object, _log, Ct));
            Assert.Equal(["expectedRefundedInOre"], problem.ProblemDetails.Errors.Keys);
        }
        _vipps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_RetriedAfterTheWebhookRecordedTheFirstTry_Returns409RefundChanged_AndRefundsOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled.", new TimeoutException()));

        AssertProblem(await Refund(db, orderId, 300_000, expected: 0), StatusCodes.Status502BadGateway, "paymentProviderUnavailable");
        // The refund did happen in Vipps, and its REFUNDED webhook records it.
        SetupGetPayment(refunded: 300_000);
        await using (var other = postgres.NewContextOn(db))
        {
            var outcome = await new ShopWebhookHandler(other, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance)
                .HandleAsync(Delivery("REFUNDED", 300_000), Ct);
            Assert.NotNull(outcome.AfterCommit);
            await other.SaveChangesAsync(Ct);
        }
        db.ChangeTracker.Clear();

        var retry = await Refund(db, orderId, 300_000, expected: 0);

        AssertProblem(retry, StatusCodes.Status409Conflict, "refundChanged");
        Assert.Equal("The order was refunded meanwhile. Reload the order.", Assert.IsType<ProblemHttpResult>(retry).ProblemDetails.Detail);
        _vipps.Verify(v => v.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);
        var order = await OrderAsync(db, orderId);
        Assert.Equal(300_000, order.RefundedInOre);
        Assert.Null(order.PendingRefundKey);
        Assert.Null(order.PendingRefundInOre);
    }

    [Fact]
    public async Task Refund_FromASecondTabLoadedBeforeTheFirstRefund_Returns409RefundChanged()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund(amount: 300_000);

        AssertOk(await Refund(db, orderId, 300_000, expected: 0));
        db.ChangeTracker.Clear();
        AssertProblem(await Refund(db, orderId, 300_000, expected: 0), StatusCodes.Status409Conflict, "refundChanged");

        _vipps.Verify(v => v.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
        Assert.Equal(300_000, (await OrderAsync(db, orderId)).RefundedInOre);
    }

    [Fact]
    public async Task Refund_WhileVippsHasNotShownTheLastOne_RefusesAnotherAmount_AndRetriesTheSameOne()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund(amount: 300_000, refunded: 0);

        var first = AssertOk(await Refund(db, orderId, 300_000));
        Assert.Equal(0, first.RefundedInOre);
        var pending = await OrderAsync(db, orderId);
        Assert.Equal($"refund-{Reference}-0", pending.PendingRefundKey);
        Assert.Equal(300_000, pending.PendingRefundInOre);
        db.ChangeTracker.Clear();

        var other = await Refund(db, orderId, 200_000);

        AssertProblem(other, StatusCodes.Status409Conflict, "refundPending");
        Assert.Equal("Wait until Vipps has confirmed the last refund.", Assert.IsType<ProblemHttpResult>(other).ProblemDetails.Detail);
        _vipps.Verify(v => v.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);

        // The same refund again reuses its key, and now Vipps shows it.
        db.ChangeTracker.Clear();
        SetupRefund(amount: 300_000);
        var same = AssertOk(await Refund(db, orderId, 300_000));

        Assert.Equal(300_000, same.RefundedInOre);
        _vipps.Verify(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        var done = await OrderAsync(db, orderId);
        Assert.Null(done.PendingRefundKey);
        Assert.Null(done.PendingRefundInOre);
    }

    [Fact]
    public async Task Capture_WhenTheCapturedWebhookWinsTheRace_AnswersOkWithTheWebhooksResult()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        _vipps.Setup(v => v.CapturePaymentAsync(Reference, Total, "capture-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (o, b) =>
                {
                    o.Status = OrderStatus.Paid;
                    o.CapturedInOre = Total;
                    o.CapturedAt = WebhookTime;
                    BuildHold.MarkSold(b, o.Id, new DateOnly(2026, 10, 18));
                });
                return Payment(captured: Total);
            });

        var dto = AssertOk(await Capture(db, orderId));

        Assert.Equal("Paid", dto.Status);
        Assert.Equal(WebhookTime, dto.CapturedAt);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(WebhookTime, order.CapturedAt);
        Assert.Equal(BuildAvailability.Sold, build.Availability);
        Assert.Equal(new DateOnly(2026, 10, 18), build.SoldOn);
    }

    [Fact]
    public async Task Capture_WhenTheOrderFailsDuringTheCall_Returns409_AndKeepsTheWebhooksState()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        _vipps.Setup(v => v.CapturePaymentAsync(Reference, Total, "capture-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (o, b) =>
                {
                    o.Status = OrderStatus.Failed;
                    BuildHold.Release(b, o.Id);
                });
                return Payment(captured: Total);
            });

        AssertProblem(await Capture(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains($"captured {Total} øre") && e.Message.Contains("Failed"));
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(0, order.CapturedInOre);
        Assert.Equal(BuildAvailability.Available, build.Availability);
    }

    [Fact]
    public async Task Capture_WhenAnAdminEditsTheBuildDuringTheCall_PaysTheOrder_AndKeepsTheAdminsBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        _vipps.Setup(v => v.CapturePaymentAsync(Reference, Total, "capture-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (_, b) =>
                {
                    b.HeldByOrderId = null;
                    b.Availability = BuildAvailability.Available;
                });
                return Payment(captured: Total);
            });

        var dto = AssertOk(await Capture(db, orderId));

        Assert.Equal("Paid", dto.Status);
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.SoldOn);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not marked sold"));
    }

    [Fact]
    public async Task Capture_ThatSellsTheBuild_LogsNoUnsoldWarning()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db);
        SetupCapture();

        AssertOk(await Capture(db, orderId));

        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Refund_WhenTheOrderMovesDuringTheCall_Returns409_AndLogsTheRefundedAmount()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, Total, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (o, _) => o.Status = OrderStatus.Cancelled);
                return Payment(captured: Total, refunded: Total);
            });

        AssertProblem(await Refund(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains($"refunded {Total} øre") && e.Message.Contains("Cancelled"));
        _notifier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("cancel")]
    [InlineData("refund")]
    public async Task Action_WhenTheAdminRequestIsAbortedAfterVippsAnswered_StillSavesAndNotifies(string action)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, action == "refund" ? OrderStatus.Paid : OrderStatus.Reserved);
        using var aborted = new CancellationTokenSource();
        _vipps.Setup(v => v.CapturePaymentAsync(Reference, Total, "capture-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(() => { aborted.Cancel(); return Task.FromResult(Payment(captured: Total)); });
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(() => { aborted.Cancel(); return Task.CompletedTask; });
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, Total, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .Returns(() => { aborted.Cancel(); return Task.FromResult(Payment(captured: Total, refunded: Total)); });

        var result = action switch
        {
            "capture" => await AdminOrders.Capture(orderId, db, _vipps.Object, _log, aborted.Token),
            "cancel" => await AdminOrders.Cancel(orderId, db, _vipps.Object, _notifier.Object, _log, aborted.Token),
            _ => await AdminOrders.Refund(orderId, new RefundRequest(null, 0), db, _vipps.Object, _notifier.Object, _log, aborted.Token)
        };

        var expected = action switch { "capture" => "Paid", "cancel" => "Cancelled", _ => "Refunded" };
        Assert.Equal(expected, AssertOk(result).Status);
        await using var fresh = postgres.NewContextOn(db);
        Assert.Equal(expected, (await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct)).Status.ToString());
        if (action == "cancel")
            _notifier.Verify(n => n.OrderCancelledAsync(orderId, It.Is<CancellationToken>(t => !t.CanBeCanceled)), Times.Once);
        if (action == "refund")
            _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total, It.Is<CancellationToken>(t => !t.CanBeCanceled)), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancel_WhenTheCancelledWebhookWinsTheRace_AnswersOk_AndSendsNoSecondEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(() => WebhookChangesAsync(db, orderId, (o, b) =>
            {
                o.Status = OrderStatus.Cancelled;
                o.CancelledAt = WebhookTime;
                BuildHold.Release(b, o.Id);
            }));

        var dto = AssertOk(await Cancel(db, orderId));

        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal(WebhookTime, dto.CancelledAt);
        var (build, _) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_WhenTheRefundedWebhookWinsTheRace_AnswersOk_AndSendsNoSecondEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, Total, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (o, _) =>
                {
                    o.Status = OrderStatus.Refunded;
                    o.RefundedInOre = Total;
                    o.RefundedAt = WebhookTime;
                });
                return Payment(captured: Total, refunded: Total);
            });

        var dto = AssertOk(await Refund(db, orderId));

        Assert.Equal("Refunded", dto.Status);
        Assert.Equal(WebhookTime, dto.RefundedAt);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_Partial_WhenTheRefundedWebhookRecordsItFirst_SendsNoSecondEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await WebhookChangesAsync(db, orderId, (o, _) => o.RefundedInOre = 300_000);
                return Payment(captured: Total, refunded: 300_000);
            });

        var dto = AssertOk(await Refund(db, orderId, 300_000));

        Assert.Equal("Paid", dto.Status);
        Assert.Equal(300_000, dto.RefundedInOre);
        var (_, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(300_000, order.RefundedInOre);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PartialAdminRefundThenItsWebhook_SendsTheEmailOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        SetupRefund(amount: 300_000);
        SetupGetPayment(refunded: 300_000);

        AssertOk(await Refund(db, orderId, 300_000));
        await using var other = postgres.NewContextOn(db);
        var delivery = Delivery("REFUNDED");
        delivery = delivery with { Event = delivery.Event with { Amount = new VippsWebhookAmount { Value = 300_000, Currency = "NOK" } } };
        var outcome = await new ShopWebhookHandler(other, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance)
            .HandleAsync(delivery, Ct);

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.AfterCommit);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REFUNDED")]
    public async Task AdminActionThenItsWebhook_SendsTheEmailOnce(string webhook)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, webhook == "REFUNDED" ? OrderStatus.Paid : OrderStatus.Reserved);
        SetupCancel();
        SetupRefund();
        SetupGetPayment(refunded: Total);

        AssertOk(webhook == "REFUNDED" ? await Refund(db, orderId) : await Cancel(db, orderId));
        await using var other = postgres.NewContextOn(db);
        var outcome = await new ShopWebhookHandler(other, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance)
            .HandleAsync(Delivery(webhook), Ct);

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.AfterCommit);
        if (webhook == "REFUNDED")
            _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total, It.IsAny<CancellationToken>()), Times.Once);
        else
            _notifier.Verify(n => n.OrderCancelledAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetInvoiceNumber_TrimsAndClears()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Refunded);

        var set = AssertOk(await AdminOrders.SetInvoiceNumber(orderId, new InvoiceNumberRequest("  10042 "), db, Ct));
        Assert.Equal("10042", set.InvoiceNumber);
        Assert.Equal("10042", (await ReloadAsync(db, buildId, orderId)).Order.InvoiceNumber);

        var cleared = AssertOk(await AdminOrders.SetInvoiceNumber(orderId, new InvoiceNumberRequest(""), db, Ct));
        Assert.Null(cleared.InvoiceNumber);
        Assert.Null((await ReloadAsync(db, buildId, orderId)).Order.InvoiceNumber);
        Assert.Equal("Refunded", cleared.Status);

        var validator = new InvoiceNumberRequestValidator();
        Assert.True(validator.Validate(new InvoiceNumberRequest(new string('1', 50))).IsValid);
        Assert.True(validator.Validate(new InvoiceNumberRequest(null)).IsValid);
        var tooLong = validator.Validate(new InvoiceNumberRequest(new string('1', 51)));
        Assert.False(tooLong.IsValid);
        Assert.Contains(tooLong.Errors, e => e.PropertyName == nameof(InvoiceNumberRequest.InvoiceNumber));
    }
}
