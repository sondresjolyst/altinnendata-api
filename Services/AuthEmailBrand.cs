using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Services
{
    /// <summary>Gives the auth emails the company details from the admin settings.</summary>
    public class AuthEmailBrand(ApplicationDbContext db) : IEmailBrand
    {
        public async Task<EmailBrand> GetBrandAsync(CancellationToken cancellationToken)
        {
            var s = await db.AppSettings.FindAsync([1], cancellationToken) ?? new AppSettings();
            return new EmailBrand(s.CompanyName)
            {
                LegalName = s.CompanyLegalName,
                OrgNumber = s.OrgNumber,
                VatRegistered = s.VatRegistered,
                Address = s.FormattedAddress,
                Email = s.PublicEmail,
            };
        }
    }
}
