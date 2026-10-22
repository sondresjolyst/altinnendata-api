using System.Globalization;
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

        /// <summary>
        /// Saves the enquiry, then sends the notice and the receipt. Once the request is valid nothing here is
        /// cancelled, so an aborted request still saves. A failed email never fails the request.
        /// </summary>
        public static async Task<IResult> Handle(
            ContactRequest req,
            ApplicationDbContext db,
            IEmailSender email,
            IConfiguration config,
            ContactReceiptGate receipts,
            CancellationToken _)
        {
            var ct = CancellationToken.None;
            var sender = req.Email.Trim();
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();

            var build = string.IsNullOrWhiteSpace(req.BuildSlug)
                ? null
                : await db.PcBuilds
                    .Include(b => b.Translations)
                    .FirstOrDefaultAsync(b => b.Slug == req.BuildSlug, ct);

            var now = DateTime.UtcNow;
            var receipt = receipts.TryAcquire(sender);
            var enquiry = new Enquiry
            {
                CreatedAt = now,
                UpdatedAt = now,
                Name = req.Name,
                Email = sender,
                Phone = req.Phone,
                UseCase = req.UseCase,
                BudgetNok = req.BudgetNok,
                BuildSlug = req.BuildSlug,
                BuildTitle = build == null ? null : BuildMapping.PickTranslation(build, Locales.Default)?.Title,
                Locale = Locales.Normalize(req.Locale),
                Message = req.Message,
                ReceiptSent = receipt,
                Status = EnquiryStatus.New,
            };
            db.Enquiries.Add(enquiry);
            await db.SaveChangesAsync(ct);

            await TrySendNoticeAsync(db, email, config, enquiry.Id, now, ct);

            if (receipt)
            {
                try
                {
                    await email.SendAsync(BuildReceipt(sender, req.Locale, settings, now), ct);
                }
                catch (Exception)
                {
                    // The enquiry is saved, and a lost receipt must not fail the request.
                }
            }

            return TypedResults.Ok(new MessageResponse("Thanks. We will be in touch."));
        }

        /// <summary>
        /// Sends the notice for a saved enquiry and sets <see cref="Enquiry.NoticeSentAt"/> when it went out.
        /// Every call counts as an attempt. Returns false when the send failed or threw.
        /// </summary>
        public static async Task<bool> TrySendNoticeAsync(
            ApplicationDbContext db, IEmailSender email, IConfiguration config, int enquiryId, DateTime now, CancellationToken ct)
        {
            var enquiry = await db.Enquiries.FindAsync([enquiryId], ct);
            if (enquiry == null) return false;

            var sent = false;
            try
            {
                var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
                var result = await email.SendAsync(BuildNotice(enquiry, settings, config), ct);
                sent = result.Sent;
            }
            catch (Exception)
            {
                // Counted as a failed attempt below, so the retry job picks it up.
            }

            enquiry.NoticeAttempts++;
            enquiry.NoticeAttemptedAt = now;
            if (sent) enquiry.NoticeSentAt = now;
            await db.SaveChangesAsync(ct);
            return sent;
        }

        /// <summary>
        /// The notice to the owner. It holds no name, address, phone or message, only a link to the enquiry in the
        /// admin. When <see cref="ContactReceiptGate"/> held the receipt back, it says so, since a withdrawal sent
        /// through the form then has to be confirmed by hand.
        /// </summary>
        internal static EmailMessage BuildNotice(Enquiry enquiry, AppSettings settings, IConfiguration config)
        {
            var body = "<p>En ny henvendelse har kommet inn.</p>"
                + (enquiry.ReceiptSent ? "" : $"<p><strong>{NoReceiptNote}</strong></p>");
            var html = BrandedEmail.Html(settings, true, "Henvendelse", "Ny henvendelse", body,
                new EmailButton("Åpne i admin", SiteLinks.AdminEnquiry(config, enquiry.Id)));
            return new EmailMessage(settings.ContactRecipientEmail, "Ny henvendelse", html);
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
            var body = new StringBuilder();
            body.Append(no
                ? $"<p>Vi har mottatt meldingen din {local.ToString("dd.MM.yyyy 'kl.' HH:mm", CultureInfo.InvariantCulture)} og svarer så snart vi kan.</p>"
                : $"<p>We received your message on {local.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)} and will reply as soon as we can.</p>");
            var html = BrandedEmail.Html(settings, no, no ? "Melding mottatt" : "Message received", subject, body.ToString(),
                footer: BrandedEmail.MessageFooter(no));
            return new EmailMessage(to, subject, html) { ReplyTo = settings.PublicEmail };
        }

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/contact", Handle).AllowAnonymous().WithValidation<ContactRequest>();
        }
    }
}
