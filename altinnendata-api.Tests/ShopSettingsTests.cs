using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SjolystInnovation.AppKit.SiteSettings;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class ShopSettingsTests : TestBase
{
    private static async Task<ApplicationDbContext> SeededDb()
    {
        var db = CreateDbContext();
        await ShopSeed.EnsureAsync(db, default);
        return db;
    }

    private static void AddTerms(ApplicationDbContext db, string locale, DateTime updatedAt) =>
        db.LegalPages.Add(new LegalPage { Key = "terms", Locale = locale, Title = "Terms", BodyMarkdown = "Text", UpdatedAt = updatedAt });

    private static UpdateShopSettingsDto Update(string buyButton = "Off", bool vippsTestMode = true, Action<List<DeliveryOptionDto>>? edit = null)
    {
        List<DeliveryOptionDto> delivery =
        [
            new("Shipping", false, 0, "Sending", "Shipping", null, null, null),
            new("HomeDelivery", false, 0, "Levering hjem", "Home delivery", null, null, 30),
            new("Pickup", true, 0, "Henting", "Pickup", null, null, null)
        ];
        edit?.Invoke(delivery);
        return new UpdateShopSettingsDto { BuyButton = buyButton, VippsTestMode = vippsTestMode, Delivery = delivery };
    }

    [Fact]
    public async Task Seed_AddsSettingsAndThreeOptions_AndKeepsEditsOnTheSecondRun()
    {
        await using var db = CreateDbContext();

        await ShopSeed.EnsureAsync(db, default);
        (await db.DeliveryOptions.FirstAsync(o => o.Method == DeliveryMethod.Shipping)).PriceNok = 99;
        await db.SaveChangesAsync();
        await ShopSeed.EnsureAsync(db, default);

        var settings = await db.ShopSettings.SingleAsync();
        Assert.Equal(BuyButtonMode.Off, settings.BuyButton);
        Assert.True(settings.VippsTestMode);
        Assert.Equal(3, await db.DeliveryOptions.CountAsync());
        Assert.Equal(99, (await db.DeliveryOptions.FirstAsync(o => o.Method == DeliveryMethod.Shipping)).PriceNok);
        Assert.Equal(30, (await db.DeliveryOptions.FirstAsync(o => o.Method == DeliveryMethod.HomeDelivery)).MaxDistanceKm);
        Assert.True((await db.DeliveryOptions.FirstAsync(o => o.Method == DeliveryMethod.Pickup)).Enabled);
    }

    [Fact]
    public async Task Public_ListsOnlyEnabledOptions_InTheAskedLocale()
    {
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<ShopConfigDto>>(await ShopSettingsEndpoints.GetPublic(db, default, "en"));

        Assert.Equal("Off", ok.Value!.BuyButton);
        var option = Assert.Single(ok.Value.Delivery);
        Assert.Equal(new PublicDeliveryOptionDto("Pickup", 0, "Pickup", null, null), option);
    }

    [Fact]
    public async Task Update_RefusesEveryone_UntilBothTermsPagesAreSavedAfterTheShop()
    {
        await using var db = await SeededDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var created = (await db.ShopSettings.SingleAsync()).CreatedAt;
        AddTerms(db, "no", created.AddMinutes(1));
        AddTerms(db, "en", created.AddMinutes(-1));
        await db.SaveChangesAsync();

        var refused = Assert.IsType<ProblemHttpResult>(await ShopSettingsEndpoints.Update(Update("Everyone"), db, cache, default));

        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Equal("termsNotUpdated", refused.ProblemDetails.Extensions["code"]?.ToString());
        Assert.Equal(BuyButtonMode.Off, (await db.ShopSettings.AsNoTracking().SingleAsync()).BuyButton);

        var en = await db.LegalPages.SingleAsync(p => p.Locale == "en");
        en.UpdatedAt = created.AddMinutes(2);
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<ShopSettingsDto>>(await ShopSettingsEndpoints.Update(Update("Everyone"), db, cache, default));

        Assert.Equal("Everyone", ok.Value!.BuyButton);
        Assert.Equal(BuyButtonMode.Everyone, (await db.ShopSettings.AsNoTracking().SingleAsync()).BuyButton);
    }

    [Fact]
    public async Task Update_AllowsAdminsOnly_WithoutNewTerms()
    {
        await using var db = await SeededDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var ok = Assert.IsType<Ok<ShopSettingsDto>>(await ShopSettingsEndpoints.Update(Update("AdminsOnly"), db, cache, default));

        Assert.Equal("AdminsOnly", ok.Value!.BuyButton);
        Assert.False(ok.Value.TermsUpdatedSinceShop);
    }

    [Fact]
    public async Task Update_StoresTheDeliveryEdits()
    {
        await using var db = await SeededDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await ShopSettingsEndpoints.Update(Update(edit: d => d[0] = d[0] with { Enabled = true, PriceNok = 149, DescriptionNo = " Pakke " }), db, cache, default);

        var shipping = await db.DeliveryOptions.AsNoTracking().SingleAsync(o => o.Method == DeliveryMethod.Shipping);
        Assert.True(shipping.Enabled);
        Assert.Equal(149, shipping.PriceNok);
        Assert.Equal("Pakke", shipping.DescriptionNo);
    }

    [Fact]
    public async Task Update_ClearsTheVippsModeCache_WhenTheModeChanges()
    {
        await using var db = await SeededDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(ShopSettingsEndpoints.VippsTestModeCacheKey, true);

        await ShopSettingsEndpoints.Update(Update(vippsTestMode: true), db, cache, default);
        Assert.True(cache.TryGetValue(ShopSettingsEndpoints.VippsTestModeCacheKey, out _));

        await ShopSettingsEndpoints.Update(Update(vippsTestMode: false), db, cache, default);
        Assert.False(cache.TryGetValue(ShopSettingsEndpoints.VippsTestModeCacheKey, out _));
    }

    [Fact]
    public void TermsUpdatedSince_NeedsBothLocales()
    {
        var since = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        LegalPage Page(string locale, int days) =>
            new() { Key = "terms", Locale = locale, Title = "T", BodyMarkdown = "B", UpdatedAt = since.AddDays(days) };

        Assert.True(ShopSettingsEndpoints.TermsUpdatedSince([Page("no", 1), Page("en", 1)], since));
        Assert.False(ShopSettingsEndpoints.TermsUpdatedSince([Page("no", 1)], since));
        Assert.False(ShopSettingsEndpoints.TermsUpdatedSince([Page("no", 1), Page("en", 0)], since));
    }

    [Fact]
    public void Validator_AcceptsTheDefaults() =>
        Assert.True(new UpdateShopSettingsValidator().Validate(Update()).IsValid);

    [Fact]
    public void Validator_RejectsAMissingMethod()
    {
        var result = new UpdateShopSettingsValidator().Validate(Update(edit: d => d.RemoveAt(2)));

        Assert.Contains(result.Errors, e => e.PropertyName == "Delivery");
    }

    [Fact]
    public void Validator_RejectsAnUnknownBuyButtonMode()
    {
        var result = new UpdateShopSettingsValidator().Validate(Update("Sometimes"));

        Assert.Contains(result.Errors, e => e.PropertyName == "BuyButton");
    }

    [Fact]
    public void Validator_RequiresADistance_ForEnabledHomeDelivery()
    {
        var result = new UpdateShopSettingsValidator().Validate(
            Update(edit: d => d[1] = d[1] with { Enabled = true, MaxDistanceKm = null }));

        Assert.Contains(result.Errors, e => e.PropertyName == "Delivery[1].MaxDistanceKm");
    }

    [Fact]
    public void Validator_DoesNotNeedADistance_WhenHomeDeliveryIsOff() =>
        Assert.True(new UpdateShopSettingsValidator().Validate(
            Update(edit: d => d[1] = d[1] with { MaxDistanceKm = null })).IsValid);

    [Theory]
    [InlineData(-1, "Sending", "Shipping")]
    [InlineData(100001, "Sending", "Shipping")]
    [InlineData(0, "", "Shipping")]
    [InlineData(0, "Sending", "")]
    public void Validator_RejectsBadPricesAndEmptyLabels(int price, string labelNo, string labelEn)
    {
        var result = new UpdateShopSettingsValidator().Validate(
            Update(edit: d => d[0] = d[0] with { PriceNok = price, LabelNo = labelNo, LabelEn = labelEn }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsLongLabelsAndDescriptions()
    {
        var validator = new UpdateShopSettingsValidator();

        Assert.False(validator.Validate(Update(edit: d => d[0] = d[0] with { LabelNo = new string('a', 81) })).IsValid);
        Assert.True(validator.Validate(Update(edit: d => d[0] = d[0] with { LabelNo = new string('a', 80) })).IsValid);
        Assert.False(validator.Validate(Update(edit: d => d[0] = d[0] with { DescriptionEn = new string('a', 301) })).IsValid);
    }
}
