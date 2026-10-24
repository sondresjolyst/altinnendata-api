using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

public class OrderStatusTests : TestBase
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Order NewOrder() => new()
    {
        Reference = "AID-000001-ABCDEF12", VippsReference = "AID-000001-ABCDEF12",
        PublicToken = Token,
        Status = OrderStatus.Paid,
        Locale = "no",
        BuyerName = "Ola Nordmann",
        BuyerEmail = "ola@example.com",
        BuyerPhone = "4791234567",
        DeliveryMethod = DeliveryMethod.Pickup,
        DeliveryLabel = "Henting",
        BuildTitle = "Gaming-PC",
        BuildSlug = "gaming-pc",
        BuildPriceInOre = 1_299_000,
        TotalInOre = 1_299_000
    };

    [Fact]
    public async Task Get_ReturnsTheStatusWithoutBuyerData()
    {
        await using var db = CreateDbContext();
        db.Orders.Add(NewOrder());
        await db.SaveChangesAsync(Ct);

        var ok = Assert.IsType<Ok<OrderStatusDto>>(await OrderStatusEndpoint.Get(Token, db, Ct));

        Assert.Equal(new OrderStatusDto("Paid", "AID-000001-ABCDEF12", "Gaming-PC", 1_299_000, "Pickup", "gaming-pc"), ok.Value);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("BuyerEmail", json);
        Assert.DoesNotContain("BuyerName", json);
        Assert.DoesNotContain("BuyerPhone", json);
    }

    [Fact]
    public async Task Get_NamesTheBoughtBuild_BySlugOnly()
    {
        await using var db = CreateDbContext();
        var order = NewOrder();
        order.PcBuildId = 42;
        order.BuildSlug = "stille-gaming-pc";
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var ok = Assert.IsType<Ok<OrderStatusDto>>(await OrderStatusEndpoint.Get(Token, db, Ct));

        Assert.Equal("stille-gaming-pc", ok.Value!.BuildSlug);
        var json = JsonSerializer.Serialize(ok.Value, JsonSerializerOptions.Web);
        Assert.Contains("\"buildSlug\":\"stille-gaming-pc\"", json);
        Assert.DoesNotContain("42", json);
        Assert.DoesNotContain("buildId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("gggggggggggggggggggggggggggggggg")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("fedcba9876543210fedcba9876543210")]
    public async Task Get_IsNotFound_ForAnUnknownOrMalformedToken(string token)
    {
        await using var db = CreateDbContext();
        db.Orders.Add(NewOrder());
        await db.SaveChangesAsync(Ct);

        Assert.IsType<NotFound>(await OrderStatusEndpoint.Get(token, db, Ct));
    }
}
