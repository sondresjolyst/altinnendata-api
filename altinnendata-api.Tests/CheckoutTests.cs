using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class CheckoutTests(PostgresDatabase postgres) : TestBase
{
    private const string FakeUrl = "https://vipps.example/pay/abc";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://www.altinnendata.no" })
        .Build();

    private static CheckoutRequest Request(string method = "Pickup", string slug = "gaming-pc") =>
        new(slug, method, "Ola Nordmann", "ola@example.com", "912 34 567",
            method == "Pickup" ? null : "Storgata 1",
            method == "Pickup" ? null : "0155",
            method == "Pickup" ? null : "Oslo",
            method == "HomeDelivery", null, true, "en", ShopTerms.NoTermsVersion);

    private static HttpContext Caller(bool admin, string? ip = null)
    {
        var claims = new List<Claim>();
        if (admin) claims.Add(new(ClaimTypes.Role, RoleNames.Admin));
        var http = new DefaultHttpContext
        {
            User = admin ? new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) : new ClaimsPrincipal(new ClaimsIdentity())
        };
        if (ip != null) http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        return http;
    }

    private static Mock<IVippsClient> Vipps(List<VippsCreatePaymentRequest>? captured = null)
    {
        var vipps = new Mock<IVippsClient>();
        vipps.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<VippsCreatePaymentRequest, string, CancellationToken>((r, key, _) =>
            {
                Assert.Equal(r.Reference, key);
                captured?.Add(r);
            })
            .ReturnsAsync((VippsCreatePaymentRequest r, string _, CancellationToken _) =>
                new VippsCreatePaymentResponse { RedirectUrl = FakeUrl, Reference = r.Reference, IsTest = true });
        return vipps;
    }

    private static Task<IResult> Run(ApplicationDbContext db, Mock<IVippsClient> vipps, CheckoutRequest? request = null, bool admin = true, string? ip = null) =>
        Checkout.Handle(request ?? Request(), Caller(admin, ip), db, vipps.Object, Config, NullLogger<CheckoutRequest>.Instance, Ct);

    private static async Task<SjolystInnovation.AppKit.SiteSettings.LegalPage> AddTermsAsync(ApplicationDbContext db, string locale, string body, DateTime? updatedAt = null)
    {
        var page = new SjolystInnovation.AppKit.SiteSettings.LegalPage
        {
            Key = "terms", Locale = locale, Title = locale == "en" ? "Terms of Service" : "Vilkår", BodyMarkdown = body,
            UpdatedAt = updatedAt ?? new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)
        };
        db.LegalPages.Add(page);
        await db.SaveChangesAsync(Ct);
        return page;
    }

    private static async Task<Order> OnlyOrderAsync(PostgresDatabase postgres, ApplicationDbContext db)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await fresh.Orders.AsNoTracking().SingleAsync(Ct);
    }

    private static async Task<PcBuild> SeedAsync(
        ApplicationDbContext db,
        BuyButtonMode mode = BuyButtonMode.AdminsOnly,
        Action<PcBuild>? editBuild = null,
        Action<List<DeliveryOption>>? editDelivery = null)
    {
        await ShopSeed.EnsureAsync(db, Ct);
        (await db.ShopSettings.SingleAsync(Ct)).BuyButton = mode;
        var options = await db.DeliveryOptions.ToListAsync(Ct);
        editDelivery?.Invoke(options);

        var build = new PcBuild
        {
            Slug = "gaming-pc",
            Published = true,
            PriceNok = 12990,
            Availability = BuildAvailability.Available,
            Translations =
            [
                new PcBuildTranslation { Locale = "no", Title = "Gaming-PC" },
                new PcBuildTranslation { Locale = "en", Title = "Gaming PC" }
            ]
        };
        editBuild?.Invoke(build);
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(Ct);
        return build;
    }

    private static async Task<PcBuild> ReloadBuildAsync(ApplicationDbContext db, int id)
    {
        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).Options);
        return await fresh.PcBuilds.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
    }

    private static async Task<int> CountOrdersAsync(ApplicationDbContext db)
    {
        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).Options);
        return await fresh.Orders.CountAsync(Ct);
    }

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Checkout_HoldsTheBuild_CreatesTheOrderAndReturnsTheVippsUrl()
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db);
        var captured = new List<VippsCreatePaymentRequest>();

        var result = await Run(db, Vipps(captured));

        var ok = Assert.IsType<Ok<CheckoutResponse>>(result);
        Assert.Equal(FakeUrl, ok.Value!.RedirectUrl);
        Assert.Equal(32, ok.Value.Token.Length);
        Assert.Matches("^[0-9a-f]{32}$", ok.Value.Token);

        await using var fresh = postgres.NewContextOn(db);
        var order = await fresh.Orders.SingleAsync(Ct);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1_299_000, order.TotalInOre);
        Assert.Equal(1_299_000, order.BuildPriceInOre);
        Assert.Equal(0, order.DeliveryPriceInOre);
        Assert.Equal("Gaming PC", order.BuildTitle);
        Assert.Equal(DeliveryMethod.Pickup, order.DeliveryMethod);
        Assert.Equal("Pickup", order.DeliveryLabel);
        Assert.Equal("en", order.Locale);
        Assert.Equal("4791234567", order.BuyerPhone);
        Assert.Equal(ok.Value.Token, order.PublicToken);
        Assert.True(order.IsTest);
        Assert.Equal(build.Id, order.PcBuildId);
        Assert.True(order.TermsAcceptedAt > DateTime.UtcNow.AddMinutes(-1));

        var reloaded = await ReloadBuildAsync(db, build.Id);
        Assert.Equal(BuildAvailability.Reserved, reloaded.Availability);
        Assert.Equal(order.Id, reloaded.HeldByOrderId);

        var request = Assert.Single(captured);
        Assert.Matches(@"^AID-\d{6}-[0-9A-F]{8}$", request.Reference);
        Assert.Equal(order.Reference, request.Reference);
        Assert.StartsWith($"AID-{order.Id:D6}-", request.Reference);
        Assert.Equal(1_299_000, request.AmountInOre);
        Assert.Equal("4791234567", request.PhoneNumber);
        Assert.Equal($"Altinnendata {order.Reference}", request.Description);
        var line = Assert.Single(request.ReceiptLines);
        Assert.Equal($"build-{build.Id}", line.Id);
        Assert.Equal("Gaming PC", line.Name);
        Assert.Equal(1_299_000, line.UnitPriceInOre);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(0, line.TaxRateBasisPoints);
        Assert.Equal("https://www.altinnendata.no/en/shop/return?token=" + ok.Value.Token, request.ReturnUrl);
    }

    [Fact]
    public async Task Checkout_AddsADeliveryLine_WhenDeliveryCostsSomething()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, editDelivery: options =>
        {
            var shipping = options.Single(o => o.Method == DeliveryMethod.Shipping);
            shipping.Enabled = true;
            shipping.PriceNok = 199;
        });
        var captured = new List<VippsCreatePaymentRequest>();

        var result = await Run(db, Vipps(captured), Request("Shipping"));

        Assert.IsType<Ok<CheckoutResponse>>(result);
        var request = Assert.Single(captured);
        Assert.Equal(1_299_000 + 19_900, request.AmountInOre);
        Assert.Equal(2, request.ReceiptLines.Count);
        var line = request.ReceiptLines[1];
        Assert.Equal("delivery-Shipping", line.Id);
        Assert.Equal("Shipping", line.Name);
        Assert.Equal(19_900, line.UnitPriceInOre);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(0, line.TaxRateBasisPoints);

        await using var fresh = postgres.NewContextOn(db);
        var order = await fresh.Orders.SingleAsync(Ct);
        Assert.Equal(1_299_000 + 19_900, order.TotalInOre);
        Assert.Equal(19_900, order.DeliveryPriceInOre);
        Assert.Equal("Storgata 1", order.StreetAddress);
        Assert.Equal("0155", order.PostalCode);
        Assert.Equal("Oslo", order.City);
    }

    [Theory]
    [InlineData(BuyButtonMode.Off, true)]
    [InlineData(BuyButtonMode.AdminsOnly, false)]
    public async Task Checkout_AnswersShopClosed_WhenTheCallerMayNotBuy(BuyButtonMode mode, bool admin)
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db, mode);
        var vipps = Vipps();

        var result = await Run(db, vipps, admin: admin);

        AssertProblem(result, StatusCodes.Status404NotFound, "shopClosed");
        Assert.Equal(0, await CountOrdersAsync(db));
        vipps.VerifyNoOtherCalls();
        Assert.Equal(BuildAvailability.Available, (await ReloadBuildAsync(db, build.Id)).Availability);
    }

    [Fact]
    public async Task Checkout_LetsAnyoneBuy_WhenTheBuyButtonIsForEveryone()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, BuyButtonMode.Everyone);

        var result = await Run(db, Vipps(), admin: false);

        Assert.IsType<Ok<CheckoutResponse>>(result);
    }

    [Fact]
    public async Task Checkout_AnswersDeliveryUnavailable_WhenTheMethodIsDisabled()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        var vipps = Vipps();

        var result = await Run(db, vipps, Request("Shipping"));

        AssertProblem(result, StatusCodes.Status400BadRequest, "deliveryUnavailable");
        Assert.Equal(0, await CountOrdersAsync(db));
        vipps.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("sold")]
    [InlineData("unpublished")]
    [InlineData("unpriced")]
    [InlineData("missing")]
    public async Task Checkout_AnswersBuildUnavailable_WhenTheBuildCannotBeBought(string state)
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, editBuild: b =>
        {
            switch (state)
            {
                case "reserved": b.Availability = BuildAvailability.Reserved; break;
                case "sold": b.Availability = BuildAvailability.Sold; break;
                case "unpublished": b.Published = false; break;
                case "unpriced": b.PriceNok = null; break;
            }
        });
        var vipps = Vipps();

        var result = await Run(db, vipps, Request(slug: state == "missing" ? "other-pc" : "gaming-pc"));

        AssertProblem(result, StatusCodes.Status409Conflict, "buildUnavailable");
        Assert.Equal(0, await CountOrdersAsync(db));
        vipps.VerifyNoOtherCalls();
    }

    public static TheoryData<string> VippsFailures => ["http", "timeout"];

    [Theory]
    [MemberData(nameof(VippsFailures))]
    public async Task Checkout_RollsBack_WhenVippsFailsOrTimesOut(string failure)
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db);
        var vipps = new Mock<IVippsClient>();
        // HttpClient reports its own timeout as a TaskCanceledException around a TimeoutException.
        Exception error = failure == "http"
            ? new HttpRequestException("Vipps is down")
            : new TaskCanceledException("The request was canceled.", new TimeoutException());
        vipps.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

        var result = await Run(db, vipps);

        AssertProblem(result, StatusCodes.Status502BadGateway, "paymentProviderUnavailable");
        Assert.Equal(0, await CountOrdersAsync(db));
        var reloaded = await ReloadBuildAsync(db, build.Id);
        Assert.Equal(BuildAvailability.Available, reloaded.Availability);
        Assert.Null(reloaded.HeldByOrderId);
    }

    [Fact]
    public async Task Checkout_SecondBuyerGets409_WhenTheFirstHoldsTheBuild()
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db);
        await using var first = postgres.NewContextOn(db);
        await using var second = postgres.NewContextOn(db);
        var vipps = Vipps();

        var results = await Task.WhenAll(
            Task.Run(() => Run(first, vipps)),
            Task.Run(() => Run(second, vipps)));

        Assert.Single(results.OfType<Ok<CheckoutResponse>>());
        AssertProblem(Assert.Single(results.OfType<ProblemHttpResult>()), StatusCodes.Status409Conflict, "buildUnavailable");
        Assert.Equal(1, await CountOrdersAsync(db));
        vipps.Verify(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(BuildAvailability.Reserved, (await ReloadBuildAsync(db, build.Id)).Availability);
    }

    [Fact]
    public async Task Checkout_SecondBuyerGets409_WithinTheLockTimeout_WhileTheFirstWaitsOnVipps()
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db);
        await using var first = postgres.NewContextOn(db);
        await using var second = postgres.NewContextOn(db);

        var vippsCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVipps = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new Mock<IVippsClient>();
        slow.Setup(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (VippsCreatePaymentRequest r, string _, CancellationToken _) =>
            {
                vippsCalled.SetResult();
                await releaseVipps.Task;
                return new VippsCreatePaymentResponse { RedirectUrl = FakeUrl, Reference = r.Reference, IsTest = true };
            });
        var unused = Vipps();

        var firstCheckout = Task.Run(() => Run(first, slow));
        await vippsCalled.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var timer = Stopwatch.StartNew();
        var secondCheckout = Run(second, unused);
        var finished = await Task.WhenAny(secondCheckout, Task.Delay(TimeSpan.FromSeconds(15), Ct));
        timer.Stop();
        // Lets the first checkout commit, so a second checkout stuck on the row lock cannot hang the test.
        releaseVipps.SetResult();
        var firstResult = await firstCheckout;
        var secondResult = await secondCheckout;

        Assert.Same(secondCheckout, finished);
        AssertProblem(secondResult, StatusCodes.Status409Conflict, "buildUnavailable");
        Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));
        unused.VerifyNoOtherCalls();
        Assert.IsType<Ok<CheckoutResponse>>(firstResult);
        Assert.Equal(1, await CountOrdersAsync(db));
        Assert.Equal(BuildAvailability.Reserved, (await ReloadBuildAsync(db, build.Id)).Availability);
    }

    [Fact]
    public void NewReference_And_NewToken_HaveTheirFormats()
    {
        Assert.Matches(@"^AID-000042-[0-9A-F]{8}$", Checkout.NewReference(42));
        Assert.Matches(@"^AID-1234567-[0-9A-F]{8}$", Checkout.NewReference(1234567));
        Assert.Matches("^[0-9a-f]{32}$", Checkout.NewToken());
        Assert.NotEqual(Checkout.NewToken(), Checkout.NewToken());
    }

    [Fact]
    public void Validator_AcceptsAValidRequestForEachMethod()
    {
        var validator = new CheckoutRequestValidator();

        Assert.True(validator.Validate(Request("Pickup")).IsValid);
        Assert.True(validator.Validate(Request("Shipping")).IsValid);
        Assert.True(validator.Validate(Request("HomeDelivery")).IsValid);
    }

    public static TheoryData<string, string> InvalidRequests => new()
    {
        { "homeWithoutDistance", nameof(CheckoutRequest.WithinDistance) },
        { "shippingWithoutPostalCode", nameof(CheckoutRequest.PostalCode) },
        { "shortPostalCode", nameof(CheckoutRequest.PostalCode) },
        { "termsNotAccepted", nameof(CheckoutRequest.AcceptTerms) },
        { "shortPhone", nameof(CheckoutRequest.Phone) },
        { "longName", nameof(CheckoutRequest.Name) },
        { "unknownMethod", nameof(CheckoutRequest.DeliveryMethod) },
        { "numericMethod", nameof(CheckoutRequest.DeliveryMethod) },
        { "unknownLocale", nameof(CheckoutRequest.Locale) },
        { "badEmail", nameof(CheckoutRequest.Email) },
        { "shippingWithoutStreet", nameof(CheckoutRequest.StreetAddress) },
        { "shippingWithoutCity", nameof(CheckoutRequest.City) },
        { "longPickupNote", nameof(CheckoutRequest.PickupNote) },
        { "emptySlug", nameof(CheckoutRequest.BuildSlug) }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Validator_RejectsTheNamedProperty(string variant, string property)
    {
        var request = variant switch
        {
            "homeWithoutDistance" => Request("HomeDelivery") with { WithinDistance = false },
            "shippingWithoutPostalCode" => Request("Shipping") with { PostalCode = null },
            "shortPostalCode" => Request("Shipping") with { PostalCode = "123" },
            "termsNotAccepted" => Request() with { AcceptTerms = false },
            "shortPhone" => Request() with { Phone = "123" },
            "longName" => Request() with { Name = new string('a', 121) },
            "unknownMethod" => Request() with { DeliveryMethod = "Drone" },
            "numericMethod" => Request() with { DeliveryMethod = "2" },
            "unknownLocale" => Request() with { Locale = "de" },
            "badEmail" => Request() with { Email = "not-an-email" },
            "shippingWithoutStreet" => Request("Shipping") with { StreetAddress = " " },
            "shippingWithoutCity" => Request("Shipping") with { City = null },
            "longPickupNote" => Request() with { PickupNote = new string('a', 501) },
            "emptySlug" => Request() with { BuildSlug = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };

        var result = new CheckoutRequestValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }

    [Fact]
    public void Validator_LeavesTheAddressOptional_ForPickup()
    {
        var result = new CheckoutRequestValidator().Validate(Request("Pickup") with { PostalCode = "abc" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Environment_CountsAMissingSettingsRowAsTest_AndCachesTheAnswer()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        await using var provider = services.BuildServiceProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var environment = new ShopVippsEnvironment(cache, provider.GetRequiredService<IServiceScopeFactory>());

        Assert.True(await environment.IsTestAsync(Ct));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ShopSettings.Add(new ShopSettings { VippsTestMode = false });
            await db.SaveChangesAsync(Ct);
        }

        Assert.True(await environment.IsTestAsync(Ct));
        cache.Remove(ShopSettingsEndpoints.VippsTestModeCacheKey);
        Assert.False(await environment.IsTestAsync(Ct));
    }

    [Fact]
    public void AddShopVipps_UsesTheShopEnvironment_AndAFifteenSecondTimeout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vipps:ClientId"] = "live-id", ["Vipps:TestClientId"] = "test-id" })
            .Build();

        services.AddShopVipps(config);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ShopVippsEnvironment>(provider.GetRequiredService<IVippsEnvironment>());
        var options = provider.GetRequiredService<IOptions<VippsOptions>>().Value;
        Assert.Equal("altinnendata", options.SystemName);
        Assert.False(string.IsNullOrEmpty(options.SystemVersion));
        Assert.Equal("live-id", options.Live.ClientId);
        Assert.Equal("test-id", options.Test.ClientId);

        var clientOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(nameof(IVippsClient));
        using var client = new HttpClient();
        foreach (var action in clientOptions.HttpClientActions) action(client);
        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
    }

    [Fact]
    public async Task Checkout_AddsADeliveryLine_ForTheSmallestPaidDelivery()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, editDelivery: options =>
        {
            var shipping = options.Single(o => o.Method == DeliveryMethod.Shipping);
            shipping.Enabled = true;
            shipping.PriceNok = 1;
        });
        var captured = new List<VippsCreatePaymentRequest>();

        await Run(db, Vipps(captured), Request("Shipping"));

        var request = Assert.Single(captured);
        Assert.Equal(2, request.ReceiptLines.Count);
        Assert.Equal(100, request.ReceiptLines[1].UnitPriceInOre);
        Assert.Equal(request.AmountInOre, request.ReceiptLines.Sum(l => l.UnitPriceInOre * l.Quantity));
    }

    [Fact]
    public async Task Checkout_WithTheCurrentTermsVersion_StoresTheTermsAsTheyRead_TheVersion_AndTheCutIp()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await AddTermsAsync(db, "no", "## Vilkår\n\nNorsk.");
        var en = await AddTermsAsync(db, "en", "## Terms\n\nEnglish.", new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc));

        var result = await Run(db, Vipps(), Request() with { TermsVersion = "2026-10-02T09:30:00.000000Z" }, ip: "203.0.113.77");

        Assert.IsType<Ok<CheckoutResponse>>(result);
        var order = await OnlyOrderAsync(postgres, db);
        Assert.Equal("2026-10-02T09:30:00.000000Z", order.TermsVersion);
        Assert.Equal("Terms of Service", order.TermsTitle);
        Assert.Equal("## Terms\n\nEnglish.", order.TermsMarkdown);
        Assert.Equal("en", order.TermsLocale);
        Assert.Equal("203.0.113.0", order.TermsAcceptedIp);

        // Editing the page afterwards does not change what the buyer accepted.
        en.BodyMarkdown = "Changed.";
        await db.SaveChangesAsync(Ct);
        Assert.Equal("## Terms\n\nEnglish.", (await OnlyOrderAsync(postgres, db)).TermsMarkdown);
    }

    [Fact]
    public async Task Checkout_InEnglish_WithoutEnglishTerms_UsesTheNorwegianPage()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await AddTermsAsync(db, "no", "Norsk tekst.");

        var result = await Run(db, Vipps(), Request() with { TermsVersion = "2026-10-01T08:00:00.000000Z" });

        Assert.IsType<Ok<CheckoutResponse>>(result);
        var order = await OnlyOrderAsync(postgres, db);
        Assert.Equal("no", order.TermsLocale);
        Assert.Equal("Norsk tekst.", order.TermsMarkdown);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("2026-10-01T08:00:00.000001Z")]
    [InlineData("2026-10-01T08:00:00Z")]
    public async Task Checkout_WithAnotherTermsVersion_Returns409TermsChanged_AndHoldsNothing(string version)
    {
        await using var db = await postgres.NewContextAsync();
        var build = await SeedAsync(db);
        await AddTermsAsync(db, "en", "Text.");
        var vipps = Vipps();

        var result = await Run(db, vipps, Request() with { TermsVersion = version });

        AssertProblem(result, StatusCodes.Status409Conflict, "termsChanged");
        Assert.Equal(0, await CountOrdersAsync(db));
        Assert.Equal(BuildAvailability.Available, (await ReloadBuildAsync(db, build.Id)).Availability);
        vipps.Verify(v => v.CreatePaymentAsync(It.IsAny<VippsCreatePaymentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checkout_WithoutATermsPage_AcceptsTheSentVersion_AndStoresNoSnapshot()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);

        var result = await Run(db, Vipps(), Request() with { TermsVersion = "whatever-the-app-saw" });

        Assert.IsType<Ok<CheckoutResponse>>(result);
        var order = await OnlyOrderAsync(postgres, db);
        Assert.Equal("whatever-the-app-saw", order.TermsVersion);
        Assert.Null(order.TermsMarkdown);
        Assert.Null(order.TermsTitle);
        Assert.Null(order.TermsLocale);
        Assert.Null(order.TermsAcceptedIp);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validator_RequiresTheTermsVersion(string? version)
    {
        var result = new CheckoutRequestValidator().Validate(Request() with { TermsVersion = version! });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CheckoutRequest.TermsVersion));
    }

    [Fact]
    public void Validator_RefusesATermsVersionOver40Characters()
    {
        var validator = new CheckoutRequestValidator();

        Assert.True(validator.Validate(Request() with { TermsVersion = new string('a', 40) }).IsValid);
        Assert.Contains(validator.Validate(Request() with { TermsVersion = new string('a', 41) }).Errors,
            e => e.PropertyName == nameof(CheckoutRequest.TermsVersion));
    }

    [Fact]
    public void NewReference_FitsVippsReferenceRules()
    {
        var reference = Checkout.NewReference(1);

        Assert.InRange(reference.Length, 8, 64);
        Assert.Matches("^[A-Za-z0-9-]+$", reference);
    }
}
