using System.Globalization;
using System.Net;
using System.Text;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Email;
using altinnendata_api.Infrastructure;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Builds the order emails. The buyer emails follow <see cref="Order.Locale"/>, the owner notice is Norwegian.
    /// Every order value is HTML encoded.
    /// </summary>
    public static class OrderEmails
    {

        public static EmailMessage BuyerConfirmation(Order o, AppSettings s, IConfiguration config)
        {
            var no = IsNorwegian(o);
            var body = new StringBuilder();
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
                    ? "<p>Vi tar kontakt med deg om henting.</p>"
                    : "<p>We will be in touch about pickup.</p>");
            }
            else
            {
                body.Append($"<p><strong>{(no ? "Leveringsadresse" : "Delivery address")}</strong><br/>{AddressHtml(o)}</p>");
                body.Append(no
                    ? "<p>Vi tar kontakt med deg om levering.</p>"
                    : "<p>We will be in touch about delivery.</p>");
            }

            var terms = Enc(SiteLinks.Terms(config, o.Locale));
            var email = Enc(s.PublicEmail);
            var withTerms = !string.IsNullOrWhiteSpace(o.TermsMarkdown);
            body.Append(no
                ? "<p><strong>Angrerett</strong><br/>Du har 14 dagers angrerett fra den dagen du mottar varen. "
                  + $"Bruk det vedlagte angrerettskjemaet, eller gi oss beskjed på e-post til <a href=\"mailto:{email}\" style=\"{BrandedEmail.LinkStyle}\">{email}</a>. "
                  + (withTerms ? "Vilkårene du godtok og angrerettskjemaet ligger vedlagt.</p>" : "Angrerettskjemaet ligger vedlagt.</p>")
                : "<p><strong>Right of withdrawal</strong><br/>You have 14 days to withdraw from the day you receive the item. "
                  + $"Use the attached withdrawal form, or tell us by email at <a href=\"mailto:{email}\" style=\"{BrandedEmail.LinkStyle}\">{email}</a>. "
                  + (withTerms ? "The terms you accepted and the withdrawal form are attached.</p>" : "The withdrawal form is attached.</p>"));
            body.Append(no
                ? $"<p>Les <a href=\"{terms}\" style=\"{BrandedEmail.LinkStyle}\">vilkårene</a> på nettsiden.</p>"
                : $"<p>Read the <a href=\"{terms}\" style=\"{BrandedEmail.LinkStyle}\">terms</a> on the website.</p>");

            var html = BrandedEmail.Html(s, no, no ? "Ordrebekreftelse" : "Order confirmation",
                no ? "Takk for bestillingen" : "Thank you for your order", body.ToString(), footer: BrandedEmail.OrderFooter(s, no));
            return new EmailMessage(o.BuyerEmail, no ? $"Ordrebekreftelse {o.Reference}" : $"Order confirmation {o.Reference}", html);
        }

        public static EmailMessage OwnerNewOrder(Order o, AppSettings s, IConfiguration config)
        {
            var html = BrandedEmail.Html(s, true, "Ny ordre", "Ny ordre", "", AdminButton(o, config), OwnerFacts(o));
            return new EmailMessage(s.ContactRecipientEmail, $"Ny ordre {o.Reference}: {o.BuildTitle}", html);
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
                "CANCELLED" => "Reservasjonen ble opphevet i Vipps",
                _ => "Reservasjonen ble opphevet i Vipps"
            };
            var when = o.ReservationEndedAt is { } at ? $" {OsloDate(at)}" : "";
            var subject = $"Reservasjonen i Vipps for ordre {o.Reference} er opphevet";
            var body = new StringBuilder();
            body.Append($"<p>{how}{when} ({Enc(vippsEvent)}). Det var ikke du som kansellerte ordren.</p>");
            body.Append($"<p>Datamaskinen {Enc(o.BuildTitle)} er fortsatt holdt av ordren og vises ikke som tilgjengelig.</p>");
            body.Append("<p>Ta kontakt med kjøperen og avtal videre, eller frigi datamaskinen fra ordren i admin hvis salget ikke blir noe av.</p>");
            body.Append(OwnerFacts(o));
            var html = BrandedEmail.Html(s, true, "Reservasjon opphevet", subject, body.ToString(), AdminButton(o, config));
            return new EmailMessage(s.ContactRecipientEmail, subject, html);
        }

        /// <summary>Tells the buyer that the reservation in Vipps is cancelled, that the computer is still held for them and that we will be in touch.</summary>
        public static EmailMessage BuyerReservationEnded(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var subject = no
                ? $"Reservasjonen i Vipps for ordre {o.Reference} er opphevet"
                : $"The reservation in Vipps for order {o.Reference} has been cancelled";
            var body = new StringBuilder();
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append($"<p>{(no ? "Vare" : "Item")}: {Enc(o.BuildTitle)}</p>");
            body.Append(no
                ? "<p>Reservasjonen i Vipps er opphevet. Ingen penger er trukket. Datamaskinen er fortsatt holdt av for deg. Vi tar kontakt med deg.</p>"
                : "<p>The reservation in Vipps has been cancelled. No money has been taken. The computer is still held for you. We will contact you.</p>");
            var html = BrandedEmail.Html(s, no, no ? "Reservasjon opphevet" : "Reservation cancelled", subject, body.ToString(), footer: BrandedEmail.OrderFooter(s, no));
            return new EmailMessage(o.BuyerEmail, subject, html) { ReplyTo = s.PublicEmail };
        }

        /// <summary>
        /// Gives the buyer a new link to pay after the reservation in Vipps ended. Says the computer is still held for
        /// them and until when the link works, and nothing about when the money is taken.
        /// </summary>
        public static EmailMessage BuyerPaymentLink(Order o, string link, AppSettings s)
        {
            var no = IsNorwegian(o);
            var subject = no ? $"Ny betalingslenke for ordre {o.Reference}" : $"New payment link for order {o.Reference}";
            var until = o.PaymentLinkExpiresAt is { } at ? OsloDateTime(at, no) : "";
            var body = new StringBuilder();
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? $"<p>Reservasjonen i Vipps er opphevet. Datamaskinen {Enc(o.BuildTitle)} er fortsatt holdt av for deg.</p>"
                : $"<p>The reservation in Vipps has been cancelled. The computer {Enc(o.BuildTitle)} is still held for you.</p>");
            body.Append($"<p>{(no ? "Beløp" : "Amount")}: {Kroner(o.TotalInOre, o.Locale)} kr</p>");
            var after = no ? $"<p>Lenken er gyldig til {until}.</p>" : $"<p>The link works until {until}.</p>";
            var html = BrandedEmail.Html(s, no, no ? "Betalingslenke" : "Payment link", subject, body.ToString(),
                new EmailButton(no ? "Betal med Vipps" : "Pay with Vipps", link), after, BrandedEmail.OrderFooter(s, no));
            return new EmailMessage(o.BuyerEmail, subject, html) { ReplyTo = s.PublicEmail };
        }

        /// <summary>Tells the owner that the payment link ran out unpaid while the build is still held for the order.</summary>
        public static EmailMessage OwnerPaymentLinkExpired(Order o, AppSettings s, IConfiguration config)
        {
            var subject = $"Betalingslenken for ordre {o.Reference} er utløpt";
            var body = new StringBuilder();
            var sent = o.PaymentLinkSentAt is { } at ? $" sendt {OsloDate(at)}" : "";
            body.Append($"<p>Betalingslenken{sent} ble ikke brukt før den utløp.</p>");
            body.Append($"<p>Datamaskinen {Enc(o.BuildTitle)} er fortsatt holdt av ordren og vises ikke som tilgjengelig.</p>");
            body.Append("<p>Ta kontakt med kjøperen og send en ny betalingslenke, eller frigi datamaskinen fra ordren i admin hvis salget ikke blir noe av.</p>");
            body.Append(OwnerFacts(o));
            var html = BrandedEmail.Html(s, true, "Lenke utløpt", subject, body.ToString(), AdminButton(o, config));
            return new EmailMessage(s.ContactRecipientEmail, subject, html);
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
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append($"<p>{(no ? "Vare" : "Item")}: {Enc(o.BuildTitle)}</p>");
            if (!string.IsNullOrWhiteSpace(o.ShippingNote))
                body.Append($"<p><strong>{(no ? "Melding fra oss" : "Message from us")}</strong><br/>{EncMultiline(o.ShippingNote)}</p>");
            var badge = pickup ? (no ? "Klar for henting" : "Ready for pickup") : (no ? "Sendt" : "Sent");
            return new EmailMessage(o.BuyerEmail, $"{heading} {o.Reference}", BrandedEmail.Html(s, no, badge, heading, body.ToString(), footer: BrandedEmail.OrderFooter(s, no)));
        }

        public static EmailMessage BuyerCancelled(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var body = new StringBuilder();
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? "<p>Reservasjonen i Vipps er opphevet. Ingen penger er trukket.</p>"
                : "<p>The reservation in Vipps has been cancelled. No money has been taken.</p>");
            var html = BrandedEmail.Html(s, no, no ? "Kansellert" : "Cancelled",
                no ? "Ordren er kansellert" : "Your order is cancelled", body.ToString(), footer: BrandedEmail.OrderFooter(s, no));
            return new EmailMessage(o.BuyerEmail, no ? $"Ordre {o.Reference} er kansellert" : $"Order {o.Reference} is cancelled", html);
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
            var heading = all ? (no ? "Pengene er betalt tilbake" : "Your money has been refunded") : partSubject;
            body.Append($"<p>{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append(no
                ? $"<p>Vi har betalt tilbake {amount} kr til betalingsmåten du brukte i Vipps. Det kan ta noen dager før beløpet vises hos deg.</p>"
                : $"<p>We have refunded {amount} kr to the payment method you used in Vipps. It can take a few days before the amount appears in your account.</p>");
            var subject = !all ? partSubject
                : no ? $"Pengene for ordre {o.Reference} er betalt tilbake" : $"Order {o.Reference} is refunded";
            return new EmailMessage(o.BuyerEmail, subject, BrandedEmail.Html(s, no, no ? "Tilbakebetalt" : "Refunded", heading, body.ToString(), footer: BrandedEmail.OrderFooter(s, no)));
        }

        private static void AppendLines(StringBuilder body, Order o, bool no)
        {
            body.Append("<table cellpadding=\"4\" cellspacing=\"0\">");
            body.Append($"<tr><td>{Enc(o.BuildTitle)}</td><td align=\"right\">{Kroner(o.BuildPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td>{Enc(o.DeliveryLabel)}</td><td align=\"right\">{Kroner(o.DeliveryPriceInOre, o.Locale)} kr</td></tr>");
            body.Append($"<tr><td><strong>{(no ? "Totalt" : "Total")}</strong></td><td align=\"right\"><strong>{Kroner(o.TotalInOre, o.Locale)} kr</strong></td></tr>");
            body.Append("</table>");
        }

        /// <summary>
        /// The order facts in an owner email. The owner inbox has no data processing agreement, so it carries no buyer
        /// details and points to admin for them.
        /// </summary>
        private static string OwnerFacts(Order o)
        {
            var body = new StringBuilder();
            void Row(string label, string value) => body.Append($"<p><strong>{label}:</strong> {value}</p>");
            Row("Ordrenummer", Enc(o.Reference));
            Row("Vare", Enc(o.BuildTitle));
            Row("Levering", Enc(o.DeliveryLabel));
            Row("Totalt", $"{Kroner(o.TotalInOre, "no")} kr");
            Row("Bestilt", OsloDate(o.CreatedAt));
            body.Append("<p>Kundens opplysninger finner du i admin.</p>");
            return body.ToString();
        }

        private static EmailButton AdminButton(Order o, IConfiguration config) =>
            new("Åpne ordren i admin", SiteLinks.AdminOrder(config, o.Id));

        private static string AddressHtml(Order o)
        {
            var cityLine = string.Join(" ", new[] { o.PostalCode, o.City }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return string.Join("<br/>", new[] { o.StreetAddress, cityLine }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(Enc));
        }

        private static bool IsNorwegian(Order o) => !string.Equals(o.Locale, "en", StringComparison.OrdinalIgnoreCase);

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string EncMultiline(string value) => Enc(value).Replace("\n", "<br/>");

        /// <summary>The date and time in Oslo, <c>23.10.2026 kl. 17:42</c> or <c>23.10.2026 at 17:42</c>.</summary>
        internal static string OsloDateTime(DateTime utc, bool no) =>
            OsloTime.FromUtc(utc)
                .ToString(no ? "dd.MM.yyyy 'kl.' HH:mm" : "dd.MM.yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

        internal static string OsloDate(DateTime utc) =>
            OsloTime.FromUtc(utc)
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
