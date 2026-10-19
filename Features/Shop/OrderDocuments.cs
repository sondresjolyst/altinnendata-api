using System.Globalization;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
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

        private const string Blank = "<span class=\"blank\"></span>";

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
              .lead { font-style: italic; }
              .field { margin: 12pt 0; }
              .field .label { font-weight: bold; }
              .blank { display: inline-block; min-width: 70mm; border-bottom: 1px solid #333; height: 1.1em; vertical-align: bottom; }
              .seller { margin: 4pt 0 0 0; }
            </style>
            """;

        public static string FileNameOfTerms(Order o) =>
            IsNorwegian(o) ? $"vilkar-{o.Reference}.pdf" : $"terms-{o.Reference}.pdf";

        public static string FileNameOfWithdrawalForm(Order o) =>
            IsNorwegian(o) ? "angrerettskjema.pdf" : "withdrawal-form.pdf";

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

        /// <summary>The standard withdrawal form, filled in with the seller, the item, the order number and the order date.</summary>
        public static string WithdrawalFormHtml(Order o, AppSettings s)
        {
            var no = IsNorwegian(o);
            var seller = $"{Enc(CompanyName(s))}, {Enc(s.FormattedAddress)}, {Enc(s.PublicEmail)}";
            var body = new StringBuilder();
            body.Append($"<header><div class=\"company\">{Enc(CompanyName(s))}</div>");
            if (no)
            {
                body.Append("<h1>Standardskjema for bruk av angrerett</h1></header>");
                body.Append("<p class=\"lead\">(Fyll ut og returner dette skjemaet dersom du ønsker å gå fra avtalen.)</p>");
                Field(body, "Utfylt skjema sendes til", $"<p class=\"seller\">{seller}</p>");
                body.Append($"<p>Jeg underretter herved om at jeg ønsker å gå fra min avtale om kjøp av følgende varer: <strong>{Enc(o.BuildTitle)}</strong></p>");
                Field(body, "Ordrenummer", Enc(o.Reference));
                Field(body, "Avtalen ble inngått den", OsloDate(o.TermsAcceptedAt));
                Field(body, "Varen ble mottatt den", Blank);
                Field(body, "Forbrukerens navn", Blank);
                Field(body, "Forbrukerens adresse", Blank);
                Field(body, "Dato", Blank);
                Field(body, "Underskrift (kun dersom skjemaet sendes på papir)", Blank);
                return Page(o, "Standardskjema for bruk av angrerett", body.ToString());
            }

            body.Append("<h1>Model withdrawal form</h1></header>");
            body.Append("<p class=\"lead\">(Complete and return this form only if you wish to withdraw from the contract.)</p>");
            Field(body, "To", $"<p class=\"seller\">{seller}</p>");
            body.Append($"<p>I hereby give notice that I withdraw from my contract of sale of the following goods: <strong>{Enc(o.BuildTitle)}</strong></p>");
            Field(body, "Order number", Enc(o.Reference));
            Field(body, "Ordered on", OsloDate(o.TermsAcceptedAt));
            Field(body, "Received on", Blank);
            Field(body, "Name of consumer", Blank);
            Field(body, "Address of consumer", Blank);
            Field(body, "Date", Blank);
            Field(body, "Signature of consumer (only if this form is notified on paper)", Blank);
            return Page(o, "Model withdrawal form", body.ToString());
        }

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

        private static void Field(StringBuilder body, string label, string value) =>
            body.Append($"<div class=\"field\"><span class=\"label\">{Enc(label)}:</span> {value}</div>");

        private static string Page(Order o, string title, string body) =>
            $"<!doctype html><html lang=\"{(IsNorwegian(o) ? "nb" : "en")}\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{Csp}\"><title>{Enc(title)}</title>{Style}</head><body>{body}</body></html>";

        private static string CompanyName(AppSettings s) =>
            string.IsNullOrWhiteSpace(s.CompanyLegalName) ? s.CompanyName : s.CompanyLegalName;

        private static bool IsNorwegian(Order o) => !string.Equals(o.Locale, "en", StringComparison.OrdinalIgnoreCase);

        private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string OsloDate(DateTime utc) => OrderEmails.OsloDate(utc);
    }
}
