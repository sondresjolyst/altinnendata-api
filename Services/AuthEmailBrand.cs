using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Auth;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Services
{
    /// <summary>Gives the auth emails the company details from the admin settings, the same as the shop emails.</summary>
    public class AuthEmailBrand(ApplicationDbContext db) : IEmailBrand
    {
        public async Task<EmailBrand> GetBrandAsync(CancellationToken cancellationToken)
        {
            var s = await db.AppSettings.FindAsync([1], cancellationToken) ?? new AppSettings();
            return BrandedEmail.Brand(s);
        }
    }
}
