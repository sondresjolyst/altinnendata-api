using System.Globalization;
using FluentValidation;

namespace altinnendata_api.Features.Shop
{
    /// <summary>One month of sales: the shop's own and those entered for sales outside the shop. <c>Month</c> is yyyy-MM.</summary>
    public record VatMonthDto(string Month, long ShopInOre, long OutsideInOre);

    /// <summary>
    /// Turnover over the 12 months up to today against the VAT registration threshold, with the months that period
    /// overlaps. <c>CrossedOn</c> is the first day, as yyyy-MM-dd, where the 12 months up to it passed the threshold, and
    /// <c>CrossedMonth</c> is its month as yyyy-MM. <c>CrossingOrderReference</c> names the shop sale that passed it, and
    /// is empty when sales outside the shop did.
    /// </summary>
    public record VatThresholdDto(
        long TurnoverInOre,
        long ThresholdInOre,
        long WarnFromInOre,
        bool Warning,
        string? CrossedMonth,
        string? CrossingOrderReference,
        DateTime? CrossedAt,
        IReadOnlyList<VatMonthDto> Months,
        string? CrossedOn);

    public class UpdateOutsideSalesDto
    {
        public const string MonthField = "month";
        public const string AmountField = "amountInOre";

        /// <summary>Largest amount for one month, 10 000 000 kr.</summary>
        public const int MaxAmountInOre = 1_000_000_000;

        /// <summary>The month, as yyyy-MM.</summary>
        public required string Month { get; set; }

        /// <summary>Sales outside the shop that month, in øre. Zero removes the month.</summary>
        public int AmountInOre { get; set; }

        /// <summary>The first day of <paramref name="value"/> when it is a yyyy-MM month from 2000 on.</summary>
        public static bool TryParseMonth(string? value, out DateOnly month)
        {
            month = default;
            if (!DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                || parsed.Year < 2000)
                return false;
            month = parsed;
            return true;
        }
    }

    public class UpdateOutsideSalesValidator : AbstractValidator<UpdateOutsideSalesDto>
    {
        public UpdateOutsideSalesValidator()
        {
            RuleFor(x => x.Month)
                .Must(m => UpdateOutsideSalesDto.TryParseMonth(m, out _))
                .OverridePropertyName(UpdateOutsideSalesDto.MonthField)
                .WithMessage("Use a month as yyyy-MM.");

            RuleFor(x => x.AmountInOre)
                .InclusiveBetween(0, UpdateOutsideSalesDto.MaxAmountInOre)
                .OverridePropertyName(UpdateOutsideSalesDto.AmountField);
        }
    }
}
