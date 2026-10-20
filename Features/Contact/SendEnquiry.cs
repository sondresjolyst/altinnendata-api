using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Features.Builds;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Features.Contact
{
    public record ContactRequest(
        string Name,
        string Email,
        string? Phone,
        string? UseCase,
        string? BudgetNok,
        string? BuildSlug,
        string Message,
        string? Locale = null);

    public class ContactRequestValidator : AbstractValidator<ContactRequest>
    {
        private static readonly string[] AllowedBudgetRanges =
            ["5000-10000", "10000-15000", "15000-25000", "25000+"];

        public ContactRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(200)
                .Must(IsSingleAddress).WithMessage("'{PropertyName}' is not a valid email address.")
                .WithName("Email")
                .OverridePropertyName(EmailField);
            RuleFor(x => x.Phone).MaximumLength(30);
            RuleFor(x => x.UseCase).MaximumLength(160);
            RuleFor(x => x.BudgetNok).Must(v => AllowedBudgetRanges.Contains(v)).When(x => x.BudgetNok != null);
            RuleFor(x => x.BuildSlug).MaximumLength(160);
            RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
            RuleFor(x => x.Locale).MaximumLength(10);
        }

        public const string EmailField = "email";

        /// <summary>
        /// List separators and header characters, plus the quoted local parts, backslash escapes and domain
        /// literals that let one mailbox be spelled many ways.
        /// </summary>
        private static readonly char[] Forbidden = [',', ';', '<', '>', '\r', '\n', '"', '\\', '[', ']'];

        /// <summary>
        /// True when <paramref name="value"/> is one plain address and nothing else, so it cannot add recipients
        /// or a display name to the emails it is used in.
        /// </summary>
        public static bool IsSingleAddress(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed) || value!.IndexOfAny(Forbidden) >= 0 || trimmed.EndsWith('.')) return false;
            try
            {
                return new MailAddress(trimmed).Address == trimmed;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Sends a contact / build enquiry to the configured recipient, and a receipt to the sender.</summary>
    public static class SendEnquiry
    {
        internal const string NoReceiptNote =
            "Ingen kvittering sendt til avsenderen. Bekreft mottak selv hvis meldingen gjelder angrerett.";

        private static readonly Lazy<TimeZoneInfo> Oslo = new(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        });

        public static async Task<IResult> Handle(
            ContactRequest req,
            ApplicationDbContext db,
            IEmailSender email,
            IConfiguration config,
            ContactReceiptGate receipts,
            CancellationToken ct)
        {
            var sender = req.Email.Trim();
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();

            var build = string.IsNullOrWhiteSpace(req.BuildSlug)
                ? null
                : await db.PcBuilds
                    .Include(b => b.Translations)
                    .FirstOrDefaultAsync(b => b.Slug == req.BuildSlug, ct);

            var receipt = receipts.TryAcquire(sender);
            await email.SendAsync(
                new EmailMessage(settings.ContactRecipientEmail, $"New enquiry from {req.Name}", BuildBody(req, build, config, receipt))
                {
                    ReplyTo = sender,
                },
                ct);

            if (receipt)
                await email.SendAsync(BuildReceipt(sender, req.Locale, settings, DateTime.UtcNow), ct);

            return TypedResults.Ok(new MessageResponse("Thanks. We will be in touch."));
        }

        /// <summary>
        /// The owner's copy. When <see cref="ContactReceiptGate"/> held the receipt back, it says so, since a
        /// withdrawal sent through the form then has to be confirmed by hand.
        /// </summary>
        private static string BuildBody(ContactRequest req, PcBuild? build, IConfiguration config, bool receiptSent)
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
            Row("Budget", req.BudgetNok is { } budget ? $"{budget} NOK" : null);
            Row("Build", BuildLink(req.BuildSlug, build, config));

            body.Append("<p><strong>Message:</strong></p>");
            body.Append($"<p>{Enc(req.Message).Replace("\n", "<br/>")}</p>");
            if (!receiptSent)
                body.Append($"<p><strong>{NoReceiptNote}</strong></p>");
            return body.ToString();
        }

        /// <summary>
        /// The receipt to the sender, in the locale of the page they wrote from, so a withdrawal sent through the
        /// form is confirmed at once. It holds nothing the sender typed, so it cannot carry text to someone else.
        /// </summary>
        internal static EmailMessage BuildReceipt(string to, string? locale, AppSettings settings, DateTime receivedUtc)
        {
            var no = Locales.Normalize(locale) == "no";
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(receivedUtc, DateTimeKind.Utc), Oslo.Value);
            var subject = no ? "Vi har mottatt meldingen din" : "We have received your message";
            var body = new StringBuilder($"<h2>{subject}</h2>");
            body.Append(no
                ? $"<p>Vi har mottatt meldingen din {local.ToString("dd.MM.yyyy 'kl.' HH:mm", CultureInfo.InvariantCulture)} og svarer så snart vi kan.</p>"
                : $"<p>We received your message on {local.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)} and will reply as soon as we can.</p>");
            EmailFooter.Append(body, settings, no);

            return new EmailMessage(to, subject, body.ToString()) { ReplyTo = settings.PublicEmail };
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
