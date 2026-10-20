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
                ? "<p>Beløpet er reservert i Vipps.</p>"
                : "<p>The amount is reserved in Vipps.</p>");

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
            var email = Enc(s.PublicEmail);
            var withTerms = !string.IsNullOrWhiteSpace(o.TermsMarkdown);
            body.Append(no
                ? "<p><strong>Angrerett</strong><br/>Du har 14 dagers angrerett fra den dagen du mottar varen. "
                  + $"Bruk det vedlagte angrerettskjemaet, eller gi oss beskjed på e-post til <a href=\"mailto:{email}\">{email}</a>. "
                  + (withTerms ? "Vilkårene du godtok og angrerettskjemaet ligger vedlagt.</p>" : "Angrerettskjemaet ligger vedlagt.</p>")
                : "<p><strong>Right of withdrawal</strong><br/>You have 14 days to withdraw from the day you receive the item. "
                  + $"Use the attached withdrawal form, or tell us by email at <a href=\"mailto:{email}\">{email}</a>. "
                  + (withTerms ? "The terms you accepted and the withdrawal form are attached.</p>" : "The withdrawal form is attached.</p>"));
            body.Append(no
                ? $"<p>Les <a href=\"{terms}\">vilkårene</a> på nettsiden.</p>"
                : $"<p>Read the <a href=\"{terms}\">terms</a> on the website.</p>");

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
            if (!s.VatRegistered) body.Append("<p>Ikke merverdiavgiftsregistrert, ingen mva.</p>");

            return new EmailMessage(s.ContactRecipientEmail, $"Ny ordre {o.Reference}: {o.BuildTitle}", body.ToString())
            {
                ReplyTo = o.BuyerEmail,
            };
        }

        /// <summary>
        /// Tells the owner that Vipps ended the reservation of a reserved order. The build is still held for the order,
        /// so the owner contacts the buyer or releases it.
        /// </summary>
        public static EmailMessage OwnerReservationEnded(Order o, string vippsEvent, AppSettings s, IConfiguration config)
        {
            var how = vippsEvent switch
            {
                "EXPIRED" => "Reservasjonen utløp i Vipps",
                "ABORTED" => "Betalingen ble avbrutt i Vipps",
                "TERMINATED" => "Betalingen ble avsluttet i Vipps",
                "CANCELLED" => "Reservasjonen ble kansellert i Vipps",
                _ => "Reservasjonen ble avsluttet i Vipps"
            };
            var when = o.ReservationEndedAt is { } at ? $" {OsloDate(at)}" : "";
            var subject = $"Reservasjonen for ordre {o.Reference} er borte";
            var body = new StringBuilder();
            body.Append($"<h2>{Enc(subject)}</h2>");
            body.Append($"<p>{how}{when} ({Enc(vippsEvent)}). Det var ikke du som kansellerte den.</p>");
            body.Append($"<p>Datamaskinen {Enc(o.BuildTitle)} er fortsatt holdt av ordren og vises ikke som tilgjengelig.</p>");
            body.Append("<p>Ta kontakt med kjøperen og avtal videre, eller frigi datamaskinen fra ordren i admin hvis salget ikke blir noe av.</p>");
            body.Append($"<p><strong>Kjøper:</strong> {Enc(o.BuyerName)}, {Enc(o.BuyerEmail)}, {Enc(o.BuyerPhone)}</p>");
            body.Append($"<p><a href=\"{Enc(SiteLinks.AdminOrder(config, o.Id))}\">Åpne ordren i admin</a></p>");
            return new EmailMessage(s.ContactRecipientEmail, subject, body.ToString())
            {
                ReplyTo = o.BuyerEmail,
            };
        }

        /// <summary>Tells the buyer that the payment reservation in Vipps has ended and that the seller will be in touch.</summary>
        public static EmailMessage BuyerReservationEnded(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var subject = no
                ? $"Betalingsreservasjonen for ordre {o.Reference} er avsluttet"
                : $"The payment reservation for order {o.Reference} has ended";
            var body = new StringBuilder();
            body.Append($"<h2>{Enc(subject)}</h2>");
            body.Append($"<p>{(no ? "Vare" : "Item")}: {Enc(o.BuildTitle)}</p>");
            body.Append(no
                ? "<p>Reservasjonen av betalingen i Vipps er avsluttet. Selger tar kontakt med deg om ordren.</p>"
                : "<p>The reservation of the payment in Vipps has ended. The seller will contact you about the order.</p>");
            AppendFooter(body, s, no);
            return new EmailMessage(o.BuyerEmail, subject, body.ToString()) { ReplyTo = s.PublicEmail };
        }

        /// <summary>The order is on its way, or ready for pickup. Says nothing about when the money is taken.</summary>
        public static EmailMessage BuyerShipped(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var pickup = o.DeliveryMethod == DeliveryMethod.Pickup;
            var heading = pickup
                ? (no ? "Ordren din er klar for henting" : "Your order is ready for pickup")
                : (no ? "Ordren din er sendt" : "Your order has been sent");
            var body = new StringBuilder();
            body.Append($"<h2>{heading}</h2>");
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append($"<p>{(no ? "Vare" : "Item")}: {Enc(o.BuildTitle)}</p>");
            if (!string.IsNullOrWhiteSpace(o.ShippingNote))
                body.Append($"<p><strong>{(no ? "Melding fra selger" : "Message from the seller")}</strong><br/>{EncMultiline(o.ShippingNote)}</p>");
            AppendFooter(body, s, no);
            return new EmailMessage(o.BuyerEmail, $"{heading} {o.Reference}", body.ToString());
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

        /// <summary>
        /// One refund of <paramref name="amountInOre"/>. Once everything captured is paid back the subject says the
        /// order is refunded, before that it names the amount refunded this time.
        /// </summary>
        public static EmailMessage BuyerRefunded(Order o, int amountInOre, AppSettings s)
        {
            var no = IsNorwegian(o);
            var amount = Kroner(amountInOre, o.Locale);
            var all = o.Status == OrderStatus.Refunded;
            var partSubject = no
                ? $"Vi har betalt tilbake {amount} kr for ordre {o.Reference}"
                : $"We have refunded {amount} kr for order {o.Reference}";
            var body = new StringBuilder();
            var heading = all ? (no ? "Pengene er betalt tilbake" : "Your money is refunded") : partSubject;
            body.Append($"<h2>{Enc(heading)}</h2>");
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? $"<p>Vi har betalt tilbake {amount} kr til Vipps. Det kan ta noen dager før beløpet vises hos deg.</p>"
                : $"<p>We have refunded {amount} kr to Vipps. It can take a few days before the amount appears in your account.</p>");
            AppendFooter(body, s, no);
            var subject = !all ? partSubject
                : no ? $"Pengene for ordre {o.Reference} er betalt tilbake" : $"Order {o.Reference} is refunded";
            return new EmailMessage(o.BuyerEmail, subject, body.ToString());
        }

        private static void AppendLines(StringBuilder body, Order o, bool no)
        {
            body.Append("<table cellpadding=\"4\" cellspacing=\"0\">");
            body.Append($"<tr><td>{Enc(o.BuildTitle)}</td><td align=\"right\">{Kroner(o.BuildPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td>{Enc(o.DeliveryLabel)}</td><td align=\"right\">{Kroner(o.DeliveryPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td><strong>{(no ? "Totalt" : "Total")}</strong></td><td align=\"right\"><strong>{Kroner(o.TotalInOre, o.Locale)} kr</strong></td></tr>");
            body.Append("</table>");
        }

        private static void AppendFooter(StringBuilder body, AppSettings s, bool no) => EmailFooter.Append(body, s, no);

        private static string AddressHtml(Order o)
        {
            var cityLine = string.Join(" ", new[] { o.PostalCode, o.City }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return string.Join("<br/>", new[] { o.StreetAddress, cityLine }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(Enc));
        }

        private static bool IsNorwegian(Order o) => !string.Equals(o.Locale, "en", StringComparison.OrdinalIgnoreCase);

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string EncMultiline(string value) => Enc(value).Replace("\n", "<br/>");

        internal static string OsloDate(DateTime utc) =>
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
