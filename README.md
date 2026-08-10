<p align="center">
  The backend behind altinnendata.no — PC build showcase, component catalog, site content, and enquiries.
</p>

---

altinnendata-api is the API for **Altinnendata** — local building and installation
of desktop PCs. It stores the builds shown on the site with their parts list and
per-language text, keeps the component catalog, sends enquiries by email, and
serves the admin-managed content for
[altinnendata-app](https://github.com/sondresjolyst/altinnendata-app).

## What it does

- **Builds** — public showcase; admins create a build with price, availability
  (`Available` / `Reserved` / `Sold`), a parts list, a cover image, and page
  content per language.
- **Component catalog** — categories (CPU, GPU, kabinett …) → manufacturers →
  parts, reused across builds.
- **Content** — home page sections and the legal pages, stored per locale and
  edited from the admin pages.
- **Contact** — enquiries (use case, budget, which build) emailed via Brevo.
- **Accounts** — sign-in, JWT + refresh tokens, password reset, roles
  (`Default` / `Admin`). There is no public sign-up: an admin invites a user,
  who then sets their own password.

Built as vertical slices (minimal API endpoints + FluentValidation), with
PostgreSQL via EF Core.

## Languages

`Constants/Locales.cs` lists the supported locales; `no` is the default. Text
that an admin writes lives in translation tables (`PcBuildTranslations`,
`ComponentCategoryTranslations`) or in a per-locale row (`HomePageContents`,
`LegalPages`). Public endpoints take `?locale=` and fall back to the default
locale when a language has not been filled in.

Adding a language means adding its tag to `Locales.Supported` — no schema change.

---

## For developers

<details>
<summary>Run, configure, and the endpoints</summary>

### Stack

ASP.NET Core 10 · PostgreSQL (EF Core / Npgsql) · ASP.NET Identity + JWT ·
Mapster · Serilog · AspNetCoreRateLimit · Brevo.

### Run locally

```bash
dotnet restore
dotnet ef database update   # needs local Postgres (see appsettings.Development.json)
dotnet run                  # Swagger at /swagger
```

### Create the first admin

There is no public registration endpoint, so the first account is made directly
in the database — insert a user row, then grant the role:

```sql
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "AspNetUsers" u, "AspNetRoles" r
WHERE u."Email" = 'you@example.com' AND r."Name" = 'Admin';
```

Further admins are invited from `/admin/users`, which emails them a code to set
a password with.

### Configuration

In production, secrets come from environment variables:

| Variable                                                                  | What it's for                          |
| ------------------------------------------------------------------------- | -------------------------------------- |
| `ConnectionStrings__DefaultConnection`                                    | PostgreSQL connection string.          |
| `Jwt__Key`, `Jwt__Issuer`                                                 | JWT signing key and issuer.            |
| `BrevoSettings__ApiKey`, `BrevoSettings__SenderEmail`, `BrevoSettings__SenderName` | Brevo email.                  |
| `Storage__ImagesPath`                                                     | NFS-backed mount for uploaded images.  |

### API reference

Run the app and browse **Swagger at `/swagger`** for the current endpoints,
schemas, and auth.

### Layout

```
Features/        # one folder per slice (Auth, Builds, Components, Content, Users, …)
Infrastructure/  # endpoint registration, validation filter, seed data
Services/        # email, image storage
Models/          # EF Core entities + DbContext
Migrations/      # EF Core migrations
```

</details>
