namespace altinnendata_api.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string email, string subject, string message, string? replyTo = null);
    }
}
