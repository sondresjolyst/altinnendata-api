using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderRefundsTests
{
    private static readonly DateTime At = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static Order Paid(int captured = 10_000, int refunded = 0) => new()
    {
        Id = 5,
        Reference = "AID-000005-ABCDEF12", VippsReference = "AID-000005-ABCDEF12",
        PublicToken = "t",
        Status = OrderStatus.Paid,
        Locale = "no",
        BuyerName = "Ola",
        BuyerEmail = "ola@example.com",
        BuyerPhone = "4791234567",
        DeliveryLabel = "Henting",
        BuildTitle = "PC",
        BuildSlug = "pc",
        TotalInOre = captured,
        CapturedInOre = captured,
        RefundedInOre = refunded
    };

    [Fact]
    public void Record_AddsOneRowWithWhatThisCallAdded()
    {
        var order = Paid(refunded: 3_000);

        Assert.Equal(4_000, OrderRefunds.Record(order, 7_000, At, RefundSource.Admin, "refund-x-3000"));

        var row = Assert.Single(order.Refunds);
        Assert.Equal(5, row.OrderId);
        Assert.Equal(4_000, row.AmountInOre);
        Assert.Equal(At, row.RefundedAt);
        Assert.Equal("refund-x-3000", row.IdempotencyKey);
        Assert.Equal(RefundSource.Admin, row.Source);
        Assert.Equal(7_000, order.RefundedInOre);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Theory]
    [InlineData(3_000)]
    [InlineData(1_000)]
    public void Record_ThatAddsNothing_AddsNoRow(int vippsRefunded)
    {
        var order = Paid(refunded: 3_000);

        Assert.Equal(0, OrderRefunds.Record(order, vippsRefunded, At, RefundSource.Webhook, "k"));

        Assert.Empty(order.Refunds);
    }

    [Fact]
    public void Record_AboveTheCapturedTotal_RowHoldsOnlyWhatWasLeft()
    {
        var order = Paid(refunded: 6_000);

        Assert.Equal(4_000, OrderRefunds.Record(order, 99_000, At, RefundSource.Webhook, "k"));

        Assert.Equal(4_000, Assert.Single(order.Refunds).AmountInOre);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(At, order.RefundedAt);
    }
}

[Collection("postgres")]
public class OrderRefundsMigrationTests(PostgresDatabase postgres)
{
    [Fact]
    public async Task Migration_GivesEachEarlierRefundedTotalOneRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.NewContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261019202106_OutsideSalesMonths", ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Orders" ("Reference","PublicToken","Status","Locale","BuyerName","BuyerEmail","BuyerPhone","DeliveryMethod","DeliveryLabel",
              "DeliveryPriceInOre","WithinDistanceConfirmed","BuildTitle","BuildSlug","BuildPriceInOre","TotalInOre","CapturedInOre","RefundedInOre",
              "IsTest","TermsAcceptedAt","ConfirmationAttempts","CreatedAt","UpdatedAt","RefundedAt")
            VALUES ('a','ta','Refunded','no','Ola','o@e.no','47','Pickup','Henting',0,false,'PC','pc',100,100,100,100,false,now(),0,now(),'2026-10-01T10:00:00Z','2026-10-02T10:00:00Z'),
                   ('b','tb','Paid','no','Ola','o@e.no','47','Pickup','Henting',0,false,'PC','pc',100,100,100,40,false,now(),0,now(),'2026-10-03T10:00:00Z',NULL),
                   ('c','tc','Paid','no','Ola','o@e.no','47','Pickup','Henting',0,false,'PC','pc',100,100,100,0,false,now(),0,now(),now(),NULL)
            """, ct);

        await migrator.MigrateAsync(cancellationToken: ct);

        var rows = await db.OrderRefunds.AsNoTracking().OrderBy(r => r.AmountInOre).ToListAsync(ct);
        Assert.Equal([40, 100], rows.Select(r => r.AmountInOre));
        Assert.All(rows, r => Assert.Equal(RefundSource.Migrated, r.Source));
        Assert.Equal(new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc), rows[0].RefundedAt);
        Assert.Equal(new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc), rows[1].RefundedAt);
    }
}
