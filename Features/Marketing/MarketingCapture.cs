using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Consent;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>
    /// The marketing ids the site sends with a checkout. <see cref="ConsentId"/> is the browser's consent id from the
    /// consent banner. The rest are the Meta and TikTok cookies and click ids, and the page the checkout started from.
    /// </summary>
    public record CheckoutMarketing(
        string? ConsentId,
        string? Fbp = null,
        string? Fbc = null,
        string? Ttp = null,
        string? Ttclid = null,
        string? PageUrl = null);

    /// <summary>
    /// Copies the marketing ids of a checkout onto its order, only when the consent log shows the buyer allowed
    /// marketing. Invalid string values drop all of it, and the checkout goes on without it. A value of the wrong
    /// JSON type fails the binding of the whole checkout request instead.
    /// </summary>
    public static class MarketingCapture
    {
        public const int MaxConsentIdLength = 64;
        public const int MaxCookieLength = 256;
        public const int MaxTtclidLength = 1000;
        public const int MaxPageUrlLength = 2048;
        public const int MaxUserAgentLength = 512;

        /// <summary>How long a marketing consent counts, the same as the consent banner's max age.</summary>
        public static readonly TimeSpan ConsentMaxAge = TimeSpan.FromDays(365);

        /// <summary>How long after checkout the stored ids are kept at most. Meta refuses events older than 7 days.</summary>
        public static readonly TimeSpan SendWindow = TimeSpan.FromDays(7);

        /// <summary>
        /// How long after the reservation a Purchase event may be sent. Both platforms deduplicate the browser and
        /// server events only within 48 hours, so a later server event would count the purchase twice.
        /// </summary>
        public static readonly TimeSpan SendDeadline = TimeSpan.FromHours(47);

        /// <summary>The accepted ids, or null when any of them is invalid.</summary>
        public static CheckoutMarketing? Accept(CheckoutMarketing? input, IConfiguration config)
        {
            if (input == null) return null;
            if (string.IsNullOrEmpty(input.ConsentId) || input.ConsentId.Length > MaxConsentIdLength
                || !Guid.TryParseExact(input.ConsentId, "D", out var consentId) || consentId == Guid.Empty)
                return null;

            if (!TryToken(input.Fbp, MaxCookieLength, out var fbp)
                || !TryToken(input.Fbc, MaxCookieLength, out var fbc)
                || !TryToken(input.Ttp, MaxCookieLength, out var ttp)
                || !TryToken(input.Ttclid, MaxTtclidLength, out var ttclid)
                || !TryPageUrl(input.PageUrl, config, out var pageUrl))
                return null;

            return new CheckoutMarketing(consentId.ToString("D"), fbp, fbc, ttp, ttclid, pageUrl);
        }

        /// <summary>
        /// The latest consent log entry of <paramref name="consentId"/>, when it allows marketing and is younger than
        /// <see cref="ConsentMaxAge"/> at <paramref name="now"/>. Null otherwise.
        /// </summary>
        public static async Task<ConsentRecord?> ValidConsentAsync(ApplicationDbContext db, string consentId, DateTime now, CancellationToken ct)
        {
            var latest = await LatestAsync(db, consentId, ct);
            return latest is { Marketing: true } && latest.DecidedAt >= now - ConsentMaxAge ? latest : null;
        }

        /// <summary>The latest consent log entry of <paramref name="consentId"/>, or null when there is none.</summary>
        public static async Task<ConsentRecord?> LatestAsync(ApplicationDbContext db, string consentId, CancellationToken ct)
        {
            if (!Guid.TryParseExact(consentId, "D", out var id)) return null;
            return await db.Set<ConsentRecord>().AsNoTracking()
                .Where(r => r.ConsentId == id)
                .OrderByDescending(r => r.DecidedAt)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Stores the ids of <paramref name="input"/> on <paramref name="order"/> when they are valid and the consent
        /// log allows marketing. Stores nothing otherwise. Returns true when it stored them.
        /// </summary>
        public static async Task<bool> ApplyAsync(Order order, CheckoutMarketing? input, HttpContext http, ApplicationDbContext db,
            IConfiguration config, DateTime now, CancellationToken ct)
        {
            var accepted = Accept(input, config);
            if (accepted == null) return false;
            var consent = await ValidConsentAsync(db, accepted.ConsentId!, now, ct);
            if (consent == null) return false;

            order.MarketingConsentId = accepted.ConsentId;
            order.MarketingNoticeVersion = consent.NoticeVersion;
            order.Fbp = accepted.Fbp;
            order.Fbc = accepted.Fbc;
            order.Ttp = accepted.Ttp;
            order.Ttclid = accepted.Ttclid;
            order.ClientIp = PublicIp(http.Connection.RemoteIpAddress);
            order.ClientUserAgent = UserAgent(http);
            order.EventSourceUrl = accepted.PageUrl;
            order.MarketingEventsDueBy = now + SendWindow;
            return true;
        }

        /// <summary>The address ranges that are never a buyer's own public address.</summary>
        private static readonly IPNetwork[] NotPublic =
        [
            // IPv4: this network, private, shared, loopback, link-local, IETF protocol assignments, benchmarking,
            // multicast and reserved (with broadcast).
            IPNetwork.Parse("0.0.0.0/8"),
            IPNetwork.Parse("10.0.0.0/8"),
            IPNetwork.Parse("100.64.0.0/10"),
            IPNetwork.Parse("127.0.0.0/8"),
            IPNetwork.Parse("169.254.0.0/16"),
            IPNetwork.Parse("172.16.0.0/12"),
            IPNetwork.Parse("192.0.0.0/24"),
            IPNetwork.Parse("192.168.0.0/16"),
            IPNetwork.Parse("198.18.0.0/15"),
            IPNetwork.Parse("224.0.0.0/4"),
            IPNetwork.Parse("240.0.0.0/4"),
            // IPv6: unspecified and loopback, NAT64, 6to4, unique local, link-local, site-local and multicast.
            IPNetwork.Parse("::/127"),
            IPNetwork.Parse("64:ff9b::/96"),
            IPNetwork.Parse("2002::/16"),
            IPNetwork.Parse("fc00::/7"),
            IPNetwork.Parse("fe80::/10"),
            IPNetwork.Parse("fec0::/10"),
            IPNetwork.Parse("ff00::/8"),
        ];

        /// <summary>
        /// The caller's address when it is a public one. Behind the ingress the forwarded headers middleware sets the
        /// client's own address. A private, loopback, multicast or reserved address means it did not, so it is not the
        /// buyer's.
        /// </summary>
        public static string? PublicIp(IPAddress? address)
        {
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)) return null;
            return NotPublic.Any(n => n.Contains(address)) ? null : address.ToString();
        }

        private static string? UserAgent(HttpContext http)
        {
            var ua = http.Request.Headers.UserAgent.ToString().Trim();
            if (ua.Length == 0) return null;
            return ua.Length > MaxUserAgentLength ? ua[..MaxUserAgentLength] : ua;
        }

        /// <summary>An optional id: empty is absent, otherwise printable ASCII without spaces, at most <paramref name="max"/> long.</summary>
        private static bool TryToken(string? value, int max, out string? token)
        {
            token = null;
            if (string.IsNullOrEmpty(value)) return true;
            if (value.Length > max || !value.All(c => c is > ' ' and <= '~')) return false;
            token = value;
            return true;
        }

        /// <summary>An optional page URL: empty is absent, otherwise an absolute https URL on the site's own host and default port.</summary>
        private static bool TryPageUrl(string? value, IConfiguration config, out string? url)
        {
            url = null;
            if (string.IsNullOrEmpty(value)) return true;
            if (value.Length > MaxPageUrlLength) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !uri.IsDefaultPort
                || !Uri.TryCreate(SiteLinks.BaseUrl(config), UriKind.Absolute, out var site)
                || !string.Equals(uri.Host, site.Host, StringComparison.OrdinalIgnoreCase))
                return false;
            // Only the page, without its query or fragment, which can carry click ids and other personal data.
            url = uri.GetLeftPart(UriPartial.Path);
            return true;
        }
    }
}
