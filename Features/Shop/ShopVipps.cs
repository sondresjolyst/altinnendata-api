using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Sends new payments to the Vipps test environment while test mode is on in the shop settings.
    /// A missing settings row counts as test. Read for at most 30 seconds from the cache.
    /// </summary>
    public class ShopVippsEnvironment(IMemoryCache cache, IServiceScopeFactory scopes) : IVippsEnvironment
    {
        public async Task<bool> IsTestAsync(CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(ShopSettingsEndpoints.VippsTestModeCacheKey, out bool cached))
                return cached;

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await db.ShopSettings.FindAsync([1], cancellationToken);
            var isTest = settings?.VippsTestMode ?? true;
            cache.Set(ShopSettingsEndpoints.VippsTestModeCacheKey, isTest, TimeSpan.FromSeconds(30));
            return isTest;
        }
    }

    public static class ShopVipps
    {
        /// <summary>How long one Vipps HTTP call may take. Checkout holds the build's row lock while it waits.</summary>
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Vipps could not be reached or answered with an error. HttpClient reports its own timeout as a cancellation,
        /// so a cancellation counts only while <paramref name="ct"/>, the caller's own token, is not cancelled.
        /// </summary>
        public static bool IsProviderFailure(Exception ex, CancellationToken ct) =>
            ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

        /// <summary>
        /// Vipps answered and refused the request with a 4xx status, so the change did not happen. A timeout, a 5xx
        /// or a transport error may still have reached Vipps and is not a rejection.
        /// </summary>
        public static bool IsRejection(HttpRequestException ex) => ex.StatusCode is { } status && (int)status is >= 400 and < 500;

        /// <summary>The name of the shop's ePayment webhook.</summary>
        public const string Slot = "shop";

        /// <summary>The path Vipps sends the shop's ePayment events to.</summary>
        public const string WebhookPath = "/api/shop/webhook";

        /// <summary>The Postgres advisory lock that lets one instance register the webhook at a time.</summary>
        public const long AdvisoryLockKey = 7300115;

        /// <summary>
        /// Registers the Vipps client with the credentials in the <c>Vipps</c> section and the shop's test mode,
        /// and the ePayment webhook. The Data Protection keys that protect the webhook secrets are kept in the database.
        /// </summary>
        public static IServiceCollection AddShopVipps(this IServiceCollection services, IConfiguration config)
        {
            services.AddDataProtection()
                .PersistKeysToDbContext<ApplicationDbContext>()
                .SetApplicationName("altinnendata-api");
            services.AddSingleton<IVippsEnvironment, ShopVippsEnvironment>();
            services.AddAppKitVipps(config.GetSection("Vipps"), o =>
                {
                    o.SystemName = "altinnendata";
                    o.SystemVersion = SystemVersion();
                })
                .ConfigureHttpClient(c => c.Timeout = HttpTimeout);
            services.AddAppKitVippsWebhooks<ApplicationDbContext>(o =>
            {
                o.PublicBaseUrl = config["Api:PublicBaseUrl"] ?? "";
                o.AddSlot(Slot, VippsWebhookKind.Payment, WebhookPath);
                o.AdvisoryLockKey = AdvisoryLockKey;
            });
            services.AddScoped<IVippsPaymentWebhookHandler, ShopWebhookHandler>();
            return services;
        }

        private static string SystemVersion() =>
            typeof(ShopVipps).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0.0";
    }
}
