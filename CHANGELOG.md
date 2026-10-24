# Changelog

## [1.14.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.13.0...v1.14.0) (2026-10-24)


### Features

* add Meta Pixel ID setting, consent log and updated legal seed text ([#140](https://github.com/sondresjolyst/altinnendata-api/issues/140)) ([1ee1f13](https://github.com/sondresjolyst/altinnendata-api/commit/1ee1f13762d965711e658b778f835df4576953d8))

## [1.13.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.12.1...v1.13.0) (2026-10-23)


### Features

* **shop:** anonymise buyer details on orders five years after the financial year ([#138](https://github.com/sondresjolyst/altinnendata-api/issues/138)) ([cd5a0f5](https://github.com/sondresjolyst/altinnendata-api/commit/cd5a0f5491e5a21877671e087fa22f514eee6dad))
* **shop:** use the withdrawal form from SjolystInnovation.AppKit.Pdf 1.1.0 ([#139](https://github.com/sondresjolyst/altinnendata-api/issues/139)) ([a25b2d8](https://github.com/sondresjolyst/altinnendata-api/commit/a25b2d8bd9d38cb631d9ef36fe4bc6c57fef3d25))


### Bug Fixes

* **time:** find the Oslo time zone by its Windows name on Windows ([#137](https://github.com/sondresjolyst/altinnendata-api/issues/137)) ([676e5ab](https://github.com/sondresjolyst/altinnendata-api/commit/676e5ab843086da6d0ab56c56addf0a955b193a2))
* **time:** load the Oslo time zone once at startup ([#135](https://github.com/sondresjolyst/altinnendata-api/issues/135)) ([75b995f](https://github.com/sondresjolyst/altinnendata-api/commit/75b995f9e3ab50b5f6004dd8b65601a1af6a17b7))

## [1.12.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.12.0...v1.12.1) (2026-10-23)


### Miscellaneous Chores

* release 1.12.1 ([#133](https://github.com/sondresjolyst/altinnendata-api/issues/133)) ([4cb5d39](https://github.com/sondresjolyst/altinnendata-api/commit/4cb5d39b48eb0012225360c7f0d4cc604bf74424))

## [1.12.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.11.0...v1.12.0) (2026-10-22)


### Features

* **contact:** store enquiries for the admin ([#128](https://github.com/sondresjolyst/altinnendata-api/issues/128)) ([cb09663](https://github.com/sondresjolyst/altinnendata-api/commit/cb09663373073e0e79ed0588db5f289f880a1a10))
* **security:** encrypt the Data Protection keys with a certificate ([#130](https://github.com/sondresjolyst/altinnendata-api/issues/130)) ([5295f19](https://github.com/sondresjolyst/altinnendata-api/commit/5295f1928de0377bde093cdf57737f26f519f6ed))

## [1.11.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.10.1...v1.11.0) (2026-10-20)


### Features

* **shop:** sell builds with Vipps from the build page ([#120](https://github.com/sondresjolyst/altinnendata-api/issues/120)) ([077edbe](https://github.com/sondresjolyst/altinnendata-api/commit/077edbe279cb98bd6af9d5142ee4cdf7b6ba0742))

## [1.10.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.10.0...v1.10.1) (2026-10-20)


### Dependencies

* **nuget:** bump `SjolystInnovation.AppKit.Hosting` from 1.0.1 to 1.0.3 ([#117](https://github.com/sondresjolyst/altinnendata-api/issues/117)) ([6df7c6b](https://github.com/sondresjolyst/altinnendata-api/commit/6df7c6bbea282a6d1210170c66d08516972744d5))

## [1.10.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.9.0...v1.10.0) (2026-10-18)


### Features

* **email:** tag Brevo emails with the app name ([#118](https://github.com/sondresjolyst/altinnendata-api/issues/118)) ([5af43dc](https://github.com/sondresjolyst/altinnendata-api/commit/5af43dc27eadca864227393be0cd42869738d74d))

## [1.9.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.8.4...v1.9.0) (2026-10-18)


### Features

* **api:** answer unreadable JSON bodies with 400 through AppKit.Hosting 1.0.1 ([#116](https://github.com/sondresjolyst/altinnendata-api/issues/116)) ([bb2cf76](https://github.com/sondresjolyst/altinnendata-api/commit/bb2cf76e3ebd8bff8c4551cbdd5f15554aa60621))
* **api:** serve roles, email stats and storage usage from the AppKit 1.0.1 packages ([#114](https://github.com/sondresjolyst/altinnendata-api/issues/114)) ([b8ce4dc](https://github.com/sondresjolyst/altinnendata-api/commit/b8ce4dc2b3cdfef995f8ad8af3ec3d6d306a7b9f))
* **auth:** end sessions at the API on logout and after a password reset ([#92](https://github.com/sondresjolyst/altinnendata-api/issues/92)) ([fbd098c](https://github.com/sondresjolyst/altinnendata-api/commit/fbd098cac0d2dd33a736b2dca6feca2ead772617))
* **auth:** send reset and invite emails through SjolystInnovation.AppKit.Auth ([#101](https://github.com/sondresjolyst/altinnendata-api/issues/101)) ([93307bb](https://github.com/sondresjolyst/altinnendata-api/commit/93307bbc3b84faa4bc5b429159f1b75b08244410))
* **auth:** serve sign-in and password reset from SjolystInnovation.AppKit.Auth ([#97](https://github.com/sondresjolyst/altinnendata-api/issues/97)) ([9805dc1](https://github.com/sondresjolyst/altinnendata-api/commit/9805dc136cbb8a69c5907bd84715354cb6453af1))
* **auth:** use SjolystInnovation.AppKit.Auth for Identity, bearer tokens and token cleanup ([#95](https://github.com/sondresjolyst/altinnendata-api/issues/95)) ([92cd4f6](https://github.com/sondresjolyst/altinnendata-api/commit/92cd4f60ad13bcf38ea12ce2852a90160853ad56))
* **content:** serve the home page sections from SjolystInnovation.AppKit.SiteSettings ([#108](https://github.com/sondresjolyst/altinnendata-api/issues/108)) ([a86b34b](https://github.com/sondresjolyst/altinnendata-api/commit/a86b34b174d17642ec218174b0e75ef8ed824545))
* **email:** send through SjolystInnovation.AppKit.Email instead of the Brevo SDK ([#96](https://github.com/sondresjolyst/altinnendata-api/issues/96)) ([85a26a2](https://github.com/sondresjolyst/altinnendata-api/commit/85a26a270a221a9e2e58099fe7317c3f9d98a457))
* **legal:** serve legal pages from SjolystInnovation.AppKit.SiteSettings ([#105](https://github.com/sondresjolyst/altinnendata-api/issues/105)) ([afa89e3](https://github.com/sondresjolyst/altinnendata-api/commit/afa89e3cf4caa9ca589744fab7f45dc15b658590))
* **settings:** move settings, company info and branding onto SjolystInnovation.AppKit.SiteSettings ([#103](https://github.com/sondresjolyst/altinnendata-api/issues/103)) ([79ee0b8](https://github.com/sondresjolyst/altinnendata-api/commit/79ee0b89bf0e27e7f8a8f135fe0d28c40b0aa338))
* use SjolystInnovation.AppKit.Hosting for startup and rate limiting ([#94](https://github.com/sondresjolyst/altinnendata-api/issues/94)) ([0307282](https://github.com/sondresjolyst/altinnendata-api/commit/030728229e7f35cd19afb66a7303cbccf92f672c))
* **users:** delete accounts through SjolystInnovation.AppKit.Auth ([#99](https://github.com/sondresjolyst/altinnendata-api/issues/99)) ([386fe8f](https://github.com/sondresjolyst/altinnendata-api/commit/386fe8f936e6d208499dc024797e6ac012134f5a))
* **users:** serve user admin, invites and first-admin seeding from SjolystInnovation.AppKit.Auth ([#98](https://github.com/sondresjolyst/altinnendata-api/issues/98)) ([6c86536](https://github.com/sondresjolyst/altinnendata-api/commit/6c8653683c6300711cc63e837b963a071bf8b245))


### Bug Fixes

* **branding:** check logo and icon uploads by their decoded format ([#90](https://github.com/sondresjolyst/altinnendata-api/issues/90)) ([aff419a](https://github.com/sondresjolyst/altinnendata-api/commit/aff419a3a385e610d6f3d2d4003f1fe928920436))
* evict every refresh token past the cap, not just one ([#87](https://github.com/sondresjolyst/altinnendata-api/issues/87)) ([4a5b2f3](https://github.com/sondresjolyst/altinnendata-api/commit/4a5b2f30bde49a13247e056cb10db1c13efdec5e))
* trust only configured proxies for X-Forwarded-For ([#93](https://github.com/sondresjolyst/altinnendata-api/issues/93)) ([b0e2728](https://github.com/sondresjolyst/altinnendata-api/commit/b0e272876f279810a090dd6bbced2ae04d5561ff))


### Dependencies

* bump SjolystInnovation.AppKit.Auth to 0.8.0 ([#102](https://github.com/sondresjolyst/altinnendata-api/issues/102)) ([2455ea5](https://github.com/sondresjolyst/altinnendata-api/commit/2455ea5c604b5de663e37ef6f7a5cfaf596aa4fa))
* **nuget:** bump `Swashbuckle.AspNetCore.Annotations` from 10.2.3 to 10.3.0 ([#112](https://github.com/sondresjolyst/altinnendata-api/issues/112)) ([9e070b5](https://github.com/sondresjolyst/altinnendata-api/commit/9e070b5c74f28327b3f6920feea1badfaf32a9a0))
* **nuget:** bump `Swashbuckle.AspNetCore` from 10.2.3 to 10.3.0 ([#111](https://github.com/sondresjolyst/altinnendata-api/issues/111)) ([c3eff53](https://github.com/sondresjolyst/altinnendata-api/commit/c3eff53db94e9c96b8556fdf58a71ac1b929bc5e))
* **nuget:** bump `xunit.v3` from 4.0.1 to 4.0.2 ([#113](https://github.com/sondresjolyst/altinnendata-api/issues/113)) ([3635e8f](https://github.com/sondresjolyst/altinnendata-api/commit/3635e8f7c230aed86cbbeea2358e5c6605591921))
* **nuget:** Bump FluentValidation and FluentValidation.DependencyInjectionExtensions ([#110](https://github.com/sondresjolyst/altinnendata-api/issues/110)) ([ee2a726](https://github.com/sondresjolyst/altinnendata-api/commit/ee2a726f5c60b8d545a851a8aca484d6f5fd8fa4))
* **nuget:** Bump the skiasharp group with 2 updates ([#89](https://github.com/sondresjolyst/altinnendata-api/issues/89)) ([05522b6](https://github.com/sondresjolyst/altinnendata-api/commit/05522b6f6565f8db329907b759fdc474e37ca90a))

## [1.8.4](https://github.com/sondresjolyst/altinnendata-api/compare/v1.8.3...v1.8.4) (2026-10-04)


### Dependencies

* **nuget:** bump `Moq` from 4.20.72 to 4.21.0 ([#85](https://github.com/sondresjolyst/altinnendata-api/issues/85)) ([6d6e957](https://github.com/sondresjolyst/altinnendata-api/commit/6d6e9570f1f266d0e5f27c0488de6eac5f34f625))
* **nuget:** Bump Mapster and Mapster.DependencyInjection ([#84](https://github.com/sondresjolyst/altinnendata-api/issues/84)) ([8f29ee6](https://github.com/sondresjolyst/altinnendata-api/commit/8f29ee6cf980267362d565bbe6645ec7a01ca9e5))

## [1.8.3](https://github.com/sondresjolyst/altinnendata-api/compare/v1.8.2...v1.8.3) (2026-09-27)


### Dependencies

* **nuget:** bump `System.IdentityModel.Tokens.Jwt` from 8.22.0 to 8.23.0 ([#80](https://github.com/sondresjolyst/altinnendata-api/issues/80)) ([919f462](https://github.com/sondresjolyst/altinnendata-api/commit/919f462a21c46aee479a2726a60efca9d719e746))
* **nuget:** Bump the skiasharp group with 2 updates ([#79](https://github.com/sondresjolyst/altinnendata-api/issues/79)) ([416b9f3](https://github.com/sondresjolyst/altinnendata-api/commit/416b9f358249feef41797066e3f1b3a51e3b22b8))

## [1.8.2](https://github.com/sondresjolyst/altinnendata-api/compare/v1.8.1...v1.8.2) (2026-09-25)


### Bug Fixes

* add an anonymous health endpoint ([#76](https://github.com/sondresjolyst/altinnendata-api/issues/76)) ([d0c916a](https://github.com/sondresjolyst/altinnendata-api/commit/d0c916a7db8d036a23a82aab49b22d1ee5d291c3))

## [1.8.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.8.0...v1.8.1) (2026-09-21)


### Dependencies

* **nuget:** bump `Microsoft.AspNetCore.Authentication.JwtBearer` from 10.0.11 to 10.0.12 ([#65](https://github.com/sondresjolyst/altinnendata-api/issues/65)) ([7c4dbb4](https://github.com/sondresjolyst/altinnendata-api/commit/7c4dbb4a55e81b192e3900a6eb8631b97d13973b))
* **nuget:** bump `Microsoft.AspNetCore.OpenApi` from 10.0.11 to 10.0.12 ([#67](https://github.com/sondresjolyst/altinnendata-api/issues/67)) ([211bbd6](https://github.com/sondresjolyst/altinnendata-api/commit/211bbd68220c6c006ae21f33771be1f6552a251a))
* **nuget:** bump `Microsoft.EntityFrameworkCore` from 10.0.11 to 10.0.12 ([#68](https://github.com/sondresjolyst/altinnendata-api/issues/68)) ([97be010](https://github.com/sondresjolyst/altinnendata-api/commit/97be0109e2f84c2b74a07f90784c9639e9a91e07))
* **nuget:** bump `xunit.v3` from 4.0.0 to 4.0.1 ([#75](https://github.com/sondresjolyst/altinnendata-api/issues/75)) ([6220528](https://github.com/sondresjolyst/altinnendata-api/commit/6220528b8ef4888522db98d9ef32c1dde402c4d1))
* **nuget:** Bump Microsoft.EntityFrameworkCore.Design and Microsoft.EntityFrameworkCore.Tools ([#70](https://github.com/sondresjolyst/altinnendata-api/issues/70)) ([4e169af](https://github.com/sondresjolyst/altinnendata-api/commit/4e169afcde5fb3fccc383ae6de353afb8e3e0e49))
* **nuget:** Bump the aspnetcore group with 2 updates ([#73](https://github.com/sondresjolyst/altinnendata-api/issues/73)) ([a5ab30c](https://github.com/sondresjolyst/altinnendata-api/commit/a5ab30c3626bae0516f1fea157555e7f14716bd9))
* **nuget:** Bump the skiasharp group with 2 updates ([#74](https://github.com/sondresjolyst/altinnendata-api/issues/74)) ([5e2f29e](https://github.com/sondresjolyst/altinnendata-api/commit/5e2f29e7b7b4c3d311044a4109f7753b6dd4059f))

## [1.8.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.7.1...v1.8.0) (2026-09-14)


### Features

* accept budget as a range string instead of a number ([#62](https://github.com/sondresjolyst/altinnendata-api/issues/62)) ([a37007f](https://github.com/sondresjolyst/altinnendata-api/commit/a37007fbf4acc44fa55a26311dac82a7b1ca85cb))

## [1.7.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.7.0...v1.7.1) (2026-09-13)


### Dependencies

* **nuget:** bump `SkiaSharp.NativeAssets.Linux.NoDependencies` from 4.151.1 to 4.151.2 ([#60](https://github.com/sondresjolyst/altinnendata-api/issues/60)) ([13b2aa9](https://github.com/sondresjolyst/altinnendata-api/commit/13b2aa93d642a9ecf79fe04f1773fe3f2452db09))
* **nuget:** bump `SkiaSharp` from 4.151.1 to 4.151.2 ([#59](https://github.com/sondresjolyst/altinnendata-api/issues/59)) ([d7cb7c9](https://github.com/sondresjolyst/altinnendata-api/commit/d7cb7c904ea879c48d9299c60064fa5780205161))

## [1.7.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.6.0...v1.7.0) (2026-09-11)


### Features

* add component conditions ([#56](https://github.com/sondresjolyst/altinnendata-api/issues/56)) ([0e1cee7](https://github.com/sondresjolyst/altinnendata-api/commit/0e1cee7b221ee20a29168a67d24bf5067021b4b2))

## [1.6.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.5...v1.6.0) (2026-09-10)


### Features

* sale dates, sales stats and build classes ([#54](https://github.com/sondresjolyst/altinnendata-api/issues/54)) ([2c8c216](https://github.com/sondresjolyst/altinnendata-api/commit/2c8c216a3e50b732bddb53c45164b15929bde696))

## [1.5.5](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.4...v1.5.5) (2026-09-06)


### Bug Fixes

* lift lockout on password reset ([#52](https://github.com/sondresjolyst/altinnendata-api/issues/52)) ([973e503](https://github.com/sondresjolyst/altinnendata-api/commit/973e503727f09354d9f48e9d9db0889f95848163))

## [1.5.4](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.3...v1.5.4) (2026-09-06)


### Dependencies

* **nuget:** bump `brevo_csharp` from 1.1.2 to 1.1.3 ([#50](https://github.com/sondresjolyst/altinnendata-api/issues/50)) ([c5af3b7](https://github.com/sondresjolyst/altinnendata-api/commit/c5af3b7e00cc74fc5dfb9a6dd3f67e796e730cb1))

## [1.5.3](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.2...v1.5.3) (2026-08-30)


### Dependencies

* **nuget:** Bump Mapster and Mapster.DependencyInjection ([#47](https://github.com/sondresjolyst/altinnendata-api/issues/47)) ([a5eb3c6](https://github.com/sondresjolyst/altinnendata-api/commit/a5eb3c6eb53c9a765c6ecc2594bc10f593a580d5))

## [1.5.2](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.1...v1.5.2) (2026-08-29)


### Dependencies

* **nuget:** bump `Microsoft.EntityFrameworkCore.InMemory` from 10.0.10 to 10.0.11 ([#38](https://github.com/sondresjolyst/altinnendata-api/issues/38)) ([9a29fa2](https://github.com/sondresjolyst/altinnendata-api/commit/9a29fa2b3918086761c2d212a1a1b39b7b2f8c3f))
* **nuget:** bump `Microsoft.Extensions.Logging.Abstractions` from 10.0.10 to 10.0.11 ([#39](https://github.com/sondresjolyst/altinnendata-api/issues/39)) ([63fa71e](https://github.com/sondresjolyst/altinnendata-api/commit/63fa71ec439de6652091713aa3ab6e98f6fdb2d4))
* **nuget:** bump `Microsoft.NET.Test.Sdk` from 18.8.1 to 18.9.0 ([#40](https://github.com/sondresjolyst/altinnendata-api/issues/40)) ([18a3ebb](https://github.com/sondresjolyst/altinnendata-api/commit/18a3ebbf764e81818457ba277f8cd7e275dbe609))
* **nuget:** bump `xunit.runner.visualstudio` from 3.1.5 to 4.0.0 ([#41](https://github.com/sondresjolyst/altinnendata-api/issues/41)) ([d966b6a](https://github.com/sondresjolyst/altinnendata-api/commit/d966b6a9f0fc9ac7822bd639adcb8563ebcc7ae6))
* **nuget:** bump `xunit.v3` from 3.2.2 to 4.0.0 ([#42](https://github.com/sondresjolyst/altinnendata-api/issues/42)) ([530220f](https://github.com/sondresjolyst/altinnendata-api/commit/530220f8691515e6cf7444bd5f1e4e4827eec454))

## [1.5.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.5.0...v1.5.1) (2026-08-24)


### Dependencies

* **github-actions:** bump `equinor/ops-actions/.github/workflows/docker.yml` from 9.39.6 to 9.40.0 ([#28](https://github.com/sondresjolyst/altinnendata-api/issues/28)) ([856f043](https://github.com/sondresjolyst/altinnendata-api/commit/856f0433f1feeb048f429f140bd343297fb3cc9d))
* **github-actions:** bump `equinor/ops-actions/.github/workflows/release-please-manifest.yml` from 9.39.6 to 9.40.0 ([#27](https://github.com/sondresjolyst/altinnendata-api/issues/27)) ([01e5669](https://github.com/sondresjolyst/altinnendata-api/commit/01e5669aa8677b0e1f4b20beffacaa5ee685df26))
* **nuget:** bump `Microsoft.AspNetCore.Authentication.JwtBearer` from 10.0.10 to 10.0.11 ([#29](https://github.com/sondresjolyst/altinnendata-api/issues/29)) ([bfcb174](https://github.com/sondresjolyst/altinnendata-api/commit/bfcb17445cb61ee11f055d9042cfa924f9e773de))
* **nuget:** bump `Microsoft.AspNetCore.Identity.EntityFrameworkCore` from 10.0.10 to 10.0.11 ([#30](https://github.com/sondresjolyst/altinnendata-api/issues/30)) ([1e80a6c](https://github.com/sondresjolyst/altinnendata-api/commit/1e80a6cd11c493fa4453dca62af7cac84035a6e6))
* **nuget:** bump `Microsoft.AspNetCore.OpenApi` from 10.0.10 to 10.0.11 ([#31](https://github.com/sondresjolyst/altinnendata-api/issues/31)) ([c18e2a2](https://github.com/sondresjolyst/altinnendata-api/commit/c18e2a272c19a5a901e4dd0314f92de0b889db72))
* **nuget:** bump `Microsoft.EntityFrameworkCore` from 10.0.10 to 10.0.11 ([#32](https://github.com/sondresjolyst/altinnendata-api/issues/32)) ([9d851a4](https://github.com/sondresjolyst/altinnendata-api/commit/9d851a4781fc5a89595397a9d0296f5e743325a6))
* **nuget:** Bump Microsoft.EntityFrameworkCore.Design and Microsoft.EntityFrameworkCore.Tools ([#34](https://github.com/sondresjolyst/altinnendata-api/issues/34)) ([826fd36](https://github.com/sondresjolyst/altinnendata-api/commit/826fd36c04a62e42037b65f27a521abba7c5463a))

## [1.5.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.4.0...v1.5.0) (2026-08-22)


### Features

* **finn:** accept the short links the finn app produces ([#26](https://github.com/sondresjolyst/altinnendata-api/issues/26)) ([78ff083](https://github.com/sondresjolyst/altinnendata-api/commit/78ff083c8bda86d5dccab92e7bb748cb4c1fcd5c))
* **seo:** tell crawlers to leave the api host alone ([#23](https://github.com/sondresjolyst/altinnendata-api/issues/23)) ([609e356](https://github.com/sondresjolyst/altinnendata-api/commit/609e35672faae9b522709da9f6dbb61373ad4773))

## [1.4.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.3.1...v1.4.0) (2026-08-20)


### Features

* **seo:** expose company address, edit times and image sizes ([#20](https://github.com/sondresjolyst/altinnendata-api/issues/20)) ([2aaf9c6](https://github.com/sondresjolyst/altinnendata-api/commit/2aaf9c65ae246b3925cf39afa0be6cc3093fc195))

## [1.3.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.3.0...v1.3.1) (2026-08-17)


### Dependencies

* **github-actions:** bump `equinor/ops-actions/.github/workflows/docker.yml` from 9.39.5 to 9.39.6 ([#15](https://github.com/sondresjolyst/altinnendata-api/issues/15)) ([21730e5](https://github.com/sondresjolyst/altinnendata-api/commit/21730e5618ae8bebc8f16f8ba71650f9b0af710c))
* **github-actions:** bump `equinor/ops-actions/.github/workflows/release-please-manifest.yml` from 9.39.5 to 9.39.6 ([#16](https://github.com/sondresjolyst/altinnendata-api/issues/16)) ([45e0af6](https://github.com/sondresjolyst/altinnendata-api/commit/45e0af675fdec59cf7aa245938d8071ae0eb063d))
* **nuget:** bump `SkiaSharp.NativeAssets.Linux.NoDependencies` from 4.151.0 to 4.151.1 ([#18](https://github.com/sondresjolyst/altinnendata-api/issues/18)) ([0a3f2c9](https://github.com/sondresjolyst/altinnendata-api/commit/0a3f2c90306886c3be5b43c4fa58ffbbc360b887))
* **nuget:** bump `SkiaSharp` from 4.151.0 to 4.151.1 ([#17](https://github.com/sondresjolyst/altinnendata-api/issues/17)) ([a146b71](https://github.com/sondresjolyst/altinnendata-api/commit/a146b71579b3ea7c01b4b45559416a343925e6b9))

## [1.3.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.2.0...v1.3.0) (2026-08-12)


### Features

* name and link the machine an enquiry is about ([#13](https://github.com/sondresjolyst/altinnendata-api/issues/13)) ([2dfe6f5](https://github.com/sondresjolyst/altinnendata-api/commit/2dfe6f5d1062018d489a0b743de1d2ca005eb359))

## [1.2.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.1.0...v1.2.0) (2026-08-12)


### Features

* add build gallery and FINN import ([#11](https://github.com/sondresjolyst/altinnendata-api/issues/11)) ([d199cdd](https://github.com/sondresjolyst/altinnendata-api/commit/d199cdd29dbbefb30a31c4873e50c5b5909b9cfb))

## [1.1.0](https://github.com/sondresjolyst/altinnendata-api/compare/v1.0.1...v1.1.0) (2026-08-11)


### Features

* link password emails back to the site ([#10](https://github.com/sondresjolyst/altinnendata-api/issues/10)) ([795f051](https://github.com/sondresjolyst/altinnendata-api/commit/795f051a6ee821437283d9c45f8f5972f4a33a15))


### Dependencies

* **nuget:** bump `SkiaSharp` from 4.150.1 to 4.151.0 ([#3](https://github.com/sondresjolyst/altinnendata-api/issues/3)) ([7840fa5](https://github.com/sondresjolyst/altinnendata-api/commit/7840fa5aed68eeaf8220a76ecb25b298d11d7867))

## [1.0.1](https://github.com/sondresjolyst/altinnendata-api/compare/v1.0.0...v1.0.1) (2026-08-10)


### Dependencies

* **github-actions:** bump `equinor/ops-actions/.github/workflows/docker.yml` from 9.39.2 to 9.39.5 ([#2](https://github.com/sondresjolyst/altinnendata-api/issues/2)) ([1518974](https://github.com/sondresjolyst/altinnendata-api/commit/1518974ab5d7c78798958b551c7814244e446f97))
* **github-actions:** bump `equinor/ops-actions/.github/workflows/release-please-manifest.yml` from 9.39.2 to 9.39.5 ([#1](https://github.com/sondresjolyst/altinnendata-api/issues/1)) ([ffdfad0](https://github.com/sondresjolyst/altinnendata-api/commit/ffdfad0e18cf9f7f6cd7ad94cb68d896f8d2df62))
* **nuget:** bump `SkiaSharp.NativeAssets.Linux.NoDependencies` from 4.150.0 to 4.151.0 ([#4](https://github.com/sondresjolyst/altinnendata-api/issues/4)) ([07491e5](https://github.com/sondresjolyst/altinnendata-api/commit/07491e5e43a3305de594e781f48298bbe4071bc7))
* **nuget:** bump `System.IdentityModel.Tokens.Jwt` from 8.19.2 to 8.22.0 ([#5](https://github.com/sondresjolyst/altinnendata-api/issues/5)) ([548d144](https://github.com/sondresjolyst/altinnendata-api/commit/548d1443b0bf4a1a36cc9265fc963e311fef5429))

## 1.0.0 (2026-08-10)


### Features

* initial altinnendata api ([d1e4ba6](https://github.com/sondresjolyst/altinnendata-api/commit/d1e4ba62963b2d08d934ca9377aefb7ffac7bc3f))

## Changelog
