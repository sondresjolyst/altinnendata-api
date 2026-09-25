# altinnendata-api

API for Altinnendata, serving
[altinnendata-app](https://github.com/sondresjolyst/altinnendata-app).

## Stack

ASP.NET Core 10, PostgreSQL through EF Core and Npgsql, ASP.NET Identity with
JWT, Mapster, Serilog, AspNetCoreRateLimit, Brevo.

## Quick start

```bash
dotnet restore
dotnet ef database update   # needs a local Postgres, see appsettings.Development.json
dotnet run                  # Swagger at /swagger
```

## Environment

Production reads these from the cluster secret.

| Variable | Used for |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Key`, `Jwt__Issuer` | JWT signing key and issuer. The key must match the app's `ALTINNENDATA_API_JWT_SECRET` |
| `BrevoSettings__ApiKey`, `BrevoSettings__SenderEmail`, `BrevoSettings__SenderName` | Transactional email |
| `Seed__AdminEmail`, `Seed__AdminPassword` | First admin, created at startup |
| `Site__BaseUrl` | Used in links sent by email |
| `Storage__ImagesPath` | Mount for uploaded images, `/data/images` in the cluster |

## What it serves

| Area | Holds |
| --- | --- |
| Builds | Public showcase. Price, availability (`Available`, `Reserved`, `Sold`), parts list, cover image, page content per language |
| Component catalog | Categories, manufacturers and parts, reused across builds |
| Content | Home page sections and legal pages, per locale |
| Contact | Enquiries emailed through Brevo |
| Accounts | Sign-in, JWT and refresh tokens, password reset, `Default` and `Admin` roles |

There is no public sign-up. An admin invites a user from `/admin/users`, who
then sets their own password from the emailed code.

Endpoints are vertical slices of minimal API handlers with FluentValidation.
Browse `/swagger` on a running instance for the current surface.

## Health

| Path | Reports |
| --- | --- |
| `/health` | The process is up. No dependency checks, so a database outage does not restart the pod |
| `/health/ready` | The database connection. Fails while Postgres is unreachable, which takes the pod out of its Service |

Both are anonymous, and both are blocked at the ingress: only the kubelet
reaches them, over the pod address.

## Languages

`Constants/Locales.cs` lists the supported locales and `no` is the default. Text
an admin writes lives in translation tables (`PcBuildTranslations`,
`ComponentCategoryTranslations`) or in a per-locale row (`HomePageContents`,
`LegalPages`). Public endpoints take `?locale=` and fall back to the default when
a language has not been filled in.

To add a language, add its tag to `Locales.Supported`. No schema change.

## Layout

```
Features/        # one folder per slice: Auth, Builds, Components, Content, Users
Infrastructure/  # endpoint registration, validation filter, seed data
Services/        # email, image storage
Models/          # EF Core entities and the DbContext
Migrations/      # EF Core migrations
```

## Deployment

Image [`sondresjo/altinnendata-api`](https://hub.docker.com/r/sondresjo/altinnendata-api)
on Docker Hub, chart `altinnendata-api` in
[tumogroup-charts](https://github.com/sondresjolyst/tumogroup-charts), applied by
Flux from [tumo-flux](https://github.com/sondresjolyst/tumo-flux) to
`altinnendata-dev` and `altinnendata-prod`.

The container runs as the non-root `app` user with a read-only root filesystem,
so anything written at runtime needs a volume. Uploaded images go to the
`/data/images` mount, and the data protection key ring to `/home/app/.aspnet`.

Migrations run at startup, so a deploy against a cold database can take a while.
The startup probe allows for that before the liveness probe can restart the pod.

A push to `main` builds the `dev` tag. A release-please release builds `vX.Y.Z`,
tags it `latest` and opens a chart bump against
[tumogroup-charts](https://github.com/sondresjolyst/tumogroup-charts). Cluster
secrets are created by
[`scripts/altinnendata/bootstrap.sh`](https://github.com/sondresjolyst/tumo-platform/blob/main/scripts/altinnendata/bootstrap.sh)
in [tumo-platform](https://github.com/sondresjolyst/tumo-platform).

## License

Proprietary. Copyright (c) 2026 Sondre Sjølyst.
