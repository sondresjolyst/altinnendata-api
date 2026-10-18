using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using altinnendata_api.Features.Builds;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class BuildHoldTests(PostgresDatabase postgres) : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Order NewOrder(string reference) => new()
    {
        Reference = reference,
        PublicToken = $"token-{reference}",
        Status = OrderStatus.Pending,
        Locale = "no",
        BuyerName = "Ola Nordmann",
        BuyerEmail = "ola@example.com",
        BuyerPhone = "4791234567",
        DeliveryMethod = DeliveryMethod.Pickup,
        DeliveryLabel = "Henting",
        BuildTitle = "Gaming-PC",
        BuildSlug = "gaming-pc",
        BuildPriceInOre = 1_899_000,
        TotalInOre = 1_899_000,
        TermsAcceptedAt = DateTime.UtcNow
    };

    private static PcBuild NewBuild() => new()
    {
        Slug = "gaming-pc",
        Published = true,
        PriceNok = 18990,
        Availability = BuildAvailability.Available
    };

    private static async Task<(PcBuild Build, Order Order)> SeedAsync(ApplicationDbContext db)
    {
        var build = NewBuild();
        var order = NewOrder("order-a");
        db.PcBuilds.Add(build);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        return (build, order);
    }

    private static Task<PcBuild> ReloadAsync(ApplicationDbContext db, int id) =>
        db.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == id, Ct);

    [Fact]
    public async Task TryHold_ReservesAnAvailablePublishedPricedBuild_ForTheOrder()
    {
        await using var db = await postgres.NewContextAsync();
        var (build, order) = await SeedAsync(db);

        var held = await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct);

        Assert.True(held);
        var reloaded = await ReloadAsync(db, build.Id);
        Assert.Equal(BuildAvailability.Reserved, reloaded.Availability);
        Assert.Equal(order.Id, reloaded.HeldByOrderId);
        Assert.True(reloaded.UpdatedAt > build.UpdatedAt);
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("sold")]
    [InlineData("unpublished")]
    [InlineData("unpriced")]
    [InlineData("held")]
    public async Task TryHold_RefusesABuildThatCannotBeBought(string state)
    {
        await using var db = await postgres.NewContextAsync();
        var (build, order) = await SeedAsync(db);
        var other = NewOrder("order-other");
        db.Orders.Add(other);
        await db.SaveChangesAsync(Ct);

        switch (state)
        {
            case "reserved": build.Availability = BuildAvailability.Reserved; break;
            case "sold": build.Availability = BuildAvailability.Sold; break;
            case "unpublished": build.Published = false; break;
            case "unpriced": build.PriceNok = null; break;
            case "held": build.HeldByOrderId = other.Id; break;
        }
        await db.SaveChangesAsync(Ct);
        var before = await ReloadAsync(db, build.Id);

        var held = await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct);

        Assert.False(held);
        var after = await ReloadAsync(db, build.Id);
        Assert.Equal(before.Availability, after.Availability);
        Assert.Equal(before.HeldByOrderId, after.HeldByOrderId);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task TryHold_LetsOnlyOneOfTwoConcurrentOrdersWin()
    {
        await using var db = await postgres.NewContextAsync();
        var build = NewBuild();
        var orderA = NewOrder("order-a");
        var orderB = NewOrder("order-b");
        db.PcBuilds.Add(build);
        db.Orders.AddRange(orderA, orderB);
        await db.SaveChangesAsync(Ct);

        await using var first = postgres.NewContextOn(db);
        await using var second = postgres.NewContextOn(db);
        await using var firstTx = await first.Database.BeginTransactionAsync(Ct);
        await using var secondTx = await second.Database.BeginTransactionAsync(Ct);
        var connectionString = db.Database.GetConnectionString()!;

        // The winner keeps its transaction open until the other UPDATE waits on the row lock,
        // so the loser always takes the blocking path and decides only after the winner commits.
        async Task<bool> HoldAndCommit(ApplicationDbContext context, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, int orderId)
        {
            var held = await BuildHold.TryHoldAsync(context, build.Id, orderId, 1, Ct);
            if (held) await WaitForALockWaiterAsync(connectionString);
            await tx.CommitAsync(Ct);
            return held;
        }

        var results = await Task.WhenAll(
            Task.Run(() => HoldAndCommit(first, firstTx, orderA.Id)),
            Task.Run(() => HoldAndCommit(second, secondTx, orderB.Id)));

        Assert.Equal(1, results.Count(r => r));
        var winner = results[0] ? orderA.Id : orderB.Id;
        var reloaded = await ReloadAsync(db, build.Id);
        Assert.Equal(BuildAvailability.Reserved, reloaded.Availability);
        Assert.Equal(winner, reloaded.HeldByOrderId);
    }

    [Fact]
    public async Task AdminUpdate_FromAStaleRead_IsRefused_AndKeepsTheOrderHold()
    {
        await using var admin = await postgres.NewContextAsync();
        var (build, order) = await SeedAsync(admin);

        // The admin's request has read the build as Available with no hold.
        await admin.PcBuilds.SingleAsync(b => b.Id == build.Id, Ct);

        // Checkout holds it in the meantime.
        await using (var checkout = postgres.NewContextOn(admin))
            Assert.True(await BuildHold.TryHoldAsync(checkout, build.Id, order.Id, 1, Ct));

        var dto = new UpdateBuildDto
        {
            Availability = "Reserved",
            PriceNok = 18990,
            Published = true,
            Translations = [new BuildTranslationInput { Locale = "no", Title = "Gaming-PC" }]
        };
        var result = await BuildCommands.Update(build.Id, dto, admin, CreateImageService(admin), Ct);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("buildChanged", problem.ProblemDetails.Extensions["code"]);
        await using var fresh = postgres.NewContextOn(admin);
        var reloaded = await ReloadAsync(fresh, build.Id);
        Assert.Equal(BuildAvailability.Reserved, reloaded.Availability);
        Assert.Equal(order.Id, reloaded.HeldByOrderId);
        Assert.Equal("The build was changed by someone else. Reload the page and try again.", problem.ProblemDetails.Detail);
    }

    private static UpdateBuildDto FormDto(string title, string availability, DateTime? expectedUpdatedAt) => new()
    {
        Availability = availability,
        PriceNok = 18990,
        Published = true,
        Translations = [new BuildTranslationInput { Locale = "no", Title = title }],
        ExpectedUpdatedAt = expectedUpdatedAt
    };

    [Fact]
    public async Task AdminUpdate_FromAFormLoadedBeforeTheHold_Returns409_AndChangesNothing()
    {
        await using var admin = await postgres.NewContextAsync();
        var (build, order) = await SeedAsync(admin);
        admin.PcBuildTranslations.Add(new PcBuildTranslation { PcBuildId = build.Id, Locale = "no", Title = "Gaming-PC" });
        await admin.SaveChangesAsync(Ct);

        // The admin opens the edit form while the build is Available.
        DateTime formLoaded;
        await using (var form = postgres.NewContextOn(admin))
            formLoaded = (await ReloadAsync(form, build.Id)).UpdatedAt;

        // A buyer starts checkout and the build is held.
        await using (var checkout = postgres.NewContextOn(admin))
            Assert.True(await BuildHold.TryHoldAsync(checkout, build.Id, order.Id, 1, Ct));

        // The form still says Available, the admin only meant to change the title.
        await using var request = postgres.NewContextOn(admin);
        var result = await BuildCommands.Update(build.Id, FormDto("Ny tittel", "Available", formLoaded), request, CreateImageService(request), Ct);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("buildChanged", problem.ProblemDetails.Extensions["code"]);
        await using var fresh = postgres.NewContextOn(admin);
        var reloaded = await fresh.PcBuilds.AsNoTracking().Include(b => b.Translations).SingleAsync(b => b.Id == build.Id, Ct);
        Assert.Equal(BuildAvailability.Reserved, reloaded.Availability);
        Assert.Equal(order.Id, reloaded.HeldByOrderId);
        Assert.Equal("Gaming-PC", Assert.Single(reloaded.Translations).Title);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("milliseconds")]
    [InlineData("none")]
    public async Task AdminUpdate_WithTheUpdatedAtTheFormLoaded_OrWithout_Saves(string expected)
    {
        await using var admin = await postgres.NewContextAsync();
        var (build, _) = await SeedAsync(admin);
        DateTime stored;
        await using (var form = postgres.NewContextOn(admin))
            stored = (await ReloadAsync(form, build.Id)).UpdatedAt;
        // A browser keeps a date to the millisecond, so a value cut to milliseconds still matches.
        DateTime? sent = expected switch
        {
            "exact" => stored,
            "milliseconds" => new DateTime(stored.Ticks - stored.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
            _ => null
        };

        await using var request = postgres.NewContextOn(admin);
        var result = await BuildCommands.Update(build.Id, FormDto("Ny tittel", "Available", sent), request, CreateImageService(request), Ct);

        var ok = Assert.IsType<Ok<BuildAdminDto>>(result);
        Assert.Equal("Ny tittel", Assert.Single(ok.Value!.Translations).Title);
        Assert.True(ok.Value.UpdatedAt > stored);
    }

    private static async Task WaitForALockWaiterAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
            connection);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((long)(await command.ExecuteScalarAsync(Ct))! == 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The other UPDATE never waited on the row lock.");
            await Task.Delay(10, Ct);
        }
    }

    [Fact]
    public void TryHold_RefusesAQuantityOtherThanOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildHold.TryHoldAsync(null!, 1, 1, 2, Ct).GetAwaiter().GetResult());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildHold.TryHoldAsync(null!, 1, 1, 0, Ct).GetAwaiter().GetResult());
    }

    [Fact]
    public void Release_FreesItsOwnHold()
    {
        var build = new PcBuild { Slug = "b", Availability = BuildAvailability.Reserved, HeldByOrderId = 8 };

        Assert.True(BuildHold.Release(build, 8));
        Assert.Equal(BuildAvailability.Available, build.Availability);
        Assert.Null(build.HeldByOrderId);
    }

    [Fact]
    public void Release_LeavesABuildHeldByAnotherOrderOrSetReservedByHand()
    {
        var heldByOther = new PcBuild { Slug = "a", Availability = BuildAvailability.Reserved, HeldByOrderId = 7 };
        var reservedByHand = new PcBuild { Slug = "b", Availability = BuildAvailability.Reserved, HeldByOrderId = null };

        Assert.False(BuildHold.Release(heldByOther, 8));
        Assert.Equal(BuildAvailability.Reserved, heldByOther.Availability);
        Assert.Equal(7, heldByOther.HeldByOrderId);

        Assert.False(BuildHold.Release(reservedByHand, 8));
        Assert.Equal(BuildAvailability.Reserved, reservedByHand.Availability);
        Assert.Null(reservedByHand.HeldByOrderId);
    }

    [Fact]
    public void MarkSold_StampsToday_OnlyForItsOwnHold()
    {
        var today = new DateOnly(2026, 10, 18);
        var own = new PcBuild { Slug = "a", Availability = BuildAvailability.Reserved, HeldByOrderId = 8 };
        var other = new PcBuild { Slug = "b", Availability = BuildAvailability.Reserved, HeldByOrderId = 7 };

        Assert.True(BuildHold.MarkSold(own, 8, today));
        Assert.Equal(BuildAvailability.Sold, own.Availability);
        Assert.Equal(today, own.SoldOn);
        Assert.Null(own.HeldByOrderId);

        Assert.False(BuildHold.MarkSold(other, 8, today));
        Assert.Equal(BuildAvailability.Reserved, other.Availability);
        Assert.Null(other.SoldOn);
        Assert.Equal(7, other.HeldByOrderId);
    }

    [Fact]
    public async Task TryHold_LeavesEveryOtherBuildAlone()
    {
        await using var db = await postgres.NewContextAsync();
        var (build, order) = await SeedAsync(db);
        var other = NewBuild();
        other.Slug = "other-pc";
        db.PcBuilds.Add(other);
        await db.SaveChangesAsync(Ct);

        Assert.True(await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, Ct));

        var untouched = await ReloadAsync(db, other.Id);
        Assert.Equal(BuildAvailability.Available, untouched.Availability);
        Assert.Null(untouched.HeldByOrderId);
    }

    [Fact]
    public void Release_LeavesASoldBuild_EvenWithAStaleHold()
    {
        var sold = new PcBuild { Slug = "s", Availability = BuildAvailability.Sold, HeldByOrderId = 8 };

        Assert.False(BuildHold.Release(sold, 8));
        Assert.Equal(BuildAvailability.Sold, sold.Availability);
        Assert.Equal(8, sold.HeldByOrderId);
    }
}
