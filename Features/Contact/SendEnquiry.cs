using System.Net;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Features.Builds;
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
        public static async Task<IResult> Handle(
            ContactRequest req,
            ApplicationDbContext db,
            IEmailService email,
            IConfiguration config,
            CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();

            var build = string.IsNullOrWhiteSpace(req.BuildSlug)
                ? null
                : await db.PcBuilds
                    .Include(b => b.Translations)
                    .FirstOrDefaultAsync(b => b.Slug == req.BuildSlug, ct);

            await email.SendEmailAsync(
                settings.ContactRecipientEmail,
                $"New enquiry from {req.Name}",
                BuildBody(req, build, config),
                replyTo: req.Email);

            return TypedResults.Ok(new MessageResponse("Thanks — we'll be in touch."));
        }

        private static string BuildBody(ContactRequest req, PcBuild? build, IConfiguration config)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

            var body = new StringBuilder("<h2>New enquiry</h2>");

            void Row(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    body.Append($"<p><strong>{label}:</strong> {value}</p>");
            }

            Row("Name", Enc(req.Name));
            Row("Email", Enc(req.Email));
            Row("Phone", Enc(req.Phone));
            Row("Use case", Enc(req.UseCase));
            Row("Budget", req.BudgetNok.HasValue ? $"{req.BudgetNok} NOK" : null);
            Row("Build", BuildLink(req.BuildSlug, build, config));

            body.Append("<p><strong>Message:</strong></p>");
            body.Append($"<p>{Enc(req.Message).Replace("\n", "<br/>")}</p>");
            return body.ToString();
        }

        private static string? BuildLink(string? slug, PcBuild? build, IConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            if (build == null) return WebUtility.HtmlEncode(slug);

            var title = BuildMapping.PickTranslation(build, Locales.Default)?.Title ?? build.Slug;
            var url = SiteLinks.Build(config, build.Slug);
            return $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(title)}</a> ({WebUtility.HtmlEncode(build.Slug)})";
        }

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/contact", Handle).AllowAnonymous().WithValidation<ContactRequest>();
        }
    }
}
