using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using altinnendata_api.Constants;
using altinnendata_api.Features.Marketing;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class MarketingSettingsTests : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ApplicationDbContext> SeededDb()
    {
        var db = CreateDbContext();
        await ShopSeed.EnsureAsync(db, default);
        return db;
    }

    private static UpdateMarketingSettingsDto Dto(string? id) => new() { MetaPixelId = id };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("12345678901234567890")]
    [InlineData(" 1234567890 ")]
    public void Validator_AcceptsEmptyOrFiveToTwentyDigits(string? id)
    {
        Assert.True(new UpdateMarketingSettingsValidator().Validate(Dto(id)).IsValid);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("123456789012345678901")]
    [InlineData("12a45")]
    [InlineData("123 456")]
    [InlineData("-12345")]
    [InlineData("12345.6")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("<script>")]
    public void Validator_RefusesAnythingElse_OnTheMetaPixelIdField(string id)
    {
        var result = new UpdateMarketingSettingsValidator().Validate(Dto(id));

        Assert.False(result.IsValid);
        Assert.Equal(["metaPixelId"], result.ToDictionary().Keys);
    }

    [Fact]
    public async Task Admin_StartsWithoutAnId()
    {
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.GetAdmin(db, Ct));

        Assert.Null(ok.Value!.MetaPixelId);
    }

    [Fact]
    public async Task Update_StoresTheTrimmedId_AndThePublicConfigReturnsIt()
    {
        await using var db = await SeededDb();
        var before = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        (await db.ShopSettings.SingleAsync(Ct)).UpdatedAt = before;
        await db.SaveChangesAsync(Ct);

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.Update(Dto(" 1234567890 "), db, Ct));

        Assert.Equal("1234567890", ok.Value!.MetaPixelId);
        var stored = await db.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("1234567890", stored.MetaPixelId);
        Assert.True(stored.UpdatedAt > before);
        var admin = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.GetAdmin(db, Ct));
        Assert.Equal("1234567890", admin.Value!.MetaPixelId);
        var config = Assert.IsType<Ok<MarketingConfigDto>>(await MarketingSettingsEndpoints.GetPublic(db, Ct));
        Assert.Equal(new MarketingConfigDto("1234567890"), config.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Update_WithAnEmptyId_ClearsIt(string? empty)
    {
        await using var db = await SeededDb();
        await MarketingSettingsEndpoints.Update(Dto("1234567890"), db, Ct);

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.Update(Dto(empty), db, Ct));

        Assert.Null(ok.Value!.MetaPixelId);
        Assert.Null((await db.ShopSettings.AsNoTracking().SingleAsync(Ct)).MetaPixelId);
        Assert.Null(Assert.IsType<Ok<MarketingConfigDto>>(await MarketingSettingsEndpoints.GetPublic(db, Ct)).Value!.MetaPixelId);
    }

    [Fact]
    public async Task Update_LeavesTheShopSettingsAlone()
    {
        await using var db = await SeededDb();
        var shop = await db.ShopSettings.SingleAsync(Ct);
        shop.VippsTestMode = false;
        await db.SaveChangesAsync(Ct);

        await MarketingSettingsEndpoints.Update(Dto("1234567890"), db, Ct);

        var stored = await db.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.False(stored.VippsTestMode);
        Assert.Equal(Models.Shop.BuyButtonMode.Off, stored.BuyButton);
    }

    [Fact]
    public void Endpoints_OnlyThePublicConfigIsAnonymous()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("routes"));
        var app = builder.Build();
        new MarketingSettingsEndpoints.Endpoints().Map(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).Cast<RouteEndpoint>().ToList();

        Assert.Equal(3, endpoints.Count);
        var config = Assert.Single(endpoints, e => e.RoutePattern.RawText == "/api/marketing/config");
        Assert.NotNull(config.Metadata.GetMetadata<IAllowAnonymous>());
        var admin = endpoints.Where(e => e != config).ToList();
        Assert.All(admin, e =>
        {
            Assert.Equal("/api/admin/marketing/settings", e.RoutePattern.RawText!.TrimEnd('/'));
            Assert.Contains(e.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == Policies.Admin);
            Assert.Null(e.Metadata.GetMetadata<IAllowAnonymous>());
        });
    }

    private sealed class TestAuth(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "me"), new Claim(ClaimTypes.Role, role.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var name = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", null);
        builder.Services.AddAuthorization(o => o.AddPolicy(Policies.Admin, p => p.RequireRole(RoleNames.Admin)));
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        new MarketingSettingsEndpoints.Endpoints().Map(app);
        await using (var scope = app.Services.CreateAsyncScope())
            await ShopSeed.EnsureAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), Ct);
        await app.StartAsync(Ct);
        return (app, app.GetTestClient());
    }

    private static HttpRequestMessage Put(string? role, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/marketing/settings") { Content = JsonContent.Create(body) };
        if (role != null) request.Headers.Add("X-Role", role);
        return request;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    public async Task Http_OnlyAnAdminCanReadOrChangeTheSettings(string? role, HttpStatusCode expected)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        Assert.Equal(expected, (await client.SendAsync(Put(role, new { metaPixelId = "1234567890" }), Ct)).StatusCode);
        var get = new HttpRequestMessage(HttpMethod.Get, "/api/admin/marketing/settings");
        if (role != null) get.Headers.Add("X-Role", role);
        Assert.Equal(expected, (await client.SendAsync(get, Ct)).StatusCode);

        Assert.Equal("{\"metaPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));
    }

    [Fact]
    public async Task Http_AdminSetsTheId_ThePublicConfigShowsIt_AndAnInvalidIdIsRefused()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "1234567890" }), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var config = await client.GetAsync("/api/marketing/config", Ct);
        Assert.Equal("no-cache", config.Headers.CacheControl?.ToString());
        Assert.Equal("{\"metaPixelId\":\"1234567890\"}", await client.GetStringAsync("/api/marketing/config", Ct));

        var refused = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "12ab" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(["metaPixelId"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal("{\"metaPixelId\":\"1234567890\"}", await client.GetStringAsync("/api/marketing/config", Ct));

        var cleared = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "" }), Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal("{\"metaPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));
    }
}
