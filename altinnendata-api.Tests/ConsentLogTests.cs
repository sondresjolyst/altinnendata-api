using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SjolystInnovation.AppKit.Consent;
using SjolystInnovation.AppKit.Hosting;
using altinnendata_api.Constants;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>The consent log as the app maps it: the package endpoints, the admin policy and the rate limit rule from appsettings.json.</summary>
[Collection("postgres")]
public class ConsentLogTests(PostgresDatabase postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IConfiguration AppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "altinnendata-api.csproj"))) dir = dir.Parent;
        return new ConfigurationBuilder().AddJsonFile(Path.Combine(dir!.FullName, "appsettings.json")).Build();
    }

    private sealed class TestAuth(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Role, role.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private async Task<(WebApplication App, HttpClient Client, ApplicationDbContext Db)> StartAsync()
    {
        var db = await postgres.NewContextAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", null);
        builder.Services.AddAuthorization(o => o.AddPolicy(Policies.Admin, p => p.RequireRole(RoleNames.Admin)));
        builder.Services.AddAppKitConsent<ApplicationDbContext>();
        builder.Services.AddAppKitRateLimiting(AppSettings().GetSection("RateLimiting"));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAppKitRateLimiting();
        app.MapAppKitConsent<ApplicationDbContext>("/api", o => o.Policy = Policies.Admin);
        await app.StartAsync(Ct);
        return (app, app.GetTestClient(), db);
    }

    private static object Body(Guid id, bool marketing = true) =>
        new { consentId = id.ToString(), marketing, noticeVersion = "2026-10-24", schemaVersion = 1, source = "banner" };

    [Fact]
    public void AppSettings_LimitsTheConsentPost_To10AMinute()
    {
        var rules = AppSettings().GetSection("RateLimiting:Rules").GetChildren()
            .Where(r => r["Endpoint"] == "POST:/api/consent")
            .Select(r => (r["Period"], r["Limit"]))
            .ToList();

        Assert.Equal([("1m", "10")], rules);
    }

    [Fact]
    public async Task Post_StoresOneRow_WithoutAUserOrAnyIpColumn()
    {
        var (app, client, db) = await StartAsync();
        await using var _ = app;
        await using var __ = db;
        var id = Guid.NewGuid();

        var r = await client.PostAsJsonAsync("/api/consent", Body(id, marketing: false), Ct);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var row = await db.Set<ConsentRecord>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal(id, row.ConsentId);
        Assert.False(row.Marketing);
        Assert.Equal("2026-10-24", row.NoticeVersion);
        Assert.Null(row.UserId);
        Assert.InRange(row.DecidedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var columns = await db.Database
            .SqlQueryRaw<string>("select column_name as \"Value\" from information_schema.columns where table_name = 'ConsentRecords' order by column_name")
            .ToListAsync(Ct);
        Assert.Equal(["ConsentId", "DecidedAt", "Id", "Marketing", "NoticeVersion", "SchemaVersion", "Source", "UserId"], columns);
    }

    [Fact]
    public async Task Post_RefusesABadBody_WithFieldCodes()
    {
        var (app, client, db) = await StartAsync();
        await using var _ = app;
        await using var __ = db;

        var r = await client.PostAsJsonAsync("/api/consent",
            new { consentId = "not-a-guid", marketing = true, noticeVersion = "2026-10-24", schemaVersion = 1, source = "popup" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var problem = JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct));
        var codes = problem.RootElement.GetProperty("codes");
        Assert.Equal(ConsentErrorCodes.InvalidConsentId, codes.GetProperty("ConsentId")[0].GetString());
        Assert.Equal(ConsentErrorCodes.InvalidSource, codes.GetProperty("Source")[0].GetString());
        Assert.False(await db.Set<ConsentRecord>().AnyAsync(Ct));
    }

    [Fact]
    public async Task History_IsForAdminsOnly()
    {
        var (app, client, db) = await StartAsync();
        await using var _ = app;
        await using var __ = db;
        var id = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/consent", Body(id), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/consent/{id}", Ct)).StatusCode);

        var user = new HttpRequestMessage(HttpMethod.Get, $"/api/consent/{id}");
        user.Headers.Add("X-Role", "User");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(user, Ct)).StatusCode);

        var admin = new HttpRequestMessage(HttpMethod.Get, $"/api/consent/{id}");
        admin.Headers.Add("X-Role", RoleNames.Admin);
        var ok = await client.SendAsync(admin, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var list = JsonDocument.Parse(await ok.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, list.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Post_The11thInAMinute_Is429()
    {
        var (app, client, db) = await StartAsync();
        await using var _ = app;
        await using var __ = db;

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            codes.Add((await client.PostAsJsonAsync("/api/consent", Body(Guid.NewGuid()), Ct)).StatusCode);

        Assert.All(codes.Take(10), c => Assert.Equal(HttpStatusCode.NoContent, c));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[10]);
        Assert.Equal(10, await db.Set<ConsentRecord>().CountAsync(Ct));
    }
}
