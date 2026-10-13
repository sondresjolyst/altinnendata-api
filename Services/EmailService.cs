using altinnendata_api.Features.Admin;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Services
{
    /// <summary>Sends through SjolystInnovation.AppKit.Email, which logs a refused email.</summary>
    public class EmailService(IEmailSender sender, IEmailStats stats) : IEmailService
    {
        public async Task SendEmailAsync(string email, string subject, string message, string? replyTo = null) =>
            await sender.SendAsync(new EmailMessage(email, subject, message) { ReplyTo = replyTo });

        public async Task<EmailStatsDto> GetEmailStatsAsync(int days = 30)
        {
            var s = await stats.GetAsync(days);
            return new EmailStatsDto
            {
                Days = s.Days,
                Requests = s.Requests,
                Delivered = s.Delivered,
                HardBounces = s.HardBounces,
                SoftBounces = s.SoftBounces,
                SpamReports = s.SpamReports,
                Blocked = s.Blocked,
                Invalid = s.Invalid,
            };
        }
    }
}
