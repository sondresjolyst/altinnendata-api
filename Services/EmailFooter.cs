using System.Net;
using System.Text;
using altinnendata_api.Models.Admin;

namespace altinnendata_api.Services
{
    /// <summary>
    /// The seller details at the end of every email to a customer: the registered name, the organisation number
    /// with the register it is entered in, the address and the email. The number gets " MVA" when the company is
    /// VAT registered, the same as the website footer.
    /// </summary>
    public static class EmailFooter
    {
        public static void Append(StringBuilder body, AppSettings s, bool norwegian)
        {
            var name = string.IsNullOrWhiteSpace(s.CompanyLegalName) ? s.CompanyName : s.CompanyLegalName;
            body.Append("<hr/><p>");
            body.Append($"<strong>{Enc(name)}</strong>");
            if (!string.IsNullOrWhiteSpace(s.OrgNumber))
            {
                var number = s.VatRegistered ? $"{s.OrgNumber.Trim()} MVA" : s.OrgNumber.Trim();
                body.Append($"<br/>{(norwegian ? "Org.nr." : "Org. no.")} {Enc(number)} Foretaksregisteret");
            }
            body.Append($"<br/>{Enc(s.FormattedAddress)}");
            body.Append($"<br/>{Enc(s.PublicEmail)}");
            body.Append("</p>");
        }

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
