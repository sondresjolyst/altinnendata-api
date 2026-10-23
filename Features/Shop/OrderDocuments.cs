using System.Globalization;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SjolystInnovation.AppKit.Pdf;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// The HTML of the two PDFs the order confirmation carries: the terms as the buyer accepted them, and the
    /// standard withdrawal form from the annex to angrerettloven. Every value is HTML encoded, the CSS is inline
    /// and nothing loads from the network: images in the terms are dropped, links show their address as text, and a
    /// Content-Security-Policy blocks anything else.
    /// </summary>
    public static class OrderDocuments
    {
        // Raw HTML in the admin's Markdown is shown as text, never rendered.
        private static readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder().DisableHtml().Build();

        private const string Csp = "default-src 'none'; img-src data:; style-src 'unsafe-inline'";

        private const string Style = """
            <style>
              @page { size: A4; margin: 20mm 18mm; }
              body { font-family: "Liberation Sans", Arial, Helvetica, sans-serif; font-size: 10.5pt; line-height: 1.45; color: #1a1a1a; }
              header { border-bottom: 2px solid #0f3d3a; padding-bottom: 8pt; margin-bottom: 14pt; }
              header .company { font-size: 9pt; color: #0f3d3a; font-weight: bold; text-transform: uppercase; letter-spacing: .04em; }
              h1 { font-size: 17pt; margin: 4pt 0 6pt; }
              h2 { font-size: 13pt; margin: 14pt 0 4pt; }
              h3 { font-size: 11pt; margin: 10pt 0 4pt; }
              .meta { font-size: 9.5pt; color: #444; margin: 0; }
              .meta strong { color: #1a1a1a; }
              a { color: #00665c; }
              p, li { overflow-wrap: anywhere; }
            </style>
            """;

        public static string FileNameOfTerms(Order o) =>
            IsNorwegian(o) ? $"vilkar-{o.Reference}.pdf" : $"terms-{o.Reference}.pdf";

        /// <summary>The terms snapshot of the order, or null when the order has none.</summary>
        public static string? TermsHtml(Order o, AppSettings s)
        {
            if (string.IsNullOrWhiteSpace(o.TermsMarkdown)) return null;
            var no = IsNorwegian(o);
            var body = new StringBuilder();
            body.Append($"<header><div class=\"company\">{Enc(CompanyName(s))}</div>");
            body.Append($"<h1>{Enc(string.IsNullOrWhiteSpace(o.TermsTitle) ? (no ? "Vilkår" : "Terms") : o.TermsTitle)}</h1>");
            body.Append($"<p class=\"meta\">{(no ? "Ordrenummer" : "Order number")}: <strong>{Enc(o.Reference)}</strong></p>");
            body.Append($"<p class=\"meta\">{(no ? "Vilkårene ble godtatt" : "Terms accepted on")}: <strong>{OsloDate(o.TermsAcceptedAt)}</strong></p>");
            body.Append("</header>");
            body.Append(TermsBodyHtml(o.TermsMarkdown));
            return Page(o, o.TermsTitle ?? (no ? "Vilkår" : "Terms"), body.ToString());
        }

        /// <summary>
        /// The withdrawal form of the order, in the buyer's language, filled in with the seller, the item, the order
        /// number and the date the agreement was made in Oslo.
        /// </summary>
        public static WithdrawalFormRequest WithdrawalFormRequest(Order o, AppSettings s) =>
            new(
                new WithdrawalFormSeller(s.CompanyName)
                {
                    LegalName = s.CompanyLegalName,
                    OrganizationNumber = s.OrgNumber,
                    AddressLines = [s.FormattedAddress],
                    Email = s.PublicEmail,
                },
                new WithdrawalFormOrder(o.Reference, DateOnly.FromDateTime(OsloTime.FromUtc(AgreedAt(o))), [o.BuildTitle]))
            {
                Language = IsNorwegian(o) ? WithdrawalFormLanguage.Norwegian : WithdrawalFormLanguage.English,
            };

        /// <summary>The terms Markdown as HTML, with images left out and each link as its text followed by its address.</summary>
        internal static string TermsBodyHtml(string markdown)
        {
            var document = Markdig.Markdown.Parse(markdown, Markdown);
            foreach (var link in document.Descendants<LinkInline>().ToList())
            {
                if (link.IsImage)
                {
                    link.Remove();
                    continue;
                }
                var label = string.Concat(link.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
                var text = string.IsNullOrWhiteSpace(link.Url) || label == link.Url ? label : $"{label} ({link.Url})";
                link.ReplaceBy(new LiteralInline(text), copyChildren: false);
            }
            foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
                autolink.ReplaceBy(new LiteralInline(autolink.Url), copyChildren: false);

            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            var renderer = new HtmlRenderer(writer);
            Markdown.Setup(renderer);
            renderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }

        private static string Page(Order o, string title, string body) =>
            $"<!doctype html><html lang=\"{(IsNorwegian(o) ? "nb" : "en")}\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{Csp}\"><title>{Enc(title)}</title>{Style}</head><body>{body}</body></html>";

        /// <summary>
        /// When the agreement was made: when Vipps confirmed the payment that succeeded, which after a payment link is
        /// the link payment. Falls back to when the terms were accepted for an order Vipps has not confirmed.
        /// </summary>
        internal static DateTime AgreedAt(Order o) => o.AuthorizedAt ?? o.TermsAcceptedAt;

        private static string CompanyName(AppSettings s) =>
            string.IsNullOrWhiteSpace(s.CompanyLegalName) ? s.CompanyName : s.CompanyLegalName;

        private static bool IsNorwegian(Order o) => !string.Equals(o.Locale, "en", StringComparison.OrdinalIgnoreCase);

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string OsloDate(DateTime utc) => OrderEmails.OsloDate(utc);
    }
}
