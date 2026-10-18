using FluentValidation;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    public record DeliveryOptionDto(
        string Method,
        bool Enabled,
        int PriceNok,
        string LabelNo,
        string LabelEn,
        string? DescriptionNo,
        string? DescriptionEn,
        int? MaxDistanceKm);

    public record ShopSettingsDto(
        string BuyButton,
        bool VippsTestMode,
        IReadOnlyList<DeliveryOptionDto> Delivery,
        bool TermsUpdatedSinceShop,
        DateTime ShopCreatedAt);

    public class UpdateShopSettingsDto
    {
        public required string BuyButton { get; set; }
        public bool VippsTestMode { get; set; }
        public List<DeliveryOptionDto> Delivery { get; set; } = [];
    }

    public record PublicDeliveryOptionDto(string Method, int PriceNok, string Label, string? Description, int? MaxDistanceKm);

    public record ShopConfigDto(string BuyButton, IReadOnlyList<PublicDeliveryOptionDto> Delivery);

    public class UpdateShopSettingsValidator : AbstractValidator<UpdateShopSettingsDto>
    {
        public UpdateShopSettingsValidator()
        {
            RuleFor(x => x.BuyButton)
                .Must(v => Enum.TryParse<BuyButtonMode>(v, true, out _))
                .WithMessage("Unknown buy button mode.");

            RuleFor(x => x.Delivery)
                .NotNull()
                .Must(HoldEachMethodOnce)
                .WithMessage("Delivery must hold each delivery method exactly once.");

            RuleForEach(x => x.Delivery).ChildRules(d =>
            {
                d.RuleFor(x => x.PriceNok).InclusiveBetween(0, 100000);
                d.RuleFor(x => x.LabelNo).NotEmpty().MaximumLength(80);
                d.RuleFor(x => x.LabelEn).NotEmpty().MaximumLength(80);
                d.RuleFor(x => x.DescriptionNo).MaximumLength(300);
                d.RuleFor(x => x.DescriptionEn).MaximumLength(300);
                d.RuleFor(x => x.MaxDistanceKm).InclusiveBetween(1, 1000).When(x => x.MaxDistanceKm != null);
                d.RuleFor(x => x.MaxDistanceKm).NotNull()
                    .When(x => x.Enabled && IsMethod(x.Method, DeliveryMethod.HomeDelivery))
                    .WithMessage("A maximum distance is required for home delivery.");
            });
        }

        private static bool IsMethod(string value, DeliveryMethod method) =>
            Enum.TryParse<DeliveryMethod>(value, true, out var parsed) && parsed == method;

        private static bool HoldEachMethodOnce(List<DeliveryOptionDto> delivery)
        {
            var parsed = new List<DeliveryMethod>();
            foreach (var option in delivery)
            {
                if (!Enum.TryParse<DeliveryMethod>(option.Method, true, out var method)) return false;
                parsed.Add(method);
            }

            return parsed.Distinct().Count() == parsed.Count
                && Enum.GetValues<DeliveryMethod>().All(parsed.Contains);
        }
    }
}
