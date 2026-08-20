using FluentValidation;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;

namespace altinnendata_api.Features.Settings
{
    public record SettingsBody(
        string ContactRecipientEmail,
        string CompanyName,
        string CompanyLegalName,
        string OrgNumber,
        bool VatRegistered,
        string StreetAddress,
        string PostalCode,
        string AddressLocality,
        string AddressRegion,
        string PublicEmail,
        string PublicPhone);

    public class SettingsValidator : AbstractValidator<SettingsBody>
    {
        public SettingsValidator()
        {
            RuleFor(x => x.ContactRecipientEmail).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.CompanyLegalName).MaximumLength(200);
            RuleFor(x => x.OrgNumber).MaximumLength(50);
            RuleFor(x => x.StreetAddress).NotEmpty().MaximumLength(200);
            RuleFor(x => x.PostalCode).MaximumLength(20).Matches(@"^\d{4}$")
                .When(x => !string.IsNullOrWhiteSpace(x.PostalCode))
                .WithMessage("A Norwegian postal code is four digits.");
            RuleFor(x => x.AddressLocality).MaximumLength(100);
            RuleFor(x => x.AddressRegion).MaximumLength(100);
            RuleFor(x => x.AddressLocality)
                .NotEmpty()
                .When(x => !string.IsNullOrWhiteSpace(x.PostalCode))
                .WithMessage("A postal code needs the place name that goes with it.");
            RuleFor(x => x.PublicEmail).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.PublicPhone).NotEmpty().MaximumLength(40);
            RuleFor(x => x.OrgNumber)
                .NotEmpty()
                .When(x => x.VatRegistered)
                .WithMessage("An organisation number is required when the business is VAT registered.");
        }
    }

    /// <summary>Get / update admin-managed application settings.</summary>
    public static class Settings
    {
        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
            return TypedResults.Ok(ToBody(settings));
        }

        public static async Task<IResult> Update(SettingsBody body, ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct);
            if (settings == null)
            {
                settings = new AppSettings { Id = 1 };
                db.AppSettings.Add(settings);
            }

            settings.ContactRecipientEmail = body.ContactRecipientEmail;
            settings.CompanyName = body.CompanyName;
            settings.CompanyLegalName = body.CompanyLegalName;
            settings.OrgNumber = body.OrgNumber;
            settings.VatRegistered = body.VatRegistered;
            settings.StreetAddress = body.StreetAddress;
            settings.PostalCode = body.PostalCode;
            settings.AddressLocality = body.AddressLocality;
            settings.AddressRegion = body.AddressRegion;
            settings.PublicEmail = body.PublicEmail;
            settings.PublicPhone = body.PublicPhone;

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToBody(settings));
        }

        private static SettingsBody ToBody(AppSettings s) => new(
            s.ContactRecipientEmail, s.CompanyName, s.CompanyLegalName, s.OrgNumber,
            s.VatRegistered, s.StreetAddress, s.PostalCode, s.AddressLocality, s.AddressRegion,
            s.PublicEmail, s.PublicPhone);

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/settings").RequireAuthorization(Policies.Admin);
                group.MapGet("", Get);
                group.MapPut("", Update).WithValidation<SettingsBody>();
            }
        }
    }
}
