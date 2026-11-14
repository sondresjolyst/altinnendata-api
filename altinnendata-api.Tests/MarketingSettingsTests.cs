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

    private static UpdateMarketingSettingsDto Dto(string? id, string? tikTok = null) => new() { MetaPixelId = id, TikTokPixelId = tikTok };

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

    private const string TikTokId = "BRU69P41M9ALI4R4B2QG";
    private const string TikTokMessage = "The TikTok Pixel ID must be 10 to 32 capital letters and digits.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(TikTokId)]
    [InlineData("ABCDE12345")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    [InlineData("0123456789")]
    [InlineData(" BRU69P41M9ALI4R4B2QG ")]
    public void Validator_AcceptsEmptyOrTenToThirtyTwoCapitalLettersAndDigits_ForTikTok(string? id)
    {
        Assert.True(new UpdateMarketingSettingsValidator().Validate(Dto(null, id)).IsValid);
    }

    [Theory]
    [InlineData("bru69p41m9ali4r4b2qg")]
    [InlineData("BRU69P41M9aLI4R4B2QG")]
    [InlineData("ABCDE1234")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    [InlineData("<script>")]
    [InlineData("<SCRIPT>ALERT(1)</SCRIPT>")]
    [InlineData("BRU69P41 M9ALI4R4B2QG")]
    [InlineData("BRU69P41\tM9ALI4R4B2QG")]
    [InlineData("BRU69P41M9ÆLI4R4B2QG")]
    [InlineData("BRU69P41M9ÀLI4R4B2QG")]
    [InlineData("ＢＲＵ69Ｐ41Ｍ9ＡＬＩ4Ｒ4Ｂ2ＱＧ")]
    [InlineData("BRU69P41M9ALI4R4B٢QG")]
    [InlineData("BRU69P41-M9ALI4R4B2QG")]
    [InlineData("BRU69P41M9ALI4R4B2QG\nX")]
    public void Validator_RefusesAnythingElse_OnTheTikTokPixelIdField(string id)
    {
        var result = new UpdateMarketingSettingsValidator().Validate(Dto("1234567890", id));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("tikTokPixelId", error.PropertyName);
        Assert.Equal(TikTokMessage, error.ErrorMessage);
    }

    [Fact]
    public void Validator_ReportsEachInvalidId_UnderItsOwnField()
    {
        var result = new UpdateMarketingSettingsValidator().Validate(Dto("12ab", "bad"));

        Assert.Equal(["metaPixelId", "tikTokPixelId"], result.ToDictionary().Keys.Order());
    }

    [Fact]
    public async Task Admin_StartsWithoutAnId()
    {
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.GetAdmin(db, Ct));

        Assert.Null(ok.Value!.MetaPixelId);
        Assert.Null(ok.Value!.TikTokPixelId);
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
        Assert.Equal(new MarketingConfigDto("1234567890", null), config.Value);
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
    public async Task Update_StoresBothTrimmedIds_AndThePublicConfigReturnsBoth()
    {
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.Update(Dto(" 1234567890 ", $" {TikTokId} "), db, Ct));

        Assert.Equal(new MarketingSettingsDto("1234567890", TikTokId), ok.Value);
        var stored = await db.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("1234567890", stored.MetaPixelId);
        Assert.Equal(TikTokId, stored.TikTokPixelId);
        var admin = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.GetAdmin(db, Ct));
        Assert.Equal(new MarketingSettingsDto("1234567890", TikTokId), admin.Value);
        var config = Assert.IsType<Ok<MarketingConfigDto>>(await MarketingSettingsEndpoints.GetPublic(db, Ct));
        Assert.Equal(new MarketingConfigDto("1234567890", TikTokId), config.Value);
    }

    [Theory]
    [InlineData("BRU69P41M9ALI4R4B2QG\n")]
    [InlineData(" BRU69P41M9ALI4R4B2QG ")]
    [InlineData("　BRU69P41M9ALI4R4B2QG　")]
    [InlineData(" 　BRU69P41M9ALI4R4B2QG　 ")]
    public async Task Update_TrimsSurroundingWhitespace_FromTheTikTokId(string padded)
    {
        Assert.True(new UpdateMarketingSettingsValidator().Validate(Dto(null, padded)).IsValid);
        await using var db = await SeededDb();

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.Update(Dto(null, padded), db, Ct));

        Assert.Equal(TikTokId, ok.Value!.TikTokPixelId);
        Assert.Equal(TikTokId, (await db.ShopSettings.AsNoTracking().SingleAsync(Ct)).TikTokPixelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Update_WithAnEmptyTikTokId_TurnsTikTokOff_AndKeepsMeta(string? empty)
    {
        await using var db = await SeededDb();
        await MarketingSettingsEndpoints.Update(Dto("1234567890", TikTokId), db, Ct);

        var ok = Assert.IsType<Ok<MarketingSettingsDto>>(await MarketingSettingsEndpoints.Update(Dto("1234567890", empty), db, Ct));

        Assert.Equal(new MarketingSettingsDto("1234567890", null), ok.Value);
        var stored = await db.ShopSettings.AsNoTracking().SingleAsync(Ct);
        Assert.Null(stored.TikTokPixelId);
        Assert.Equal("1234567890", stored.MetaPixelId);
        Assert.Equal(new MarketingConfigDto("1234567890", null),
            Assert.IsType<Ok<MarketingConfigDto>>(await MarketingSettingsEndpoints.GetPublic(db, Ct)).Value);
    }

    [Fact]
    public async Task Update_WithOnlyTikTok_TurnsMetaOff_AndKeepsTikTok()
    {
        await using var db = await SeededDb();
        await MarketingSettingsEndpoints.Update(Dto("1234567890", TikTokId), db, Ct);

        await MarketingSettingsEndpoints.Update(Dto(null, TikTokId), db, Ct);

        Assert.Equal(new MarketingConfigDto(null, TikTokId),
            Assert.IsType<Ok<MarketingConfigDto>>(await MarketingSettingsEndpoints.GetPublic(db, Ct)).Value);
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

        Assert.Equal("{\"metaPixelId\":null,\"tikTokPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));
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
        Assert.Equal("{\"metaPixelId\":\"1234567890\",\"tikTokPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));

        var refused = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "12ab" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(["metaPixelId"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal("{\"metaPixelId\":\"1234567890\",\"tikTokPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));

        var cleared = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "" }), Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal("{\"metaPixelId\":null,\"tikTokPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));
    }

    [Fact]
    public async Task Http_AdminSetsBothIds_AnInvalidTikTokIdIsRefused_AndNothingIsSaved()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        const string both = "{\"metaPixelId\":\"1234567890\",\"tikTokPixelId\":\"" + TikTokId + "\"}";

        var saved = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "1234567890", tikTokPixelId = TikTokId }), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(both, await saved.Content.ReadAsStringAsync(Ct));
        Assert.Equal(both, await client.GetStringAsync("/api/marketing/config", Ct));

        foreach (var bad in new[] { "bru69p41m9ali4r4b2qg", "ABC123", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456", "<script>", "BRU69 P41M9ALI4R4B2QG", "BRU69P41M9ÆLI4R4B2QG" })
        {
            var refused = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "9876543210", tikTokPixelId = bad }), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Ct));
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal(["tikTokPixelId"], errors.EnumerateObject().Select(p => p.Name));
            Assert.Equal([TikTokMessage], errors.GetProperty("tikTokPixelId").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(both, await client.GetStringAsync("/api/marketing/config", Ct));
        }

        var cleared = await client.SendAsync(Put(RoleNames.Admin, new { metaPixelId = "1234567890", tikTokPixelId = "" }), Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal("{\"metaPixelId\":\"1234567890\",\"tikTokPixelId\":null}", await client.GetStringAsync("/api/marketing/config", Ct));

        var tikTokOnly = await client.SendAsync(Put(RoleNames.Admin, new { tikTokPixelId = TikTokId }), Ct);
        Assert.Equal(HttpStatusCode.OK, tikTokOnly.StatusCode);
        Assert.Equal("{\"metaPixelId\":null,\"tikTokPixelId\":\"" + TikTokId + "\"}", await client.GetStringAsync("/api/marketing/config", Ct));
    }
}
