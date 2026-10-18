using System.Globalization;
using System.Net;
using System.Text;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Builds the order emails. The buyer emails follow <see cref="Order.Locale"/>, the owner notice is Norwegian.
    /// Every order value is HTML encoded.
    /// </summary>
    public static class OrderEmails
    {
        private static readonly Lazy<TimeZoneInfo> Oslo = new(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        });

        public static EmailMessage BuyerConfirmation(Order o, AppSettings s, IConfiguration config)
        {
            var no = IsNorwegian(o);
            var body = new StringBuilder();
            body.Append($"<h2>{(no ? "Takk for bestillingen" : "Thank you for your order")}</h2>");
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? "<p>Betalingen er reservert i Vipps. Pengene trekkes når du får varen utlevert eller sendt.</p>"
                : "<p>The payment is reserved in Vipps. The money is taken when the item is handed over or shipped.</p>");

            AppendLines(body, o, no);

            if (o.DeliveryMethod == DeliveryMethod.Pickup)
            {
                body.Append($"<p><strong>{(no ? "Henting" : "Pickup")}</strong></p>");
                if (!string.IsNullOrWhiteSpace(o.PickupNote))
                    body.Append($"<p>{EncMultiline(o.PickupNote)}</p>");
                body.Append(no
                    ? "<p>Selger tar kontakt med deg om henting.</p>"
                    : "<p>The seller will be in touch about pickup.</p>");
            }
            else
            {
                body.Append($"<p><strong>{(no ? "Leveringsadresse" : "Delivery address")}</strong><br/>{AddressHtml(o)}</p>");
                body.Append(no
                    ? "<p>Selger tar kontakt med deg om levering.</p>"
                    : "<p>The seller will be in touch about delivery.</p>");
            }

            var terms = Enc(SiteLinks.Terms(config, o.Locale));
            body.Append(no
                ? $"<p>Les <a href=\"{terms}\">vilkårene</a>. Du har 14 dagers angrerett fra du mottar varen.</p>"
                : $"<p>Read the <a href=\"{terms}\">terms</a>. You have a 14 day right of withdrawal from the day you receive the item.</p>");

            AppendFooter(body, s, no);
            return new EmailMessage(o.BuyerEmail, no ? $"Ordrebekreftelse {o.Reference}" : $"Order confirmation {o.Reference}", body.ToString());
        }

        public static EmailMessage OwnerNewOrder(Order o, AppSettings s, IConfiguration config)
        {
            var body = new StringBuilder();
            body.Append("<h2>Ny ordre</h2>");
            body.Append($"<p><a href=\"{Enc(SiteLinks.AdminOrder(config, o.Id))}\">Åpne ordren i admin</a></p>");

            void Row(string label, string value) => body.Append($"<p><strong>{label}:</strong> {value}</p>");

            Row("Ordrenummer og Vipps-referanse", Enc(o.Reference));
            Row("Navn", Enc(o.BuyerName));
            Row("E-post", Enc(o.BuyerEmail));
            Row("Telefon", Enc(o.BuyerPhone));
            if (o.DeliveryMethod == DeliveryMethod.Pickup)
            {
                Row("Levering", Enc(o.DeliveryLabel));
                if (!string.IsNullOrWhiteSpace(o.PickupNote)) Row("Henting", EncMultiline(o.PickupNote));
            }
            else
            {
                Row("Adresse", AddressHtml(o));
            }

            body.Append("<p><strong>Fakturalinjer</strong></p>");
            body.Append($"<p>1. {Enc(o.BuildTitle)}: {Kroner(o.BuildPriceInOre, "no")} kr<br/>");
            body.Append($"2. {Enc(o.DeliveryLabel)}: {Kroner(o.DeliveryPriceInOre, "no")} kr</p>");
            Row("Totalt", $"{Kroner(o.TotalInOre, "no")} kr");
            Row("Bestilt", OsloDate(o.CreatedAt));
            Row("Betaling reservert", o.AuthorizedAt is { } at ? OsloDate(at) : "Ukjent");
            body.Append("<p>Ikke merverdiavgiftsregistrert, ingen mva.</p>");

            return new EmailMessage(s.ContactRecipientEmail, $"Ny ordre {o.Reference}: {o.BuildTitle}", body.ToString())
            {
                ReplyTo = o.BuyerEmail,
            };
        }

        public static EmailMessage BuyerCancelled(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var body = new StringBuilder();
            body.Append($"<h2>{(no ? "Ordren er kansellert" : "Your order is cancelled")}</h2>");
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? "<p>Reservasjonen i Vipps er frigitt. Ingen penger er trukket.</p>"
                : "<p>The reservation in Vipps is released. No money has been taken.</p>");
            AppendFooter(body, s, no);
            return new EmailMessage(o.BuyerEmail, no ? $"Ordre {o.Reference} er kansellert" : $"Order {o.Reference} is cancelled", body.ToString());
        }

        public static EmailMessage BuyerRefunded(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var amount = Kroner(o.RefundedInOre > 0 ? o.RefundedInOre : o.TotalInOre, o.Locale);
            var body = new StringBuilder();
            body.Append($"<h2>{(no ? "Pengene er betalt tilbake" : "Your money is refunded")}</h2>");
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? $"<p>Vi har betalt tilbake {amount} kr til Vipps. Det kan ta noen dager før beløpet vises hos deg.</p>"
                : $"<p>We have refunded {amount} kr to Vipps. It can take a few days before the amount appears in your account.</p>");
            AppendFooter(body, s, no);
            return new EmailMessage(o.BuyerEmail, no ? $"Pengene for ordre {o.Reference} er betalt tilbake" : $"Order {o.Reference} is refunded", body.ToString());
        }

        private static void AppendLines(StringBuilder body, Order o, bool no)
        {
            body.Append("<table cellpadding=\"4\" cellspacing=\"0\">");
            body.Append($"<tr><td>{Enc(o.BuildTitle)}</td><td align=\"right\">{Kroner(o.BuildPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td>{Enc(o.DeliveryLabel)}</td><td align=\"right\">{Kroner(o.DeliveryPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td><strong>{(no ? "Totalt" : "Total")}</strong></td><td align=\"right\"><strong>{Kroner(o.TotalInOre, o.Locale)} kr</strong></td></tr>");
            body.Append("</table>");
        }

        private static void AppendFooter(StringBuilder body, AppSettings s, bool no)
        {
            var name = string.IsNullOrWhiteSpace(s.CompanyLegalName) ? s.CompanyName : s.CompanyLegalName;
            body.Append("<hr/><p>");
            body.Append($"<strong>{Enc(name)}</strong>");
            if (!string.IsNullOrWhiteSpace(s.OrgNumber))
                body.Append($"<br/>{(no ? "Org.nr." : "Org. no.")} {Enc(s.OrgNumber)}");
            body.Append($"<br/>{Enc(s.FormattedAddress)}");
            body.Append($"<br/>{Enc(s.PublicEmail)}");
            if (!s.VatRegistered)
                body.Append($"<br/>{(no ? "Ikke merverdiavgiftsregistrert" : "Not VAT registered")}");
            body.Append("</p>");
        }

        private static string AddressHtml(Order o)
        {
            var cityLine = string.Join(" ", new[] { o.PostalCode, o.City }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return string.Join("<br/>", new[] { o.StreetAddress, cityLine }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(Enc));
        }

        private static bool IsNorwegian(Order o) => !string.Equals(o.Locale, "en", StringComparison.OrdinalIgnoreCase);

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string EncMultiline(string value) => Enc(value).Replace("\n", "<br/>");

        private static string OsloDate(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Oslo.Value)
                .ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        /// <summary>
        /// Kroner with two decimals. The API runs with invariant globalization, so the Norwegian
        /// grouping (narrow no-break space) and decimal comma are applied by hand.
        /// </summary>
        private static string Kroner(int ore, string locale)
        {
            var text = (ore / 100m).ToString("N2", CultureInfo.InvariantCulture);
            return string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
                ? text
                : text.Replace(',', ' ').Replace('.', ',');
        }
    }
}
