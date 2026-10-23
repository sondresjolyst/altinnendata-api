using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Email;
using SjolystInnovation.AppKit.Pdf;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Sends the buyer's order confirmation with the terms and the withdrawal form as PDFs (angrerettloven § 18),
    /// and records it in <see cref="Order.ConfirmationSentAt"/>. Each try is claimed on the order first, so two
    /// instances or a retry never send the same try twice.
    /// </summary>
    public class OrderConfirmation(ApplicationDbContext db, IEmailSender email, IPdfRenderer pdf, IConfiguration config, ILogger<OrderConfirmation> log)
    {
        /// <summary>How many tries a confirmation gets, the first one included.</summary>
        public const int MaxAttempts = 8;

        /// <summary>
        /// Tries once to send the confirmation of order <paramref name="orderId"/>, if it is still unsent and has had
        /// <paramref name="attemptsSeen"/> tries. Returns true when Brevo accepted it. Never throws, except on cancellation.
        /// </summary>
        public async Task<bool> TrySendAsync(int orderId, int attemptsSeen, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var claimed = await db.Orders
                .Where(o => o.Id == orderId && o.AnonymisedAt == null && o.ConfirmationSentAt == null && o.ConfirmationAttempts == attemptsSeen)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.ConfirmationAttempts, attemptsSeen + 1)
                    .SetProperty(o => o.ConfirmationAttemptedAt, now), ct);
            if (claimed == 0) return false;

            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
            var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1, ct) ?? new AppSettings();
            var attempt = attemptsSeen + 1;
            if (string.IsNullOrWhiteSpace(order.BuyerEmail))
            {
                log.LogWarning("Order {OrderId} has no buyer email, so the confirmation is not sent", orderId);
                return false;
            }

            EmailMessage message;
            try
            {
                message = OrderEmails.BuyerConfirmation(order, settings, config);
                message = message with { Attachments = await AttachmentsAsync(order, settings, ct) };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Failed(ex, order, attempt, "rendering the PDFs failed");
                return false;
            }

            EmailResult result;
            try
            {
                result = await email.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Failed(ex, order, attempt, "sending failed");
                return false;
            }

            if (!result.Sent)
            {
                Failed(null, order, attempt, $"Brevo refused it: {result.Error}");
                return false;
            }

            // Brevo has the email, so an aborted request must not lose the record of it.
            await db.Orders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConfirmationSentAt, DateTime.UtcNow), CancellationToken.None);
            log.LogInformation("Sent the order confirmation of order {OrderId} on try {Attempt}", orderId, attempt);
            return true;
        }

        private async Task<List<EmailAttachment>> AttachmentsAsync(Order order, AppSettings settings, CancellationToken ct)
        {
            var attachments = new List<EmailAttachment>();
            if (OrderDocuments.TermsHtml(order, settings) is { } terms)
                attachments.Add(new EmailAttachment(OrderDocuments.FileNameOfTerms(order), await pdf.RenderAsync(terms, ct)));
            else
                log.LogWarning("Order {OrderId} has no terms snapshot, so the confirmation carries only the withdrawal form", order.Id);
            var form = OrderDocuments.WithdrawalFormRequest(order, settings);
            attachments.Add(new EmailAttachment(WithdrawalForm.FileName(form.Language),
                await pdf.RenderAsync(WithdrawalForm.Html(form), ct)));
            return attachments;
        }

        private void Failed(Exception? ex, Order order, int attempt, string reason)
        {
            if (attempt >= MaxAttempts)
                log.LogError(ex, "The order confirmation of order {OrderId} failed on the last try {Attempt}, {Reason}. Send it by hand.",
                    order.Id, attempt, reason);
            else
                log.LogError(ex, "The order confirmation of order {OrderId} failed on try {Attempt}, {Reason}. It is retried later.",
                    order.Id, attempt, reason);
        }
    }

    /// <summary>Finds the reserved and paid orders whose confirmation did not go out, and tries again with backoff.</summary>
    public class OrderConfirmationRetry(ApplicationDbContext db, OrderConfirmation confirmation)
    {
        /// <summary>How long after checkout an order that was never tried counts as missed.</summary>
        public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);

        /// <summary>The wait after try <paramref name="attempts"/> before the next one: 5, 10, 20 minutes and so on.</summary>
        public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Max(0, attempts - 1)));

        public async Task<int> ResendMissingAsync(DateTime now, CancellationToken ct)
        {
            var candidates = await db.Orders.AsNoTracking()
                .Where(o => (o.Status == OrderStatus.Reserved || o.Status == OrderStatus.Paid)
                    && o.AnonymisedAt == null && o.ConfirmationSentAt == null && o.ConfirmationAttempts < OrderConfirmation.MaxAttempts)
                .Select(o => new { o.Id, o.ConfirmationAttempts, o.ConfirmationAttemptedAt, o.CreatedAt })
                .ToListAsync(ct);

            var sent = 0;
            foreach (var o in candidates)
            {
                var due = o.ConfirmationAttempts == 0 || o.ConfirmationAttemptedAt == null
                    ? o.CreatedAt <= now - FirstDelay
                    : o.ConfirmationAttemptedAt.Value + Backoff(o.ConfirmationAttempts) <= now;
                if (due && await confirmation.TrySendAsync(o.Id, o.ConfirmationAttempts, ct)) sent++;
            }
            return sent;
        }
    }

    /// <summary>Runs <see cref="OrderConfirmationRetry"/> every five minutes, so every order ends up with its confirmation.</summary>
    public class OrderConfirmationRetryService(IServiceScopeFactory scopes, ILogger<OrderConfirmationRetryService> log) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // The first pass waits too, so a fresh start does not race the confirmations it is sending.
                await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
                if (stoppingToken.IsCancellationRequested) return;
                try
                {
                    using var scope = scopes.CreateScope();
                    var sent = await scope.ServiceProvider.GetRequiredService<OrderConfirmationRetry>().ResendMissingAsync(DateTime.UtcNow, stoppingToken);
                    if (sent > 0) log.LogInformation("Sent {Count} order confirmations that had failed", sent);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    log.LogError(ex, "The order confirmation retry failed");
                }
            }
        }
    }
}
