using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Services
{
    /// <summary>Sends through SjolystInnovation.AppKit.Email, which logs a refused email.</summary>
    public class EmailService(IEmailSender sender) : IEmailService
    {
        public async Task SendEmailAsync(string email, string subject, string message, string? replyTo = null) =>
            await sender.SendAsync(new EmailMessage(email, subject, message) { ReplyTo = replyTo });
    }
}
