using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Public_ReportsNone_AsTheTermsVersion_WhileThereAreNoTerms()
    {
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<ShopConfigDto>>(await ShopSettingsEndpoints.GetPublic(db, default, "no"));

        Assert.Equal("none", ok.Value!.TermsVersion);
    }

    [Fact]
    public async Task Public_ReportsTheTermsVersion_OfTheAskedLocale_OrTheNorwegianPage()
    {
        await using var db = await SeededDb();
        AddTerms(db, "no", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234560));
        await db.SaveChangesAsync();

        var en = Assert.IsType<Ok<ShopConfigDto>>(await ShopSettingsEndpoints.GetPublic(db, default, "en")).Value!;
        Assert.Equal("2026-10-01T08:00:00.123456Z", en.TermsVersion);

        AddTerms(db, "en", new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();
        Assert.Equal("2026-10-02T09:30:00.000000Z", Assert.IsType<Ok<ShopConfigDto>>(await ShopSettingsEndpoints.GetPublic(db, default, "en")).Value!.TermsVersion);
        Assert.Equal("2026-10-01T08:00:00.123456Z", Assert.IsType<Ok<ShopConfigDto>>(await ShopSettingsEndpoints.GetPublic(db, default, "no")).Value!.TermsVersion);
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

        var refused = Assert.IsType<ProblemHttpResult>(await ShopSettingsEndpoints.Update(
            Update("Everyone", vippsTestMode: false, edit: d => d[0] = d[0] with { Enabled = true, PriceNok = 50 }), db, cache, default));

        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Equal("termsNotUpdated", refused.ProblemDetails.Extensions["code"]?.ToString());
        var unchanged = await db.ShopSettings.AsNoTracking().SingleAsync();
        Assert.Equal(BuyButtonMode.Off, unchanged.BuyButton);
        Assert.True(unchanged.VippsTestMode);
        Assert.False((await db.DeliveryOptions.AsNoTracking().SingleAsync(o => o.Method == DeliveryMethod.Shipping)).Enabled);

        var en = await db.LegalPages.SingleAsync(p => p.Locale == "en");
        en.UpdatedAt = created.AddMinutes(2);
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<ShopSettingsDto>>(await ShopSettingsEndpoints.Update(Update("Everyone"), db, cache, default));

        Assert.Equal("Everyone", ok.Value!.BuyButton);
        Assert.True(ok.Value.VippsTestMode);
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

    [Theory]
    [InlineData("7")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("Maybe")]
    public void Validator_RejectsNumericAndUndefinedBuyButtonModes(string mode) =>
        Assert.Contains(new UpdateShopSettingsValidator().Validate(Update(mode)).Errors, e => e.PropertyName == "BuyButton");

    [Theory]
    [InlineData("7")]
    [InlineData("1")]
    public void Validator_RejectsNumericAndUndefinedDeliveryMethods(string method)
    {
        var extra = new UpdateShopSettingsValidator().Validate(Update(edit: d => d.Add(d[0] with { Method = method })));
        var replaced = new UpdateShopSettingsValidator().Validate(Update(edit: d => d[2] = d[2] with { Method = method }));

        Assert.Contains(extra.Errors, e => e.PropertyName == "Delivery");
        Assert.Contains(replaced.Errors, e => e.PropertyName == "Delivery");
    }

    [Fact]
    public void Validator_RejectsANullDeliveryListAndNullEntries()
    {
        var validator = new UpdateShopSettingsValidator();
        var noList = Update();
        noList.Delivery = null!;
        var nullEntry = Update(edit: d => d.Add(null!));

        Assert.Contains(validator.Validate(noList).Errors, e => e.PropertyName == "Delivery");
        Assert.Contains(validator.Validate(nullEntry).Errors, e => e.PropertyName == "Delivery");
    }

    [Fact]
    public async Task Problem_WritesCodeAtTheTopLevelOfTheJsonBody()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        await ShopProblems.Code(StatusCodes.Status409Conflict, "termsNotUpdated", "Save the terms.").ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var json = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("termsNotUpdated", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Save the terms.", json.RootElement.GetProperty("message").GetString());
    }
}
