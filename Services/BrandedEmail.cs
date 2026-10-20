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

        /// <summary>The inline style of links in the body, in the accent colour like the button.</summary>
        public const string LinkStyle = "color:" + AccentColor;

        /// <summary>The register the company is entered in, shown after the organisation number.</summary>
        public const string RegisterName = "Foretaksregisteret";

        public static EmailCompany Company(AppSettings s) => new(s.CompanyName)
        {
            LegalName = s.CompanyLegalName,
            OrgNumber = s.OrgNumber,
            VatRegistered = s.VatRegistered,
            RegisterName = RegisterName,
            Address = s.FormattedAddress,
            Email = s.PublicEmail,
        };

        /// <summary>The footer of the buyer's order emails, naming the registered name, or the company name until there is one.</summary>
        public static string OrderFooter(AppSettings s, bool norwegian)
        {
            var name = string.IsNullOrWhiteSpace(s.CompanyLegalName) ? s.CompanyName : s.CompanyLegalName;
            return norwegian
                ? $"Du får denne e-posten fordi du har bestilt fra {name}."
                : $"You receive this email because you ordered from {name}.";
        }

        /// <summary>The footer of the receipt for a contact message.</summary>
        public static string MessageFooter(bool norwegian) => norwegian
            ? "Du får denne e-posten fordi du sendte oss en melding."
            : "You receive this email because you sent us a message.";

        /// <summary>
        /// Renders the email in the layout. <paramref name="bodyHtml"/> and <paramref name="afterButtonHtml"/> must
        /// already be encoded. They get the layout's text style. <paramref name="footer"/> is plain text.
        /// </summary>
        public static string Html(AppSettings s, bool norwegian, string badge, string heading, string bodyHtml,
            EmailButton? button = null, string? afterButtonHtml = null, string? footer = null) =>
            EmailLayout.Render(new EmailLayoutRequest(Company(s), norwegian ? EmailLayoutLanguage.Norwegian : EmailLayoutLanguage.English,
                badge, heading, Styled(bodyHtml))
            {
                HeaderColor = HeaderColor,
                AccentColor = AccentColor,
                Button = button,
                AfterButtonHtml = Styled(afterButtonHtml),
                Footer = footer,
            });

        private static string Styled(string? html) =>
            string.IsNullOrEmpty(html) ? "" : $"<div style=\"{EmailLayout.TextStyle}\">{html}</div>";
    }
}
