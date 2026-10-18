using System.Net;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>The shop routes as Program.cs maps them: the admin routes refuse a caller who is not signed in, and the public status page is never cached.</summary>
public class ShopEndpointTests : IClassFixture<BadRequestsTests.ApiFactory>
{
    private readonly BadRequestsTests.ApiFactory _factory;

    public ShopEndpointTests(BadRequestsTests.ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("GET", "/api/admin/shop/settings")]
    [InlineData("PUT", "/api/admin/shop/settings")]
    [InlineData("GET", "/api/admin/shop/orders")]
    [InlineData("GET", "/api/admin/shop/orders/1")]
    [InlineData("POST", "/api/admin/shop/orders/1/capture")]
    [InlineData("POST", "/api/admin/shop/orders/1/cancel")]
    [InlineData("POST", "/api/admin/shop/orders/1/refund")]
    [InlineData("PUT", "/api/admin/shop/orders/1/invoice-number")]
    public async Task AdminRoutes_RefuseACallerWhoIsNotSignedIn(string method, string path)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("not-a-token")]
    public async Task OrderStatus_IsNeverCached(string token)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/shop/orders/{token}/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
