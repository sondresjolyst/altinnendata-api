using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class VatThresholdTests : TestBase
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VatThreshold.Sale Sale(string reference, DateTime at, int amountInOre, params VatThreshold.Refund[] refunds) =>
        new(reference, at, amountInOre, refunds);

    private static DateTime Utc(int year, int month, int day, int hour = 12) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static Dictionary<DateOnly, long> NoOutside() => [];

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    private static int _next;

    private static Order NewOrder(DateTime capturedAt, int capturedInOre, int refundedInOre = 0, bool isTest = false, OrderStatus status = OrderStatus.Paid)
    {
        var n = Interlocked.Increment(ref _next);
        var order = new Order
        {
            Reference = $"ae-{n:D6}-vat", VippsReference = $"ae-{n:D6}-vat",
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = status,
            Locale = "no",
            BuyerName = "Ola",
            BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567",
            DeliveryLabel = "Henting",
            BuildTitle = "PC",
            BuildSlug = "pc",
            TotalInOre = capturedInOre,
            CapturedInOre = capturedInOre,
            RefundedInOre = refundedInOre,
            IsTest = isTest,
            CapturedAt = capturedAt,
            CreatedAt = capturedAt.AddDays(-1),
        };
        if (refundedInOre > 0)
            order.Refunds.Add(new OrderRefund { AmountInOre = refundedInOre, RefundedAt = capturedAt.AddHours(1), Source = RefundSource.Admin });
        return order;
    }

    [Fact]
    public async Task Turnover_CountsTheLast12MonthsOfRealSalesLessRefunds_AndTheOutsideSales()
    {
        await using var db = CreateDbContext();
        db.Orders.AddRange(
            NewOrder(Now.AddMonths(-11), 1_000_000),
            NewOrder(Now.AddDays(-3), 500_000, refundedInOre: 100_000),
            NewOrder(Now.AddMonths(-13), 700_000),                                   // before the window
            NewOrder(Now.AddDays(-1), 700_000, isTest: true),                        // test sale
            NewOrder(Now.AddDays(-2), 800_000, refundedInOre: 800_000, status: OrderStatus.Refunded),
            NewOrder(Now.AddDays(-2), 0, status: OrderStatus.Reserved));             // nothing captured
        db.OutsideSalesMonths.AddRange(
            new OutsideSalesMonth { Month = Month(2026, 10), AmountInOre = 20_000 },
            new OutsideSalesMonth { Month = Month(2025, 11), AmountInOre = 30_000 },
            new OutsideSalesMonth { Month = Month(2025, 9), AmountInOre = 999_000 });  // before the window
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal(1_000_000 + 400_000 + 20_000 + 30_000, status.TurnoverInOre);
        Assert.Equal(5_000_000, status.ThresholdInOre);
        Assert.Equal(4_000_000, status.WarnFromInOre);
        Assert.False(status.Warning);
        Assert.Null(status.CrossedMonth);
        Assert.Null(status.CrossingOrderReference);
        Assert.Null(status.CrossedAt);
    }

    [Fact]
    public async Task AnOrder_CountsFromWhenItWasCaptured_AndARefundAboveTheCaptureCountsAsNothing()
    {
        await using var db = CreateDbContext();
        var order = NewOrder(Now.AddDays(-3), 300_000);
        order.CreatedAt = Now.AddMonths(-14);
        db.Orders.AddRange(order, NewOrder(Now.AddDays(-3), 100_000, refundedInOre: 200_000));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(300_000, (await VatThreshold.StatusAsync(db, Now, Ct)).TurnoverInOre);
    }

    [Fact]
    public async Task AnOrderWithoutACaptureDate_CountsFromWhenItWasCreated()
    {
        await using var db = CreateDbContext();
        var order = NewOrder(Now, 300_000);
        order.CapturedAt = null;
        order.CreatedAt = Now.AddMonths(-2);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal(300_000, status.TurnoverInOre);
        Assert.Equal(300_000, status.Months.Single(m => m.Month == "2026-08").ShopInOre);
    }

    [Fact]
    public async Task AnOrderSentBeforeItWasCaptured_CountsFromWhenItWasSent()
    {
        await using var db = CreateDbContext();
        var order = NewOrder(Utc(2026, 10, 5), 300_000);
        order.ShippedAt = Utc(2026, 9, 20);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal(300_000, status.Months.Single(m => m.Month == "2026-09").ShopInOre);
        Assert.Equal(0, status.Months.Single(m => m.Month == "2026-10").ShopInOre);
    }

    [Fact]
    public async Task AnOrderCapturedBeforeItWasSent_CountsFromWhenItWasCaptured()
    {
        await using var db = CreateDbContext();
        var order = NewOrder(Utc(2026, 9, 20), 300_000);
        order.ShippedAt = Utc(2026, 10, 5);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal(300_000, status.Months.Single(m => m.Month == "2026-09").ShopInOre);
        Assert.Equal(0, status.Months.Single(m => m.Month == "2026-10").ShopInOre);
    }

    [Fact]
    public async Task AnOrderSentButNotCaptured_DoesNotCount()
    {
        await using var db = CreateDbContext();
        var order = NewOrder(Utc(2026, 10, 5), 0, status: OrderStatus.Reserved);
        order.CapturedAt = null;
        order.ShippedAt = Utc(2026, 10, 1);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, (await VatThreshold.StatusAsync(db, Now, Ct)).TurnoverInOre);
    }

    [Theory]
    [InlineData(null, null, "2026-08-01")]
    [InlineData(null, "2026-09-10", "2026-09-10")]
    [InlineData("2026-09-05", null, "2026-09-05")]
    [InlineData("2026-09-05", "2026-09-10", "2026-09-05")]
    [InlineData("2026-09-15", "2026-09-10", "2026-09-10")]
    public void ASale_IsDatedByTheFirstOfSentAndCaptured_ElseByWhenItWasCreated(string? shipped, string? captured, string expected)
    {
        static DateTime? At(string? day) => day == null ? null : DateTime.SpecifyKind(DateTime.Parse(day, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

        var date = VatThreshold.SaleDate(At(shipped), At(captured), At("2026-08-01")!.Value);

        Assert.Equal(At(expected), date);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Fact]
    public async Task ASaleSentBeforeItWasCaptured_MovesTheCrossingToTheDayItWasSent()
    {
        await using var db = CreateDbContext();
        var first = NewOrder(Utc(2026, 9, 1), 4_000_000);
        var crossing = NewOrder(Utc(2026, 9, 20), 1_100_000);
        crossing.ShippedAt = Utc(2026, 9, 10);
        db.Orders.AddRange(first, crossing);
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal("2026-09-10", status.CrossedOn);
        Assert.Equal(crossing.Reference, status.CrossingOrderReference);
        Assert.Equal(Utc(2026, 9, 10), status.CrossedAt);
    }

    [Theory]
    [InlineData(21, "2026-09")]
    [InlineData(22, "2026-10")]
    public async Task TheDayASaleWasSent_FollowsNorwegianTime(int shippedHourUtc, string month)
    {
        // 22:30 UTC on 30 September is 00:30 on 1 October in Oslo, 21:30 UTC is still 30 September.
        await using var db = CreateDbContext();
        var order = NewOrder(Utc(2026, 10, 5), 300_000);
        order.ShippedAt = new DateTime(2026, 9, 30, shippedHourUtc, 30, 0, DateTimeKind.Utc);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var status = await VatThreshold.StatusAsync(db, Now, Ct);

        Assert.Equal(300_000, status.Months.Single(m => m.Month == month).ShopInOre);
    }

    [Fact]
    public void Months_AreTheMonthsTheWindowEndingTodayOverlaps_OldestFirst()
    {
        // The window ending 19 October 2026 starts 20 October 2025, so it overlaps 13 months.
        var status = VatThreshold.Compute(
            [Sale("a", Now.AddMonths(-1), 10_000), Sale("b", Now.AddMonths(-1).AddDays(2), 5_000)],
            new Dictionary<DateOnly, long> { [Month(2026, 9)] = 7_000 },
            Now);

        Assert.Equal(13, status.Months.Count);
        Assert.Equal("2025-10", status.Months[0].Month);
        Assert.Equal("2026-10", status.Months[^1].Month);
        Assert.Equal(new VatMonthDto("2026-09", 15_000, 7_000), status.Months[^2]);
        Assert.Equal(22_000, status.TurnoverInOre);
    }

    [Fact]
    public void Months_OnTheLastDayOfAMonth_AreTwelve()
    {
        var status = VatThreshold.Compute([], NoOutside(), new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(12, status.Months.Count);
        Assert.Equal("2025-11", status.Months[0].Month);
    }

    [Fact]
    public void Months_CountOnlyTheShopSalesInsideTheWindow_AndAllOfTheOutsideSales()
    {
        var status = VatThreshold.Compute(
            [Sale("out", new DateTime(2025, 10, 19, 12, 0, 0, DateTimeKind.Utc), 100), Sale("in", new DateTime(2025, 10, 20, 12, 0, 0, DateTimeKind.Utc), 1)],
            new Dictionary<DateOnly, long> { [Month(2025, 10)] = 5_000 },
            Now);

        Assert.Equal(new VatMonthDto("2025-10", 1, 5_000), status.Months[0]);
        Assert.Equal(5_001, status.TurnoverInOre);
    }

    [Fact]
    public void Months_FollowNorwegianTime()
    {
        // 22:30 UTC on 30 September is 00:30 on 1 October in Oslo.
        var status = VatThreshold.Compute([Sale("a", new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc), 10_000)], NoOutside(), Now);

        Assert.Equal(10_000, status.Months.Single(m => m.Month == "2026-10").ShopInOre);
        Assert.Equal(0, status.Months.Single(m => m.Month == "2026-09").ShopInOre);
        Assert.Equal(Month(2026, 10), VatThreshold.MonthOf(new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(Month(2026, 9), VatThreshold.MonthOf(new DateTime(2026, 9, 30, 21, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Window_StartsTheDayAfterTheSameDateAYearBack()
    {
        // Today is 19 October 2026, so the window starts 20 October 2025.
        var status = VatThreshold.Compute(
            [Sale("old", new DateTime(2025, 10, 19, 12, 0, 0, DateTimeKind.Utc), 100), Sale("in", new DateTime(2025, 10, 20, 12, 0, 0, DateTimeKind.Utc), 1)],
            NoOutside(), Now);

        Assert.Equal(1, status.TurnoverInOre);
    }

    [Fact]
    public void Window_FollowsNorwegianTime()
    {
        // 22:30 UTC on 19 October 2025 is 00:30 on 20 October in Oslo, so the sale is inside the window.
        var status = VatThreshold.Compute([Sale("a", new DateTime(2025, 10, 19, 22, 30, 0, DateTimeKind.Utc), 100)], NoOutside(), Now);

        Assert.Equal(100, status.TurnoverInOre);
        Assert.Equal(new DateOnly(2025, 10, 20), VatThreshold.DayOf(new DateTime(2025, 10, 19, 22, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void AWindowStartingMidMonth_PassesTheThreshold_EvenWhenNoCalendarMonthWindowDoes()
    {
        // 15 March 2025 to 10 March 2026 is inside the window ending 10 March 2026, but no run of 12 calendar months
        // holds both sales.
        var status = VatThreshold.Compute(
            [Sale("first", new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc), 3_000_000), Sale("second", new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), 2_500_000)],
            NoOutside(), Now);

        Assert.Equal("second", status.CrossingOrderReference);
        Assert.Equal("2026-03-10", status.CrossedOn);
        Assert.Equal("2026-03", status.CrossedMonth);
        Assert.Equal(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), status.CrossedAt);
        Assert.Equal(DateTimeKind.Utc, status.CrossedAt?.Kind);
    }

    [Theory]
    [InlineData(16, "second")]
    [InlineData(15, null)]
    public void ASaleOnTheSameDateAYearEarlier_IsOutsideTheWindow_TheDayAfterIsInside(int firstDay, string? crossing)
    {
        var status = VatThreshold.Compute(
            [Sale("first", new DateTime(2025, 3, firstDay, 12, 0, 0, DateTimeKind.Utc), 3_000_000), Sale("second", new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), 2_500_000)],
            NoOutside(), Now);

        Assert.Equal(crossing, status.CrossingOrderReference);
        Assert.Equal(crossing == null ? null : "2026-03-15", status.CrossedOn);
    }

    [Theory]
    [InlineData(30, "sale")]
    [InlineData(31, null)]
    public void OutsideSales_CountInEveryWindowTheirMonthOverlaps(int saleDay, string? crossing)
    {
        // The window ending 30 March 2026 starts 31 March 2025 and overlaps March 2025. The one ending 31 March starts 1 April.
        var status = VatThreshold.Compute(
            [Sale("sale", new DateTime(2026, 3, saleDay, 10, 0, 0, DateTimeKind.Utc), 200_000)],
            new Dictionary<DateOnly, long> { [Month(2025, 3)] = 4_900_000 },
            Now);

        Assert.Equal(crossing, status.CrossingOrderReference);
    }

    [Fact]
    public void ARefund_OfASaleCapturedBeforeTheWindow_DoesNotLowerIt()
    {
        var status = VatThreshold.Compute(
            [
                Sale("before", Utc(2025, 10, 10), 500_000, new VatThreshold.Refund(Utc(2026, 3, 1), 500_000)),
                Sale("in", Utc(2026, 2, 1), 2_000_000),
            ],
            NoOutside(), Now);

        Assert.Equal(2_000_000, status.TurnoverInOre);
        Assert.Equal(2_000_000, status.Months.Sum(m => m.ShopInOre));
    }

    [Fact]
    public void ARefund_InsideTheWindow_LowersIt()
    {
        var status = VatThreshold.Compute(
            [Sale("a", Utc(2026, 9, 10), 1_000_000, new VatThreshold.Refund(Utc(2026, 10, 1), 400_000))], NoOutside(), Now);

        Assert.Equal(600_000, status.TurnoverInOre);
        Assert.Equal(600_000, status.Months.Single(m => m.Month == "2026-09").ShopInOre);
    }

    [Fact]
    public void ARefund_AfterTheSaleThatPassedTheThreshold_DoesNotUndoTheCrossing()
    {
        // The window ending 10 March 2026 held 55 000 kr. The refund in May comes after that window has ended.
        var status = VatThreshold.Compute(
            [
                Sale("a", Utc(2026, 1, 10), 3_000_000, new VatThreshold.Refund(Utc(2026, 5, 2), 1_000_000)),
                Sale("b", Utc(2026, 3, 10), 2_500_000),
            ],
            NoOutside(), Now);

        Assert.Equal("b", status.CrossingOrderReference);
        Assert.Equal("2026-03-10", status.CrossedOn);
        Assert.Equal(4_500_000, status.TurnoverInOre);
    }

    [Fact]
    public void ARefund_BeforeALaterSale_KeepsTheWindowBelowTheThreshold()
    {
        var status = VatThreshold.Compute(
            [
                Sale("a", Utc(2026, 1, 10), 3_000_000, new VatThreshold.Refund(Utc(2026, 2, 2), 1_000_000)),
                Sale("b", Utc(2026, 3, 10), 2_500_000),
            ],
            NoOutside(), Now);

        Assert.Null(status.CrossedOn);
        Assert.Null(status.CrossingOrderReference);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(15, "b")]
    public void ARefund_OnTheDayOfALaterSale_CountsOnlyWhenItCameFirst(int refundHour, string? crossing)
    {
        var status = VatThreshold.Compute(
            [
                Sale("a", Utc(2026, 1, 10), 3_000_000, new VatThreshold.Refund(Utc(2026, 3, 5, refundHour), 1_000_000)),
                Sale("b", Utc(2026, 3, 5, 13), 2_500_000),
            ],
            NoOutside(), Now);

        Assert.Equal(crossing, status.CrossingOrderReference);
    }

    [Fact]
    public void ARefund_TheDayBeforeOutsideSalesStart_CountsBeforeThem()
    {
        // 30 000 kr less 10 000 kr refunded in May, then 29 000 kr outside the shop in June: 49 000 kr.
        var status = VatThreshold.Compute(
            [Sale("a", Utc(2026, 4, 10), 3_000_000, new VatThreshold.Refund(Utc(2026, 5, 31), 1_000_000))],
            new Dictionary<DateOnly, long> { [Month(2026, 6)] = 2_900_000 },
            Now);

        Assert.Null(status.CrossedOn);
    }

    [Fact]
    public void ARefund_AfterOutsideSalesPassedTheThreshold_DoesNotUndoTheCrossing()
    {
        // 30 000 kr in April and 21 000 kr outside the shop in June pass the threshold on 1 June. The refund is later.
        var status = VatThreshold.Compute(
            [Sale("a", Utc(2026, 4, 10), 3_000_000, new VatThreshold.Refund(Utc(2026, 6, 15), 1_000_000))],
            new Dictionary<DateOnly, long> { [Month(2026, 6)] = 2_100_000 },
            Now);

        Assert.Equal("2026-06-01", status.CrossedOn);
        Assert.Null(status.CrossingOrderReference);
    }

    [Fact]
    public void ARefund_AfterNow_IsNotCountedYet()
    {
        var status = VatThreshold.Compute(
            [Sale("a", Utc(2026, 9, 10), 1_000_000, new VatThreshold.Refund(Utc(2026, 10, 20), 400_000))], NoOutside(), Now);

        Assert.Equal(1_000_000, status.TurnoverInOre);
    }

    [Theory]
    [InlineData(3_999_999, false)]
    [InlineData(4_000_000, true)]
    [InlineData(4_000_001, true)]
    public async Task Warning_AroundTheBoundary_IsTheSameFromComputeAndFromTheStoredOrders(int amountInOre, bool warning)
    {
        Assert.Equal(warning, VatThreshold.Compute([Sale("a", Now.AddDays(-1), amountInOre)], NoOutside(), Now).Warning);

        await using var db = CreateDbContext();
        db.Orders.Add(NewOrder(Now.AddDays(-1), amountInOre));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(warning, (await VatThreshold.StatusAsync(db, Now, Ct)).Warning);
    }

    [Fact]
    public void Warning_StartsAt40000Kroner()
    {
        Assert.False(VatThreshold.Compute([Sale("a", Now.AddDays(-1), 3_999_999)], NoOutside(), Now).Warning);
        Assert.True(VatThreshold.Compute([Sale("a", Now.AddDays(-1), 4_000_000)], NoOutside(), Now).Warning);
        Assert.True(VatThreshold.Compute([Sale("a", Now.AddDays(-1), 3_000_000)], new Dictionary<DateOnly, long> { [Month(2026, 1)] = 1_000_000 }, Now).Warning);
    }

    [Fact]
    public void ExactlyTheThreshold_IsNotPassingIt()
    {
        List<VatThreshold.Sale> sales = [Sale("a", Now.AddMonths(-3), 4_000_000), Sale("b", Now.AddMonths(-1), 1_000_000)];

        Assert.Null(VatThreshold.Compute(sales, NoOutside(), Now).CrossedMonth);

        sales.Add(Sale("c", Now.AddDays(-1), 1));
        var status = VatThreshold.Compute(sales, NoOutside(), Now);
        Assert.Equal("2026-10", status.CrossedMonth);
        Assert.Equal("c", status.CrossingOrderReference);
        Assert.Equal(Now.AddDays(-1), status.CrossedAt);
    }

    [Fact]
    public void TheSaleThatPassesTheThreshold_IsNamed_NotTheOnesAfterIt()
    {
        var status = VatThreshold.Compute(
            [
                Sale("first", Now.AddMonths(-3), 4_000_000),
                Sale("second", Now.AddMonths(-2), 900_000),
                Sale("after", Now.AddMonths(-1).AddDays(3), 200_000),
                Sale("crossing", Now.AddMonths(-1), 200_000),
            ],
            NoOutside(), Now);

        Assert.Equal("crossing", status.CrossingOrderReference);
        Assert.Equal("2026-09", status.CrossedMonth);
        Assert.Equal(5_300_000, status.TurnoverInOre);
    }

    [Fact]
    public void TheThresholdIsCountedOverTheTwelveMonthsBeforeEachSale()
    {
        // 30 000 kr fourteen months ago has left the window before the later 25 000 kr.
        var status = VatThreshold.Compute(
            [Sale("old", Now.AddMonths(-14), 3_000_000), Sale("new", Now.AddMonths(-1), 2_500_000)], NoOutside(), Now);

        Assert.Null(status.CrossedMonth);
    }

    [Fact]
    public void ASaleTwelveMonthsEarlier_IsOutsideTheWindow()
    {
        var status = VatThreshold.Compute(
            [Sale("a", new DateTime(2025, 9, 10, 12, 0, 0, DateTimeKind.Utc), 3_000_000), Sale("b", new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc), 2_500_000)],
            NoOutside(), Now);

        Assert.Null(status.CrossedMonth);
    }

    [Fact]
    public void TheWindowOfAnEarlierSale_ReachesTwelveMonthsBackFromIt()
    {
        // Eleven months apart, both in one window, so the second sale passes the threshold.
        var status = VatThreshold.Compute(
            [Sale("a", Now.AddMonths(-20), 3_000_000), Sale("b", Now.AddMonths(-9), 2_500_000)], NoOutside(), Now);

        Assert.Equal("b", status.CrossingOrderReference);
        Assert.Equal("2026-01", status.CrossedMonth);
        Assert.Equal(2_500_000, status.TurnoverInOre);
    }

    [Fact]
    public void TheCrossingIsKept_AfterItLeavesTheWindow()
    {
        var crossedAt = Now.AddMonths(-30);
        var status = VatThreshold.Compute([Sale("big", crossedAt, 5_100_000), Sale("later", Now.AddDays(-1), 10_000)], NoOutside(), Now);

        Assert.Equal("big", status.CrossingOrderReference);
        Assert.Equal("2024-04", status.CrossedMonth);
        Assert.Equal(10_000, status.TurnoverInOre);
    }

    [Fact]
    public void OutsideSales_CountTowardPassingTheThreshold()
    {
        var status = VatThreshold.Compute(
            [Sale("sale", Now.AddDays(-1), 200_000)],
            new Dictionary<DateOnly, long> { [Month(2026, 3)] = 4_900_000 },
            Now);

        Assert.Equal("sale", status.CrossingOrderReference);
        Assert.Equal("2026-10", status.CrossedMonth);
    }

    [Fact]
    public void OutsideSalesThatPassTheThresholdAlone_NameNoOrder()
    {
        var status = VatThreshold.Compute(
            [Sale("sale", new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc), 4_000_000)],
            new Dictionary<DateOnly, long> { [Month(2026, 7)] = 1_000_001 },
            Now);

        Assert.Equal("2026-07", status.CrossedMonth);
        Assert.Equal("2026-07-01", status.CrossedOn);
        Assert.Null(status.CrossingOrderReference);
        Assert.Null(status.CrossedAt);
    }

    [Fact]
    public void WithinAMonth_TheOutsideSalesAreCountedBeforeTheShopSales()
    {
        // The outside sales bring the window to 49 000 kr, so the first shop sale that month passes it.
        var status = VatThreshold.Compute(
            [
                Sale("before", new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc), 4_000_000),
                Sale("first", new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), 200_000),
                Sale("second", new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc), 200_000),
            ],
            new Dictionary<DateOnly, long> { [Month(2026, 6)] = 900_000 },
            Now);

        Assert.Equal("first", status.CrossingOrderReference);
    }

    [Fact]
    public void OutsideSalesInTheFuture_DoNotCount()
    {
        var status = VatThreshold.Compute([], new Dictionary<DateOnly, long> { [Month(2026, 11)] = 9_000_000 }, Now);

        Assert.Equal(0, status.TurnoverInOre);
        Assert.Null(status.CrossedMonth);
    }

    [Fact]
    public async Task SetOutsideSales_AddsChangesAndRemovesAMonth()
    {
        await using var db = CreateDbContext();

        var added = Assert.IsType<Ok<VatThresholdDto>>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-09", AmountInOre = 150_000 }, db, Now, Ct));
        Assert.Equal(150_000, added.Value!.TurnoverInOre);
        Assert.Equal(150_000, added.Value.Months.Single(m => m.Month == "2026-09").OutsideInOre);

        var later = Now.AddMinutes(5);
        await VatThreshold.SetOutsideSalesAsync(new UpdateOutsideSalesDto { Month = "2026-09", AmountInOre = 90_000 }, db, later, Ct);
        var row = await db.OutsideSalesMonths.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((Month(2026, 9), 90_000, later), (row.Month, row.AmountInOre, row.UpdatedAt));

        var removed = Assert.IsType<Ok<VatThresholdDto>>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-09", AmountInOre = 0 }, db, Now, Ct));
        Assert.Equal(0, removed.Value!.TurnoverInOre);
        Assert.Empty(await db.OutsideSalesMonths.ToListAsync(Ct));

        Assert.IsType<Ok<VatThresholdDto>>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-08", AmountInOre = 0 }, db, Now, Ct));
        Assert.Empty(await db.OutsideSalesMonths.ToListAsync(Ct));
    }

    [Fact]
    public async Task SetOutsideSales_TakesTheCurrentMonth_AndRefusesAMonthThatHasNotStarted()
    {
        await using var db = CreateDbContext();

        Assert.IsType<Ok<VatThresholdDto>>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-10", AmountInOre = 1 }, db, Now, Ct));

        var refused = Assert.IsType<ValidationProblem>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-11", AmountInOre = 1 }, db, Now, Ct));
        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(["month"], refused.ProblemDetails.Errors.Keys);
        Assert.Equal(Month(2026, 10), (await db.OutsideSalesMonths.SingleAsync(Ct)).Month);
    }

    [Theory]
    [InlineData("2026-09", 0, true)]
    [InlineData("2026-09", 1_000_000_000, true)]
    [InlineData("2000-01", 1, true)]
    [InlineData("2026-09", -1, false)]
    [InlineData("2026-09", 1_000_000_001, false)]
    [InlineData("1999-12", 1, false)]
    [InlineData("2026-9", 1, false)]
    [InlineData("2026-13", 1, false)]
    [InlineData("2026-09-01", 1, false)]
    [InlineData("", 1, false)]
    public void Validator_TakesAMonthAndAnAmountInRange(string month, int amount, bool valid)
    {
        var result = new UpdateOutsideSalesValidator().Validate(new UpdateOutsideSalesDto { Month = month, AmountInOre = amount });

        Assert.Equal(valid, result.IsValid);
        Assert.All(result.Errors, e => Assert.Contains(e.PropertyName, new[] { "month", "amountInOre" }));
    }
}

[Collection("postgres")]
public class VatThresholdPostgresTests(PostgresDatabase postgres)
{
    [Fact]
    public async Task StatusAndOutsideSales_RunOnPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.NewContextAsync();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        db.Orders.Add(new Order
        {
            Reference = "ae-000001-vatpg", VippsReference = "ae-000001-vatpg", PublicToken = Guid.NewGuid().ToString("N"), Status = OrderStatus.Paid, Locale = "no",
            BuyerName = "Ola", BuyerEmail = "ola@example.com", BuyerPhone = "4791234567", DeliveryLabel = "Henting",
            BuildTitle = "PC", BuildSlug = "pc", TotalInOre = 4_900_000, CapturedInOre = 4_900_000, RefundedInOre = 0,
            CapturedAt = now.AddDays(-2),
        });
        await db.SaveChangesAsync(ct);

        var ok = Assert.IsType<Ok<VatThresholdDto>>(await VatThreshold.SetOutsideSalesAsync(
            new UpdateOutsideSalesDto { Month = "2026-10", AmountInOre = 200_000 }, db, now, ct));

        Assert.Equal(5_100_000, ok.Value!.TurnoverInOre);
        Assert.Equal("2026-10", ok.Value.CrossedMonth);
        Assert.Equal("ae-000001-vatpg", ok.Value.CrossingOrderReference);

        // A sale sent in September and captured after the first one counts from September, so it passes the threshold first.
        var shippedAt = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        db.Orders.Add(new Order
        {
            Reference = "ae-000002-vatpg", VippsReference = "ae-000002-vatpg", PublicToken = Guid.NewGuid().ToString("N"), Status = OrderStatus.Paid, Locale = "no",
            BuyerName = "Kari", BuyerEmail = "kari@example.com", BuyerPhone = "4791234568", DeliveryLabel = "Henting",
            BuildTitle = "PC", BuildSlug = "pc", TotalInOre = 5_100_000, CapturedInOre = 5_100_000, RefundedInOre = 0,
            ShippedAt = shippedAt, CapturedAt = now.AddDays(-1),
        });
        await db.SaveChangesAsync(ct);

        var status = await VatThreshold.StatusAsync(db, now, ct);

        Assert.Equal("2026-09", status.CrossedMonth);
        Assert.Equal("ae-000002-vatpg", status.CrossingOrderReference);
        Assert.Equal(shippedAt, status.CrossedAt);
    }
}
