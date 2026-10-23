using System.Globalization;
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
                .Must(EmailAddress.IsSingle).WithMessage("'{PropertyName}' is not a valid email address.")
                .WithName("Email")
                .OverridePropertyName(EmailField);
            RuleFor(x => x.Phone).MaximumLength(30);
            RuleFor(x => x.UseCase).MaximumLength(160);
            RuleFor(x => x.BudgetNok).Must(v => AllowedBudgetRanges.Contains(v)).When(x => x.BudgetNok != null);
            RuleFor(x => x.BuildSlug).MaximumLength(160);
            RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
            RuleFor(x => x.Locale).MaximumLength(10);

            // Postgres refuses a text value with a NUL character, which would turn a bad request into a server error.
            RuleFor(x => x.Name).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.Email).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.").WithName("Email").OverridePropertyName(EmailField);
            RuleFor(x => x.Phone).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.UseCase).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.BudgetNok).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.BuildSlug).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.Message).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
            RuleFor(x => x.Locale).Must(HasNoNul).WithMessage("'{PropertyName}' must not contain NUL characters.");
        }

        private static bool HasNoNul(string? value) => value is null || !value.Contains('\0');

        public const string EmailField = "email";
    }

    /// <summary>Sends a contact / build enquiry to the configured recipient, and a receipt to the sender.</summary>
    public static class SendEnquiry
    {
        internal const string NoReceiptNote =
            "Ingen kvittering sendt til avsenderen. Bekreft mottak selv hvis meldingen gjelder angrerett.";

        /// <summary>
        /// The limits on receipts: one per mailbox in 10 minutes, and at most 30 in an hour across all addresses.
        /// The owner's copy of an enquiry is never held back by them.
        /// </summary>
        public static void ReceiptLimits(EmailReceiptGateOptions o)
        {
            o.AddressWindow = TimeSpan.FromMinutes(10);
            o.GlobalWindow = TimeSpan.FromHours(1);
            o.GlobalLimit = 30;
        }

        private static readonly Lazy<TimeZoneInfo> Oslo = new(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        });

        /// <summary>
        /// Saves the enquiry, then sends the notice and the receipt. A failed email never fails the request, and a
        /// receipt that did not go out is recorded on the enquiry. <paramref name="aborted"/> is deliberately ignored
        /// once the request is valid, so an aborted request still saves.
        /// </summary>
        public static async Task<IResult> Handle(
            ContactRequest req,
            ApplicationDbContext db,
            IEmailSender email,
            IConfiguration config,
            EmailReceiptGate receipts,
            ILogger<ContactRequest> log,
            CancellationToken aborted)
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

            try
            {
                if (!await TrySendNoticeAsync(db, email, config, enquiry.Id, now, ct))
                    log.LogWarning("The notice of enquiry {EnquiryId} was not sent. The retry job will try again", enquiry.Id);
            }
            catch (Exception ex)
            {
                // The enquiry is saved, so a database error here must not fail the request and invite a resubmit.
                log.LogError(ex, "The notice of enquiry {EnquiryId} could not be attempted", enquiry.Id);
            }

            if (receipt && !await TrySendReceiptAsync(email, BuildReceipt(sender, req.Locale, settings, now), enquiry.Id, log, ct))
            {
                try
                {
                    await db.Enquiries.Where(e => e.Id == enquiry.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReceiptSent, false), ct);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Could not record that the receipt of enquiry {EnquiryId} was not sent", enquiry.Id);
                }
            }

            return TypedResults.Ok(new MessageResponse("Thanks. We will be in touch."));
        }

        private static async Task<bool> TrySendReceiptAsync(IEmailSender email, EmailMessage receipt, int enquiryId, ILogger log, CancellationToken ct)
        {
            try
            {
                var result = await email.SendAsync(receipt, ct);
                if (result.Sent) return true;
                log.LogWarning("The receipt of enquiry {EnquiryId} was refused: {Error}", enquiryId, result.Error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "The receipt of enquiry {EnquiryId} could not be sent", enquiryId);
            }
            return false;
        }

        /// <summary>How long a claimed attempt is left alone, so a try still sending is not taken over by another.</summary>
        public static readonly TimeSpan NoticeLease = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Sends the notice of enquiry <paramref name="enquiryId"/> if it is still unsent and no attempt was claimed
        /// within <see cref="NoticeLease"/>. The attempt is claimed on the row first, so two instances or a retry
        /// never send the same try twice. Returns true when the email was
        /// accepted. A failed or throwing send gives false. A database error or cancellation reaches the caller, who logs it. Logs nothing itself.
        /// </summary>
        public static async Task<bool> TrySendNoticeAsync(
            ApplicationDbContext db, IEmailSender email, IConfiguration config, int enquiryId, DateTime now, CancellationToken ct)
        {
            var leaseEnded = now - NoticeLease;
            var seen = await db.Enquiries.AsNoTracking()
                .Where(e => e.Id == enquiryId && e.NoticeSentAt == null && (e.NoticeAttemptedAt == null || e.NoticeAttemptedAt <= leaseEnded))
                .Select(e => (int?)e.NoticeAttempts)
                .FirstOrDefaultAsync(ct);
            if (seen == null) return false;

            var claimed = await db.Enquiries
                .Where(e => e.Id == enquiryId && e.NoticeSentAt == null && e.NoticeAttempts == seen
                    && (e.NoticeAttemptedAt == null || e.NoticeAttemptedAt <= leaseEnded))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.NoticeAttempts, seen.Value + 1)
                    .SetProperty(e => e.NoticeAttemptedAt, now), ct);
            if (claimed == 0) return false;

            try
            {
                var enquiry = await db.Enquiries.AsNoTracking().FirstAsync(e => e.Id == enquiryId, ct);
                var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1, ct) ?? new AppSettings();
                if (!(await email.SendAsync(BuildNotice(enquiry, settings, config), ct)).Sent) return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return false;
            }

            // The email is out, so a failed record of it must not fail the caller. It is swallowed here because this
            // method has no logger and its signature is fixed, and the retry job would resend the notice.
            try
            {
                await db.Enquiries.Where(e => e.Id == enquiryId)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.NoticeSentAt, now), CancellationToken.None);
            }
            catch (Exception)
            {
                return true;
            }
            return true;
        }

        /// <summary>
        /// The notice to the owner. It holds no name, address, phone or message, only a link to the enquiry in the
        /// admin. When <see cref="EmailReceiptGate"/> held the receipt back, it says so, since a withdrawal sent
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
