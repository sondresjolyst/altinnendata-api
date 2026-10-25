using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.VatThreshold;
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
    /// the admin enters per month. A sale counts from when it was made. Merverdiavgiftshåndboken 2-1.3 says the time of
    /// delivery decides this, not invoicing, so a sale is dated by when it was sent or captured, whichever came first,
    /// and by when the order was created when it has neither. The rule itself, with every 12 month window and the sale
    /// that passed the threshold, comes from SjolystInnovation.AppKit.VatThreshold. Days are counted in Norwegian time.
    /// </summary>
    public static class VatThreshold
    {
        public const long ThresholdInOre = VatRegistrationThreshold.LimitInOre;

        /// <summary>The share of the threshold the admin is warned from.</summary>
        public const int WarnFromPercent = 80;

        /// <summary>The admin is warned from 40 000 kr, 80 % of the threshold.</summary>
        public const long WarnFromInOre = ThresholdInOre * WarnFromPercent / 100;

        /// <summary>One shop sale: what was captured, when the sale was made, and the refunds of it.</summary>
        public sealed record Sale(string Reference, DateTime At, int AmountInOre, IReadOnlyList<Refund>? Refunds = null);

        /// <summary>One refund of a shop sale and when it was made.</summary>
        public sealed record Refund(DateTime At, int AmountInOre);

        /// <summary>The first day of the Norwegian calendar month <paramref name="utc"/> falls in.</summary>
        public static DateOnly MonthOf(DateTime utc) => FirstOfMonth(DayOf(utc));

        /// <summary>The Norwegian date <paramref name="utc"/> falls on.</summary>
        public static DateOnly DayOf(DateTime utc) => VatRegistrationThreshold.DayOf(utc, OsloTime.Zone);

        private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

        /// <summary>When a sale was made: the first of when it was sent and when it was captured, else when it was created.</summary>
        public static DateTime SaleDate(DateTime? shippedAt, DateTime? capturedAt, DateTime createdAt) =>
            (shippedAt, capturedAt) switch
            {
                ({ } shipped, { } captured) => shipped < captured ? shipped : captured,
                ({ } shipped, null) => shipped,
                (null, { } captured) => captured,
                _ => createdAt,
            };

        public static async Task<VatThresholdDto> StatusAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var orders = await db.Orders.AsNoTracking()
                .Where(o => !o.IsTest && o.CapturedInOre > 0)
                .Select(o => new { o.Id, o.Reference, o.ShippedAt, o.CapturedAt, o.CreatedAt, o.CapturedInOre })
                .ToListAsync(ct);
            var ids = orders.Select(o => o.Id).ToList();
            var refunds = (await db.OrderRefunds.AsNoTracking()
                    .Where(r => ids.Contains(r.OrderId))
                    .Select(r => new { r.OrderId, r.RefundedAt, r.AmountInOre })
                    .ToListAsync(ct))
                .ToLookup(r => r.OrderId, r => new Refund(r.RefundedAt, r.AmountInOre));
            var sales = orders
                .Select(o => new Sale(o.Reference, SaleDate(o.ShippedAt, o.CapturedAt, o.CreatedAt), o.CapturedInOre, refunds[o.Id].ToList()))
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
            var dated = sales
                .Select(s => new VatSale(s.Reference, s.At, s.AmountInOre,
                    (s.Refunds ?? []).Select(r => new VatRefund(r.At, r.AmountInOre)).ToList()))
                .ToList();
            var turnover = VatRegistrationThreshold.TurnoverOn(today, dated, outside, OsloTime.Zone);
            var crossing = VatRegistrationThreshold.FindCrossing(dated, outside, OsloTime.Zone, today);

            return new VatThresholdDto(
                turnover.TotalInOre,
                ThresholdInOre,
                WarnFromInOre,
                VatRegistrationThreshold.WarningLevel(turnover.TotalInOre, [WarnFromPercent]) > 0,
                crossing is { } c ? FormatMonth(c.Day) : null,
                crossing?.Sale?.Reference,
                crossing?.Sale?.AtUtc,
                turnover.Months.Select(m => new VatMonthDto(FormatMonth(m.Month), m.SalesInOre, m.OutsideInOre)).ToList(),
                crossing is { } d ? d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
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
