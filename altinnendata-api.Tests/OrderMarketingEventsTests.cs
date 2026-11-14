using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SjolystInnovation.AppKit.Consent;
using altinnendata_api.Features.Marketing;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>The server-side Purchase events to Meta and TikTok, against fake platforms that record each request.</summary>
[Collection("postgres")]
public class OrderMarketingEventsTests(PostgresDatabase postgres) : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string MetaPixel = "1234567890";
    private const string MetaToken = "EAAGmetaSecretTokenValue0123456789";
    private const string TikTokPixel = "BRU69P41M9ALI4R4B2QG";
    private const string TikTokToken = "tiktokSecretTokenValue0123456789";
    private const string Email = " Ola.Nordmann@Example.NO ";
    private const string ConsentId = "3f2a6c1e-0b7d-4e8a-9c55-1d2e3f4a5b6c";
    private const string Reference = "AID-000042-1A2B3C4D";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Test/1.0";

    private static readonly DateTime AuthorizedAt = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = AuthorizedAt.AddHours(1);

    private sealed class FakePlatforms : HttpMessageHandler
    {
        public List<(Uri Uri, string? AccessToken, string? ContentType, string Body)> Requests { get; } = [];
        public Func<HttpResponseMessage> Meta { get; set; } = () => Json(HttpStatusCode.OK, """{"events_received":1,"fbtrace_id":"x"}""");
        public Func<HttpResponseMessage> TikTok { get; set; } = () => Json(HttpStatusCode.OK, """{"code":0,"message":"OK","request_id":"r","data":{}}""");
        public Exception? Throw { get; set; }

        public IEnumerable<(Uri Uri, string? AccessToken, string? ContentType, string Body)> To(string host) =>
            Requests.Where(r => r.Uri.Host == host);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var token = request.Headers.TryGetValues("Access-Token", out var values) ? values.Single() : null;
            lock (Requests) Requests.Add((request.RequestUri!, token, request.Content.Headers.ContentType?.MediaType, body));
            if (Throw != null) throw Throw;
            return request.RequestUri!.Host == "meta.test" ? Meta() : TikTok();
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class LogSink : ILoggerProvider
    {
        public List<(string Category, string Message)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        public string All => string.Join("\n", Lines.Select(l => l.Category + ": " + l.Message));

        private sealed class Logger(LogSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink.Lines) sink.Lines.Add((category, formatter(state, exception) + (exception == null ? "" : " " + exception)));
            }
        }
    }

    private readonly FakePlatforms _fake = new();
    private readonly LogSink _logs = new();

    private ServiceProvider Services(ApplicationDbContext db, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Site:BaseUrl"] = "https://www.altinnendata.no",
            ["Marketing:MetaBaseUrl"] = "https://meta.test/v27.0",
            ["Marketing:TikTokBaseUrl"] = "https://tiktok.test/open_api/v1.3/",
        };
        foreach (var (k, v) in extra ?? []) values[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        services.AddOrderMarketingEvents(config);
        services.AddHttpClient<MetaConversionsClient>().ConfigurePrimaryHttpMessageHandler(() => _fake);
        services.AddHttpClient<TikTokEventsClient>().ConfigurePrimaryHttpMessageHandler(() => _fake);
        return services.BuildServiceProvider();
    }

    private static async Task<int> RunAsync(ServiceProvider services, DateTime now)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderMarketingEvents>().RunAsync(now, Ct);
    }

    private static async Task SettingsAsync(ApplicationDbContext db, bool meta = true, bool tikTok = true)
    {
        await ShopSeed.EnsureAsync(db, Ct);
        var settings = await db.ShopSettings.SingleAsync(Ct);
        settings.MetaPixelId = MetaPixel;
        settings.MetaCapiToken = meta ? MetaToken : null;
        settings.TikTokPixelId = TikTokPixel;
        settings.TikTokEventsToken = tikTok ? TikTokToken : null;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task ConsentAsync(ApplicationDbContext db, bool marketing, DateTime decidedAt)
    {
        db.Set<ConsentRecord>().Add(new ConsentRecord
        {
            ConsentId = Guid.Parse(ConsentId),
            Marketing = marketing,
            NoticeVersion = "2026-10-24",
            SchemaVersion = 1,
            DecidedAt = decidedAt,
            Source = ConsentSources.Banner
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<int> SeedAsync(ApplicationDbContext db, Action<Order>? edit = null, bool meta = true, bool tikTok = true,
        DateTime? consentAt = null)
    {
        await SettingsAsync(db, meta, tikTok);
        await ConsentAsync(db, true, consentAt ?? AuthorizedAt.AddDays(-3));
        var order = new Order
        {
            Reference = Reference,
            VippsReference = Reference,
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = OrderStatus.Reserved,
            Locale = "no",
            BuyerName = "Ola Nordmann",
            BuyerEmail = Email,
            BuyerPhone = "4791234567",
            StreetAddress = "Storgata 1",
            PostalCode = "0155",
            City = "Oslo",
            DeliveryMethod = DeliveryMethod.Shipping,
            DeliveryLabel = "Sending",
            DeliveryPriceInOre = 50_050,
            BuildTitle = "Gaming-PC",
            BuildSlug = "gaming-pc",
            BuildPriceInOre = 1_299_000,
            TotalInOre = 1_349_050,
            TermsAcceptedAt = AuthorizedAt.AddMinutes(-5),
            CreatedAt = AuthorizedAt.AddMinutes(-5),
            AuthorizedAt = AuthorizedAt,
            MarketingConsentId = ConsentId,
            MarketingNoticeVersion = "2026-10-24",
            Fbp = "fb.1.1558571054389.1098115397",
            Fbc = "fb.1.1554763741205.AbCdEf",
            Ttp = "ttp-cookie-value",
            Ttclid = "E.C.P.ttclid",
            ClientIp = "203.0.113.7",
            ClientUserAgent = UserAgent,
            EventSourceUrl = "https://www.altinnendata.no/no/shop/checkout/gaming-pc",
            MarketingEventsDueBy = AuthorizedAt.AddMinutes(-5).AddDays(7),
        };
        edit?.Invoke(order);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        return order.Id;
    }

    private async Task<Order> ReadAsync(ApplicationDbContext db)
    {
        await using var fresh = postgres.NewContextOn(db);
        return await fresh.Orders.AsNoTracking().SingleAsync(Ct);
    }

    private static void AssertJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), "Expected\n" + expected + "\nbut got\n" + actual);

    private void AssertNoSecretsLogged()
    {
        var all = _logs.All;
        Assert.NotEmpty(_logs.Lines);
        foreach (var secret in new[] { MetaToken, TikTokToken, Email.Trim(), Email.Trim().ToLowerInvariant(), "4791234567", "203.0.113.7", "ttp-cookie-value", "fb.1.1558571054389" })
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_logs.Lines, l => l.Category.StartsWith("System.Net.Http", StringComparison.Ordinal));
    }

    private static void AssertPersonalColumnsCleared(Order o)
    {
        Assert.Null(o.Fbp);
        Assert.Null(o.Fbc);
        Assert.Null(o.Ttp);
        Assert.Null(o.Ttclid);
        Assert.Null(o.ClientIp);
        Assert.Null(o.ClientUserAgent);
        Assert.Null(o.EventSourceUrl);
    }

    [Fact]
    public void Hashing_MatchesThePlatformsDocumentedValues()
    {
        // Meta customer information parameters.
        Assert.Equal("62a14e44f765419d10fea99367361a727c12365e2520f32218d505ed9aa0f62f", PurchaseEvents.Sha256(PurchaseEvents.NormalizeEmail("John_Smith@gmail.com")!));
        Assert.Equal("79adb2a2fce5c6ba215fe5f27f532d4e7edbac4b6a5e09e1ef3a08084a904621", PurchaseEvents.Sha256("us"));
        // TikTok user parameters.
        Assert.Equal("848a771458438fc2ec420560d769fb9b9b86851ee338ec56517baabd79d3bb4f", PurchaseEvents.Sha256(PurchaseEvents.NormalizeEmail(" ALICE_abc@gmail.com ")!));
        Assert.Equal("9f7ec22d72092cd3c0b58726ed9c2d91b92e51a3f29837508fb2948bb22dd2fd", PurchaseEvents.Sha256("+12133734253"));
        Assert.Equal("383f7fffd99f3b1c3066086bcf991eae7ad543589d12bd792ecb6dc8b5acebc1", PurchaseEvents.Sha256(PurchaseEvents.NormalizeZip("95110")!));
    }

    [Theory]
    [InlineData("4791234567", "4791234567")]
    [InlineData("+47 912 34 567", "4791234567")]
    [InlineData("912 34 567", "4791234567")]
    [InlineData("", null)]
    [InlineData("4691234567", null)]
    [InlineData("12345", null)]
    public void NormalizePhone_IsNorwegian47AndEightDigits(string raw, string? expected)
    {
        Assert.Equal(expected, PurchaseEvents.NormalizePhone(raw));
    }

    [Theory]
    [InlineData("Oslo", "oslo")]
    [InlineData(" Mo i Rana ", "moirana")]
    [InlineData("Ålesund", "ålesund")]
    [InlineData("St. Hanshaugen", "sthanshaugen")]
    [InlineData("  ", null)]
    public void NormalizeCity_IsLowercaseLettersAndDigits(string raw, string? expected)
    {
        Assert.Equal(expected, PurchaseEvents.NormalizeCity(raw));
    }

    [Theory]
    [InlineData("Ålesund", "alesund")]
    [InlineData("Tromsø", "tromso")]
    [InlineData("Bærum", "baerum")]
    [InlineData("St. Hanshaugen", "sthanshaugen")]
    [InlineData("Čačak", "aak")]
    [InlineData("ÆØÅ", "aeoa")]
    [InlineData("  ", null)]
    public void NormalizeTikTokCity_FoldsNorwegianLetters_AndKeepsOnlyAscii(string raw, string? expected)
    {
        Assert.Equal(expected, PurchaseEvents.NormalizeTikTokCity(raw));
    }

    [Fact]
    public async Task ACityWithNorwegianLetters_IsFoldedForTikTok_AndKeptAsUtf8ForMeta()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.City = "Ålesund");
        await using var services = Services(db);

        await RunAsync(services, Now);

        var meta = JsonNode.Parse(Assert.Single(_fake.To("meta.test")).Body)!;
        Assert.Equal(PurchaseEvents.Sha256("ålesund"), (string?)meta["data"]![0]!["user_data"]!["ct"]![0]);
        var tikTok = JsonNode.Parse(Assert.Single(_fake.To("tiktok.test")).Body)!;
        Assert.Equal("alesund", (string?)tikTok["data"]![0]!["user"]!["city"]);
    }

    [Fact]
    public async Task SendsTheExactPayloads_ToBothPlatforms_ThenClearsTheIds()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db, new() { ["Marketing:MetaTestEventCode"] = "TEST123", ["Marketing:TikTokTestEventCode"] = "TEST456" });

        Assert.Equal(2, await RunAsync(services, Now));

        var meta = Assert.Single(_fake.To("meta.test"));
        Assert.Equal($"https://meta.test/v27.0/{MetaPixel}/events", meta.Uri.AbsoluteUri);
        Assert.Empty(meta.Uri.Query);
        Assert.DoesNotContain(MetaToken, meta.Uri.ToString());
        Assert.Equal("application/json", meta.ContentType);
        Assert.Null(meta.AccessToken);
        AssertJson($$"""
            {
              "data": [{
                "event_name": "Purchase",
                "event_time": 1794657600,
                "action_source": "website",
                "event_source_url": "https://www.altinnendata.no/no/shop/checkout/gaming-pc",
                "event_id": "{{Reference}}",
                "user_data": {
                  "em": ["12b86616c4899c10b877db3368dcec1ce42cbc58e89ed89e848585ac52229c2e"],
                  "ph": ["8e691d17cd72d7f630c95c400ce73b503202c6d5102af569c3a8546e7f72f21c"],
                  "zp": ["58df228579cf74c88c07f3898571b38b6c11104f203970d7ad995c0bfccd0293"],
                  "ct": ["8e016391c88c77d6773deab343b91f53bb64bfbc9ca18101844cb84c3d01e561"],
                  "country": ["9390298f3fb0c5b160498935d79cb139aef28e1c47358b4bbba61862b9c26e59"],
                  "client_ip_address": "203.0.113.7",
                  "client_user_agent": "{{UserAgent}}",
                  "fbp": "fb.1.1558571054389.1098115397",
                  "fbc": "fb.1.1554763741205.AbCdEf"
                },
                "custom_data": {
                  "value": 13490.5,
                  "currency": "NOK",
                  "content_ids": ["gaming-pc"],
                  "content_type": "product",
                  "contents": [{ "id": "gaming-pc", "quantity": 1 }]
                }
              }],
              "test_event_code": "TEST123",
              "access_token": "{{MetaToken}}"
            }
            """, meta.Body);

        var tikTok = Assert.Single(_fake.To("tiktok.test"));
        Assert.Equal("https://tiktok.test/open_api/v1.3/event/track/", tikTok.Uri.AbsoluteUri);
        Assert.Equal(TikTokToken, tikTok.AccessToken);
        Assert.Equal("application/json", tikTok.ContentType);
        AssertJson($$"""
            {
              "event_source": "web",
              "event_source_id": "{{TikTokPixel}}",
              "data": [{
                "event": "Purchase",
                "event_time": 1794657600,
                "event_id": "{{Reference}}",
                "user": {
                  "email": "12b86616c4899c10b877db3368dcec1ce42cbc58e89ed89e848585ac52229c2e",
                  "phone": "ffcbee1d17948bbad790c0b467ce90267dc1d9f9f4494f28451642fee49ff9a9",
                  "zip_code": "58df228579cf74c88c07f3898571b38b6c11104f203970d7ad995c0bfccd0293",
                  "city": "oslo",
                  "country": "no",
                  "ip": "203.0.113.7",
                  "user_agent": "{{UserAgent}}",
                  "ttp": "ttp-cookie-value",
                  "ttclid": "E.C.P.ttclid"
                },
                "properties": {
                  "contents": [{ "content_id": "gaming-pc", "quantity": 1, "price": 12990 }],
                  "content_type": "product",
                  "value": 13490.5,
                  "currency": "NOK"
                },
                "page": { "url": "https://www.altinnendata.no/no/shop/checkout/gaming-pc" }
              }],
              "test_event_code": "TEST456"
            }
            """, tikTok.Body);
        foreach (var body in new[] { meta.Body, tikTok.Body })
        {
            Assert.DoesNotContain("ola.nordmann", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ola Nordmann", body);
            Assert.DoesNotContain("Storgata", body);
        }

        var o = await ReadAsync(db);
        Assert.NotNull(o.MetaEventSentAt);
        Assert.NotNull(o.TikTokEventSentAt);
        Assert.Equal(1, o.MetaEventAttempts);
        Assert.Equal(1, o.TikTokEventAttempts);
        Assert.Equal(Now, o.MetaEventAttemptedAt);
        AssertPersonalColumnsCleared(o);
        Assert.Equal(ConsentId, o.MarketingConsentId);
        Assert.Equal("2026-10-24", o.MarketingNoticeVersion);
        Assert.Equal(Email, o.BuyerEmail);

        Assert.Equal(0, await RunAsync(services, Now.AddHours(1)));
        Assert.Equal(2, _fake.Requests.Count);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task WithoutTheTestCodes_OrAPageOrAPhone_SendsOnlyWhatItHas()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o =>
        {
            o.EventSourceUrl = null;
            o.BuyerPhone = "";
            o.PostalCode = null;
            o.City = null;
            o.ClientIp = null;
            o.Fbc = null;
            o.Ttclid = null;
            o.DeliveryMethod = DeliveryMethod.Pickup;
        });
        await using var services = Services(db);

        await RunAsync(services, Now);

        var meta = JsonNode.Parse(Assert.Single(_fake.To("meta.test")).Body)!.AsObject();
        Assert.False(meta.ContainsKey("test_event_code"));
        var metaEvent = meta["data"]![0]!;
        Assert.Equal("https://www.altinnendata.no/no/builds/gaming-pc", (string?)metaEvent["event_source_url"]);
        Assert.Equal(["em", "country", "client_user_agent", "fbp"], metaEvent["user_data"]!.AsObject().Select(p => p.Key));

        var tikTok = JsonNode.Parse(Assert.Single(_fake.To("tiktok.test")).Body)!.AsObject();
        Assert.False(tikTok.ContainsKey("test_event_code"));
        var tikTokEvent = tikTok["data"]![0]!;
        Assert.Equal("https://www.altinnendata.no/no/builds/gaming-pc", (string?)tikTokEvent["page"]!["url"]);
        Assert.Equal(["email", "country", "user_agent", "ttp"], tikTokEvent["user"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task AFailure_IsRetriedWithBackoff_AndTheLogsHoldNoSecrets()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        // Answers that echo the token and the email back, as an error message can.
        _fake.Meta = () => FakePlatforms.Json(HttpStatusCode.BadRequest,
            $$$"""{"error":{"message":"bad {{{MetaToken}}} {{{Email}}}","type":"OAuthException","code":4}}""");
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.OK, $$"""{"code":40002,"message":"Invalid {{TikTokToken}} {{Email}}"}""");

        Assert.Equal(0, await RunAsync(services, Now));
        var o = await ReadAsync(db);
        Assert.Equal(1, o.MetaEventAttempts);
        Assert.Equal(1, o.TikTokEventAttempts);
        Assert.Null(o.MetaEventSentAt);
        Assert.Null(o.TikTokEventSentAt);
        Assert.NotNull(o.Fbp);
        Assert.Contains(_logs.Lines, l => l.Message.Contains($"Meta refused the Purchase event of order {o.Id} on try 1 with HTTP 400 code 4"));
        Assert.Contains(_logs.Lines, l => l.Message.Contains($"TikTok refused the Purchase event of order {o.Id} on try 1 with HTTP 200 code 40002"));

        // Within the backoff nothing is tried.
        await RunAsync(services, Now.AddMinutes(4));
        Assert.Equal(2, _fake.Requests.Count);

        _fake.Throw = new HttpRequestException($"failed https://meta.test/v27.0/{MetaPixel}/events?access_token={MetaToken}");
        await RunAsync(services, Now.AddMinutes(6));
        Assert.Equal(4, _fake.Requests.Count);
        Assert.Contains(_logs.Lines, l => l.Message.Contains("failed on try 2 with HttpRequestException"));

        _fake.Throw = null;
        _fake.Meta = () => FakePlatforms.Json(HttpStatusCode.OK, """{"events_received":1}""");
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.OK, """{"code":0,"message":"OK","data":{}}""");
        Assert.Equal(2, await RunAsync(services, Now.AddMinutes(17)));
        o = await ReadAsync(db);
        Assert.Equal(3, o.MetaEventAttempts);
        Assert.NotNull(o.MetaEventSentAt);
        Assert.NotNull(o.TikTokEventSentAt);
        AssertPersonalColumnsCleared(o);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task AfterTheLastTry_NothingMoreIsSent_AndTheIdsAreCleared()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        _fake.Meta = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");

        var at = Now;
        for (var i = 0; i < OrderMarketingEvents.MaxAttempts + 2; i++)
        {
            await RunAsync(services, at);
            at += OrderMarketingEvents.Backoff(OrderMarketingEvents.MaxAttempts);
        }

        Assert.Equal(OrderMarketingEvents.MaxAttempts * 2, _fake.Requests.Count);
        var o = await ReadAsync(db);
        Assert.Equal(OrderMarketingEvents.MaxAttempts, o.MetaEventAttempts);
        Assert.Null(o.MetaEventSentAt);
        AssertPersonalColumnsCleared(o);
        Assert.Equal(ConsentId, o.MarketingConsentId);
    }

    [Fact]
    public async Task APlatformWithoutAToken_IsSkipped_AndSentOnceTheTokenIsAdded()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, meta: false);
        await using var services = Services(db);

        Assert.Equal(1, await RunAsync(services, Now));
        Assert.Empty(_fake.To("meta.test"));
        Assert.Single(_fake.To("tiktok.test"));
        var o = await ReadAsync(db);
        Assert.Equal(0, o.MetaEventAttempts);
        Assert.Null(o.MetaEventSentAt);
        Assert.NotNull(o.TikTokEventSentAt);
        // Meta can still send within the window, so the ids stay.
        Assert.NotNull(o.Fbp);

        (await db.ShopSettings.SingleAsync(Ct)).MetaCapiToken = MetaToken;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(1, await RunAsync(services, Now.AddDays(1)));
        Assert.Single(_fake.To("meta.test"));
        Assert.Single(_fake.To("tiktok.test"));
        AssertPersonalColumnsCleared(await ReadAsync(db));
    }

    [Fact]
    public async Task WithoutAUserAgent_MetaIsDoneForGood_AndTikTokStillSends()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.ClientUserAgent = null);
        await using var services = Services(db);

        Assert.Equal(1, await RunAsync(services, Now));

        Assert.Empty(_fake.To("meta.test"));
        var tikTok = JsonNode.Parse(Assert.Single(_fake.To("tiktok.test")).Body)!;
        Assert.False(tikTok["data"]![0]!["user"]!.AsObject().ContainsKey("user_agent"));
        var o = await ReadAsync(db);
        Assert.Equal(0, o.MetaEventAttempts);
        Assert.Null(o.MetaEventSentAt);
        Assert.NotNull(o.TikTokEventSentAt);
        // Meta can never send, so the order is finished and its ids are cleared at once.
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);

        Assert.Equal(0, await RunAsync(services, Now.AddHours(1)));
        Assert.Single(_fake.Requests);
    }

    [Fact]
    public async Task APlatformWithoutAPixelId_IsSkipped()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        (await db.ShopSettings.SingleAsync(Ct)).TikTokPixelId = null;
        await db.SaveChangesAsync(Ct);
        await using var services = Services(db);

        await RunAsync(services, Now);

        Assert.Single(_fake.To("meta.test"));
        Assert.Empty(_fake.To("tiktok.test"));
        Assert.Equal(0, (await ReadAsync(db)).TikTokEventAttempts);
    }

    [Fact]
    public async Task AWithdrawnConsent_SendsNothing_AndClearsTheIds()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await ConsentAsync(db, false, AuthorizedAt.AddMinutes(30));
        await using var services = Services(db);

        Assert.Equal(0, await RunAsync(services, Now));
        Assert.Empty(_fake.Requests);

        var o = await ReadAsync(db);
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);
        Assert.Equal(0, o.MetaEventAttempts);
        Assert.Equal(ConsentId, o.MarketingConsentId);

        await ConsentAsync(db, true, AuthorizedAt.AddMinutes(40));
        Assert.Equal(0, await RunAsync(services, Now.AddHours(1)));
        Assert.Empty(_fake.Requests);
    }

    [Fact]
    public async Task AConsentThatExpiresBeforeTheSend_SendsNothing_AndClearsTheIds()
    {
        await using var db = await postgres.NewContextAsync();
        // Valid at checkout, 365 days old and one minute by the time of the run.
        await SeedAsync(db, consentAt: Now.AddDays(-365).AddMinutes(-1));
        await using var services = Services(db);

        Assert.Equal(0, await RunAsync(services, Now));

        Assert.Empty(_fake.Requests);
        var o = await ReadAsync(db);
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);
    }

    [Fact]
    public async Task AWithdrawal_WhileNoPlatformIsDue_ClearsTheIdsAtOnce()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        _fake.Meta = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");
        await RunAsync(services, Now);
        Assert.Equal(2, _fake.Requests.Count);

        await ConsentAsync(db, false, Now.AddMinutes(1));
        await RunAsync(services, Now.AddMinutes(2));

        Assert.Equal(2, _fake.Requests.Count);
        var o = await ReadAsync(db);
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);
        Assert.Equal(1, o.MetaEventAttempts);
        Assert.Equal(ConsentId, o.MarketingConsentId);
    }

    [Fact]
    public async Task AWithdrawal_AfterTheMetaSend_StopsTheTikTokSend()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        _fake.Meta = () =>
        {
            // The buyer withdraws while the Meta call is in flight.
            using var other = postgres.NewContextOn(db);
            other.Set<ConsentRecord>().Add(new ConsentRecord
            {
                ConsentId = Guid.Parse(ConsentId), Marketing = false, NoticeVersion = "2026-10-24", SchemaVersion = 1,
                DecidedAt = Now.AddMinutes(-1), Source = ConsentSources.Settings
            });
            other.SaveChanges();
            return FakePlatforms.Json(HttpStatusCode.OK, """{"events_received":1}""");
        };

        Assert.Equal(1, await RunAsync(services, Now));

        Assert.Single(_fake.To("meta.test"));
        Assert.Empty(_fake.To("tiktok.test"));
        var o = await ReadAsync(db);
        Assert.NotNull(o.MetaEventSentAt);
        Assert.Null(o.TikTokEventSentAt);
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);
    }

    [Fact]
    public async Task AfterTheSevenDays_NothingIsSent_AndTheIdsAreCleared()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        var o = await ReadAsync(db);

        Assert.Equal(0, await RunAsync(services, o.MarketingEventsDueBy!.Value));

        Assert.Empty(_fake.Requests);
        o = await ReadAsync(db);
        AssertPersonalColumnsCleared(o);
        Assert.Equal(0, o.MetaEventAttempts);
        Assert.Equal(ConsentId, o.MarketingConsentId);
    }

    [Fact]
    public async Task AReservation48HoursOld_IsNotSent_AndItsIdsAreClearedAtOnce()
    {
        await using var db = await postgres.NewContextAsync();
        // Still inside the 7-day window after checkout, but past the platforms' 48-hour deduplication window.
        await SeedAsync(db, o => o.AuthorizedAt = Now.AddHours(-48));
        await using var services = Services(db);

        Assert.Equal(0, await RunAsync(services, Now));

        Assert.Empty(_fake.Requests);
        var o = await ReadAsync(db);
        Assert.Null(o.MarketingEventsDueBy);
        Assert.Equal(0, o.MetaEventAttempts);
        Assert.Equal(0, o.TikTokEventAttempts);
        Assert.Null(o.MetaEventSentAt);
        Assert.Null(o.TikTokEventSentAt);
        AssertPersonalColumnsCleared(o);
        Assert.Equal(ConsentId, o.MarketingConsentId);
    }

    [Fact]
    public async Task AReservation46HoursOld_IsStillSent()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.AuthorizedAt = Now.AddHours(-46));
        await using var services = Services(db);

        Assert.Equal(2, await RunAsync(services, Now));
    }

    [Fact]
    public async Task AllTries_FitInsideTheSendDeadline()
    {
        var waits = Enumerable.Range(1, OrderMarketingEvents.MaxAttempts - 1).Select(OrderMarketingEvents.Backoff).ToList();
        Assert.Equal([5, 10, 20, 40, 80], waits.Select(w => w.TotalMinutes));
        Assert.True(waits.Aggregate(TimeSpan.Zero, (a, b) => a + b) < MarketingCapture.SendDeadline);
        Assert.Equal(TimeSpan.FromHours(47), MarketingCapture.SendDeadline);

        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        _fake.Meta = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.InternalServerError, "oops");

        // Each run at the earliest moment the next try is due.
        var at = AuthorizedAt;
        for (var attempt = 1; attempt <= OrderMarketingEvents.MaxAttempts; attempt++)
        {
            await RunAsync(services, at);
            at += OrderMarketingEvents.Backoff(attempt);
        }

        Assert.Equal(OrderMarketingEvents.MaxAttempts * 2, _fake.Requests.Count);
        var o = await ReadAsync(db);
        Assert.Equal(OrderMarketingEvents.MaxAttempts, o.MetaEventAttempts);
        Assert.Equal(OrderMarketingEvents.MaxAttempts, o.TikTokEventAttempts);
        Assert.True(o.MetaEventAttemptedAt < AuthorizedAt + MarketingCapture.SendDeadline);
        Assert.True(o.TikTokEventAttemptedAt < AuthorizedAt + MarketingCapture.SendDeadline);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task OnlyReservedOrPaidOrders_AreSent(OrderStatus status)
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.Status = status);
        await using var services = Services(db);

        await RunAsync(services, Now);

        Assert.Empty(_fake.Requests);
    }

    [Fact]
    public async Task APaidOrder_IsSent()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.Status = OrderStatus.Paid);
        await using var services = Services(db);

        Assert.Equal(2, await RunAsync(services, Now));
    }

    [Fact]
    public async Task TestOrders_AreSkipped()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.IsTest = true);
        await using var services = Services(db);

        Assert.Equal(0, await RunAsync(services, Now));

        Assert.Empty(_fake.Requests);
    }

    [Fact]
    public async Task AnOrderWithoutConsent_IsSkipped()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o => o.MarketingConsentId = null);
        await using var services = Services(db);

        await RunAsync(services, Now);

        Assert.Empty(_fake.Requests);
    }

    [Fact]
    public async Task EachPlatformsSentState_IsItsOwn()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);
        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.TooManyRequests, """{"code":40100}""");

        Assert.Equal(1, await RunAsync(services, Now));
        var o = await ReadAsync(db);
        Assert.NotNull(o.MetaEventSentAt);
        Assert.Null(o.TikTokEventSentAt);
        Assert.Equal(1, o.TikTokEventAttempts);
        Assert.NotNull(o.MarketingEventsDueBy);

        _fake.TikTok = () => FakePlatforms.Json(HttpStatusCode.OK, """{"code":0}""");
        Assert.Equal(1, await RunAsync(services, Now.AddMinutes(6)));
        Assert.Single(_fake.To("meta.test"));
        Assert.Equal(2, _fake.To("tiktok.test").Count());
        o = await ReadAsync(db);
        Assert.Equal(1, o.MetaEventAttempts);
        Assert.Equal(2, o.TikTokEventAttempts);
        Assert.NotNull(o.TikTokEventSentAt);
    }

    [Fact]
    public async Task TwoRunsAtOnce_SendEachEventOnce()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db);
        await using var services = Services(db);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => RunAsync(services, Now), Ct)));

        Assert.Equal(2, results.Sum());
        Assert.Single(_fake.To("meta.test"));
        Assert.Single(_fake.To("tiktok.test"));
    }

    [Fact]
    public async Task CleanUp_ClearsOnlyThePersonalColumns_OfFinishedOrders()
    {
        await using var db = await postgres.NewContextAsync();
        await SeedAsync(db, o =>
        {
            o.MetaEventSentAt = Now;
            o.MetaEventAttempts = 1;
            o.TikTokEventSentAt = Now;
            o.TikTokEventAttempts = 2;
        });
        var openId = await SeedOpenAsync(db);
        await using var services = Services(db);
        using var scope = services.CreateScope();

        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrderMarketingEvents>().CleanUpAsync(Now, Ct));

        await using var fresh = postgres.NewContextOn(db);
        var done = await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id != openId, Ct);
        AssertPersonalColumnsCleared(done);
        Assert.Equal(ConsentId, done.MarketingConsentId);
        Assert.Equal("2026-10-24", done.MarketingNoticeVersion);
        Assert.Equal(Now, done.MetaEventSentAt);
        Assert.Equal(Now, done.TikTokEventSentAt);
        Assert.Equal(2, done.TikTokEventAttempts);
        Assert.Null(done.MarketingEventsDueBy);
        Assert.Equal(Email, done.BuyerEmail);
        Assert.Equal("Oslo", done.City);
        Assert.Equal("0155", done.PostalCode);

        var open = await fresh.Orders.AsNoTracking().SingleAsync(o => o.Id == openId, Ct);
        Assert.Equal("ttp-cookie-value", open.Ttp);
        Assert.Equal("203.0.113.7", open.ClientIp);
    }

    private static async Task AddOrdersAsync(ApplicationDbContext db, int count, Action<Order> edit)
    {
        for (var i = 0; i < count; i++)
        {
            var reference = $"AID-{100 + i:D6}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            var order = new Order
            {
                Reference = reference, VippsReference = reference,
                PublicToken = Guid.NewGuid().ToString("N"),
                Status = OrderStatus.Reserved, Locale = "no",
                BuyerName = "Kari", BuyerEmail = "kari@example.no", BuyerPhone = "4798765432",
                DeliveryLabel = "Henting", BuildTitle = "PC", BuildSlug = "pc",
                AuthorizedAt = AuthorizedAt, MarketingConsentId = ConsentId, ClientUserAgent = UserAgent,
                MarketingEventsDueBy = AuthorizedAt.AddDays(7),
            };
            edit(order);
            db.Orders.Add(order);
        }
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task OrdersWaitingOnABackoff_DoNotCrowdOutADueOrder()
    {
        await using var db = await postgres.NewContextAsync();
        await SettingsAsync(db);
        await ConsentAsync(db, true, AuthorizedAt.AddDays(-3));
        await AddOrdersAsync(db, OrderMarketingEvents.BatchSize + 10, o =>
        {
            o.MetaEventAttempts = 1;
            o.MetaEventNextAttemptAt = Now.AddHours(1);
            o.TikTokEventAttempts = 1;
            o.TikTokEventNextAttemptAt = Now.AddHours(1);
        });
        db.Orders.Add(DueOrder());
        await db.SaveChangesAsync(Ct);
        await using var services = Services(db);

        Assert.Equal(2, await RunAsync(services, Now));

        Assert.All(_fake.Requests, r => Assert.Contains(Reference, r.Body));
        Assert.Equal(2, _fake.Requests.Count);
    }

    [Fact]
    public async Task OrdersWaitingOnAPlatformThatIsNotSetUp_DoNotCrowdOutADueOrder()
    {
        await using var db = await postgres.NewContextAsync();
        await SettingsAsync(db, meta: false);
        await ConsentAsync(db, true, AuthorizedAt.AddDays(-3));
        await AddOrdersAsync(db, OrderMarketingEvents.BatchSize + 10, o =>
        {
            o.TikTokEventAttempts = 1;
            o.TikTokEventSentAt = Now.AddMinutes(-30);
        });
        db.Orders.Add(DueOrder());
        await db.SaveChangesAsync(Ct);
        await using var services = Services(db);

        Assert.Equal(1, await RunAsync(services, Now));

        var request = Assert.Single(_fake.Requests);
        Assert.Equal("tiktok.test", request.Uri.Host);
        Assert.Contains(Reference, request.Body);
    }

    [Fact]
    public async Task FinishedAndExpiredOrders_DoNotCrowdOutTheConsentRecheckOfALiveOrder()
    {
        await using var db = await postgres.NewContextAsync();
        await SettingsAsync(db);
        await ConsentAsync(db, true, AuthorizedAt.AddDays(-3));
        // Sent on both platforms with the ids cleared, but the due-by still set.
        await AddOrdersAsync(db, OrderMarketingEvents.ConsentBatchSize + 5, o =>
        {
            o.ClientUserAgent = null;
            o.MetaEventSentAt = AuthorizedAt;
            o.TikTokEventSentAt = AuthorizedAt;
        });
        // Past the send window, with ids not yet cleaned up.
        await AddOrdersAsync(db, OrderMarketingEvents.ConsentBatchSize + 5, o => o.MarketingEventsDueBy = Now.AddMinutes(-1));
        // Live, both platforms waiting on a backoff, so only the recheck can notice the withdrawal.
        var live = DueOrder();
        live.Fbp = "fb.1.1.1";
        live.MetaEventAttempts = 1;
        live.MetaEventNextAttemptAt = Now.AddHours(1);
        live.TikTokEventAttempts = 1;
        live.TikTokEventNextAttemptAt = Now.AddHours(1);
        db.Orders.Add(live);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        await ConsentAsync(db, false, Now.AddMinutes(-1));
        await using var services = Services(db);

        Assert.Equal(0, await RunAsync(services, Now));

        Assert.Empty(_fake.Requests);
        await using var fresh = postgres.NewContextOn(db);
        var o = await fresh.Orders.AsNoTracking().SingleAsync(x => x.Reference == Reference, Ct);
        AssertPersonalColumnsCleared(o);
        Assert.Null(o.MarketingEventsDueBy);
        Assert.Equal(0, await fresh.Orders.CountAsync(x => x.MarketingEventsDueBy != null, Ct));
    }

    [Fact]
    public async Task TheConsentRecheck_PagesOnFromRunToRun_UntilEveryLiveOrderIsChecked()
    {
        await using var db = await postgres.NewContextAsync();
        await SettingsAsync(db);
        await ConsentAsync(db, true, AuthorizedAt.AddDays(-3));
        await AddOrdersAsync(db, OrderMarketingEvents.ConsentBatchSize + 5, o =>
        {
            o.Fbp = "fb.1.1.1";
            o.MetaEventAttempts = 1;
            o.MetaEventNextAttemptAt = Now.AddHours(1);
            o.TikTokEventAttempts = 1;
            o.TikTokEventNextAttemptAt = Now.AddHours(1);
        });
        await ConsentAsync(db, false, Now.AddMinutes(-1));
        await using var services = Services(db);

        await RunAsync(services, Now);
        await using (var fresh = postgres.NewContextOn(db))
            Assert.Equal(5, await fresh.Orders.CountAsync(o => o.MarketingEventsDueBy != null, Ct));

        await RunAsync(services, Now.AddMinutes(5));
        await using (var fresh = postgres.NewContextOn(db))
            Assert.Equal(0, await fresh.Orders.CountAsync(o => o.MarketingEventsDueBy != null || o.Fbp != null, Ct));
        Assert.Empty(_fake.Requests);
    }

    private static Order DueOrder() => new()
    {
        Reference = Reference, VippsReference = Reference,
        PublicToken = Guid.NewGuid().ToString("N"),
        Status = OrderStatus.Reserved, Locale = "no",
        BuyerName = "Ola", BuyerEmail = Email, BuyerPhone = "4791234567",
        DeliveryLabel = "Henting", BuildTitle = "PC", BuildSlug = "pc",
        AuthorizedAt = AuthorizedAt, MarketingConsentId = ConsentId, ClientUserAgent = UserAgent,
        MarketingEventsDueBy = AuthorizedAt.AddDays(7),
    };

    private static async Task<int> SeedOpenAsync(ApplicationDbContext db)
    {
        var order = new Order
        {
            Reference = "AID-000043-AAAAAAAA",
            VippsReference = "AID-000043-AAAAAAAA",
            PublicToken = Guid.NewGuid().ToString("N"),
            Status = OrderStatus.Reserved,
            Locale = "no",
            BuyerName = "Kari",
            BuyerEmail = "kari@example.no",
            BuyerPhone = "4798765432",
            DeliveryLabel = "Henting",
            BuildTitle = "PC",
            BuildSlug = "pc",
            AuthorizedAt = AuthorizedAt,
            MarketingConsentId = ConsentId,
            Ttp = "ttp-cookie-value",
            ClientIp = "203.0.113.7",
            MarketingEventsDueBy = AuthorizedAt.AddDays(7),
            MetaEventSentAt = Now,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        return order.Id;
    }

    [Fact]
    public async Task TheService_LogsATimeout_WhenItIsNotStopping_AndStopsQuietlyWhenItIs()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OrderMarketingEvents>(_ => throw new TaskCanceledException("timed out"));
        await using var provider = services.BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<OrderMarketingEventsService>(provider);

        await service.RunOnceAsync(CancellationToken.None);
        Assert.Contains(_logs.Lines, l => l.Message.Contains("the Purchase event run failed with TaskCanceledException"));

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => service.RunOnceAsync(stopping.Token));
    }

    [Fact]
    public void TheService_IsRegistered_WithClientsThatLogNothing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrderMarketingEvents(config);
        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is OrderMarketingEventsService);
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        Assert.Equal(new Uri("https://graph.facebook.com/v27.0/"), factory.CreateClient(nameof(MetaConversionsClient)).BaseAddress);
        Assert.Equal(new Uri("https://business-api.tiktok.com/open_api/v1.3/"), factory.CreateClient(nameof(TikTokEventsClient)).BaseAddress);
    }
}
