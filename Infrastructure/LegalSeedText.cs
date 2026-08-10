namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Starting text for the legal pages, written once on an empty database. An admin edits
    /// these from /admin/legal afterwards; the seeder never overwrites an existing page.
    /// </summary>
    public static class LegalSeedText
    {
        public record Page(string Key, string Locale, string Title, string Body);

        public static readonly Page[] All =
        [
            new("terms", "no", "Vilkår", TermsNo),
            new("terms", "en", "Terms of Service", TermsEn),
            new("privacy", "no", "Personvern", PrivacyNo),
            new("privacy", "en", "Privacy Policy", PrivacyEn),
            new("cookies", "no", "Informasjonskapsler", CookiesNo),
            new("cookies", "en", "Cookie Policy", CookiesEn),
        ];

        private const string TermsNo = """
## 1. Om oss

Denne nettsiden drives av Altinnendata, Mårvegen 21a, 4347 Lye.
E-post: altinnendata@gmail.com. Telefon: +47 473 88 759.

## 2. Aksept av vilkårene

Ved å bruke Altinnendata godtar du disse vilkårene. Er du uenig, bør du ikke bruke nettsiden.

## 3. Bruk av tjenesten

Altinnendata viser informasjon om PC-bygging og tidligere leverte maskiner, og lar deg ta kontakt for
et tilbud. Innlogging er forbeholdt oss som drifter siden.

## 4. Immaterielle rettigheter

Alt innhold på siden — tekst, bilder, spesifikasjoner og navnet og logoen til Altinnendata — eies av
Altinnendata. Du kan se på innholdet til privat, ikke-kommersielt bruk. Innholdet kan ikke publiseres
på nytt, selges videre eller distribueres uten skriftlig samtykke fra oss.

## 5. Henvendelser og bestilling

Å sende inn kontaktskjemaet er en forespørsel, ikke en bindende avtale. Omfang, spesifikasjon og pris
avtales direkte mellom deg og Altinnendata.

## 6. Priser og spesifikasjoner

Priser og delelister som vises for et bygg, gjelder den maskinen slik den ble satt sammen på det
tidspunktet. Delepriser endrer seg, og tilsvarende maskin kan derfor koste noe annet i dag. Et bygg
merket som solgt er ikke lenger tilgjengelig.

## 7. Tilgjengelighet

Vi tilstreber høy oppetid, men garanterer ikke uavbrutt tilgang. Tjenesten kan oppdateres eller tas
ned for vedlikehold.

## 8. Personvern

Se [personvernerklæringen](/no/privacy) for hvordan vi behandler personopplysninger, og
[informasjonskapsler](/no/cookies) for bruk av cookies.

## 9. Lovvalg

Norsk rett gjelder for disse vilkårene. Tvister som ikke lar seg løse direkte, kan bringes inn for
norske domstoler.

## 10. Endringer

Vi kan oppdatere vilkårene. Den til enhver tid gjeldende versjonen er den som vises her, med datoen
over.

## 11. Kontakt

Spørsmål om vilkårene? [Ta kontakt](/no/contact).
""";

        private const string TermsEn = """
## 1. About us

This website is operated by Altinnendata, Mårvegen 21a, 4347 Lye, Norway.
Email: altinnendata@gmail.com. Phone: +47 473 88 759.

## 2. Acceptance of terms

By using Altinnendata you agree to these terms. If you do not agree, please do not use the site.

## 3. Use of the service

Altinnendata provides information about PC building and previously delivered machines, and lets you
get in touch for a quote. Sign-in is reserved for the people who run the site.

## 4. Intellectual property

All content on this site — text, images, specifications, and the Altinnendata name and logo — is owned
by Altinnendata. You may view it for personal, non-commercial use. You may not republish, resell, or
redistribute it without our written permission.

## 5. Enquiries and orders

Submitting the contact form is a request — it is not a binding agreement. Scope, specification and
price are agreed directly between you and Altinnendata.

## 6. Prices and specifications

The price and parts list shown for a build describe that machine as it was assembled at that time.
Component prices change, so an equivalent machine may cost something else today. A build marked as
sold is no longer available.

## 7. Availability

We aim for high availability but do not guarantee uninterrupted access. The service may be updated or
taken offline for maintenance at any time.

## 8. Data and privacy

See our [Privacy Policy](/en/privacy) for how we handle personal data, and our
[Cookie Policy](/en/cookies) for cookies.

## 9. Governing law

Norwegian law applies to these terms. Disputes that cannot be resolved directly may be brought before
the Norwegian courts.

## 10. Changes to terms

We may update these terms from time to time. The current version always applies, with the date shown
above.

## 11. Contact

Questions about these terms? [Contact us](/en/contact).
""";

        private const string PrivacyNo = """
## Behandlingsansvarlig

Altinnendata er behandlingsansvarlig for personopplysninger som samles inn gjennom denne nettsiden.

## Hvilke opplysninger vi samler inn

- **Henvendelser:** navn, e-post, telefonnummer, bruksområde, budsjett og meldingen du sender inn i
  kontaktskjemaet.
- **Brukerkontoer:** navn, e-postadresse og passord (lagret som hash). Kontoer opprettes kun for oss
  som drifter siden — det er ingen registrering for besøkende.
- **Bruksdata:** enkle tjenerlogger (anonyme bruker-ID-er, forespurte adresser) til feilsøking og
  sikkerhet.

Vi samler ikke inn betalingsopplysninger gjennom denne siden.

## Hva vi bruker opplysningene til

- Å svare på henvendelsen din og avtale arbeidet du spør om.
- Å sende e-post knyttet til innlogging (invitasjon, tilbakestilling av passord).
- Å forbedre tjenesten og finne tekniske feil.

Vi bruker ikke opplysningene til markedsføring, og vi selger dem ikke videre.

## Behandlingsgrunnlag

- **Avtale (art. 6 nr. 1 bokstav b)** — kontoopplysninger og e-post om innlogging er nødvendige for å
  gi tilgang til administrasjonssidene.
- **Berettiget interesse (art. 6 nr. 1 bokstav f)** — henvendelser behandles for at vi skal kunne
  svare deg, og tjenerlogger oppbevares for sikkerhet og feilsøking. Interessen vår går ikke foran
  rettighetene dine — du kan protestere når som helst.

## Deling av opplysninger

Vi selger ikke personopplysninger. Plattformen driftes på infrastruktur vi styrer selv; det er ingen
skyleverandør bak. Vi bruker én databehandler:

- [Brevo](https://www.brevo.com/legal/termsofuse/) (Frankrike, EØS) — utsending av e-post
  (invitasjon, tilbakestilling av passord) og videresending av henvendelser fra kontaktskjemaet.
  E-postadressen din og innholdet i e-posten deles med Brevo kun til dette formålet.

Vi kan i tillegg utlevere opplysninger når loven krever det.

## Lagringstid

- **Henvendelser** — så lenge det trengs for å håndtere forespørselen og eventuell oppfølging,
  deretter slettes de.
- **Kontoopplysninger** — til kontoen slettes, hvorpå navn, e-post og passord fjernes.
- **Tjenerlogger** — 90 dager. **Måledata** — 60 dager.
- **Sikkerhetskopier av databasen** — kryptert. Etter sletting kan rester ligge i sikkerhetskopier i
  inntil omtrent 6 måneder til rotasjonen er fullført. Sikkerhetskopier brukes ikke til behandling.

## Rettighetene dine

Etter personvernforordningen har du rett til:

- **Innsyn og dataportabilitet** — [ta kontakt](/no/contact) for en kopi av opplysningene vi har om
  deg.
- **Sletting** — [ta kontakt](/no/contact) for å få slettet henvendelsen din og opplysningene i den.
- **Retting** — [ta kontakt](/no/contact) hvis noe er feil.
- **Protest** — du kan protestere mot behandling som bygger på berettiget interesse.

Henvendelser sendes til [altinnendata@gmail.com](mailto:altinnendata@gmail.com). Du kan også klage
til Datatilsynet.

## Informasjonskapsler

Vi bruker informasjonskapsler til innlogging og økter. Se
[informasjonskapsler](/no/cookies) for detaljer.
""";

        private const string PrivacyEn = """
## Who is responsible

Altinnendata is the data controller for personal data collected through this website.

## What data we collect

- **Enquiries:** the name, email, phone number, use case, budget and message you submit through the
  contact form.
- **User accounts:** name, email address and password (stored hashed). Accounts exist only for the
  people who run the site — there is no sign-up for visitors.
- **Usage data:** basic server logs (opaque user IDs, request paths) for debugging and security.

We do not collect payment data through this site.

## How we use your data

- To answer your enquiry and arrange the work you ask about.
- To send sign-in related emails (invitation, password reset).
- To improve the service and diagnose technical issues.

We do not use your data for advertising and we do not sell or trade it.

## Legal basis for processing

- **Contract (Art. 6(1)(b))** — account data and sign-in emails are necessary to provide access to
  the admin pages.
- **Legitimate interests (Art. 6(1)(f))** — enquiries are processed so we can respond to you, and
  server logs are retained for security monitoring and debugging. Our interest does not override your
  rights — you can object at any time.

## Data sharing

We do not sell or trade your personal data. The platform is self-hosted on infrastructure under our
direct control; there is no upstream cloud or hosting provider. We use one third-party data processor:

- [Brevo](https://www.brevo.com/legal/termsofuse/) (France, EEA) — transactional email delivery
  (invitation, password reset) and delivery of contact-form enquiries. Your email address and the
  email body are shared with Brevo solely for this purpose.

We may also disclose data when required by law.

## Data retention

- **Enquiries** — kept for as long as needed to handle your request and any follow-up, then deleted.
- **Account data** — until the account is deleted, after which name, email and password are scrubbed.
- **Server logs** — 90 days. **Metrics** — 60 days.
- **Database backups** — encrypted at rest. After deletion, residual data may persist in backups for
  up to about 6 months until rotation completes; backups are not used for any processing.

## Your rights

Under GDPR you have the right to:

- **Access & portability** — [contact us](/en/contact) for a copy of the data we hold about you.
- **Erasure** — [contact us](/en/contact) to have your enquiry and its data deleted.
- **Correction** — [contact us](/en/contact) if something is wrong.
- **Object** — object to processing carried out on the basis of our legitimate interests.

Requests go to [altinnendata@gmail.com](mailto:altinnendata@gmail.com). You may also lodge a
complaint with the Norwegian Data Protection Authority.

## Cookies

We use cookies for authentication and session management. See our
[Cookie Policy](/en/cookies) for details.
""";

        private const string CookiesNo = """
## Hva er informasjonskapsler?

Informasjonskapsler er små tekstfiler som lagres i nettleseren din. De lar en nettside huske økten
din mellom forespørsler.

## Kapslene vi bruker

Å surfe på siden setter ingen kapsler for sporing eller annonser. Kapslene under settes bare når noen
logger inn.

| Navn | Formål | Varighet |
| --- | --- | --- |
| `next-auth.session-token` | Holder deg innlogget | Økt / 30 dager |
| `next-auth.csrf-token` | Sikkerhet — hindrer forfalskning av forespørsler på tvers av nettsteder | Økt |
| `next-auth.callback-url` | Husker hvor du skal sendes etter innlogging | Økt |

## Tredjeparter

Vi bruker ingen kapsler fra tredjeparter til analyse, annonsering eller sporing.

## Hvordan styre kapsler

Du kan slette eller blokkere informasjonskapsler i nettleserinnstillingene. Å blokkere kapslene over
påvirker bare det å holde seg innlogget; resten av siden virker uten dem.

## Spørsmål?

[Ta kontakt](/no/contact) hvis du lurer på noe rundt bruken av informasjonskapsler.
""";

        private const string CookiesEn = """
## What are cookies?

Cookies are small text files stored in your browser. They help a site remember your session between
requests.

## Cookies we use

Browsing the site sets no tracking or advertising cookies. The cookies below are only set when
someone signs in.

| Name | Purpose | Duration |
| --- | --- | --- |
| `next-auth.session-token` | Keeps you signed in | Session / 30 days |
| `next-auth.csrf-token` | Security — prevents cross-site request forgery | Session |
| `next-auth.callback-url` | Remembers where to redirect after login | Session |

## Third-party cookies

We do not use any third-party analytics, advertising, or tracking cookies.

## Managing cookies

You can clear or block cookies through your browser settings. Blocking the cookies above only affects
staying signed in; the rest of the site works without them.

## Questions?

[Contact us](/en/contact) if you have any questions about how we use cookies.
""";
    }
}
