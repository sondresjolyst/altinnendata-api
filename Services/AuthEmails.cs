using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Services
{
    /// <summary>Sends the auth endpoints' emails with this app's templates.</summary>
    public class AuthEmails(ApplicationDbContext db, IEmailService email, IConfiguration config) : IAuthEmails<User>
    {
        public async Task SendPasswordResetAsync(User user, string code, CancellationToken cancellationToken)
        {
        var settings = await db.AppSettings.FindAsync([1], cancellationToken) ?? new AppSettings();
        var link = SiteLinks.SetPassword(config, user.Email!, code);
        await email.SendEmailAsync(user.Email!, "Tilbakestill passordet",
            AuthEmailTemplates.PasswordReset(settings.CompanyName, user.FirstName, code, link));
        }

        public async Task SendInviteAsync(User user, string code, CancellationToken cancellationToken)
        {
            var settings = await db.AppSettings.FindAsync([1], cancellationToken) ?? new AppSettings();
            var link = SiteLinks.SetPassword(config, user.Email!, code);
            await email.SendEmailAsync(user.Email!, $"Sett passordet ditt hos {settings.CompanyName}",
                AuthEmailTemplates.Invite(settings.CompanyName, user.FirstName, code, link));
        }
    }
}
