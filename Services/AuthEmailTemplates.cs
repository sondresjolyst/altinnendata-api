using System.Net;

namespace altinnendata_api.Services
{
    public static class AuthEmailTemplates
    {
        private const string ButtonStyle =
            "display:inline-block;padding:12px 20px;border-radius:8px;background:#00887a;color:#ffffff;font-weight:600;text-decoration:none";

        public static string PasswordReset(string companyName, string? firstName, string code, string link)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
            var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hei," : $"Hei {Enc(firstName)},";
            return $@"
<h2>Tilbakestill passordet</h2>
<p>{greeting}</p>
<p>Vi har fått en forespørsel om å tilbakestille passordet ditt hos {Enc(companyName)}. Åpne siden under og velg et nytt passord:</p>
<p><a href=""{Enc(link)}"" style=""{ButtonStyle}"">Velg nytt passord</a></p>
<p>Koden din er:</p>
<p style=""font-size:24px;font-weight:bold;letter-spacing:3px"">{Enc(code)}</p>
<p>Koden utløper om 30 minutter. Har du ikke bedt om dette, kan du se bort fra e-posten — passordet endres ikke.</p>
<p style=""color:#6b7280;font-size:12px"">Virker ikke knappen? Kopier denne lenken: {Enc(link)}</p>";
        }

        public static string Invite(string companyName, string? firstName, string code, string link)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
            var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hei," : $"Hei {Enc(firstName)},";
            return $@"
<h2>Sett passordet ditt</h2>
<p>{greeting}</p>
<p>Det er opprettet en konto for deg hos {Enc(companyName)}. Åpne siden under og velg et passord:</p>
<p><a href=""{Enc(link)}"" style=""{ButtonStyle}"">Sett passord</a></p>
<p>Koden din er:</p>
<p style=""font-size:24px;font-weight:bold;letter-spacing:3px"">{Enc(code)}</p>
<p>Koden utløper om 7 dager.</p>
<p style=""color:#6b7280;font-size:12px"">Virker ikke knappen? Kopier denne lenken: {Enc(link)}</p>";
        }
    }
}
