using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Services
{
    /// <summary>
    /// The site's email look, shared by the auth emails and the shop and contact emails: the colours, and the
    /// company details from the admin settings for the header of the layout.
    /// </summary>
    public static class BrandedEmail
    {
        public const string HeaderColor = "#0f3d3a";
        public const string AccentColor = "#00887a";

        /// <summary>The register the company is entered in, shown after the organisation number.</summary>
        public const string RegisterName = "Foretaksregisteret";

        public static EmailBrand Brand(AppSettings s) => new(s.CompanyName)
        {
            LegalName = s.CompanyLegalName,
            OrgNumber = s.OrgNumber,
            VatRegistered = s.VatRegistered,
            RegisterName = RegisterName,
            Address = s.FormattedAddress,
            Email = s.PublicEmail,
        };

        /// <summary>
        /// Renders the email in the layout. <paramref name="bodyHtml"/> and <paramref name="afterButtonHtml"/> must
        /// already be encoded. They get the layout's text style.
        /// </summary>
        public static string Html(AppSettings s, bool norwegian, string badge, string heading, string bodyHtml,
            EmailButton? button = null, string? afterButtonHtml = null) =>
            EmailLayout.Render(new EmailLayoutRequest(Brand(s), norwegian ? EmailLanguage.Norwegian : EmailLanguage.English,
                badge, heading, Styled(bodyHtml))
            {
                HeaderColor = HeaderColor,
                AccentColor = AccentColor,
                Button = button,
                AfterButtonHtml = Styled(afterButtonHtml),
            });

        private static string Styled(string? html) =>
            string.IsNullOrEmpty(html) ? "" : $"<div style=\"{EmailLayout.TextStyle}\">{html}</div>";
    }
}
