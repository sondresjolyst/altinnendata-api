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
    /// the admin enters per month. Any 12 month period counts, so the threshold is checked over the window that ends on
    /// each day, in Norwegian time. A shop sale counts on the day it was captured, and a refund takes it down only in the
    /// windows that hold both the capture and the refund, from the moment of the refund. The day of a sale outside the shop
    /// is not known, so a month of them counts in every window the month overlaps, which warns early rather than late.
    /// Ported from garge-api's VatThresholdService.
    /// </summary>
    public static class VatThreshold
    {
        public const long ThresholdInOre = 5_000_000;

        /// <summary>The admin is warned from 40 000 kr, 80 % of the threshold.</summary>
        public const long WarnFromInOre = 4_000_000;

        public const int WindowMonths = 12;


        /// <summary>One shop sale: what was captured and when, and the refunds of it.</summary>
        public sealed record Sale(string Reference, DateTime At, int AmountInOre, IReadOnlyList<Refund>? Refunds = null);

        /// <summary>One refund of a shop sale and when it was made.</summary>
        public sealed record Refund(DateTime At, int AmountInOre);

        /// <summary>
        /// What is left of <paramref name="sale"/> once the refunds <paramref name="made"/> says have happened are taken
        /// off, never below zero. A refund always comes after its capture, so a window that holds the refund holds the
        /// capture too.
        /// </summary>
        private static long Kept(Sale sale, Func<Refund, bool> made) =>
            Math.Max(0, sale.AmountInOre - (sale.Refunds ?? []).Where(made).Sum(r => (long)r.AmountInOre));

        /// <summary>The first day of the Norwegian calendar month <paramref name="utc"/> falls in.</summary>
        public static DateOnly MonthOf(DateTime utc) => FirstOfMonth(DayOf(utc));

        /// <summary>The Norwegian date <paramref name="utc"/> falls on.</summary>
        public static DateOnly DayOf(DateTime utc) =>
            DateOnly.FromDateTime(OsloTime.FromUtc(utc));

        /// <summary>The first day of the 12 month window that ends on <paramref name="end"/>, both days included.</summary>
        public static DateOnly WindowStart(DateOnly end) => end.AddMonths(-WindowMonths).AddDays(1);

        private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

        private static bool InWindow(DateOnly day, DateOnly start, DateOnly end) => day >= start && day <= end;

        /// <summary>True when any day of <paramref name="month"/> is in the window from <paramref name="start"/> to <paramref name="end"/>.</summary>
        private static bool Overlaps(DateOnly month, DateOnly start, DateOnly end) => month <= end && month.AddMonths(1) > start;

        public static async Task<VatThresholdDto> StatusAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var orders = await db.Orders.AsNoTracking()
                .Where(o => !o.IsTest && o.CapturedInOre > 0)
                .Select(o => new { o.Id, o.Reference, o.CapturedAt, o.CreatedAt, o.CapturedInOre })
                .ToListAsync(ct);
            var ids = orders.Select(o => o.Id).ToList();
            var refunds = (await db.OrderRefunds.AsNoTracking()
                    .Where(r => ids.Contains(r.OrderId))
                    .Select(r => new { r.OrderId, r.RefundedAt, r.AmountInOre })
                    .ToListAsync(ct))
                .ToLookup(r => r.OrderId, r => new Refund(r.RefundedAt, r.AmountInOre));
            var sales = orders
                .Select(o => new Sale(o.Reference, o.CapturedAt ?? o.CreatedAt, o.CapturedInOre, refunds[o.Id].ToList()))
                .ToList();
            var outside = await db.OutsideSalesMonths.AsNoTracking()
                .ToDictionaryAsync(m => m.Month, m => (long)m.AmountInOre, ct);

            return Compute(sales, outside, now);
        }

        /// <summary>
        /// The figure from the shop's sales and the sales outside it, as it stands at <paramref name="now"/>. The turnover
        /// is the window that ends today, and the months are the ones that window overlaps, oldest first.
        /// </summary>
        public static VatThresholdDto Compute(IReadOnlyList<Sale> sales, IReadOnlyDictionary<DateOnly, long> outside, DateTime now)
        {
            var today = DayOf(now);
            var start = WindowStart(today);
            var shopByMonth = sales
                .Where(s => InWindow(DayOf(s.At), start, today))
                .GroupBy(s => MonthOf(s.At))
                .ToDictionary(g => g.Key, g => g.Sum(s => Kept(s, r => DayOf(r.At) <= today)));

            var months = new List<VatMonthDto>();
            for (var month = FirstOfMonth(start); month <= today; month = month.AddMonths(1))
                months.Add(new VatMonthDto(FormatMonth(month), shopByMonth.GetValueOrDefault(month), outside.GetValueOrDefault(month)));
            var turnover = months.Sum(m => m.ShopInOre + m.OutsideInOre);

            var crossing = FindCrossing(sales, outside, today);

            return new VatThresholdDto(
                turnover,
                ThresholdInOre,
                WarnFromInOre,
                turnover >= WarnFromInOre,
                crossing is { } c ? FormatMonth(c.Day) : null,
                crossing?.Sale?.Reference,
                crossing?.Sale?.At,
                months,
                crossing is { } d ? d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
        }

        /// <summary>
        /// The first day, up to <paramref name="today"/>, where the 12 month window ending on it passes the threshold, and
        /// the sale that day that passed it. A window only grows on a day a shop sale is captured or a month of outside
        /// sales starts, so those are the days checked. On a day the outside sales are counted first, since their day is
        /// not known, and the sale is empty when they passed the threshold. Refunds count from the moment they were made.
        /// </summary>
        private static (DateOnly Day, Sale? Sale)? FindCrossing(
            IReadOnlyList<Sale> sales,
            IReadOnlyDictionary<DateOnly, long> outside,
            DateOnly today)
        {
            var dated = sales.Select(s => (Day: DayOf(s.At), Sale: s)).ToList();
            var days = dated.Select(s => s.Day)
                .Concat(outside.Keys)
                .Where(d => d <= today)
                .Distinct()
                .Order();

            foreach (var day in days)
            {
                var start = WindowStart(day);
                var outsideInWindow = outside.Where(m => Overlaps(m.Key, start, day)).Sum(m => m.Value);
                var beforeDay = outsideInWindow
                    + dated.Where(s => s.Day >= start && s.Day < day).Sum(s => Kept(s.Sale, r => DayOf(r.At) < day));
                if (beforeDay > ThresholdInOre) return (day, null);

                foreach (var sale in dated.Where(s => s.Day == day).Select(s => s.Sale).OrderBy(s => s.At))
                {
                    var running = outsideInWindow
                        + dated.Where(s => s.Day >= start && s.Sale.At <= sale.At).Sum(s => Kept(s.Sale, r => r.At <= sale.At));
                    if (running > ThresholdInOre) return (day, sale);
                }
            }

            return null;
        }

        private static string FormatMonth(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

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
