using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SjolystInnovation.AppKit.Consent;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Features.Marketing;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>The marketing ids a checkout stores, only with a marketing consent in the consent log.</summary>
[Collection("postgres")]
public class CheckoutMarketingTests(PostgresDatabase postgres) : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://www.altinnendata.no" })
        .Build();

    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Test/1.0";
    private const string PageUrl = "https://www.altinnendata.no/no/shop/checkout/gaming-pc?utm_source=tiktok&ttclid=abc#top";
    private const string StoredPageUrl = "https://www.altinnendata.no/no/shop/checkout/gaming-pc";
    private static readonly Guid ConsentId = Guid.Parse("3f2a6c1e-0b7d-4e8a-9c55-1d2e3f4a5b6c");

    private static CheckoutMarketing Marketing(string? consentId = null, string? fbp = "fb.1.1558571054389.1098115397",
        string? fbc = "fb.1.1554763741205.AbCdEfGhIjKlMnOpQrStUvWxYz1234567890", string? ttp = "2QhJk9xYz-ttp-cookie",
        string? ttclid = "E.C.P.ttclid-value", string? pageUrl = PageUrl) =>
        new(consentId ?? ConsentId.ToString(), fbp, fbc, ttp, ttclid, pageUrl);

    private static CheckoutRequest Request(CheckoutMarketing? marketing, string method = "Shipping") =>
        new("gaming-pc", method, "Ola Nordmann", "ola@example.com", "912 34 567",
            method == "Pickup" ? null : "Storgata 1", method == "Pickup" ? null : "0155", method == "Pickup" ? null : "Oslo",
            false, null, true, "no", ShopTerms.NoTermsVersion, marketing);

    private static HttpContext Caller(string ip = "203.0.113.7", string? userAgent = UserAgent)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleNames.Admin)], "Test"))
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userAgent != null) http.Request.Headers.UserAgent = userAgent;
        return http;
    }

    private static Mock<IVippsClient> Vipps()
    {
        var vipps = new Mock<IVippsClient>();
        vipps.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VippsCreatePaymentRequest r, string _, CancellationToken _) =>
                new VippsCreatePaymentResponse { RedirectUrl = "https://vipps.example/pay", Reference = r.Reference, IsTest = false });
        return vipps;
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        await ShopSeed.EnsureAsync(db, Ct);
        (await db.ShopSettings.SingleAsync(Ct)).BuyButton = BuyButtonMode.AdminsOnly;
        (await db.DeliveryOptions.SingleAsync(o => o.Method == DeliveryMethod.Shipping, Ct)).Enabled = true;
        db.PcBuilds.Add(new PcBuild
        {
            Slug = "gaming-pc",
            Published = true,
            PriceNok = 12990,
            Availability = BuildAvailability.Available,
            Components = [PartWithCondition()],
            Translations = [new PcBuildTranslation { Locale = "no", Title = "Gaming-PC" }]
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task ConsentAsync(ApplicationDbContext db, bool marketing, DateTime decidedAt, Guid? id = null, string notice = "2026-10-24")
    {
        db.Set<ConsentRecord>().Add(new ConsentRecord
        {
            ConsentId = id ?? ConsentId,
            Marketing = marketing,
            NoticeVersion = notice,
            SchemaVersion = 1,
            DecidedAt = decidedAt,
            Source = ConsentSources.Banner
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Order> CheckoutAsync(ApplicationDbContext db, CheckoutMarketing? marketing, HttpContext? http = null)
    {
        var result = await Checkout.Handle(Request(marketing), http ?? Caller(), db, Vipps().Object, Config, NullLogger<CheckoutRequest>.Instance, Ct);
        Assert.IsType<Ok<CheckoutResponse>>(result);
        await using var fresh = postgres.NewContextOn(db);
        return await fresh.Orders.AsNoTracking().SingleAsync(Ct);
    }

    private static void AssertNoMarketing(Order order)
    {
        Assert.Null(order.MarketingConsentId);
        Assert.Null(order.MarketingNoticeVersion);
        Assert.Null(order.Fbp);
        Assert.Null(order.Fbc);
        Assert.Null(order.Ttp);
        Assert.Null(order.Ttclid);
        Assert.Null(order.ClientIp);
        Assert.Null(order.ClientUserAgent);
        Assert.Null(order.EventSourceUrl);
        Assert.Null(order.MarketingEventsDueBy);
    }

    [Fact]
    public async Task AValidConsent_StoresTheMarketingColumns()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, false, DateTime.UtcNow.AddDays(-10), notice: "old");
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-2), notice: "2026-11-01");
        var ttclid = "E.C.P." + new string('x', 994);

        var order = await CheckoutAsync(db, Marketing(ttclid: ttclid));

        Assert.Equal(ConsentId.ToString(), order.MarketingConsentId);
        Assert.Equal("2026-11-01", order.MarketingNoticeVersion);
        Assert.Equal("fb.1.1558571054389.1098115397", order.Fbp);
        Assert.Equal("fb.1.1554763741205.AbCdEfGhIjKlMnOpQrStUvWxYz1234567890", order.Fbc);
        Assert.Equal("2QhJk9xYz-ttp-cookie", order.Ttp);
        Assert.Equal(1000, order.Ttclid!.Length);
        Assert.Equal(ttclid, order.Ttclid);
        Assert.Equal("203.0.113.7", order.ClientIp);
        Assert.Equal(UserAgent, order.ClientUserAgent);
        Assert.Equal(StoredPageUrl, order.EventSourceUrl);
        Assert.InRange(order.MarketingEventsDueBy!.Value, DateTime.UtcNow.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7));
        Assert.Equal(0, order.MetaEventAttempts);
        Assert.Null(order.MetaEventSentAt);
    }

    [Fact]
    public async Task OnlyTheConsentIdIsRequired_AndAPrivateIpOrALongUserAgentIsHandled()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-364));

        var order = await CheckoutAsync(db, new CheckoutMarketing(ConsentId.ToString()), Caller("10.42.0.17", new string('u', 600)));

        Assert.Equal(ConsentId.ToString(), order.MarketingConsentId);
        Assert.Null(order.Fbp);
        Assert.Null(order.EventSourceUrl);
        Assert.Null(order.ClientIp);
        Assert.Equal(new string('u', 512), order.ClientUserAgent);
        Assert.NotNull(order.MarketingEventsDueBy);
    }

    public static TheoryData<string, CheckoutMarketing?> Refused => new()
    {
        { "no marketing object", null },
        { "unknown consent id", Marketing(consentId: "0b0b0b0b-1111-4222-8333-444444444444") },
        { "consent id not a guid", Marketing(consentId: "not-a-guid") },
        { "empty guid", Marketing(consentId: Guid.Empty.ToString()) },
        { "consent id too long", Marketing(consentId: ConsentId + new string('a', 40)) },
        { "page on another host", Marketing(pageUrl: "https://evil.example/no/shop") },
        { "page on http", Marketing(pageUrl: "http://www.altinnendata.no/no/shop") },
        { "relative page", Marketing(pageUrl: "/no/shop") },
        { "page with user info", Marketing(pageUrl: "https://user@www.altinnendata.no/no/shop") },
        { "page on another port", Marketing(pageUrl: "https://www.altinnendata.no:8443/no/shop") },
        { "page too long", Marketing(pageUrl: "https://www.altinnendata.no/" + new string('a', 2048)) },
        { "fbp too long", Marketing(fbp: new string('f', 257)) },
        { "fbc too long", Marketing(fbc: new string('f', 257)) },
        { "ttp too long", Marketing(ttp: new string('t', 257)) },
        { "ttclid too long", Marketing(ttclid: new string('t', 1001)) },
        { "fbp with a space", Marketing(fbp: "fb.1 2") },
        { "ttp with a control character", Marketing(ttp: "a\nb") },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task AnInvalidMarketingObject_StoresNothing_AndTheCheckoutSucceeds(string _, CheckoutMarketing? marketing)
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-1));

        AssertNoMarketing(await CheckoutAsync(db, marketing));
    }

    [Fact]
    public async Task ALatestConsentOfNo_StoresNothing()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-5));
        await ConsentAsync(db, false, DateTime.UtcNow.AddDays(-1));

        AssertNoMarketing(await CheckoutAsync(db, Marketing()));
    }

    [Fact]
    public async Task AConsentOlderThan365Days_StoresNothing()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-366));

        AssertNoMarketing(await CheckoutAsync(db, Marketing()));
    }

    [Fact]
    public async Task AnotherBrowsersConsent_DoesNotCount()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, true, DateTime.UtcNow.AddDays(-1), id: Guid.NewGuid());

        AssertNoMarketing(await CheckoutAsync(db, Marketing()));
    }

    [Fact]
    public void TheRequest_ReadsTheMarketingObject_FromTheSitesJson()
    {
        const string json = """
            {"buildSlug":"gaming-pc","deliveryMethod":"Pickup","name":"Ola","email":"ola@example.com","phone":"91234567",
             "acceptTerms":true,"locale":"no","termsVersion":"none",
             "marketing":{"consentId":"3f2a6c1e-0b7d-4e8a-9c55-1d2e3f4a5b6c","fbp":"fb.1.2.3","fbc":"fb.1.2.abc","ttp":"t","ttclid":"c","pageUrl":"https://www.altinnendata.no/no"}}
            """;

        var request = System.Text.Json.JsonSerializer.Deserialize<CheckoutRequest>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Equal(new CheckoutMarketing("3f2a6c1e-0b7d-4e8a-9c55-1d2e3f4a5b6c", "fb.1.2.3", "fb.1.2.abc", "t", "c", "https://www.altinnendata.no/no"), request.Marketing);
        const string without = """{"buildSlug":"gaming-pc","deliveryMethod":"Pickup","name":"Ola","email":"ola@example.com","phone":"91234567","acceptTerms":true,"locale":"no","termsVersion":"none"}""";
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<CheckoutRequest>(without, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.Marketing);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1111:a:b0::200", "2001:db8:1111:a:b0::200")]
    [InlineData("127.0.0.1", null)]
    [InlineData("::1", null)]
    [InlineData("10.42.0.17", null)]
    [InlineData("172.16.5.4", null)]
    [InlineData("192.168.1.122", null)]
    [InlineData("100.64.0.1", null)]
    [InlineData("169.254.1.1", null)]
    [InlineData("fe80::1", null)]
    [InlineData("fd00::1", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("224.0.0.1", null)]
    [InlineData("239.255.255.250", null)]
    [InlineData("240.0.0.1", null)]
    [InlineData("255.255.255.255", null)]
    [InlineData("198.18.0.1", null)]
    [InlineData("198.19.255.254", null)]
    [InlineData("192.0.0.8", null)]
    [InlineData("::", null)]
    [InlineData("ff02::1", null)]
    [InlineData("64:ff9b::cb00:7107", null)]
    [InlineData("2002:cb00:7107::1", null)]
    [InlineData("fec0::1", null)]
    [InlineData("198.17.255.255", "198.17.255.255")]
    [InlineData("198.20.0.1", "198.20.0.1")]
    [InlineData("192.0.1.1", "192.0.1.1")]
    [InlineData("223.255.255.254", "223.255.255.254")]
    [InlineData("192.169.10.20", "192.169.10.20")]
    [InlineData("2a01:79c:cebf:1::1", "2a01:79c:cebf:1::1")]
    public void PublicIp_KeepsOnlyAPublicAddress(string address, string? expected)
    {
        Assert.Equal(expected, MarketingCapture.PublicIp(IPAddress.Parse(address)));
    }
}
