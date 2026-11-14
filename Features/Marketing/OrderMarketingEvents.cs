using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Marketing
{
    public enum MarketingPlatform { Meta, TikTok }

    /// <summary>
    /// Sends the server-side Purchase event of each reserved or paid order whose buyer consented to marketing, to Meta
    /// and to TikTok, each on its own. A platform without its pixel ID or token is skipped and tried again on a later
    /// run. No event is sent later than <see cref="MarketingCapture.SendDeadline"/> after the reservation, inside the
    /// platforms' deduplication window, nor after <see cref="Order.MarketingEventsDueBy"/>. Each try is claimed on the
    /// order first, so two instances never send the same try. Each run reads the consent log again for every order
    /// that can still send, and once more right before each call. A withdrawn or expired consent clears the stored
    /// ids. Once both platforms are done, or either deadline has passed, the ids, IP address, user agent and page are
    /// cleared.
    /// </summary>
    public class OrderMarketingEvents(
        ApplicationDbContext db,
        MetaConversionsClient meta,
        TikTokEventsClient tikTok,
        IConfiguration config,
        ConsentRecheckCursor cursor,
        ILogger<OrderMarketingEvents> log)
    {
        /// <summary>How many tries each platform gets, the first one included.</summary>
        public const int MaxAttempts = 6;

        /// <summary>The most orders one run looks at.</summary>
        public const int BatchSize = 50;

        /// <summary>The wait after try <paramref name="attempts"/> before the next one: 5, 10, 20 minutes and so on.</summary>
        public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Max(0, attempts - 1)));

        /// <summary>Sends the due events, then clears the ids of finished orders. Returns how many events were accepted.</summary>
        public async Task<int> RunAsync(DateTime now, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            var metaReady = !string.IsNullOrEmpty(settings?.MetaPixelId) && !string.IsNullOrEmpty(settings.MetaCapiToken);
            var tikTokReady = !string.IsNullOrEmpty(settings?.TikTokPixelId) && !string.IsNullOrEmpty(settings.TikTokEventsToken);

            await WithdrawRevokedAsync(now, ct);

            var reservedAfter = now - MarketingCapture.SendDeadline;
            var ids = await db.Orders.AsNoTracking()
                .Where(o => (o.Status == OrderStatus.Reserved || o.Status == OrderStatus.Paid)
                    && !o.IsTest
                    && o.AnonymisedAt == null
                    && o.MarketingConsentId != null
                    && o.MarketingEventsDueBy > now
                    && o.AuthorizedAt > reservedAfter
                    // Only orders with a platform that is set up and due, so orders waiting on a backoff never fill the batch.
                    && ((metaReady && o.MetaEventSentAt == null && o.MetaEventAttempts < MaxAttempts
                            && (o.MetaEventNextAttemptAt == null || o.MetaEventNextAttemptAt <= now))
                        || (tikTokReady && o.TikTokEventSentAt == null && o.TikTokEventAttempts < MaxAttempts
                            && (o.TikTokEventNextAttemptAt == null || o.TikTokEventNextAttemptAt <= now))))
                .OrderBy(o => o.Id)
                .Select(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            var accepted = 0;
            foreach (var id in ids)
            {
                var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == id, ct);
                var sendMeta = metaReady && IsDue(order.MetaEventSentAt, order.MetaEventAttempts, order.MetaEventNextAttemptAt, now);
                var sendTikTok = tikTokReady && IsDue(order.TikTokEventSentAt, order.TikTokEventAttempts, order.TikTokEventNextAttemptAt, now);
                if (!sendMeta && !sendTikTok) continue;

                if (sendMeta && await TrySendAsync(MarketingPlatform.Meta, order, settings!, now, ct)) accepted++;
                if (sendTikTok && await TrySendAsync(MarketingPlatform.TikTok, order, settings!, now, ct)) accepted++;
            }

            await CleanUpAsync(now, ct);
            return accepted;
        }

        private static bool IsDue(DateTime? sentAt, int attempts, DateTime? nextAttemptAt, DateTime now) =>
            sentAt == null && attempts < MaxAttempts && (nextAttemptAt == null || nextAttemptAt <= now);

        /// <summary>Claims and makes one try for <paramref name="platform"/>. Returns true when the platform accepted the event.</summary>
        private async Task<bool> TrySendAsync(MarketingPlatform platform, Order order, ShopSettings settings, DateTime now, CancellationToken ct)
        {
            var seen = platform == MarketingPlatform.Meta ? order.MetaEventAttempts : order.TikTokEventAttempts;
            var reservedAfter = now - MarketingCapture.SendDeadline;
            var claim = db.Orders.Where(o => o.Id == order.Id && o.AnonymisedAt == null && o.MarketingConsentId != null
                && o.MarketingEventsDueBy > now && o.AuthorizedAt > reservedAfter);
            var next = now + Backoff(seen + 1);
            var claimed = platform == MarketingPlatform.Meta
                ? await claim.Where(o => o.MetaEventSentAt == null && o.MetaEventAttempts == seen)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.MetaEventAttempts, seen + 1)
                        .SetProperty(o => o.MetaEventAttemptedAt, now)
                        .SetProperty(o => o.MetaEventNextAttemptAt, next), ct)
                : await claim.Where(o => o.TikTokEventSentAt == null && o.TikTokEventAttempts == seen)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.TikTokEventAttempts, seen + 1)
                        .SetProperty(o => o.TikTokEventAttemptedAt, now)
                        .SetProperty(o => o.TikTokEventNextAttemptAt, next), ct);
            if (claimed == 0) return false;
            var attempt = seen + 1;

            // The buyer can withdraw the consent at any time, so it is read again right before the call.
            if (!await ConsentHoldsAsync(order.Id, order.MarketingConsentId!, now, ct)) return false;

            PlatformResult result;
            try
            {
                result = platform == MarketingPlatform.Meta
                    ? await meta.SendAsync(settings.MetaPixelId!, settings.MetaCapiToken!,
                        PurchaseEvents.MetaBody(order, config, NullIfEmpty(config["Marketing:MetaTestEventCode"])), ct)
                    : await tikTok.SendAsync(settings.TikTokEventsToken!,
                        PurchaseEvents.TikTokBody(order, settings.TikTokPixelId!, config, NullIfEmpty(config["Marketing:TikTokTestEventCode"])), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Only the exception type: its message can carry the request address, and the Meta address holds the token.
                log.LogWarning("Marketing: the {Platform} Purchase event of order {OrderId} failed on try {Attempt} with {Error}",
                    platform, order.Id, attempt, ex.GetType().Name);
                return false;
            }

            if (!result.Accepted)
            {
                log.LogWarning("Marketing: {Platform} refused the Purchase event of order {OrderId} on try {Attempt} with HTTP {Status} code {Code}",
                    platform, order.Id, attempt, result.Status, result.Code);
                return false;
            }

            // The platform has the event, so an aborted run must not lose the record of it.
            var sent = DateTime.UtcNow;
            if (platform == MarketingPlatform.Meta)
                await db.Orders.Where(o => o.Id == order.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.MetaEventSentAt, sent), CancellationToken.None);
            else
                await db.Orders.Where(o => o.Id == order.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.TikTokEventSentAt, sent), CancellationToken.None);
            log.LogInformation("Marketing: {Platform} accepted the Purchase event of order {OrderId} on try {Attempt}", platform, order.Id, attempt);
            return true;
        }

        /// <summary>The most orders one run checks for a withdrawn consent.</summary>
        public const int ConsentBatchSize = 50;

        /// <summary>
        /// Reads the consent log for one page of live orders, due or not, and clears the ids of each order whose
        /// consent no longer allows marketing or is older than <see cref="MarketingCapture.ConsentMaxAge"/>. Live means
        /// the send window is open and the order still holds an id or can still send. Each run goes on after the last
        /// order of the previous page, so every live order is reached.
        /// </summary>
        private async Task WithdrawRevokedAsync(DateTime now, CancellationToken ct)
        {
            var afterId = cursor.AfterId;
            var page = await db.Orders.AsNoTracking()
                .Where(o => o.Id > afterId
                    && o.AnonymisedAt == null
                    && o.MarketingConsentId != null
                    && o.MarketingEventsDueBy > now
                    && (o.Fbp != null || o.Fbc != null || o.Ttp != null || o.Ttclid != null
                        || o.ClientIp != null || o.ClientUserAgent != null || o.EventSourceUrl != null
                        || (o.MetaEventSentAt == null && o.MetaEventAttempts < MaxAttempts)
                        || (o.TikTokEventSentAt == null && o.TikTokEventAttempts < MaxAttempts)))
                .OrderBy(o => o.Id)
                .Select(o => new { o.Id, o.MarketingConsentId })
                .Take(ConsentBatchSize)
                .ToListAsync(ct);
            cursor.AfterId = page.Count < ConsentBatchSize ? 0 : page[^1].Id;
            foreach (var o in page) await ConsentHoldsAsync(o.Id, o.MarketingConsentId!, now, ct);
        }

        /// <summary>True when consent <paramref name="consentId"/> of order <paramref name="orderId"/> still allows marketing. Otherwise clears the order's ids.</summary>
        private async Task<bool> ConsentHoldsAsync(int orderId, string consentId, DateTime now, CancellationToken ct)
        {
            if (await MarketingCapture.ValidConsentAsync(db, consentId, now, ct) != null) return true;
            await WithdrawAsync(orderId, ct);
            log.LogInformation("Marketing: the consent of order {OrderId} no longer allows marketing, no Purchase event is sent", orderId);
            return false;
        }

        /// <summary>Clears the stored ids and the send window of an order whose consent was withdrawn, so nothing is sent.</summary>
        private Task<int> WithdrawAsync(int orderId, CancellationToken ct) =>
            db.Orders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Fbp, (string?)null)
                    .SetProperty(o => o.Fbc, (string?)null)
                    .SetProperty(o => o.Ttp, (string?)null)
                    .SetProperty(o => o.Ttclid, (string?)null)
                    .SetProperty(o => o.ClientIp, (string?)null)
                    .SetProperty(o => o.ClientUserAgent, (string?)null)
                    .SetProperty(o => o.EventSourceUrl, (string?)null)
                    .SetProperty(o => o.MarketingEventsDueBy, (DateTime?)null), ct);

        /// <summary>
        /// Ends every order whose send window has passed, whose send deadline after the reservation has passed, or
        /// whose events are sent or out of tries on both platforms: clears its ids, IP address, user agent and page, and
        /// <see cref="Order.MarketingEventsDueBy"/>, so a set due-by always means a live order. The consent id, notice
        /// version and sent times stay as the record.
        /// </summary>
        public Task<int> CleanUpAsync(DateTime now, CancellationToken ct)
        {
            var reservedAfter = now - MarketingCapture.SendDeadline;
            return db.Orders
                .Where(o => (o.MarketingEventsDueBy != null
                        && (o.MarketingEventsDueBy <= now
                            || o.AuthorizedAt <= reservedAfter
                            || ((o.MetaEventSentAt != null || o.MetaEventAttempts >= MaxAttempts)
                                && (o.TikTokEventSentAt != null || o.TikTokEventAttempts >= MaxAttempts))))
                    || (o.MarketingEventsDueBy == null
                        && (o.Fbp != null || o.Fbc != null || o.Ttp != null || o.Ttclid != null
                            || o.ClientIp != null || o.ClientUserAgent != null || o.EventSourceUrl != null)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.MarketingEventsDueBy, (DateTime?)null)
                    .SetProperty(o => o.Fbp, (string?)null)
                    .SetProperty(o => o.Fbc, (string?)null)
                    .SetProperty(o => o.Ttp, (string?)null)
                    .SetProperty(o => o.Ttclid, (string?)null)
                    .SetProperty(o => o.ClientIp, (string?)null)
                    .SetProperty(o => o.ClientUserAgent, (string?)null)
                    .SetProperty(o => o.EventSourceUrl, (string?)null), ct);
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Where the next consent recheck page starts. One per app, so it carries over from run to run.</summary>
    public sealed class ConsentRecheckCursor
    {
        public int AfterId { get; set; }
    }

    /// <summary>Runs <see cref="OrderMarketingEvents"/> every <see cref="Interval"/>, after a first wait of the same length.</summary>
    public sealed class OrderMarketingEventsService(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<OrderMarketingEventsService> log) : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval, time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        }

        /// <summary>One run. A failure is logged and the next run tries again.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OrderMarketingEvents>().RunAsync(time.GetUtcNow().UtcDateTime, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError("Marketing: the Purchase event run failed with {Error}, it is tried again in {Minutes} minutes",
                    ex.GetType().Name, Interval.TotalMinutes);
            }
        }
    }

    public static class OrderMarketingEventsRegistration
    {
        /// <summary>
        /// Registers the sender, its background service and the two platform clients. The base addresses come from
        /// <c>Marketing:MetaBaseUrl</c> and <c>Marketing:TikTokBaseUrl</c>. The clients log nothing, since the Meta
        /// request address holds the access token.
        /// </summary>
        public static IServiceCollection AddOrderMarketingEvents(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient<MetaConversionsClient>(c =>
                {
                    c.BaseAddress = new Uri(BaseUrl(config["Marketing:MetaBaseUrl"], MetaConversionsClient.DefaultBaseUrl));
                    c.Timeout = TimeSpan.FromSeconds(20);
                })
                .RemoveAllLoggers();
            services.AddHttpClient<TikTokEventsClient>(c =>
                {
                    c.BaseAddress = new Uri(BaseUrl(config["Marketing:TikTokBaseUrl"], TikTokEventsClient.DefaultBaseUrl));
                    c.Timeout = TimeSpan.FromSeconds(20);
                })
                .RemoveAllLoggers();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<ConsentRecheckCursor>();
            services.AddScoped<OrderMarketingEvents>();
            services.AddHostedService<OrderMarketingEventsService>();
            return services;
        }

        private static string BaseUrl(string? configured, string fallback)
        {
            var url = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
            return url.EndsWith('/') ? url : url + "/";
        }
    }
}
