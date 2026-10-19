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

    private static VatThreshold.Sale Sale(string reference, DateTime at, int amountInOre) => new(reference, at, amountInOre);

    private static Dictionary<DateOnly, long> NoOutside() => [];

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    private static int _next;

    private static Order NewOrder(DateTime capturedAt, int capturedInOre, int refundedInOre = 0, bool isTest = false, OrderStatus status = OrderStatus.Paid)
    {
        var n = Interlocked.Increment(ref _next);
        return new Order
        {
            Reference = $"ae-{n:D6}-vat",
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
            new OutsideSalesMonth { Month = Month(2025, 10), AmountInOre = 999_000 }); // before the window
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
    public void Months_AreTheCurrentMonthAndThe11Before_OldestFirst()
    {
        var status = VatThreshold.Compute(
            [Sale("a", Now.AddMonths(-1), 10_000), Sale("b", Now.AddMonths(-1).AddDays(2), 5_000)],
            new Dictionary<DateOnly, long> { [Month(2026, 9)] = 7_000 },
            Now);

        Assert.Equal(12, status.Months.Count);
        Assert.Equal("2025-11", status.Months[0].Month);
        Assert.Equal("2026-10", status.Months[^1].Month);
        Assert.Equal(new VatMonthDto("2026-09", 15_000, 7_000), status.Months[^2]);
        Assert.Equal(22_000, status.TurnoverInOre);
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
    public void Window_DropsTheMonthTwelveMonthsBack()
    {
        var status = VatThreshold.Compute(
            [Sale("old", new DateTime(2025, 10, 31, 12, 0, 0, DateTimeKind.Utc), 100), Sale("in", new DateTime(2025, 11, 1, 12, 0, 0, DateTimeKind.Utc), 1)],
            NoOutside(), Now);

        Assert.Equal(1, status.TurnoverInOre);
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
            Reference = "ae-000001-vatpg", PublicToken = Guid.NewGuid().ToString("N"), Status = OrderStatus.Paid, Locale = "no",
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
    }
}
