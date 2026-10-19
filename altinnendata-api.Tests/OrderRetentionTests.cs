using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderRetentionTests : TestBase
{
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int _next;

    internal static Order NewOrder(OrderStatus status, DateTime createdAt, bool isTest = false)
    {
        var n = Interlocked.Increment(ref _next);
        return new Order
        {
            Reference = $"ae-{n:D6}-ret",
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = status,
            Locale = "no",
            BuyerName = "Ola",
            BuyerEmail = "ola@example.com",
            BuyerPhone = "4791234567",
            DeliveryLabel = "Henting",
            BuildTitle = "PC",
            BuildSlug = "pc",
            IsTest = isTest,
            CreatedAt = createdAt,
        };
    }

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    [Fact]
    public async Task Purge_DeletesFailedAndTestOrdersOlderThan90Days_AndKeepsTheRest()
    {
        await using var db = CreateDbContext();
        var old = Now.AddDays(-90).AddSeconds(-1);
        var failedOld = NewOrder(OrderStatus.Failed, old);
        var testPaidOld = NewOrder(OrderStatus.Paid, old, isTest: true);
        var testCancelledOld = NewOrder(OrderStatus.Cancelled, old, isTest: true);
        var failedNew = NewOrder(OrderStatus.Failed, Now.AddDays(-90));
        var testNew = NewOrder(OrderStatus.Paid, Now.AddDays(-89), isTest: true);
        var paidOld = NewOrder(OrderStatus.Paid, Now.AddYears(-2));
        var cancelledOld = NewOrder(OrderStatus.Cancelled, old);
        var refundedOld = NewOrder(OrderStatus.Refunded, old);
        db.Orders.AddRange(failedOld, testPaidOld, testCancelledOld, failedNew, testNew, paidOld, cancelledOld, refundedOld);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(3, await OrderRetention.PurgeAsync(db, Now, Ct));

        var left = await db.Orders.Select(o => o.Reference).ToListAsync(Ct);
        Assert.Equal(
            new[] { failedNew, testNew, paidOld, cancelledOld, refundedOld }.Select(o => o.Reference).Order(),
            left.Order());
        Assert.Equal(0, await OrderRetention.PurgeAsync(db, Now, Ct));
    }

    [Fact]
    public async Task Purge_KeepsAnOrderThatStillHoldsABuild()
    {
        await using var db = CreateDbContext();
        var held = NewOrder(OrderStatus.Failed, Now.AddDays(-200));
        var free = NewOrder(OrderStatus.Failed, Now.AddDays(-200));
        db.Orders.AddRange(held, free);
        await db.SaveChangesAsync(Ct);
        db.PcBuilds.Add(new PcBuild { Slug = "pc", Availability = BuildAvailability.Reserved, HeldByOrderId = held.Id });
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await OrderRetention.PurgeAsync(db, Now, Ct));

        Assert.Equal(held.Id, (await db.Orders.SingleAsync(Ct)).Id);
    }

    [Fact]
    public async Task Purge_DeletesOneBatchPerRun()
    {
        await using var db = CreateDbContext();
        db.Orders.AddRange(Enumerable.Range(0, OrderRetention.BatchSize + 2).Select(_ => NewOrder(OrderStatus.Failed, Now.AddDays(-100))));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(OrderRetention.BatchSize, await OrderRetention.PurgeAsync(db, Now, Ct));
        Assert.Equal(2, await OrderRetention.PurgeAsync(db, Now, Ct));
        Assert.Equal(500, OrderRetention.BatchSize);
    }

    [Fact]
    public void TheJobRunsDaily_AndKeeps90Days()
    {
        Assert.Equal(TimeSpan.FromDays(1), OrderRetentionService.Interval);
        Assert.Equal(TimeSpan.FromDays(90), OrderRetention.KeepFor);
    }

    [Fact]
    public async Task RunOnce_UsesTheClock_AndLogsWhatItDeleted()
    {
        var db = CreateDbContext();
        // Years from the real clock, so only the injected clock can make the first order due.
        var clock = new DateTime(2040, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        db.Orders.AddRange(NewOrder(OrderStatus.Failed, clock.AddDays(-91)), NewOrder(OrderStatus.Failed, clock.AddDays(-89)));
        await db.SaveChangesAsync(Ct);
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var log = new ListLogger<OrderRetentionService>();

        await new OrderRetentionService(services.GetRequiredService<IServiceScopeFactory>(), new FixedTime(clock), log).RunOnceAsync(Ct);

        Assert.Equal(1, await db.Orders.CountAsync(Ct));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Deleted 1 failed or test orders older than 90 days", entry.Message);
    }

    [Fact]
    public async Task RunOnce_LogsNothing_WhenNothingIsDue()
    {
        var db = CreateDbContext();
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var log = new ListLogger<OrderRetentionService>();

        await new OrderRetentionService(services.GetRequiredService<IServiceScopeFactory>(), new FixedTime(Now), log).RunOnceAsync(Ct);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task RunOnce_LogsAFailure_AndDoesNotThrow()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var log = new ListLogger<OrderRetentionService>();

        await new OrderRetentionService(services.GetRequiredService<IServiceScopeFactory>(), new FixedTime(Now), log).RunOnceAsync(Ct);

        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }
}

[Collection("postgres")]
public class OrderRetentionPostgresTests(PostgresDatabase postgres)
{
    [Fact]
    public async Task Purge_RunsOnPostgres_AndKeepsTheHeldOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);
        await using var db = await postgres.NewContextAsync();
        var held = OrderRetentionTests.NewOrder(OrderStatus.Failed, now.AddDays(-120));
        var test = OrderRetentionTests.NewOrder(OrderStatus.Reserved, now.AddDays(-120), isTest: true);
        var sale = OrderRetentionTests.NewOrder(OrderStatus.Paid, now.AddDays(-120));
        db.Orders.AddRange(held, test, sale);
        await db.SaveChangesAsync(ct);
        db.PcBuilds.Add(new PcBuild { Slug = "pc-ret", Availability = BuildAvailability.Reserved, HeldByOrderId = held.Id });
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await OrderRetention.PurgeAsync(db, now, ct));

        Assert.Equal(new[] { held.Id, sale.Id }.Order(), (await db.Orders.Select(o => o.Id).ToListAsync(ct)).Order());
    }
}
