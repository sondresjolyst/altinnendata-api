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
    private const string Reference = "AID-000001-ABCDEF12";

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
        var build = new PcBuild { Slug = slug, Published = true, PriceNok = 12990, Availability = BuildAvailability.Available, Components = [PartWithCondition()] };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);

        var order = new Order
        {
            Reference = reference, VippsReference = reference,
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
        var (_, olderId) = await SeedAsync(db, OrderStatus.Paid, "AID-000001-0000AAAA", "older-pc", DateTime.UtcNow.AddDays(-2),
            o => o.RefundedInOre = 300_000);
        var (newerBuildId, newerId) = await SeedAsync(db, OrderStatus.Reserved, "AID-000002-0000BBBB", "newer-pc", DateTime.UtcNow,
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
        Assert.Equal("AID-000002-0000BBBB", newer.Reference);
        Assert.Equal("Reserved", newer.Status);
        Assert.Equal("Ola Nordmann", newer.BuyerName);
        Assert.Equal("Gaming-PC", newer.BuildTitle);
        Assert.Equal(newerBuildId, newer.PcBuildId);
        Assert.Equal("Shipping", newer.DeliveryMethod);
        Assert.Equal(Total, newer.TotalInOre);
        Assert.True(newer.IsTest);
        Assert.Equal(guarantee, newer.CaptureGuaranteedUntil);
        Assert.Equal("10042", newer.InvoiceNumber);
        Assert.Equal(0, newer.CapturedInOre);
        Assert.Equal(0, newer.RefundedInOre);
        Assert.Null(list[1].CaptureGuaranteedUntil);
        Assert.Null(list[1].InvoiceNumber);
        Assert.Equal(Total, list[1].CapturedInOre);
        Assert.Equal(300_000, list[1].RefundedInOre);
    }

    [Fact]
    public async Task Get_ShowsTheDeliveryDateAndPlace_ForShipping()
    {
        await using var db = await postgres.NewContextAsync();
        var shippedAt = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid, editOrder: o =>
        {
            o.DeliveryMethod = DeliveryMethod.Shipping;
            o.StreetAddress = "Storgata 1";
            o.PostalCode = "0155";
            o.City = "Oslo";
            o.ShippedAt = shippedAt;
        });

        var dto = AssertOk(await AdminOrders.Get(orderId, db, Ct));

        Assert.Equal(shippedAt, dto.DeliveryDate);
        Assert.Equal("Storgata 1, 0155 Oslo", dto.DeliveryPlace);
        Assert.Empty(dto.Refunds);
    }

    [Fact]
    public async Task Get_ForPickup_ShowsTheSellersAddressAsTheDeliveryPlace()
    {
        await using var db = await postgres.NewContextAsync();
        db.AppSettings.Add(new Models.Admin.AppSettings { Id = 1, StreetAddress = "Mårvegen 21a", PostalCode = "4347", AddressLocality = "Lye" });
        await db.SaveChangesAsync(Ct);
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);

        var dto = AssertOk(await AdminOrders.Get(orderId, db, Ct));

        Assert.Null(dto.DeliveryDate);
        Assert.Equal("Mårvegen 21a, 4347 Lye", dto.DeliveryPlace);
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

        _vipps.Verify(v => v.CapturePaymentAsync("AID-000001-ABCDEF12", 1299000, "capture-AID-000001-ABCDEF12", true, It.IsAny<CancellationToken>()), Times.Once);
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

        _vipps.Verify(v => v.RefundPaymentAsync(Reference, Total, "refund-AID-000001-ABCDEF12-0", true, It.IsAny<CancellationToken>()), Times.Once);
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

        _vipps.Verify(v => v.RefundPaymentAsync(Reference, 300_000, "refund-AID-000001-ABCDEF12-0", true, It.IsAny<CancellationToken>()), Times.Once);
        _vipps.Verify(v => v.RefundPaymentAsync(Reference, 400_000, "refund-AID-000001-ABCDEF12-300000", true, It.IsAny<CancellationToken>()), Times.Once);
        _vipps.Verify(v => v.RefundPaymentAsync(Reference, Total - 700_000, "refund-AID-000001-ABCDEF12-700000", true, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 400_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, Total - 700_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(BuildAvailability.Sold, build.Availability);

        Assert.Equal([300_000, 400_000, Total - 700_000], rest.Refunds.Select(r => r.AmountInOre));
        Assert.All(rest.Refunds, r => Assert.Equal("Admin", r.Source));
        await using var fresh = postgres.NewContextOn(db);
        var rows = await fresh.OrderRefunds.AsNoTracking().Where(r => r.OrderId == orderId).OrderBy(r => r.Id).ToListAsync(Ct);
        Assert.Equal([$"refund-{Reference}-0", $"refund-{Reference}-300000", $"refund-{Reference}-700000"], rows.Select(r => r.IdempotencyKey));
        Assert.Equal(rows.Select(r => r.RefundedAt), rest.Refunds.Select(r => r.RefundedAt));
        Assert.Equal(order.RefundedAt, rows[^1].RefundedAt);
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
        else if (action == "cancel")
        {
            // The cancel may have happened in Vipps, so the order remembers that the admin asked for it.
            Assert.NotNull(order.AdminCancelRequestedAt);
            Assert.Equal(orderBefore.UpdatedAt, order.UpdatedAt);
        }
        else
        {
            Assert.Equal(orderBefore.Version, order.Version);
        }
        Assert.Equal(orderBefore.Status, order.Status);
        Assert.Equal(buildBefore.Version, build.Version);
        _notifier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refund_ThatVippsRejects_ClearsThePendingRefund_SoAnotherAmountGoesThrough(bool orderChangedDuringTheCall)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (orderChangedDuringTheCall)
                    await WebhookChangesAsync(db, orderId, (o, _) => o.UpdatedAt = DateTime.UtcNow);
                throw new HttpRequestException("Bad Request", null, System.Net.HttpStatusCode.BadRequest);
            });
        // Vipps shows no refund, so nothing was paid back.
        SetupGetPayment(refunded: 0);

        var rejected = await Refund(db, orderId, 300_000);

        _vipps.Verify(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>()), Times.Once);
        AssertProblem(rejected, StatusCodes.Status502BadGateway, "refundRefused");
        Assert.Equal("Vipps refused the refund. Nothing was paid back.", Assert.IsType<ProblemHttpResult>(rejected).ProblemDetails.Detail);
        var cleared = await OrderAsync(db, orderId);
        Assert.Null(cleared.PendingRefundKey);
        Assert.Null(cleared.PendingRefundInOre);
        Assert.Equal(0, cleared.RefundedInOre);
        Assert.Equal(OrderStatus.Paid, cleared.Status);
        _notifier.VerifyNoOtherCalls();

        db.ChangeTracker.Clear();
        SetupRefund(amount: 200_000);
        var other = AssertOk(await Refund(db, orderId, 200_000));

        Assert.Equal(200_000, other.RefundedInOre);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 200_000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refund_ThatVippsRejectsButShowsAsPaid_RecordsIt_AndEmails()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Bad Request", null, System.Net.HttpStatusCode.BadRequest));
        SetupGetPayment(refunded: 300_000);

        var dto = AssertOk(await Refund(db, orderId, 300_000));

        Assert.Equal("Paid", dto.Status);
        Assert.Equal(300_000, dto.RefundedInOre);
        _notifier.Verify(n => n.OrderRefundedAsync(orderId, 300_000, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
        var order = await OrderAsync(db, orderId);
        Assert.Equal(300_000, order.RefundedInOre);
        Assert.Null(order.PendingRefundKey);
        Assert.Null(order.PendingRefundInOre);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("server")]
    [InlineData("transport")]
    [InlineData("408")]
    [InlineData("409")]
    [InlineData("429")]
    [InlineData("400, then the lookup fails")]
    [InlineData("400, then the lookup times out")]
    public async Task Refund_ThatVippsMayHaveMade_KeepsThePendingRefund_AndRefusesAnotherAmount(string failure)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db, OrderStatus.Paid);
        Exception error = failure switch
        {
            "timeout" => new TaskCanceledException("The request was canceled.", new TimeoutException()),
            "server" => new HttpRequestException("Internal Server Error", null, System.Net.HttpStatusCode.InternalServerError),
            "transport" => new HttpRequestException("Connection refused"),
            "408" or "409" or "429" => new HttpRequestException(failure, null, (System.Net.HttpStatusCode)int.Parse(failure)),
            _ => new HttpRequestException("Bad Request", null, System.Net.HttpStatusCode.BadRequest)
        };
        _vipps.Setup(v => v.RefundPaymentAsync(Reference, 300_000, $"refund-{Reference}-0", true, It.IsAny<CancellationToken>())).ThrowsAsync(error);
        if (failure.Contains("lookup"))
            _vipps.Setup(v => v.GetPaymentAsync(Reference, true, It.IsAny<CancellationToken>())).ThrowsAsync(failure.EndsWith("times out")
                ? new TaskCanceledException("The request was canceled.", new TimeoutException())
                : new HttpRequestException("Vipps is down"));

        var result = await Refund(db, orderId, 300_000);

        AssertProblem(result, StatusCodes.Status502BadGateway, "paymentProviderUnavailable");
        Assert.DoesNotContain("Nothing was paid back", Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail);
        _vipps.Verify(v => v.GetPaymentAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            failure.Contains("lookup") ? Times.Once() : Times.Never());
        var pending = await OrderAsync(db, orderId);
        Assert.Equal($"refund-{Reference}-0", pending.PendingRefundKey);
        Assert.Equal(300_000, pending.PendingRefundInOre);

        db.ChangeTracker.Clear();
        AssertProblem(await Refund(db, orderId, 200_000), StatusCodes.Status409Conflict, "refundPending");
        _vipps.Verify(v => v.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Cancel_MarksTheRequestBeforeVippsIsCalled()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db);
        DateTime? markedDuringTheCall = null;
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(async () => markedDuringTheCall = (await OrderAsync(db, orderId)).AdminCancelRequestedAt);

        AssertOk(await Cancel(db, orderId));

        Assert.NotNull(markedDuringTheCall);
        Assert.Null((await OrderAsync(db, orderId)).ReservationEndedAt);
    }

    [Fact]
    public async Task Cancel_WhenVippsFails_KeepsTheRequestMark_AndTheOrderReserved()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        AssertProblem(await Cancel(db, orderId), StatusCodes.Status502BadGateway, "paymentProviderUnavailable");

        var (build, order) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.NotNull(order.AdminCancelRequestedAt);
        Assert.Equal(orderId, build.HeldByOrderId);
    }

    [Fact]
    public async Task Cancel_WhenAWebhookChangesTheOrderBeforeTheMark_MarksTheFreshOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db);
        SetupCancel();
        var interceptor = new OnceBeforeSave(() => WebhookChangesAsync(db, orderId, (o, _) => o.InvoiceNumber = "F-1"));

        await using var admin = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options);
        var dto = AssertOk(await Cancel(admin, orderId));

        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal("F-1", dto.InvoiceNumber);
        Assert.True(interceptor.Fired);
    }

    [Fact]
    public async Task Cancel_WhenAWebhookCancelsBeforeTheMark_Returns409_AndDoesNotCallVipps()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedAsync(db);
        var interceptor = new OnceBeforeSave(() => WebhookChangesAsync(db, orderId, (o, _) => o.Status = OrderStatus.Cancelled));

        await using var admin = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options);

        AssertProblem(await Cancel(admin, orderId), StatusCodes.Status409Conflict, "wrongStatus");
        _vipps.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancel_RacingTheRealCancelledWebhook_ReleasesTheBuild_SendsTheCancelEmailOnce_AndNoReservationEndedEmail()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db);
        var webhookNotifier = new Mock<IOrderNotifier>();
        _vipps.Setup(v => v.CancelPaymentAsync(Reference, "cancel-" + Reference, true, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Vipps sends CANCELLED while the admin action still waits for its answer.
                await using var hook = postgres.NewContextOn(db);
                var delivery = new VippsWebhookDelivery<VippsPaymentEvent>
                {
                    Event = new VippsPaymentEvent
                    {
                        Msn = "1", Reference = Reference, PspReference = "psp-cancel", Name = "CANCELLED",
                        Amount = new VippsWebhookAmount { Value = Total, Currency = "NOK" }, Timestamp = WebhookTime, Success = true
                    },
                    IsTest = true, EventKey = "cancel-event", Slot = ShopVipps.Slot
                };
                var outcome = await new ShopWebhookHandler(hook, _vipps.Object, webhookNotifier.Object, new ListLogger<ShopWebhookHandler>())
                    .HandleAsync(delivery, Ct);
                await hook.SaveChangesAsync(Ct);
                await outcome.AfterCommit!(Ct);
            });

        var dto = AssertOk(await Cancel(db, orderId));

        Assert.Equal("Cancelled", dto.Status);
        Assert.Null(dto.ReservationEndedAt);
        Assert.False(dto.HoldsBuild);
        var (build, _) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);
        webhookNotifier.Verify(n => n.OrderCancelledAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        webhookNotifier.VerifyNoOtherCalls();
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
        Assert.Empty(dto.Refunds);
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

    private Task<IResult> Shipped(ApplicationDbContext db, int id, string? note = null) =>
        AdminOrders.MarkShipped(id, new ShippedRequest(note), db, _notifier.Object, _log, Ct);

    /// <summary>A context on the same database that runs <paramref name="beforeFirstSave"/> once, just before its first save.</summary>
    private ApplicationDbContext ContextWithRace(ApplicationDbContext existing, Func<Task> beforeFirstSave) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(existing.Database.GetConnectionString())
            .AddInterceptors(new RaceBeforeSave(beforeFirstSave))
            .Options);

    private sealed class RaceBeforeSave(Func<Task> race) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private bool _ran;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_ran)
            {
                _ran = true;
                await race();
            }
            return result;
        }
    }

    [Theory]
    [InlineData(OrderStatus.Reserved)]
    [InlineData(OrderStatus.Paid)]
    public async Task Shipped_MarksTheOrder_WithTheTrimmedNote_AndEmailsTheBuyerOnce(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db, status);

        var dto = AssertOk(await Shipped(db, id, "  Sporing 123  "));

        Assert.NotNull(dto.ShippedAt);
        Assert.Equal("Sporing 123", dto.ShippingNote);
        var order = await OrderAsync(db, id);
        Assert.Equal(dto.ShippedAt, order.ShippedAt);
        Assert.Equal("Sporing 123", order.ShippingNote);
        Assert.Equal(status, order.Status);
        _notifier.Verify(n => n.OrderShippedAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Shipped_WithoutANote_StoresNone(string? note)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db);

        var dto = AssertOk(await Shipped(db, id, note));

        Assert.Null(dto.ShippingNote);
        Assert.Null((await OrderAsync(db, id)).ShippingNote);
    }

    [Fact]
    public async Task Shipped_WithoutABody_Works()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db);

        AssertOk(await AdminOrders.MarkShipped(id, null, db, _notifier.Object, _log, Ct));
    }

    [Fact]
    public async Task Shipped_TakesANoteOf500Characters_AndRefuses501OnTheNoteField()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db);

        var refused = Assert.IsType<ValidationProblem>(await Shipped(db, id, new string('a', 501)));
        Assert.Equal(["Use 500 or fewer characters."], refused.ProblemDetails.Errors["note"]);
        Assert.Null((await OrderAsync(db, id)).ShippedAt);
        _notifier.Verify(n => n.OrderShippedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(500, AssertOk(await Shipped(db, id, new string('a', 500) + "  ")).ShippingNote!.Length);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Shipped_OnAnOrderThatIsNotReservedOrPaid_Returns409WrongStatus(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db, status);

        AssertProblem(await Shipped(db, id), StatusCodes.Status409Conflict, "wrongStatus");

        Assert.Null((await OrderAsync(db, id)).ShippedAt);
        _notifier.Verify(n => n.OrderShippedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Shipped_Twice_Returns409AlreadyShipped_AndKeepsTheFirstMark()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db);
        var first = AssertOk(await Shipped(db, id, "Første"));
        db.ChangeTracker.Clear();

        AssertProblem(await Shipped(db, id, "Andre"), StatusCodes.Status409Conflict, "alreadyShipped");

        var order = await OrderAsync(db, id);
        Assert.Equal(first.ShippedAt, order.ShippedAt);
        Assert.Equal("Første", order.ShippingNote);
        _notifier.Verify(n => n.OrderShippedAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Shipped_OnAnUnknownOrder_Returns404()
    {
        await using var db = await postgres.NewContextAsync();

        Assert.IsType<NotFound>(await Shipped(db, 999));
    }

    [Fact]
    public async Task Shipped_WhenAWebhookPaysTheOrderMeanwhile_MarksItAgainOnTheFreshRead()
    {
        await using var seed = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(seed);
        await using var db = ContextWithRace(seed, () => WebhookChangesAsync(seed, id, (o, b) =>
        {
            o.Status = OrderStatus.Paid;
            o.CapturedInOre = o.TotalInOre;
        }));

        var dto = AssertOk(await Shipped(db, id, "Sendt"));

        Assert.Equal("Paid", dto.Status);
        var order = await OrderAsync(seed, id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.ShippedAt);
        Assert.Equal("Sendt", order.ShippingNote);
        _notifier.Verify(n => n.OrderShippedAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Shipped_WhenAnotherTabMarksItMeanwhile_Returns409AlreadyShipped_AndSendsNoSecondEmail()
    {
        await using var seed = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(seed);
        var otherAt = DateTime.UtcNow.AddMinutes(-1);
        await using var db = ContextWithRace(seed, () => WebhookChangesAsync(seed, id, (o, _) =>
        {
            o.ShippedAt = otherAt;
            o.ShippingNote = "Fra den andre fanen";
        }));

        AssertProblem(await Shipped(db, id, "Min"), StatusCodes.Status409Conflict, "alreadyShipped");

        Assert.Equal("Fra den andre fanen", (await OrderAsync(seed, id)).ShippingNote);
        _notifier.Verify(n => n.OrderShippedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Shipped_WhenTheOrderIsCancelledMeanwhile_Returns409WrongStatus()
    {
        await using var seed = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(seed);
        await using var db = ContextWithRace(seed, () => WebhookChangesAsync(seed, id, (o, b) =>
        {
            o.Status = OrderStatus.Cancelled;
            BuildHold.Release(b, o.Id);
        }));

        AssertProblem(await Shipped(db, id), StatusCodes.Status409Conflict, "wrongStatus");

        var order = await OrderAsync(seed, id);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(order.ShippedAt);
        _notifier.Verify(n => n.OrderShippedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Shipped_WhenTheAdminRequestIsAborted_AfterTheSave_StillEmails()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, id) = await SeedAsync(db);

        AssertOk(await AdminOrders.MarkShipped(id, new ShippedRequest(null), db, _notifier.Object, _log, Ct));

        _notifier.Verify(n => n.OrderShippedAsync(id, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Dtos_CarryTheShippingConfirmationAndTermsFields()
    {
        await using var db = await postgres.NewContextAsync();
        var shippedAt = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        var confirmedAt = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        var (_, id) = await SeedAsync(db, OrderStatus.Paid, editOrder: o =>
        {
            o.ShippedAt = shippedAt;
            o.ShippingNote = "Posten";
            o.ConfirmationSentAt = confirmedAt;
            o.TermsVersion = "2026-10-01T08:00:00.000000Z";
            o.TermsAcceptedIp = "203.0.113.0";
        });

        var dto = Assert.IsType<Ok<AdminOrderDto>>(await AdminOrders.Get(id, db, Ct)).Value!;
        var summary = Assert.Single(Assert.IsType<Ok<List<AdminOrderSummaryDto>>>(await AdminOrders.List(db, Ct)).Value!);

        Assert.Equal((shippedAt, "Posten", confirmedAt, "2026-10-01T08:00:00.000000Z", "203.0.113.0"),
            (dto.ShippedAt, dto.ShippingNote, dto.ConfirmationSentAt, dto.TermsVersion, dto.TermsAcceptedIp));
        Assert.Equal((shippedAt, "Posten", confirmedAt, "2026-10-01T08:00:00.000000Z"),
            (summary.ShippedAt, summary.ShippingNote, summary.ConfirmationSentAt, summary.TermsVersion));
        Assert.Equal(dto.TermsAcceptedAt, summary.TermsAcceptedAt);
    }

    private Task<IResult> ReleaseBuild(ApplicationDbContext db, int id) => AdminOrders.ReleaseBuild(id, db, _log, Ct);

    /// <summary>An order Vipps ended while it was reserved: it still holds its build.</summary>
    private async Task<(int BuildId, int OrderId)> SeedEndedAsync(ApplicationDbContext db, OrderStatus status)
    {
        var (buildId, orderId) = await SeedAsync(db, OrderStatus.Reserved);
        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, status)
            .SetProperty(o => o.ReservationEndedAt, WebhookTime), Ct);
        return (buildId, orderId);
    }

    [Theory]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task ReleaseBuild_MakesTheBuildAvailable_AndThenAnswersBuildNotHeld(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedEndedAsync(db, status);

        var before = AssertOk(await AdminOrders.Get(orderId, db, Ct));
        Assert.True(before.HoldsBuild);
        Assert.Null(before.ReleaseBuildBlockedReason);
        Assert.Equal(WebhookTime, before.ReservationEndedAt);

        var dto = AssertOk(await ReleaseBuild(db, orderId));

        Assert.False(dto.HoldsBuild);
        Assert.Equal("buildNotHeld", dto.ReleaseBuildBlockedReason);
        Assert.Equal(status.ToString(), dto.Status);
        var (build, _) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);

        AssertProblem(await ReleaseBuild(db, orderId), StatusCodes.Status409Conflict, "buildNotHeld");
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Reserved)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Refunded)]
    public async Task ReleaseBuild_OnAnOrderThatIsNotFailedOrCancelled_Returns409WrongStatus(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedAsync(db, status);
        var before = (await ReloadAsync(db, buildId, orderId)).Build;

        AssertProblem(await ReleaseBuild(db, orderId), StatusCodes.Status409Conflict, "wrongStatus");

        var after = (await ReloadAsync(db, buildId, orderId)).Build;
        Assert.Equal(before.Availability, after.Availability);
        Assert.Equal(before.HeldByOrderId, after.HeldByOrderId);
        Assert.Equal("wrongStatus", AssertOk(await AdminOrders.Get(orderId, db, Ct)).ReleaseBuildBlockedReason);
    }

    [Fact]
    public async Task ReleaseBuild_LeavesABuildTheAdminReservedByHand()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedEndedAsync(db, OrderStatus.Failed);
        await db.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s.SetProperty(b => b.HeldByOrderId, (int?)null), Ct);

        Assert.Equal("buildNotHeld", AssertOk(await AdminOrders.Get(orderId, db, Ct)).ReleaseBuildBlockedReason);
        AssertProblem(await ReleaseBuild(db, orderId), StatusCodes.Status409Conflict, "buildNotHeld");

        var (build, _) = await ReloadAsync(db, buildId, orderId);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
    }

    [Fact]
    public async Task ReleaseBuild_LeavesASoldBuildStillPointingAtTheOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedEndedAsync(db, OrderStatus.Cancelled);
        await db.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Availability, BuildAvailability.Sold), Ct);

        Assert.False(AssertOk(await AdminOrders.Get(orderId, db, Ct)).HoldsBuild);
        AssertProblem(await ReleaseBuild(db, orderId), StatusCodes.Status409Conflict, "buildNotHeld");
        Assert.Equal(BuildAvailability.Sold, (await ReloadAsync(db, buildId, orderId)).Build.Availability);
    }

    [Fact]
    public async Task ReleaseBuild_ForAnOrderWithoutABuild_Returns409BuildNotHeld_AndUnknownReturns404()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedEndedAsync(db, OrderStatus.Failed);
        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PcBuildId, (int?)null), Ct);

        AssertProblem(await ReleaseBuild(db, orderId), StatusCodes.Status409Conflict, "buildNotHeld");
        Assert.IsType<NotFound>(await ReleaseBuild(db, orderId + 1000));
        Assert.Equal(orderId, (await ReloadAsync(db, buildId, orderId)).Build.HeldByOrderId);
    }

    /// <summary>Runs <paramref name="before"/> once, just before the first save, so another writer can get in first.</summary>
    private sealed class OnceBeforeSave(Func<Task> before) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
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
