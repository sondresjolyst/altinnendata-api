using System.Net;
using System.Text;
using altinnendata_api.Models.Admin;

namespace altinnendata_api.Services
{
    /// <summary>
    /// The seller details at the end of every email to a customer: the registered name, the organisation number
    /// with the register it is entered in, the address, the email and the VAT status.
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
                body.Append($"<br/>{(norwegian ? "Org.nr." : "Org. no.")} {Enc(s.OrgNumber)}");
                body.Append($"<br/>{(norwegian ? "Foretaksregisteret" : "Register of Business Enterprises")}");
            }
            body.Append($"<br/>{Enc(s.FormattedAddress)}");
            body.Append($"<br/>{Enc(s.PublicEmail)}");
            if (!s.VatRegistered)
                body.Append($"<br/>{(norwegian ? "Ikke registrert i Merverdiavgiftsregisteret" : "Not registered in the VAT Register")}");
            body.Append("</p>");
        }

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
