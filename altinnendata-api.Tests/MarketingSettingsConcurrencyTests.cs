using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using altinnendata_api.Features.Marketing;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>The marketing and shop settings share one row. After two saves in turn, both settings keep their saved values.</summary>
[Collection("postgres")]
public class MarketingSettingsConcurrencyTests(PostgresDatabase postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Runs <paramref name="between"/> once, after the context loaded the row and before it saves.</summary>
    private sealed class BeforeSave(Func<Task> between) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await between();
            }
            return result;
        }
    }

    private static ApplicationDbContext With(ApplicationDbContext existing, SaveChangesInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(existing.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options);

    private static UpdateShopSettingsDto ShopUpdate(string buyButton, bool vippsTestMode) => new()
    {
        BuyButton = buyButton,
        VippsTestMode = vippsTestMode,
        Delivery =
        [
            new("Shipping", false, 0, "Sending", "Shipping", null, null, null),
            new("HomeDelivery", false, 0, "Levering hjem", "Home delivery", null, null, 30),
            new("Pickup", true, 0, "Henting", "Pickup", null, null, null)
        ]
    };

    [Fact]
    public async Task BothSettingsKeepTheirValues_WhenTheShopSavesDuringAMarketingSave()
    {
        await using var seed = await postgres.NewContextAsync();
        await ShopSeed.EnsureAsync(seed, Ct);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await using var marketing = With(seed, new BeforeSave(async () =>
        {
            await using var other = postgres.NewContextOn(seed);
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ShopSettingsDto>>(
                await ShopSettingsEndpoints.Update(ShopUpdate("AdminsOnly", vippsTestMode: false), other, cache, Ct));
        }));
        await MarketingSettingsEndpoints.Update(new UpdateMarketingSettingsDto { MetaPixelId = "1234567890" }, marketing, Ct);

        await using var check = postgres.NewContextOn(seed);
        var row = await check.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("1234567890", row.MetaPixelId);
        Assert.Equal(BuyButtonMode.AdminsOnly, row.BuyButton);
        Assert.False(row.VippsTestMode);
    }

    [Fact]
    public async Task BothSettingsKeepTheirValues_WhenTheMarketingSavesDuringAShopSave()
    {
        await using var seed = await postgres.NewContextAsync();
        await ShopSeed.EnsureAsync(seed, Ct);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await using var shop = With(seed, new BeforeSave(async () =>
        {
            await using var other = postgres.NewContextOn(seed);
            await MarketingSettingsEndpoints.Update(new UpdateMarketingSettingsDto { MetaPixelId = "1234567890" }, other, Ct);
        }));
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ShopSettingsDto>>(
            await ShopSettingsEndpoints.Update(ShopUpdate("AdminsOnly", vippsTestMode: false), shop, cache, Ct));

        await using var check = postgres.NewContextOn(seed);
        var row = await check.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("1234567890", row.MetaPixelId);
        Assert.Equal(BuyButtonMode.AdminsOnly, row.BuyButton);
        Assert.False(row.VippsTestMode);
    }
}
