using FluentValidation;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    public record CheckoutRequest(
        string BuildSlug,
        string DeliveryMethod,
        string Name,
        string Email,
        string Phone,
        string? StreetAddress,
        string? PostalCode,
        string? City,
        bool WithinDistance,
        string? PickupNote,
        bool AcceptTerms,
        string Locale);

    public record CheckoutResponse(string RedirectUrl, string Token);

    public class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
    {
        public CheckoutRequestValidator()
        {
            RuleFor(x => x.BuildSlug).NotEmpty().MaximumLength(160);
            RuleFor(x => x.DeliveryMethod)
                .Must(v => UpdateShopSettingsValidator.TryParseName<DeliveryMethod>(v, out _))
                .WithMessage("Unknown delivery method.");
            RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Phone)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(v => VippsPhoneNumber.TryNormalize(v, out _))
                .WithMessage("Use a Norwegian mobile number.");

            When(x => Is(x, Models.Shop.DeliveryMethod.Shipping) || Is(x, Models.Shop.DeliveryMethod.HomeDelivery), () =>
            {
                RuleFor(x => x.StreetAddress).NotEmpty().MaximumLength(200);
                RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{4}$");
                RuleFor(x => x.City).NotEmpty().MaximumLength(100);
            });

            RuleFor(x => x.WithinDistance)
                .Equal(true)
                .When(x => Is(x, Models.Shop.DeliveryMethod.HomeDelivery))
                .WithMessage("Confirm that the address is within the delivery distance.");

            RuleFor(x => x.PickupNote).MaximumLength(500);
            RuleFor(x => x.AcceptTerms).Equal(true).WithMessage("Accept the terms.");
            RuleFor(x => x.Locale).Must(Locales.IsSupported).WithMessage("Unsupported locale.");
        }

        private static bool Is(CheckoutRequest request, DeliveryMethod method) =>
            UpdateShopSettingsValidator.TryParseName<DeliveryMethod>(request.DeliveryMethod, out var parsed) && parsed == method;
    }
}
