using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using altinnendata_api.Models.Admin;
using Moq;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class PaymentLinkTests : TestBase
{
    private const int Total = 1_899_000;
    private const string Reference = "AID-000001-ABCDEF12";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IVippsClient> _vipps = new(MockBehavior.Strict);
    private readonly Mock<IOrderNotifier> _notifier = new();
    private readonly List<(VippsCreatePaymentRequest Request, string Key)> _created = [];
    private readonly PostgresDatabase postgres;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://shop.test" })
        .Build();

    public PaymentLinkTests(PostgresDatabase postgres)
    {
        this.postgres = postgres;
        _vipps.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<VippsCreatePaymentRequest, string, CancellationToken>((r, key, _) => _created.Add((r, key)))
            .ReturnsAsync((VippsCreatePaymentRequest r, string _, CancellationToken _) =>
                new VippsCreatePaymentResponse { RedirectUrl = $"https://landing.test/{r.Reference}", Reference = r.Reference, IsTest = true });
    }

    /// <summary>A Reserved order whose reservation Vipps ended on its own, so it is Failed and still holds its build.</summary>
    private static async Task<(int BuildId, int OrderId)> SeedLostAsync(ApplicationDbContext db, Action<Order>? edit = null)
    {
        var build = new PcBuild { Slug = "gaming-pc", Published = true, PriceNok = 18990, Availability = BuildAvailability.Available, Components = [PartWithCondition()] };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);

        var order = new Order
        {
            Reference = Reference, VippsReference = Reference,
            PublicToken = "0123456789abcdef0123456789abcdef",
            Status = OrderStatus.Failed,
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
            TermsAcceptedAt = DateTime.UtcNow,
            AuthorizedAt = DateTime.UtcNow.AddDays(-8),
            ReservationEndedAt = DateTime.UtcNow.AddHours(-1),
            ConfirmationSentAt = DateTime.UtcNow.AddDays(-8),
            ConfirmationAttempts = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        };
        order.Payments.Add(new OrderPayment { VippsReference = Reference, State = "EXPIRED", CreatedAt = order.CreatedAt });
        edit?.Invoke(order);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        Assert.True(await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct));
        return (build.Id, order.Id);
    }

    private async Task<string> SendAsync(ApplicationDbContext db, int orderId)
    {
        string? token = null;
        _notifier.Setup(n => n.PaymentLinkSentAsync(orderId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, t, _) => token = t);
        await using var fresh = postgres.NewContextOn(db);
        var result = await PaymentLink.Send(orderId, fresh, _notifier.Object, NullLogger<PayLinkDto>.Instance, Ct);
        Assert.IsType<Ok<AdminOrderDto>>(result);
        return token!;
    }

    private async Task<IResult> PayAsync(ApplicationDbContext db, string token)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await PaymentLink.Pay(token, fresh, _vipps.Object, Config, NullLogger<PayLinkDto>.Instance, Ct);
    }

    private async Task<IResult> GetAsync(ApplicationDbContext db, string token)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await PaymentLink.Get(token, fresh, Ct);
    }

    private async Task<Order> OrderAsync(ApplicationDbContext db, int id)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await fresh.Orders.AsNoTracking().Include(o => o.Payments).SingleAsync(o => o.Id == id, Ct);
    }

    private async Task<PcBuild> BuildAsync(ApplicationDbContext db, int id)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await fresh.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
    }

    private async Task<VippsWebhookOutcome> DeliverAsync(ApplicationDbContext existing, string reference, string name)
    {
        var delivery = new VippsWebhookDelivery<VippsPaymentEvent>
        {
            Event = new VippsPaymentEvent
            {
                Msn = "123456", Reference = reference, PspReference = Guid.NewGuid().ToString("N"), Name = name,
                Amount = new VippsWebhookAmount { Value = Total, Currency = "NOK" }, Timestamp = DateTimeOffset.UtcNow,
                CaptureGuaranteedUntil = DateTimeOffset.UtcNow.AddDays(7), Success = true
            },
            IsTest = true,
            EventKey = Guid.NewGuid().ToString("N"),
            Slot = ShopVipps.Slot
        };
        await using var db = postgres.NewContextOn(existing);
        var outcome = await new ShopWebhookHandler(db, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance).HandleAsync(delivery, Ct);
        if (outcome.Kind != VippsWebhookOutcomeKind.Applied) return outcome;
        db.ProcessedWebhookEvents.Add(new VippsProcessedWebhookEvent { Id = delivery.EventKey, Source = delivery.Slot, ProcessedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);
        if (outcome.AfterCommit is { } afterCommit) await afterCommit(Ct);
        return outcome;
    }

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    // Pure rules.

    [Fact]
    public void NewToken_Is32RandomBytesInBase64Url_AndOnlyItsHashIsKept()
    {
        var a = PaymentLink.NewToken();
        var b = PaymentLink.NewToken();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, b);
        Assert.Matches("^[0-9a-f]{64}$", PaymentLink.HashOf(a));
        Assert.Equal(PaymentLink.HashOf(a), PaymentLink.HashOf(a));
        Assert.NotEqual(PaymentLink.HashOf(a), PaymentLink.HashOf(b));
        // SHA-256 of "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PaymentLink.HashOf("abc"));
    }

    [Theory]
    [InlineData(1, Reference)]
    [InlineData(2, Reference + "-2")]
    [InlineData(12, Reference + "-12")]
    public void VippsReferenceOf_KeepsTheOrderNumberForTheFirstPayment(int number, string expected)
    {
        var order = new Order
        {
            Reference = Reference, VippsReference = Reference, PublicToken = "t", Locale = "no", BuyerName = "a", BuyerEmail = "b",
            BuyerPhone = "c", DeliveryLabel = "d", BuildTitle = "e", BuildSlug = "f"
        };
        Assert.Equal(expected, PaymentLink.VippsReferenceOf(order, number));
        Assert.False(PaymentLink.IsLinkPayment(order));
        order.VippsReference = PaymentLink.VippsReferenceOf(order, 2);
        Assert.True(PaymentLink.IsLinkPayment(order));
    }

    [Theory]
    [InlineData(OrderStatus.Failed, true, true, null)]
    [InlineData(OrderStatus.Cancelled, true, true, null)]
    [InlineData(OrderStatus.Failed, true, false, "buildNotHeld")]
    [InlineData(OrderStatus.Failed, false, true, "wrongStatus")]
    [InlineData(OrderStatus.Cancelled, false, true, "wrongStatus")]
    [InlineData(OrderStatus.Pending, true, true, "wrongStatus")]
    [InlineData(OrderStatus.Reserved, true, true, "wrongStatus")]
    [InlineData(OrderStatus.Paid, true, true, "wrongStatus")]
    [InlineData(OrderStatus.Refunded, true, true, "wrongStatus")]
    [InlineData(OrderStatus.Reserved, true, false, "wrongStatus")]
    public void BlockedReasonOf_AllowsOnlyALostReservationThatStillHoldsTheBuild(OrderStatus status, bool ended, bool holds, string? expected)
    {
        var order = new Order
        {
            Reference = Reference, VippsReference = Reference, PublicToken = "t", Locale = "no", BuyerName = "a", BuyerEmail = "b",
            BuyerPhone = "c", DeliveryLabel = "d", BuildTitle = "e", BuildSlug = "f",
            Status = status, ReservationEndedAt = ended ? DateTime.UtcNow : null
        };
        Assert.Equal(expected, PaymentLink.BlockedReasonOf(order, holds));
    }

    [Theory]
    [InlineData(true, false, 1, true)]
    [InlineData(false, false, 1, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(true, false, -1, false)]
    public void IsOpen_NeedsAnUnusedLinkThatHasNotRunOut(bool withHash, bool used, int minutesLeft, bool expected)
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Reference = Reference, VippsReference = Reference, PublicToken = "t", Locale = "no", BuyerName = "a", BuyerEmail = "b",
            BuyerPhone = "c", DeliveryLabel = "d", BuildTitle = "e", BuildSlug = "f",
            PaymentLinkTokenHash = withHash ? new string('a', 64) : null,
            PaymentLinkUsedAt = used ? now.AddMinutes(-5) : null,
            PaymentLinkExpiresAt = now.AddMinutes(minutesLeft)
        };
        Assert.Equal(expected, PaymentLink.IsOpen(order, now));
    }

    // Sending the link.

    [Fact]
    public async Task Send_StoresOnlyTheHash_AndEmailsTheBuyerTheLink()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);

        var before = DateTime.UtcNow;
        var token = await SendAsync(db, orderId);

        var order = await OrderAsync(db, orderId);
        Assert.Equal(PaymentLink.HashOf(token), order.PaymentLinkTokenHash);
        Assert.NotEqual(token, order.PaymentLinkTokenHash);
        Assert.InRange(order.PaymentLinkSentAt!.Value, before.AddSeconds(-1), DateTime.UtcNow);
        Assert.Equal(order.PaymentLinkSentAt + TimeSpan.FromDays(3), order.PaymentLinkExpiresAt);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(BuildAvailability.Reserved, (await BuildAsync(db, buildId)).Availability);
        _notifier.Verify(n => n.PaymentLinkSentAsync(orderId, token, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Send_AgainReplacesTheOldLink()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);

        var first = await SendAsync(db, orderId);
        var second = await SendAsync(db, orderId);

        Assert.NotEqual(first, second);
        AssertProblem(await GetAsync(db, first), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        Assert.IsType<Ok<PayLinkDto>>(await GetAsync(db, second));
    }

    [Fact]
    public async Task Send_IsRefusedWithoutALostReservation_OrWithoutTheBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db, o => o.ReservationEndedAt = null);

        AssertProblem(await PaymentLink.Send(orderId, db, _notifier.Object, NullLogger<PayLinkDto>.Instance, Ct), StatusCodes.Status409Conflict, "wrongStatus");

        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationEndedAt, DateTime.UtcNow), Ct);
        await db.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s.SetProperty(b => b.HeldByOrderId, (int?)null).SetProperty(b => b.Availability, BuildAvailability.Available), Ct);
        db.ChangeTracker.Clear();
        AssertProblem(await PaymentLink.Send(orderId, db, _notifier.Object, NullLogger<PayLinkDto>.Instance, Ct), StatusCodes.Status409Conflict, "buildNotHeld");

        Assert.Null((await OrderAsync(db, orderId)).PaymentLinkTokenHash);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminDto_ShowsTheLinkAndEveryVippsReference()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);
        var before = Assert.IsType<Ok<AdminOrderDto>>(await AdminOrders.Get(orderId, db, Ct)).Value!;
        Assert.Null(before.PaymentLinkBlockedReason);
        Assert.Null(before.PaymentLinkSentAt);

        var token = await SendAsync(db, orderId);
        Assert.IsType<Ok<CheckoutResponse>>(await PayAsync(db, token));

        db.ChangeTracker.Clear();
        var dto = Assert.IsType<Ok<AdminOrderDto>>(await AdminOrders.Get(orderId, db, Ct)).Value!;
        Assert.Equal("wrongStatus", dto.PaymentLinkBlockedReason);
        Assert.NotNull(dto.PaymentLinkSentAt);
        Assert.Equal(dto.PaymentLinkSentAt + TimeSpan.FromDays(3), dto.PaymentLinkExpiresAt);
        Assert.Equal([(Reference, "EXPIRED"), (Reference + "-2", "CREATED")], dto.VippsReferences.Select(r => (r.Reference, r.State)));
    }

    // The pay page.

    [Fact]
    public async Task Get_ShowsTheOrder_AndAnswersTheSame404ForEveryLinkThatDoesNotWork()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);

        var dto = Assert.IsType<Ok<PayLinkDto>>(await GetAsync(db, token)).Value!;
        Assert.Equal(Reference, dto.Reference);
        Assert.Equal("Gaming-PC", dto.BuildTitle);
        Assert.Equal(Total, dto.TotalInOre);
        Assert.Equal((await OrderAsync(db, orderId)).PaymentLinkExpiresAt, dto.ExpiresAt);

        AssertProblem(await GetAsync(db, "not-a-token"), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        AssertProblem(await GetAsync(db, PaymentLink.NewToken()), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        AssertProblem(await GetAsync(db, PaymentLink.HashOf(token)), StatusCodes.Status404NotFound, "paymentLinkInvalid");

        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentLinkExpiresAt, DateTime.UtcNow.AddSeconds(-1)), Ct);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        AssertProblem(await PayAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");

        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.PaymentLinkExpiresAt, DateTime.UtcNow.AddDays(1))
            .SetProperty(o => o.PaymentLinkUsedAt, DateTime.UtcNow), Ct);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");

        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentLinkUsedAt, (DateTime?)null), Ct);
        Assert.IsType<Ok<PayLinkDto>>(await GetAsync(db, token));
        await db.PcBuilds.Where(b => b.Id == buildId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Availability, BuildAvailability.Sold), Ct);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        AssertProblem(await PayAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Pay_StartsTheNextVippsPayment_WithItsOwnReferenceAndKey()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);

        var ok = Assert.IsType<Ok<CheckoutResponse>>(await PayAsync(db, token)).Value!;

        Assert.Equal($"https://landing.test/{Reference}-2", ok.RedirectUrl);
        Assert.Equal("0123456789abcdef0123456789abcdef", ok.Token);
        var (request, key) = Assert.Single(_created);
        Assert.Equal(Reference + "-2", request.Reference);
        Assert.Equal($"payment-{Reference}-2", key);
        Assert.Equal(Total, request.AmountInOre);
        Assert.Null(request.Scope);
        Assert.Equal("https://shop.test/no/shop/return?token=0123456789abcdef0123456789abcdef", request.ReturnUrl);
        Assert.Equal($"build-{buildId}", Assert.Single(request.ReceiptLines!).Id);

        var order = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(Reference + "-2", order.VippsReference);
        Assert.NotNull(order.ReservationEndedAt);
        Assert.Equal(2, order.Payments.Count);
        Assert.Equal(BuildAvailability.Reserved, (await BuildAsync(db, buildId)).Availability);

        // A second click while that payment is open does not start another.
        AssertProblem(await PayAsync(db, token), StatusCodes.Status409Conflict, "paymentInProgress");
        Assert.Single(_created);
    }

    [Fact]
    public async Task Pay_WhenVippsFails_Is502AndLeavesTheOrderAsItWas()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);
        _vipps.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        AssertProblem(await PayAsync(db, token), StatusCodes.Status502BadGateway, "paymentProviderUnavailable");

        var order = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(Reference, order.VippsReference);
        Assert.Single(order.Payments);
        Assert.IsType<Ok<PayLinkDto>>(await GetAsync(db, token));
    }

    [Fact]
    public async Task Pay_IsRefusedForAReservedOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);
        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Reserved), Ct);

        AssertProblem(await PayAsync(db, token), StatusCodes.Status409Conflict, "wrongStatus");
        Assert.Empty(_created);
    }

    // The payment's events.

    [Fact]
    public async Task Authorized_ReservesTheOrder_UsesUpTheLink_AndSendsAConfirmationOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);
        await PayAsync(db, token);

        Assert.Equal(VippsWebhookOutcomeKind.Applied, (await DeliverAsync(db, Reference + "-2", "AUTHORIZED")).Kind);
        Assert.Equal(VippsWebhookOutcomeKind.Applied, (await DeliverAsync(db, Reference + "-2", "AUTHORIZED")).Kind);

        var order = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Null(order.ReservationEndedAt);
        Assert.Null(order.PaymentLinkTokenHash);
        Assert.NotNull(order.PaymentLinkUsedAt);
        Assert.Null(order.ConfirmationSentAt);
        Assert.Equal(0, order.ConfirmationAttempts);
        Assert.Equal("AUTHORIZED", order.Payments.Single(p => p.VippsReference == Reference + "-2").State);
        Assert.Equal("EXPIRED", order.Payments.Single(p => p.VippsReference == Reference).State);
        Assert.Equal(orderId, (await BuildAsync(db, buildId)).HeldByOrderId);
        _notifier.Verify(n => n.OrderReservedAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
    }

    [Theory]
    [InlineData("AUTHORIZED")]
    [InlineData("CAPTURED")]
    [InlineData("EXPIRED")]
    [InlineData("CANCELLED")]
    public async Task ALateEventForAnEarlierPayment_IsIgnored(string name)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        await PayAsync(db, await SendAsync(db, orderId));
        var before = await OrderAsync(db, orderId);

        var outcome = await DeliverAsync(db, Reference, name);

        Assert.Equal(VippsWebhookOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.AfterCommit);
        var after = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Pending, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal("EXPIRED", after.Payments.Single(p => p.VippsReference == Reference).State);
        Assert.Equal(BuildAvailability.Reserved, (await BuildAsync(db, buildId)).Availability);
        _notifier.Verify(n => n.PaymentLinkSentAsync(orderId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownReference_IsStillRejected()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedLostAsync(db);

        Assert.Equal(VippsWebhookOutcomeKind.Rejected, (await DeliverAsync(db, Reference + "-9", "AUTHORIZED")).Kind);
    }

    [Theory]
    [InlineData("ABORTED")]
    [InlineData("EXPIRED")]
    [InlineData("TERMINATED")]
    [InlineData("CANCELLED")]
    public async Task APaymentFromTheLinkThatEndsUnpaid_KeepsTheBuildHeldQuietly_AndTheBuyerCanPayAgain(string name)
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);
        await PayAsync(db, token);

        await DeliverAsync(db, Reference + "-2", name);

        var order = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(name, order.Payments.Single(p => p.VippsReference == Reference + "-2").State);
        var build = await BuildAsync(db, buildId);
        Assert.Equal(BuildAvailability.Reserved, build.Availability);
        Assert.Equal(orderId, build.HeldByOrderId);
        _notifier.Verify(n => n.PaymentLinkSentAsync(orderId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();

        Assert.IsType<Ok<CheckoutResponse>>(await PayAsync(db, token));
        Assert.Equal(Reference + "-3", (await OrderAsync(db, orderId)).VippsReference);
        Assert.Equal([Reference + "-2", Reference + "-3"], _created.Select(c => c.Request.Reference));
    }

    [Fact]
    public async Task AReservedOrderFromTheLinkThatVippsEnds_LosesItsReservationAgain()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        await PayAsync(db, await SendAsync(db, orderId));
        await DeliverAsync(db, Reference + "-2", "AUTHORIZED");

        await DeliverAsync(db, Reference + "-2", "EXPIRED");

        var order = await OrderAsync(db, orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.NotNull(order.ReservationEndedAt);
        Assert.Equal(orderId, (await BuildAsync(db, buildId)).HeldByOrderId);
        _notifier.Verify(n => n.ReservationEndedAsync(orderId, "EXPIRED", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseBuild_ClosesTheLink()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);

        await using (var fresh = postgres.NewContextOn(db))
            Assert.IsType<Ok<AdminOrderDto>>(await AdminOrders.ReleaseBuild(orderId, fresh, NullLogger<AdminOrderDto>.Instance, Ct));

        Assert.Null((await OrderAsync(db, orderId)).PaymentLinkTokenHash);
        Assert.Equal(BuildAvailability.Available, (await BuildAsync(db, buildId)).Availability);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
    }

    // Polling.

    private async Task<int> CloseExpiredAsync(ApplicationDbContext existing, DateTime now)
    {
        await using var db = postgres.NewContextOn(existing);
        var handler = new ShopWebhookHandler(db, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance);
        return await new ShopPaymentPolling(db, _vipps.Object, handler, _notifier.Object, NullLogger<ShopPaymentPolling>.Instance)
            .CloseExpiredLinksAsync(now, Ct);
    }

    [Fact]
    public async Task Polling_TellsTheOwnerOnceWhenTheLinkRunsOutUnpaid()
    {
        await using var db = await postgres.NewContextAsync();
        var (buildId, orderId) = await SeedLostAsync(db);
        var token = await SendAsync(db, orderId);
        var expiresAt = (await OrderAsync(db, orderId)).PaymentLinkExpiresAt!.Value;

        Assert.Equal(0, await CloseExpiredAsync(db, expiresAt.AddSeconds(-1)));
        _notifier.Verify(n => n.PaymentLinkExpiredAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(1, await CloseExpiredAsync(db, expiresAt));
        Assert.Equal(0, await CloseExpiredAsync(db, expiresAt.AddMinutes(3)));

        _notifier.Verify(n => n.PaymentLinkExpiredAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        var order = await OrderAsync(db, orderId);
        Assert.Null(order.PaymentLinkTokenHash);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Null(PaymentLink.BlockedReasonOf(order, true));
        Assert.Equal(orderId, (await BuildAsync(db, buildId)).HeldByOrderId);
        AssertProblem(await GetAsync(db, token), StatusCodes.Status404NotFound, "paymentLinkInvalid");
    }

    [Fact]
    public async Task Polling_LeavesALinkWhosePaymentIsStillOpen()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);
        await PayAsync(db, await SendAsync(db, orderId));
        var expiresAt = (await OrderAsync(db, orderId)).PaymentLinkExpiresAt!.Value;

        Assert.Equal(0, await CloseExpiredAsync(db, expiresAt.AddMinutes(1)));
        _notifier.Verify(n => n.PaymentLinkExpiredAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Polling_LeavesAPaymentFromTheLinkAloneForItsFirst15Minutes()
    {
        await using var db = await postgres.NewContextAsync();
        var (_, orderId) = await SeedLostAsync(db);
        await PayAsync(db, await SendAsync(db, orderId));
        var started = (await OrderAsync(db, orderId)).Payments.Single(p => p.VippsReference == Reference + "-2").CreatedAt;
        _vipps.Setup(v => v.GetPaymentAsync(Reference + "-2", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VippsPayment { Reference = Reference + "-2", State = "EXPIRED" });

        async Task RunAsync(DateTime now)
        {
            await using var fresh = postgres.NewContextOn(db);
            var handler = new ShopWebhookHandler(fresh, _vipps.Object, _notifier.Object, NullLogger<ShopWebhookHandler>.Instance);
            await new ShopPaymentPolling(fresh, _vipps.Object, handler, _notifier.Object, NullLogger<ShopPaymentPolling>.Instance).RunAsync(0, now, Ct);
        }

        await RunAsync(started.AddMinutes(14));
        Assert.Equal(OrderStatus.Pending, (await OrderAsync(db, orderId)).Status);

        await RunAsync(started.AddMinutes(16));
        Assert.Equal(OrderStatus.Failed, (await OrderAsync(db, orderId)).Status);
        _vipps.Verify(v => v.GetPaymentAsync(Reference + "-2", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Emails.

    [Theory]
    [InlineData("no", "Ny betalingslenke for ordre " + Reference, "Lenken er gyldig til 23.10.2026 kl. 14:00.", "fortsatt holdt av til deg")]
    [InlineData("en", "New payment link for order " + Reference, "The link works until 23.10.2026 at 14:00.", "still held for you")]
    public void BuyerPaymentLink_SaysTheComputerIsHeldAndUntilWhen(string locale, string subject, string until, string held)
    {
        var order = new Order
        {
            Reference = Reference, VippsReference = Reference, PublicToken = "t", Locale = locale, BuyerName = "Ola", BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567", DeliveryLabel = "Henting", BuildTitle = "Gaming-PC <b>", BuildSlug = "gaming-pc", TotalInOre = Total,
            PaymentLinkExpiresAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)
        };
        var mail = OrderEmails.BuyerPaymentLink(order, "https://shop.test/no/shop/pay/abc_-1", new AppSettings { PublicEmail = "post@shop.test" });

        Assert.Equal("ola@example.com", mail.To);
        Assert.Equal(subject, mail.Subject);
        Assert.Contains(until, mail.HtmlBody);
        Assert.Contains(held, mail.HtmlBody);
        Assert.Contains("href=\"https://shop.test/no/shop/pay/abc_-1\"", mail.HtmlBody);
        Assert.Contains("Gaming-PC &lt;b&gt;", mail.HtmlBody);
        Assert.DoesNotContain("—", mail.HtmlBody);
    }

    [Fact]
    public void OwnerPaymentLinkExpired_GoesToTheOwner()
    {
        var order = new Order
        {
            Id = 7, Reference = Reference, VippsReference = Reference, PublicToken = "t", Locale = "en", BuyerName = "Ola", BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567", DeliveryLabel = "Henting", BuildTitle = "Gaming-PC", BuildSlug = "gaming-pc",
            PaymentLinkSentAt = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        var mail = OrderEmails.OwnerPaymentLinkExpired(order, new AppSettings { ContactRecipientEmail = "owner@shop.test" }, Config);

        Assert.Equal("owner@shop.test", mail.To);
        Assert.Equal("Betalingslenken for ordre " + Reference + " er utløpt", mail.Subject);
        Assert.Contains("https://shop.test/no/admin/shop/orders/7", mail.HtmlBody);
        Assert.Equal("ola@example.com", mail.ReplyTo);
    }
}
