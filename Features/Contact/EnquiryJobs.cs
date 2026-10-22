using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Features.Contact
{
    /// <summary>Sends the notices that did not go out, with backoff. It never gives up, so every enquiry ends up announced.</summary>
    public static class EnquiryNoticeRetry
    {
        /// <summary>The wait after try <paramref name="attempts"/> before the next one: 5, 10, 20 minutes and so on, at most an hour.</summary>
        public static TimeSpan Backoff(int attempts) =>
            TimeSpan.FromMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10))));

        /// <summary>
        /// Tries every due unsent notice once and returns how many were sent. A failure on one enquiry is logged to
        /// <paramref name="log"/> and does not stop the rest.
        /// </summary>
        public static async Task<int> ResendMissingAsync(
            ApplicationDbContext db, IEmailSender email, IConfiguration config, DateTime now, CancellationToken ct, ILogger? log = null)
        {
            var candidates = await db.Enquiries.AsNoTracking()
                .Where(e => e.NoticeSentAt == null)
                .OrderBy(e => e.Id)
                .Select(e => new { e.Id, e.NoticeAttempts, e.NoticeAttemptedAt })
                .ToListAsync(ct);

            var sent = 0;
            foreach (var e in candidates)
            {
                var due = e.NoticeAttemptedAt == null || e.NoticeAttemptedAt.Value + Backoff(e.NoticeAttempts) <= now;
                if (!due) continue;
                try
                {
                    if (await SendEnquiry.TrySendNoticeAsync(db, email, config, e.Id, now, ct)) sent++;
                    else log?.LogWarning("The notice of enquiry {EnquiryId} was not sent on retry {Attempt}", e.Id, e.NoticeAttempts + 1);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    log?.LogError(ex, "The notice of enquiry {EnquiryId} could not be retried", e.Id);
                }
            }
            return sent;
        }
    }

    /// <summary>Deletes enquiries 12 months after they were created, and lets go of cases whose owner is no longer an admin.</summary>
    public static class EnquiryRetention
    {
        public static readonly TimeSpan KeepFor = TimeSpan.FromDays(365);

        /// <summary>The most enquiries one run deletes. The rest go in the next run.</summary>
        public const int BatchSize = 500;

        /// <summary>Deletes one batch of enquiries older than <see cref="KeepFor"/>, whatever their status, and returns how many.</summary>
        public static async Task<int> PurgeAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var cutoff = now - KeepFor;
            var ids = await db.Enquiries
                .Where(e => e.CreatedAt < cutoff)
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (ids.Count == 0) return 0;
            return await db.Enquiries.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
        }

        /// <summary>
        /// Clears the owner of every case whose owner was deleted or lost the Admin role, and marks it
        /// <see cref="Enquiry.OwnerRemoved"/>. Returns how many cases changed.
        /// </summary>
        public static Task<int> ReleaseRemovedOwnersAsync(ApplicationDbContext db, CancellationToken ct) =>
            db.Enquiries
                .Where(e => e.AssignedUserId != null && !EnquiryOwners.ActiveAdminIds(db).Contains(e.AssignedUserId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.AssignedUserId, (string?)null)
                    .SetProperty(e => e.OwnerRemoved, true), ct);
    }

    /// <summary>Runs <see cref="EnquiryNoticeRetry"/> every five minutes, so every enquiry ends up announced.</summary>
    public sealed class EnquiryNoticeRetryService(IServiceScopeFactory scopes, ILogger<EnquiryNoticeRetryService> log) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // The first pass waits too, so a fresh start does not race the notices it is sending.
                await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
                if (stoppingToken.IsCancellationRequested) return;
                try
                {
                    using var scope = scopes.CreateScope();
                    var services = scope.ServiceProvider;
                    var sent = await EnquiryNoticeRetry.ResendMissingAsync(
                        services.GetRequiredService<ApplicationDbContext>(),
                        services.GetRequiredService<IEmailSender>(),
                        services.GetRequiredService<IConfiguration>(),
                        DateTime.UtcNow, stoppingToken, log);
                    if (sent > 0) log.LogInformation("Sent {Count} enquiry notices that had failed", sent);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    log.LogError(ex, "The enquiry notice retry failed");
                }
            }
        }
    }

    /// <summary>Runs <see cref="EnquiryRetention"/> at start and then once a day.</summary>
    public sealed class EnquiryRetentionService(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<EnquiryRetentionService> log) : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval, time);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <summary>One run. The owner release and the purge each log their own failure, so one does not skip the other. The next run tries again.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                try
                {
                    var released = await EnquiryRetention.ReleaseRemovedOwnersAsync(db, ct);
                    if (released > 0)
                        log.LogInformation("Released {Count} enquiries whose owner is no longer an admin", released);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogError(ex, "Releasing the owners of enquiries failed, it is tried again tomorrow");
                }
                try
                {
                    var deleted = await EnquiryRetention.PurgeAsync(db, time.GetUtcNow().UtcDateTime, ct);
                    if (deleted > 0)
                        log.LogInformation("Deleted {Count} enquiries older than {Days} days", deleted, EnquiryRetention.KeepFor.TotalDays);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogError(ex, "Deleting old enquiries failed, it is tried again tomorrow");
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Cleaning up enquiries failed, it is tried again tomorrow");
            }
        }
    }
}
