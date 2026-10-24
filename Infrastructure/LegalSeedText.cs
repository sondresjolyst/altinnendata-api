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

Altinnendata AS er behandlingsansvarlig for personopplysningene vi samler inn gjennom denne nettsiden. Organisasjonsnummeret er 938 433 240, og adressen er Mårvegen 21a, 4347 Lye.

For Meta Pixel er vi felles behandlingsansvarlige med Meta Platforms Ireland Ltd. Se «Deling av opplysninger».

## Hvilke opplysninger vi samler inn

- Bestillinger: navn, e-postadresse, mobilnummer, leveringsmåte, merknad om henting, hvilken datamaskin du kjøper, priser, betalingsstatus og beskjeder om levering. Ved sending og hjemlevering lagrer vi også adressen din og om den er innenfor leveringsavstanden. Som bevis på avtalen lagrer vi vilkårene du godtok, hvilken versjon det var, når du godtok dem og en forkortet IP-adresse.
- Henvendelser: navn, e-postadresse, telefonnummer, bruksområde, budsjett og meldingen du sender i kontaktskjemaet.
- Brukerkontoer: navn, e-postadresse og passord. Passordet lagres som hash. Bare vi som driver siden, har kontoer. Besøkende kan ikke registrere seg.
- Bruksdata: enkle tjenerlogger med anonyme bruker-ID-er og forespurte adresser. Vi bruker dem til feilsøking og sikkerhet.
- Meta Pixel, bare etter samtykke og bare når vi har slått den på: sidene du besøker, produktene du ser på, når du starter en betaling, og verdien og produkt-ID-en når du kjøper. Meta mottar også IP-adressen din, opplysninger om nettleseren og enheten din, og informasjonskapsler. Vi sender ikke navn, e-postadresse eller mobilnummer til Meta.
- Samtykkelogg: hvert valg du tar om informasjonskapsler, med en tilfeldig samtykke-ID, ja eller nei til markedsføring, versjonen av teksten i banneret, tidspunktet, og bruker-ID-en din bare hvis du er innlogget. Oppføringen inneholder ikke IP-adressen din eller opplysninger om nettleseren.

Betalingen skjer i Vipps. Vi ser aldri kortnummeret ditt eller andre betalingsopplysninger. Vi får bare vite beløpet, ordrenummeret og om betalingen er reservert, trukket eller betalt tilbake. Vi får ingen profilopplysninger fra Vipps.

## Hva vi bruker opplysningene til

- Å levere datamaskinen du har kjøpt, sende ordrebekreftelse og faktura, føre regnskap og dokumentere at du godtok vilkårene.
- Å svare på henvendelsen din og avtale arbeidet du spør om.
- Å sende e-post om innlogging, som invitasjoner og tilbakestilling av passord.
- Å forbedre tjenesten og finne tekniske feil.
- Å dokumentere samtykkene dine.

Vi bruker opplysningene til markedsføring og måling av annonser hos Meta bare når du har samtykket. Vi selger dem ikke.

## Behandlingsgrunnlag

- Avtale (art. 6 nr. 1 bokstav b). Vi trenger opplysningene i bestillingen for å gjennomføre kjøpet og levere datamaskinen. Vi trenger kontoopplysninger og e-post om innlogging for å gi tilgang til administrasjonssidene.
- Rettslig forpliktelse (art. 6 nr. 1 bokstav c). Bokføringsloven krever at vi tar vare på regnskapsmateriale, også opplysninger om salget.
- Berettiget interesse (art. 6 nr. 1 bokstav f). Vi behandler henvendelser for å kunne svare deg. Vi tar vare på tjenerlogger for sikkerhet og feilsøking. Vi lagrer den forkortede IP-adressen som bevis på at du godtok vilkårene. Vi fører en logg over samtykkene fordi vi må kunne dokumentere dem, slik art. 7 nr. 1 krever. Interessen vår går ikke foran rettighetene dine, og du kan protestere når som helst.
- Samtykke (art. 6 nr. 1 bokstav a og ekomloven § 3-15). Meta Pixel lastes og setter informasjonskapsler bare etter at du har sagt ja til markedsføring.

## Deling av opplysninger

Vi selger ikke personopplysninger. Plattformen kjører på infrastruktur vi styrer selv, uten noen skyleverandør. Vi deler opplysninger med disse:

- Brevo er databehandler og holder til i Frankrike, innenfor EØS. Brevo sender e-post for oss. Det gjelder invitasjoner, tilbakestilling av passord, ordrebekreftelser og andre beskjeder om bestillingen, og kvitteringen når du sender kontaktskjemaet. Brevo videresender også henvendelser fra kontaktskjemaet til oss. Vi deler e-postadressen din og innholdet i e-posten med Brevo bare til dette formålet.
- Fiken er databehandler og holder til i Norge. Fiken er regnskapssystemet vårt. Når vi lager faktura og fører regnskap, lagres navn, e-postadresse, adresse og opplysninger om kjøpet der.
- Vipps MobilePay holder til i Norge og er selvstendig behandlingsansvarlig. For å gjennomføre betalingen får Vipps beløpet, ordrenummeret, hva du kjøper og mobilnummeret ditt. Vipps behandler opplysningene etter sin egen personvernerklæring.
- Google er databehandler for e-posten vår i Google Workspace. Der får vi varsel om nye bestillinger og henvendelser, og derfra svarer vi deg på e-post.
- Meta Platforms Ireland Ltd får opplysningene fra Meta Pixel, bare etter samtykke. Vi er felles behandlingsansvarlige med Meta etter personvernforordningen art. 26 for innsamlingen på denne siden og overføringen til Meta. Det Meta gjør med opplysningene etterpå, er Meta alene ansvarlig for. Meta kan overføre opplysningene til USA. Overføringen bygger på EU-U.S. Data Privacy Framework. Les Metas [personvernerklæring](https://www.facebook.com/privacy/policy) og Metas [retningslinjer for informasjonskapsler](https://www.facebook.com/policies/cookies/).

Vi kan også utlevere opplysninger når loven krever det.

## Lagringstid

- Bestillinger: fem år etter slutten av regnskapsåret da du kjøpte, fordi bokføringsloven krever det. Deretter sletter vi dem. Hvis betalingen feilet eller bestillingen ble kansellert før vi trakk beløpet, sletter vi bestillingen etter 90 dager. Hvis bestillingen fortsatt holder av en datamaskin, sletter vi den først når datamaskinen er frigitt.
- Henvendelser: 12 måneder etter at vi mottok dem. Deretter sletter vi dem automatisk.
- Kontoopplysninger: til kontoen slettes. Da fjerner vi navn, e-postadresse og passord.
- Tjenerlogger: 90 dager. Måledata: 60 dager.
- Sikkerhetskopier av databasen: Sikkerhetskopiene er kryptert. Etter sletting kan rester ligge igjen i sikkerhetskopier i opptil omtrent 6 måneder, til de gamle kopiene er skiftet ut. Vi bruker ikke sikkerhetskopier til behandling.
- Samtykkelogg: 3 år. Deretter sletter vi oppføringene automatisk.
- Informasjonskapslene fra Meta: se siden om informasjonskapsler. Hvor lenge Meta lagrer opplysningene, står i Metas personvernerklæring.

## Rettighetene dine

Etter personvernforordningen har du rett til:

- Innsyn og dataportabilitet. Ta kontakt for å få en kopi av opplysningene vi har om deg.
- Sletting. Ta kontakt for å få slettet henvendelsen din og opplysningene i den.
- Retting. Ta kontakt hvis noe er feil.
- Protest. Du kan protestere mot behandling som bygger på berettiget interesse.
- Å trekke tilbake samtykket. Bruk lenken «Innstillinger for informasjonskapsler» nederst på siden. Det er like enkelt som å gi samtykket.

Opplysninger vi må ta vare på etter bokføringsloven, kan vi ikke slette før lagringstiden er ute.

Send henvendelser til altinnendata@gmail.com. Du kan også klage til Datatilsynet.

## Informasjonskapsler

Vi bruker informasjonskapsler til innlogging og økter. Med samtykket ditt setter Meta Pixel også informasjonskapsler til markedsføring. Uten samtykke settes de ikke. Les mer på siden om informasjonskapsler.
""";

        private const string PrivacyEn = """
## Who is responsible

Altinnendata AS is the data controller for the personal data we collect through this website. Our organisation number is 938 433 240, and our address is Mårvegen 21a, 4347 Lye, Norway.

For the Meta Pixel we are joint controllers with Meta Platforms Ireland Ltd. See "Data sharing".

## What data we collect

- Orders: name, email address, mobile number, delivery method, collection note, which PC you buy, prices, payment status and delivery messages. For shipping and home delivery we also store your address and whether it is within the delivery distance. As proof of the agreement we store the terms you accepted, which version they were, when you accepted them and a shortened IP address.
- Enquiries: the name, email address, phone number, use case, budget and message you send through the contact form.
- User accounts: name, email address and password. The password is stored hashed. Only the people who run the site have accounts. Visitors cannot sign up.
- Usage data: basic server logs with opaque user IDs and request paths. We use them for debugging and security.
- Meta Pixel, only after consent and only when we have turned it on: the pages you visit, the products you view, when you start a checkout, and the value and product ID when you buy. Meta also receives your IP address, information about your browser and device, and cookies. We do not send your name, email address or mobile number to Meta.
- Consent log: each cookie choice you make, with a random consent ID, yes or no to marketing, the version of the banner text, the time, and your user ID only when you are signed in. The entry holds no IP address or browser data.

Payment happens in Vipps. We never see your card number or other payment details. We only learn the amount, the order number and whether the payment is reserved, taken or refunded. We get no profile data from Vipps.

## How we use your data

- To deliver the PC you bought, send the order confirmation and invoice, keep our accounts and document that you accepted the terms.
- To answer your enquiry and arrange the work you ask about.
- To send emails about signing in, such as invitations and password resets.
- To improve the service and find technical faults.
- To document your consent choices.

We use your data for marketing and ad measurement at Meta only when you have given consent. We do not sell it.

## Legal basis for processing

- Contract (Art. 6(1)(b)). We need the order details to complete the purchase and deliver the PC. We need account data and sign-in emails to give access to the admin pages.
- Legal obligation (Art. 6(1)(c)). The Norwegian Bookkeeping Act requires us to keep accounting records, including details of the sale.
- Legitimate interests (Art. 6(1)(f)). We process enquiries so we can answer you. We keep server logs for security and debugging. We store the shortened IP address as proof that you accepted the terms. We keep a log of consent choices because we must be able to document them, as Art. 7(1) requires. Our interest does not override your rights, and you can object at any time.
- Consent (Art. 6(1)(a) and section 3-15 of the Norwegian Electronic Communications Act). The Meta Pixel loads and sets cookies only after you have accepted marketing.

## Data sharing

We do not sell personal data. The platform runs on infrastructure we control ourselves, with no cloud provider behind it. We share data with:

- Brevo is a data processor based in France, within the EEA. Brevo sends emails for us. These are invitations, password resets, order confirmations and other order messages, and the receipt when you send the contact form. Brevo also forwards contact form enquiries to us. We share your email address and the email content with Brevo only for this purpose.
- Fiken is a data processor based in Norway. Fiken is our accounting system. When we create an invoice and keep our accounts, your name, email address, address and details of the purchase are stored there.
- Vipps MobilePay is based in Norway and is an independent controller. To process the payment, Vipps receives the amount, the order number, what you buy and your mobile number. Vipps processes the data under its own privacy notice.
- Google is the data processor for our email in Google Workspace. We receive notices of new orders and enquiries there, and we reply to you by email from it.
- Meta Platforms Ireland Ltd receives the Meta Pixel data, only after consent. We are joint controllers with Meta under GDPR Art. 26 for the collection on this site and the transmission to Meta. Meta alone is responsible for what it does with the data after that. Meta may transfer the data to the United States. The transfer is based on the EU-U.S. Data Privacy Framework. Read Meta's [Privacy Policy](https://www.facebook.com/privacy/policy) and Meta's [Cookies Policy](https://www.facebook.com/policies/cookies/).

We may also disclose data when the law requires it.

## Data retention

- Orders: five years after the end of the financial year of the purchase, as the Bookkeeping Act requires. Then we delete them. If the payment failed or the order was cancelled before we took the payment, we delete the order after 90 days. If an order still holds a PC, we delete it only once the PC has been released.
- Enquiries: 12 months after we receive them. Then we delete them automatically.
- Account data: until the account is deleted. We then remove the name, email address and password.
- Server logs: 90 days. Metrics: 60 days.
- Database backups: The backups are encrypted. After deletion, traces may remain in backups for up to about 6 months, until the old copies have been replaced. We do not use backups for processing.
- Consent log: 3 years. Then we delete the entries automatically.
- Meta cookies: see our Cookie Policy. How long Meta keeps the data is set out in Meta's Privacy Policy.

## Your rights

Under the GDPR you have the right to:

- Access and portability. Contact us for a copy of the data we hold about you.
- Erasure. Contact us to have your enquiry and its data deleted.
- Correction. Contact us if something is wrong.
- Object. You can object to processing based on our legitimate interests.
- Withdraw consent. Use the "Cookie settings" link at the bottom of the page. It is as easy as giving consent.

Data we must keep under the Bookkeeping Act cannot be deleted until the retention period has ended.

Send requests to altinnendata@gmail.com. You can also complain to the Norwegian Data Protection Authority (Datatilsynet).

## Cookies

We use cookies for signing in and sessions. With your consent, the Meta Pixel also sets cookies for marketing. Without consent they are not set. Read more in our Cookie Policy.
""";

        private const string CookiesNo = """
## Hva er informasjonskapsler?

Informasjonskapsler er små tekstfiler som lagres i nettleseren din. De lar en nettside huske økten din mellom forespørsler.

## Kapslene vi bruker

Å surfe på siden setter ingen kapsler for sporing eller annonser uten samtykket ditt. Innloggingssystemet setter `__Host-next-auth.csrf-token` og `__Secure-next-auth.callback-url` ved hvert besøk for å beskytte innloggingen mot forfalskede forespørsler. De er nødvendige og inneholder ingen personopplysninger. `__Secure-next-auth.session-token` settes bare når noen logger inn.

| Navn | Formål | Varighet |
|---|---|---|
| `__Secure-next-auth.session-token` | Holder deg innlogget. Settes bare ved innlogging | 7 dager |
| `__Host-next-auth.csrf-token` | Sikkerhet. Hindrer forfalskning av forespørsler på tvers av nettsteder. Settes ved hvert besøk | Økt |
| `__Secure-next-auth.callback-url` | Husker hvor du skal sendes etter innlogging. Settes ved hvert besøk | Økt |
| `consent` (lokal lagring) | Nødvendig. Husker valget ditt om markedsføring, slik at vi ikke spør på nytt ved hvert besøk | Til du velger på nytt eller sletter nettstedsdata. Vi spør på nytt etter 12 måneder |

`consent` er ikke en informasjonskapsel, men lagres i nettleserens lokale lagring for denne siden. Den inneholder valget ditt om markedsføring, versjonen av valget, når du tok det og en tilfeldig samtykke-ID. Hvert valg lagres også på serveren vår for å dokumentere samtykket. Oppføringen i loggen inneholder samtykke-ID-en, valget, versjonen av teksten i banneret, tidspunktet, og bruker-ID-en din bare hvis du er innlogget. Den inneholder ikke IP-adressen din eller opplysninger om nettleseren. Loggen slettes etter 3 år.

## Markedsføring

Når vi har lagt inn en Meta Pixel-ID, bruker vi Meta Pixel fra Meta Platforms Ireland Ltd til markedsføring og måling av annonser på Facebook og Instagram. Kapslene under settes bare etter at du har sagt ja til markedsføring. Sier du nei, eller tar du ikke stilling, settes de ikke.

| Navn | Leverandør | Formål | Varighet |
|---|---|---|---|
| `_fbp` | Meta | Markedsføring. Kjenner igjen nettleseren din for å måle og målrette annonser. Settes bare etter samtykke | 90 dager |
| `_fbc` | Meta | Markedsføring. Husker at du kom fra en annonse, så Meta kan måle den. Settes bare etter samtykke, og bare når du kommer fra en annonse | 90 dager |
| `fr` | Meta | Markedsføring. Leverer og måler annonser på Facebook og Instagram. Settes bare etter samtykke | 90 dager |

Behandlingsgrunnlaget er samtykket ditt, etter ekomloven § 3-15 og personvernforordningen art. 6 nr. 1 bokstav a. Vi er felles behandlingsansvarlige med Meta etter personvernforordningen art. 26 for innsamlingen og overføringen til Meta. Les Metas [retningslinjer for informasjonskapsler](https://www.facebook.com/policies/cookies/). Mer om hva Meta mottar, står i [personvernerklæringen](/no/privacy).

## Hvordan styre kapsler

Du kan når som helst endre eller trekke tilbake samtykket med lenken «Innstillinger for informasjonskapsler» nederst på siden. Det er like enkelt som å gi samtykket. Da sletter vi `_fbp` og `_fbc`. `fr` ligger på Metas eget domene, og den kan du slette i nettleseren.

Du kan også slette eller blokkere informasjonskapsler i nettleserinnstillingene. Å blokkere innloggingskapslene påvirker bare det å logge inn og holde seg innlogget. Resten av siden virker uten dem.

## Spørsmål?

Ta kontakt hvis du lurer på noe rundt bruken av informasjonskapsler.
""";

        private const string CookiesEn = """
## What are cookies?

Cookies are small text files stored in your browser. They help a site remember your session between requests.

## Cookies we use

Browsing the site sets no tracking or advertising cookies without your consent. Buying in the online shop sets no cookies on our site either. Payment happens at Vipps, which has its own cookie rules. The sign-in system sets `__Host-next-auth.csrf-token` and `__Secure-next-auth.callback-url` on every visit to protect sign-in against forged requests. They are necessary and hold no personal data. `__Secure-next-auth.session-token` is set only when someone signs in.

| Name | Purpose | Duration |
|---|---|---|
| `__Secure-next-auth.session-token` | Keeps you signed in. Set only on sign-in | 7 days |
| `__Host-next-auth.csrf-token` | Security. Prevents cross-site request forgery. Set on every visit | Session |
| `__Secure-next-auth.callback-url` | Remembers where to redirect after login. Set on every visit | Session |
| `consent` (local storage) | Necessary. Remembers your marketing choice so we do not ask again on every visit | Until you choose again or clear site data. We ask again after 12 months |

`consent` is not a cookie. It is kept in your browser's local storage for this site. It holds your marketing choice, the version of the choice, when you made it and a random consent ID. Each choice is also stored on our server to document the consent. The log entry holds the consent ID, the choice, the version of the banner text, the time, and your user ID only when you are signed in. It holds no IP address or browser data. The log is deleted after 3 years.

## Marketing cookies

When we have set a Meta Pixel ID, we use the Meta Pixel from Meta Platforms Ireland Ltd for marketing and to measure ads on Facebook and Instagram. The cookies below are set only after you have accepted marketing. If you decline, or make no choice, they are not set.

| Name | Provider | Purpose | Duration |
|---|---|---|---|
| `_fbp` | Meta | Marketing. Recognises your browser to measure and target ads. Set only after consent | 90 days |
| `_fbc` | Meta | Marketing. Remembers that you came from an ad so Meta can measure it. Set only after consent, and only when you arrive from an ad | 90 days |
| `fr` | Meta | Marketing. Delivers and measures ads on Facebook and Instagram. Set only after consent | 90 days |

The legal basis is your consent, under section 3-15 of the Norwegian Electronic Communications Act (ekomloven) and GDPR Art. 6(1)(a). We are joint controllers with Meta under GDPR Art. 26 for the collection and the transmission to Meta. Read Meta's [Cookies Policy](https://www.facebook.com/policies/cookies/). Our [Privacy Policy](/en/privacy) explains what Meta receives.

## Managing cookies

You can change or withdraw your consent at any time through the "Cookie settings" link at the bottom of the page. It is as easy as giving consent. We then delete `_fbp` and `_fbc`. `fr` sits on Meta's own domain, and you can delete it in your browser.

You can also clear or block cookies through your browser settings. Blocking the sign-in cookies only affects signing in and staying signed in. The rest of the site works without them.

## Questions?

Contact us if you have any questions about how we use cookies.
""";
    }
}
