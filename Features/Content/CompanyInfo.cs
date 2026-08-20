using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;

namespace altinnendata_api.Features.Content
{
    /// <summary>
    /// <paramref name="Address"/> is the one-line form for display; the parts beside it are what
    /// structured data needs, since a search engine cannot split a free-text line reliably.
    /// </summary>
    public record CompanyInfoResponse(
        string Name,
        string LegalName,
        string OrgNumber,
        bool VatRegistered,
        string Address,
        string StreetAddress,
        string PostalCode,
        string AddressLocality,
        string AddressRegion,
        string Email,
        string Phone);

    /// <summary>Public company info (name, legal name, org number, VAT status, address, contact) for SSR pages and structured data.</summary>
    public static class CompanyInfo
    {
        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
            var legalName = string.IsNullOrWhiteSpace(settings.CompanyLegalName)
                ? settings.CompanyName
                : settings.CompanyLegalName;

            return TypedResults.Ok(new CompanyInfoResponse(
                settings.CompanyName,
                legalName,
                settings.OrgNumber,
                settings.VatRegistered,
                settings.FormattedAddress,
                settings.StreetAddress,
                settings.PostalCode,
                settings.AddressLocality,
                settings.AddressRegion,
                settings.PublicEmail,
                settings.PublicPhone));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/api/company", Get).AllowAnonymous();
        }
    }
}
