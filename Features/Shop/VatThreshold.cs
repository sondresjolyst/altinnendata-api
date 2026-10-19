using System.Globalization;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Tracks turnover against the 50 000 kr VAT registration threshold (merverdiavgiftsloven § 2-1). Registration is
    /// required once turnover over 12 months passes the threshold, and VAT is owed from the sale that passes it.
    /// Turnover is what the shop captured less what it refunded, test orders left out, plus the sales outside the shop
    /// the admin enters per month. Months are calendar months in Norwegian time, and the window is the current month
    /// and the 11 before it. Ported from garge-api's VatThresholdService.
    /// </summary>
    public static class VatThreshold
    {
        public const long ThresholdInOre = 5_000_000;

        /// <summary>The admin is warned from 40 000 kr, 80 % of the threshold.</summary>
        public const long WarnFromInOre = 4_000_000;

        public const int WindowMonths = 12;

        private static readonly Lazy<TimeZoneInfo> Oslo = new(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        });

        /// <summary>One shop sale: what was kept of it once refunds are taken off, and when it was captured.</summary>
        public sealed record Sale(string Reference, DateTime At, int AmountInOre);

        /// <summary>The first day of the Norwegian calendar month <paramref name="utc"/> falls in.</summary>
        public static DateOnly MonthOf(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Oslo.Value);
            return new DateOnly(local.Year, local.Month, 1);
        }

        public static async Task<VatThresholdDto> StatusAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var orders = await db.Orders.AsNoTracking()
                .Where(o => !o.IsTest && o.CapturedInOre > 0)
                .Select(o => new { o.Reference, o.CapturedAt, o.CreatedAt, o.CapturedInOre, o.RefundedInOre })
                .ToListAsync(ct);
            var sales = orders
                .Select(o => new Sale(o.Reference, o.CapturedAt ?? o.CreatedAt, Math.Max(0, o.CapturedInOre - o.RefundedInOre)))
                .ToList();
            var outside = await db.OutsideSalesMonths.AsNoTracking()
                .ToDictionaryAsync(m => m.Month, m => (long)m.AmountInOre, ct);

            return Compute(sales, outside, now);
        }

        /// <summary>The figure from the shop's sales and the sales outside it, as it stands at <paramref name="now"/>.</summary>
        public static VatThresholdDto Compute(IReadOnlyList<Sale> sales, IReadOnlyDictionary<DateOnly, long> outside, DateTime now)
        {
            var current = MonthOf(now);
            var shopByMonth = sales
                .GroupBy(s => MonthOf(s.At))
                .ToDictionary(g => g.Key, g => g.Sum(s => (long)s.AmountInOre));

            long Shop(DateOnly month) => shopByMonth.GetValueOrDefault(month);
            long Outside(DateOnly month) => outside.GetValueOrDefault(month);

            var months = Enumerable.Range(0, WindowMonths)
                .Select(i => current.AddMonths(i - (WindowMonths - 1)))
                .Select(m => new VatMonthDto(Format(m), Shop(m), Outside(m)))
                .ToList();
            var turnover = months.Sum(m => m.ShopInOre + m.OutsideInOre);

            var crossing = FindCrossing(sales, shopByMonth, outside, current);

            return new VatThresholdDto(
                turnover,
                ThresholdInOre,
                WarnFromInOre,
                turnover >= WarnFromInOre,
                crossing is { } c ? Format(c.Month) : null,
                crossing?.Sale?.Reference,
                crossing?.Sale?.At,
                months);
        }

        /// <summary>
        /// The first month where the 12 months up to and including it pass the threshold, and the sale in it that
        /// passed it. Within a month the sales outside the shop are counted first, since their day is not known.
        /// </summary>
        private static (DateOnly Month, Sale? Sale)? FindCrossing(
            IReadOnlyList<Sale> sales,
            Dictionary<DateOnly, long> shopByMonth,
            IReadOnlyDictionary<DateOnly, long> outside,
            DateOnly current)
        {
            var known = shopByMonth.Keys.Concat(outside.Keys).ToList();
            if (known.Count == 0) return null;

            long Total(DateOnly month) => shopByMonth.GetValueOrDefault(month) + outside.GetValueOrDefault(month);

            for (var month = known.Min(); month <= current; month = month.AddMonths(1))
            {
                var running = Enumerable.Range(1, WindowMonths - 1).Sum(i => Total(month.AddMonths(-i)))
                    + outside.GetValueOrDefault(month);
                if (running > ThresholdInOre) return (month, null);

                foreach (var sale in sales.Where(s => MonthOf(s.At) == month).OrderBy(s => s.At))
                {
                    running += sale.AmountInOre;
                    if (running > ThresholdInOre) return (month, sale);
                }
            }

            return null;
        }

        private static string Format(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await StatusAsync(db, DateTime.UtcNow, ct));

        public static Task<IResult> SetOutsideSales(UpdateOutsideSalesDto dto, ApplicationDbContext db, CancellationToken ct) =>
            SetOutsideSalesAsync(dto, db, DateTime.UtcNow, ct);

        /// <summary>Stores the sales outside the shop for one month that has started. Zero removes the month.</summary>
        public static async Task<IResult> SetOutsideSalesAsync(UpdateOutsideSalesDto dto, ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            UpdateOutsideSalesDto.TryParseMonth(dto.Month, out var month);
            if (month > MonthOf(now))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [UpdateOutsideSalesDto.MonthField] = ["The month has not started yet."]
                });

            var row = await db.OutsideSalesMonths.FirstOrDefaultAsync(m => m.Month == month, ct);
            if (dto.AmountInOre == 0)
            {
                if (row != null) db.OutsideSalesMonths.Remove(row);
            }
            else if (row == null)
            {
                db.OutsideSalesMonths.Add(new OutsideSalesMonth { Month = month, AmountInOre = dto.AmountInOre, UpdatedAt = now });
            }
            else
            {
                row.AmountInOre = dto.AmountInOre;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await StatusAsync(db, now, ct));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var admin = app.MapGroup("/api/admin/shop/vat-threshold").RequireAuthorization(Policies.Admin);
                admin.MapGet("", Get);
                admin.MapPut("outside-sales", SetOutsideSales).WithValidation<UpdateOutsideSalesDto>();
            }
        }
    }
}
