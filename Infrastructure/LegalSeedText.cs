using SjolystInnovation.AppKit.SiteSettings;

namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Starting text for the legal pages, written once on an empty database. An admin edits
    /// these from /admin/legal afterwards; the seeder never overwrites an existing page.
    /// </summary>
    public static class LegalSeedText
    {
        public static readonly LegalPageSeed[] All =
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

Altinnendata viser datamaskiner vi har bygget — både maskiner som er til salgs og maskiner som er
solgt — og lar deg ta kontakt for å få et tilbud. Det er bare vi som drifter nettsiden som kan logge
inn.

## 4. Immaterielle rettigheter

Alt innhold på siden — tekst, bilder, spesifikasjoner og navnet og logoen til Altinnendata — eies av
Altinnendata. Innholdet kan ikke kopieres, publiseres på nytt eller brukes i egen markedsføring uten
skriftlig samtykke fra oss.

## 5. Henvendelser og bestilling

Å sende inn kontaktskjemaet er en forespørsel, ikke en bindende avtale. Hva som skal leveres, hvilke
deler som inngår og hva det koster, avtaler du direkte med oss.

## 6. Priser og spesifikasjoner

Prisen og delelisten som står oppført på en datamaskin, gjelder maskinen slik den var satt sammen da
den ble lagt ut. Delepriser endrer seg, så en tilsvarende maskin kan koste noe annet i dag. En
datamaskin som er merket som solgt, er ikke lenger til salgs.

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

Vi kan oppdatere vilkårene. Det er versjonen som står her, med datoen øverst, som gjelder.

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
by Altinnendata. It may not be copied, republished, or used in your own marketing without our written
permission.

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
For Meta Pixel er vi felles behandlingsansvarlige med Meta Platforms Ireland Ltd, se under.

## Hvilke opplysninger vi samler inn

- **Henvendelser:** navn, e-post, telefonnummer, bruksområde, budsjett og meldingen du sender inn i
  kontaktskjemaet.
- **Brukerkontoer:** navn, e-postadresse og passord (lagret som hash). Kontoer opprettes kun for oss
  som drifter siden. Det er ingen registrering for besøkende.
- **Bruksdata:** enkle tjenerlogger (anonyme bruker-ID-er, forespurte adresser) til feilsøking og
  sikkerhet.
- **Meta Pixel, bare med samtykke:** sidene du besøker, produktene du ser på, når du starter en
  betaling, og verdien og produkt-ID-en når du kjøper. Meta mottar også IP-adressen din, opplysninger
  om nettleseren og enheten din, og informasjonskapsler. Vi sender ikke navn, e-post eller
  telefonnummer til Meta.

Vi samler ikke inn betalingsopplysninger gjennom denne siden.

## Hva vi bruker opplysningene til

- Å svare på henvendelsen din og avtale arbeidet du spør om.
- Å sende e-post knyttet til innlogging (invitasjon, tilbakestilling av passord).
- Å forbedre tjenesten og finne tekniske feil.
- Markedsføring og måling av annonser på Facebook og Instagram med Meta Pixel. Dette skjer bare når
  vi har slått på Meta Pixel og du har samtykket.

Vi bruker ikke opplysningene til markedsføring uten samtykket ditt, og vi selger dem ikke videre.

## Behandlingsgrunnlag

- **Avtale (art. 6 nr. 1 bokstav b):** kontoopplysninger og e-post om innlogging er nødvendige for å
  gi tilgang til administrasjonssidene.
- **Berettiget interesse (art. 6 nr. 1 bokstav f):** henvendelser behandles for at vi skal kunne
  svare deg, og tjenerlogger oppbevares for sikkerhet og feilsøking. Interessen vår går ikke foran
  rettighetene dine. Du kan protestere når som helst.
- **Samtykke (art. 6 nr. 1 bokstav a og ekomloven § 3-15):** Meta Pixel lastes og setter
  informasjonskapsler bare etter at du har sagt ja til markedsføring.

## Meta Pixel

Meta Pixel leveres av Meta Platforms Ireland Ltd. Vi bruker den til markedsføring og måling av
annonser på Facebook og Instagram, og bare når vi har slått den på. Den lastes først etter at du har
samtykket. Uten samtykke lastes den ikke, og ingen opplysninger sendes til Meta gjennom den.

Altinnendata og Meta Platforms Ireland Ltd er felles behandlingsansvarlige etter personvernforordningen
art. 26 for innsamlingen av opplysningene på denne siden og overføringen til Meta. Det Meta gjør med
opplysningene etterpå, er Meta alene ansvarlig for. Du kan bruke rettighetene dine overfor oss eller
overfor Meta. Les Metas [personvernerklæring](https://www.facebook.com/privacy/policy) og Metas
[retningslinjer for informasjonskapsler](https://www.facebook.com/policies/cookies/).

**Overføring til USA:** Meta kan overføre opplysningene til USA. Overføringen bygger på EU-U.S. Data
Privacy Framework, som Meta er sertifisert under.

**Trekk tilbake samtykket:** du kan når som helst trekke tilbake samtykket med lenken «Innstillinger
for informasjonskapsler» nederst på hver side. Det er like enkelt som å gi det. Tilbaketrekkingen
gjelder fra da av og påvirker ikke behandlingen før det.

## Deling av opplysninger

Vi selger ikke personopplysninger. Plattformen driftes på infrastruktur vi styrer selv. Det er ingen
skyleverandør bak. Vi bruker én databehandler:

- [Brevo](https://www.brevo.com/legal/termsofuse/) (Frankrike, EØS) for utsending av e-post
  (invitasjon, tilbakestilling av passord) og videresending av henvendelser fra kontaktskjemaet.
  E-postadressen din og innholdet i e-posten deles med Brevo kun til dette formålet.

Med samtykket ditt deles opplysningene fra Meta Pixel med Meta Platforms Ireland Ltd, som beskrevet
over.

Vi kan i tillegg utlevere opplysninger når loven krever det.

## Lagringstid

- **Henvendelser:** så lenge det trengs for å håndtere forespørselen og eventuell oppfølging,
  deretter slettes de.
- **Kontoopplysninger:** til kontoen slettes, hvorpå navn, e-post og passord fjernes.
- **Tjenerlogger:** 90 dager. **Måledata:** 60 dager.
- **Sikkerhetskopier av databasen:** kryptert. Etter sletting kan rester ligge i sikkerhetskopier i
  inntil omtrent 6 måneder til rotasjonen er fullført. Sikkerhetskopier brukes ikke til behandling.
- **Meta Pixel:** informasjonskapslene `_fbp` og `fr` varer i 90 dager. Hvor lenge Meta lagrer
  opplysningene, står i Metas personvernerklæring.

## Rettighetene dine

Etter personvernforordningen har du rett til:

- **Innsyn og dataportabilitet:** [ta kontakt](/no/contact) for en kopi av opplysningene vi har om
  deg.
- **Sletting:** [ta kontakt](/no/contact) for å få slettet henvendelsen din og opplysningene i den.
- **Retting:** [ta kontakt](/no/contact) hvis noe er feil.
- **Protest:** du kan protestere mot behandling som bygger på berettiget interesse.
- **Trekke tilbake samtykke:** med lenken «Innstillinger for informasjonskapsler» nederst på hver
  side.

Henvendelser sendes til [altinnendata@gmail.com](mailto:altinnendata@gmail.com). Du har også rett til
å klage til [Datatilsynet](https://www.datatilsynet.no).

## Informasjonskapsler

Vi bruker informasjonskapsler til innlogging og økter, og til markedsføring med Meta Pixel når du har
samtykket. Se [informasjonskapsler](/no/cookies) for detaljer.
""";

        private const string PrivacyEn = """
## Who is responsible

Altinnendata is the data controller for personal data collected through this website. For the Meta
Pixel we are joint controllers with Meta Platforms Ireland Ltd, see below.

## What data we collect

- **Enquiries:** the name, email, phone number, use case, budget and message you submit through the
  contact form.
- **User accounts:** name, email address and password (stored hashed). Accounts exist only for the
  people who run the site. There is no sign-up for visitors.
- **Usage data:** basic server logs (opaque user IDs, request paths) for debugging and security.
- **Meta Pixel, only with consent:** the pages you visit, the products you view, when you start a
  checkout, and the value and product ID when you buy. Meta also receives your IP address, information
  about your browser and device, and cookies. We do not send your name, email or phone number to Meta.

We do not collect payment data through this site.

## How we use your data

- To answer your enquiry and arrange the work you ask about.
- To send sign-in related emails (invitation, password reset).
- To improve the service and diagnose technical issues.
- For marketing and to measure ads on Facebook and Instagram with the Meta Pixel. This happens only
  when we have turned on the Meta Pixel and you have given consent.

We do not use your data for advertising without your consent, and we do not sell or trade it.

## Legal basis for processing

- **Contract (Art. 6(1)(b)):** account data and sign-in emails are necessary to provide access to
  the admin pages.
- **Legitimate interests (Art. 6(1)(f)):** enquiries are processed so we can respond to you, and
  server logs are retained for security monitoring and debugging. Our interest does not override your
  rights. You can object at any time.
- **Consent (Art. 6(1)(a) and section 3-15 of the Norwegian Electronic Communications Act):** the Meta
  Pixel loads and sets cookies only after you have accepted marketing.

## Meta Pixel

The Meta Pixel is provided by Meta Platforms Ireland Ltd. We use it for marketing and to measure ads
on Facebook and Instagram, and only when we have turned it on. It loads only after you have given
consent. Without consent it does not load and no data is sent to Meta through it.

Altinnendata and Meta Platforms Ireland Ltd are joint controllers under GDPR Art. 26 for collecting the
data on this site and transmitting it to Meta. Meta alone is responsible for what it does with the
data after that. You can exercise your rights with us or with Meta. Read Meta's
[Privacy Policy](https://www.facebook.com/privacy/policy) and Meta's
[Cookies Policy](https://www.facebook.com/policies/cookies/).

**Transfer to the United States:** Meta may transfer the data to the United States. The transfer is
based on the EU-U.S. Data Privacy Framework, under which Meta is certified.

**Withdrawing consent:** you can withdraw your consent at any time through the "Cookie settings" link
at the bottom of every page. It is as easy as giving it. Withdrawal applies from then on and does not
affect processing before it.

## Data sharing

We do not sell or trade your personal data. The platform is self-hosted on infrastructure under our
direct control. There is no upstream cloud or hosting provider. We use one third-party data processor:

- [Brevo](https://www.brevo.com/legal/termsofuse/) (France, EEA) for transactional email delivery
  (invitation, password reset) and delivery of contact-form enquiries. Your email address and the
  email body are shared with Brevo solely for this purpose.

With your consent, the Meta Pixel data is shared with Meta Platforms Ireland Ltd as described above.

We may also disclose data when required by law.

## Data retention

- **Enquiries:** kept for as long as needed to handle your request and any follow-up, then deleted.
- **Account data:** until the account is deleted, after which name, email and password are scrubbed.
- **Server logs:** 90 days. **Metrics:** 60 days.
- **Database backups:** encrypted at rest. After deletion, residual data may persist in backups for
  up to about 6 months until rotation completes. Backups are not used for any processing.
- **Meta Pixel:** the `_fbp` and `fr` cookies last 90 days. How long Meta keeps the data is set out in
  Meta's Privacy Policy.

## Your rights

Under GDPR you have the right to:

- **Access & portability:** [contact us](/en/contact) for a copy of the data we hold about you.
- **Erasure:** [contact us](/en/contact) to have your enquiry and its data deleted.
- **Correction:** [contact us](/en/contact) if something is wrong.
- **Object:** object to processing carried out on the basis of our legitimate interests.
- **Withdraw consent:** through the "Cookie settings" link at the bottom of every page.

Requests go to [altinnendata@gmail.com](mailto:altinnendata@gmail.com). You also have the right to
lodge a complaint with the
[Norwegian Data Protection Authority (Datatilsynet)](https://www.datatilsynet.no/en/).

## Cookies

We use cookies for authentication and session management, and for marketing with the Meta Pixel when
you have given consent. See our [Cookie Policy](/en/cookies) for details.
""";

        private const string CookiesNo = """
## Hva er informasjonskapsler?

Informasjonskapsler er små tekstfiler som lagres i nettleseren din. De lar en nettside huske økten
din mellom forespørsler. Denne siden gjelder også lignende teknologi, som skript og piksler som leser
eller lagrer informasjon i enheten din.

## Nødvendige kapsler

Disse kapslene trengs for at siden skal virke, og de settes uten samtykke. Kapslene under settes bare
når noen logger inn.

| Navn | Leverandør | Formål | Varighet |
| --- | --- | --- | --- |
| `next-auth.session-token` | Altinnendata | Holder deg innlogget | Økt / 30 dager |
| `next-auth.csrf-token` | Altinnendata | Sikkerhet. Hindrer forfalskning av forespørsler på tvers av nettsteder | Økt |
| `next-auth.callback-url` | Altinnendata | Husker hvor du skal sendes etter innlogging | Økt |

Valget du tar om informasjonskapsler lagres i nettleseren din, slik at vi ikke spør på nytt ved hvert
besøk. Det lagrer bare valget og når du tok det.

## Markedsføringskapsler (bare med samtykke)

Når vi har slått på Meta Pixel, bruker vi den til markedsføring og måling av annonser på Facebook og
Instagram. Meta Pixel leveres av Meta Platforms Ireland Ltd. Den lastes først når du har sagt ja til
markedsføring. Sier du nei, eller tar du ikke stilling, lastes den ikke, og kapslene under settes
ikke. Er Meta Pixel slått av, settes de heller ikke.

| Navn | Leverandør | Formål | Varighet |
| --- | --- | --- | --- |
| `_fbp` | Meta | Markedsføring. Kjenner igjen nettleseren din for å måle og målrette annonser. Settes bare etter samtykke | 90 dager |
| `fr` | Meta | Markedsføring. Leverer og måler annonser på Facebook og Instagram. Settes bare etter samtykke | 90 dager |

Meta Pixel registrerer hvilke sider du besøker, hvilke produkter du ser på, når du starter en
betaling, og verdien og produkt-ID-en når du kjøper. Meta mottar også IP-adressen din, opplysninger om
nettleseren og enheten din, og informasjonskapslene over. Vi sender ikke navn, e-post eller
telefonnummer til Meta.

Behandlingsgrunnlaget er samtykket ditt, etter ekomloven § 3-15 og personvernforordningen art. 6 nr. 1
bokstav a.

Altinnendata og Meta Platforms Ireland Ltd er felles behandlingsansvarlige etter personvernforordningen
art. 26 for innsamlingen av opplysningene og overføringen til Meta. Det Meta gjør med opplysningene
etterpå, er Meta alene ansvarlig for. Se Metas [personvernerklæring](https://www.facebook.com/privacy/policy)
og Metas [retningslinjer for informasjonskapsler](https://www.facebook.com/policies/cookies/).

Meta kan overføre opplysningene til USA. Overføringen bygger på EU-U.S. Data Privacy Framework. Les mer
i [personvernerklæringen](/no/privacy).

## Trekk tilbake samtykket

Du kan når som helst endre eller trekke tilbake samtykket med lenken «Innstillinger for
informasjonskapsler» nederst på hver side. Det er like enkelt som å gi samtykket. Etter at du har
trukket det tilbake, lastes ikke Meta Pixel lenger. Kapsler som allerede er satt, kan du slette i
nettleseren.

## Hvordan styre kapsler

Du kan også slette eller blokkere informasjonskapsler i nettleserinnstillingene. Blokkerer du de
nødvendige kapslene, påvirker det bare det å holde seg innlogget. Resten av siden virker uten dem.

## Spørsmål?

[Ta kontakt](/no/contact) hvis du lurer på noe rundt bruken av informasjonskapsler. Du kan også klage
til [Datatilsynet](https://www.datatilsynet.no).
""";

        private const string CookiesEn = """
## What are cookies?

Cookies are small text files stored in your browser. They help a site remember your session between
requests. This page also covers similar technology, such as scripts and pixels that read or store
information on your device.

## Necessary cookies

These cookies are needed for the site to work, and they are set without consent. The cookies below
are only set when someone signs in.

| Name | Provider | Purpose | Duration |
| --- | --- | --- | --- |
| `next-auth.session-token` | Altinnendata | Keeps you signed in | Session / 30 days |
| `next-auth.csrf-token` | Altinnendata | Security. Prevents cross-site request forgery | Session |
| `next-auth.callback-url` | Altinnendata | Remembers where to redirect after login | Session |

Your cookie choice is stored in your browser so we do not ask again on every visit. It holds only the
choice and when you made it.

## Marketing cookies (only with consent)

When we have turned on the Meta Pixel, we use it for marketing and to measure ads on Facebook and
Instagram. The Meta Pixel is provided by Meta Platforms Ireland Ltd. It loads only after you have
accepted marketing. If you decline, or make no choice, it does not load and the cookies below are not
set. They are not set either while the Meta Pixel is turned off.

| Name | Provider | Purpose | Duration |
| --- | --- | --- | --- |
| `_fbp` | Meta | Marketing. Recognises your browser to measure and target ads. Set only after consent | 90 days |
| `fr` | Meta | Marketing. Delivers and measures ads on Facebook and Instagram. Set only after consent | 90 days |

The Meta Pixel records the pages you visit, the products you view, when you start a checkout, and the
value and product ID when you buy. Meta also receives your IP address, information about your browser
and device, and the cookies above. We do not send your name, email or phone number to Meta.

The legal basis is your consent, under section 3-15 of the Norwegian Electronic Communications Act
(ekomloven) and GDPR Art. 6(1)(a).

Altinnendata and Meta Platforms Ireland Ltd are joint controllers under GDPR Art. 26 for collecting
the data and transmitting it to Meta. Meta alone is responsible for what it does with the data after
that. See Meta's [Privacy Policy](https://www.facebook.com/privacy/policy) and Meta's
[Cookies Policy](https://www.facebook.com/policies/cookies/).

Meta may transfer the data to the United States. The transfer is based on the EU-U.S. Data Privacy
Framework. Read more in our [Privacy Policy](/en/privacy).

## Withdrawing consent

You can change or withdraw your consent at any time through the "Cookie settings" link at the bottom
of every page. It is as easy as giving consent. Once you withdraw it, the Meta Pixel no longer loads.
You can delete cookies that were already set in your browser.

## Managing cookies

You can also clear or block cookies through your browser settings. Blocking the necessary cookies only
affects staying signed in. The rest of the site works without them.

## Questions?

[Contact us](/en/contact) if you have any questions about how we use cookies. You may also lodge a
complaint with the [Norwegian Data Protection Authority (Datatilsynet)](https://www.datatilsynet.no/en/).
""";
    }
}
