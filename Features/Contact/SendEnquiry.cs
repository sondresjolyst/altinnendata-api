using System.Net;
using FluentValidation;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Contact
{
    public record ContactRequest(
        string Name,
        string Email,
        string? Phone,
        string? UseCase,
        int? BudgetNok,
        string? BuildSlug,
        string Message);

    public class ContactRequestValidator : AbstractValidator<ContactRequest>
    {
        public ContactRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Phone).MaximumLength(30);
            RuleFor(x => x.UseCase).MaximumLength(160);
            RuleFor(x => x.BudgetNok).GreaterThanOrEqualTo(0).When(x => x.BudgetNok.HasValue);
            RuleFor(x => x.BuildSlug).MaximumLength(160);
            RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
        }
    }

    /// <summary>Sends a contact / build enquiry to the configured recipient.</summary>
    public static class SendEnquiry
    {
        public static async Task<IResult> Handle(ContactRequest req, ApplicationDbContext db, IEmailService email, CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();

            await email.SendEmailAsync(
                settings.ContactRecipientEmail,
                $"New enquiry from {req.Name}",
                BuildBody(req),
                replyTo: req.Email);

            return TypedResults.Ok(new MessageResponse("Thanks — we'll be in touch."));
        }

        private static string BuildBody(ContactRequest req)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
            var budget = req.BudgetNok.HasValue ? $"{req.BudgetNok} NOK" : string.Empty;
            return $@"
<h2>New enquiry</h2>
<p><strong>Name:</strong> {Enc(req.Name)}</p>
<p><strong>Email:</strong> {Enc(req.Email)}</p>
<p><strong>Phone:</strong> {Enc(req.Phone)}</p>
<p><strong>Use case:</strong> {Enc(req.UseCase)}</p>
<p><strong>Budget:</strong> {Enc(budget)}</p>
<p><strong>Build:</strong> {Enc(req.BuildSlug)}</p>
<p><strong>Message:</strong></p>
<p>{Enc(req.Message).Replace("\n", "<br/>")}</p>";
        }

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/contact", Handle).AllowAnonymous().WithValidation<ContactRequest>();
        }
    }
}
