# Changelog

All notable changes to PoracleWeb.NET are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Giovanni quick pick** ([#221](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/221)): split out while fixing the Rocket Leaders pick. New `invasion-giovanni` default quick pick tracks Giovanni encounters (`gruntType=giovanni`). Kept separate from "Rocket Leaders" because Giovanni spawns from the Super Rocket Radar only, while Sierra/Cliff/Arlo come from standard Rocket Radars — users typically want them on distinct alert profiles.

### Fixed
- **"Rocket Leaders" quick pick subscribed users to the wrong grunts** ([#221](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/221)): the `invasion-leader` pick sent `gruntType=mixed`, which in PoracleNG is the untyped `CHARACTER_GRUNT_MALE/FEMALE` grunt, not Sierra/Cliff/Arlo. Applying the pick now fans out to three invasion alarms with the real leader grunt types (`cliff`, `arlo`, `sierra`) via a single bulk-create. Users who previously applied the broken pick should unapply and re-apply it to replace their stale `mixed` alarms. Added `QuickPickDefaultsTests` to assert default invasion picks use valid PoracleNG `grunt_type` values and to regression-guard against `mixed` being used for leaders.
- **Invasion grunt icons and labels** ([#216](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/216)): `mixed` grunts rendered as "Rocket Leader" with the Cliff icon (invasion ID 41 = `EXECUTIVE_CLIFF`), and `decoy` grunts rendered with the Electric icon (invasion ID 50 = `ELECTRIC_GRUNT_MALE`). Corrected both to their real Niantic `InvasionCharacter` IDs — `mixed` → 4 (untyped grunt), `decoy` → 45 (decoy grunt). Added missing grunt types surfaced by PoracleNG: `darkness` (Shadow) and the Rocket Leaders `cliff`/`arlo`/`sierra`. Also replaced the empty-string fallback in `getGruntIconUrl()` with a generic unknown-grunt icon so an unmapped `grunt_type` renders a valid placeholder instead of a broken image. Updated both `invasion.constants.ts` and the add-invasion dialog.

## [2.7.0] - 2026-04-13

### Changed
- Bump the microsoft group with 10 updates ([PR #190](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/190))
- bump the angular group across 1 directory with 13 updates ([PR #189](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/189))

### Added
- **Three-channel Docker release process**: introduced `:beta` (published on every push to `main`) and `:pr-<number>` (published on PRs labeled `preview`) image tags alongside the existing `:latest` / semver stable tags. Enables public testers to opt into pre-release builds before an official release is cut. Includes a nightly prune workflow that removes `pr-*` tags for closed PRs and keeps only the last 10 `main-<sha>` builds. See `TESTING.md` for the user-facing guide.
- **Automated PR labeling and release-notes generation**: new `pr-labeler.yml` workflow applies conventional labels (`feat`, `fix`, `perf`, `docs`, `refactor`, `test`, `build`, `ci`, `chore`, `breaking`) based on branch prefix (`feat/`, `fix/`, etc.) or PR title (Conventional Commits). `.github/release.yml` groups labeled PRs into sections when using GitHub's "Generate release notes" button on a new release. Branch naming convention documented in README.
- **Dependabot configuration**: weekly (Monday) updates for NuGet, npm (ClientApp), GitHub Actions, and Docker base images. Grouped bumps for Angular, ESLint, Jest, and Microsoft stacks to avoid PR floods. Angular/Material major versions intentionally pinned — coordinated upgrades are done manually.

### Fixed
- **i18n**: bulk-delete confirm button on Quests and Invasions pages rendered the raw translation key (`QUESTS.CONFIRM_DELETE_SELECTED` / `INVASIONS.CONFIRM_DELETE_SELECTED`) instead of the translated text. Added the missing keys to all 11 locale files, reusing the existing `RAIDS.CONFIRM_DELETE_SELECTED` translations. ([#209](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/209))
- **i18n**: closed a 277-key coverage gap where keys added to `en.json` had not been propagated to the 10 non-English locale files (fr, de, es, nl, it, pt, pt-BR, pl, da, sv). Translated all 277 keys into each target language — covering alarm snackbars, admin settings labels and descriptions, GeoJSON import wizard, geofence detail fields, profile picker, map controls, and game brand terms (leagues, teams, lure types, Gigantamax). 2,770 translations applied. ([#209](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/209))
- Documentation branding: replaced 88 remaining bare `PoracleWeb` references with `PoracleWeb.NET` across 16 files under `docs/`, completing the branding cleanup from PR #180. URLs, path identifiers, `.csproj` names, and Mermaid diagram node IDs were preserved.

### Changed
- **Dependabot auto-merge workflow**: added `.github/workflows/dependabot-auto-merge.yml` to auto-approve and queue patch bumps, grouped bundles, and GitHub Actions minor bumps for auto-merge (gated on CI). Major-version bumps still require manual review. Pinned the `node` Docker base image to the current major (22) to require deliberate Angular-coordinated upgrades.

## [2.6.0] - 2026-04-12

### Added
- remove Poracle server management feature (fixes #176) ([PR #181](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/181))
- show login buttons based on .env config, with admin-disabled state (#172) ([PR #177](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/177))

### Changed
- **Replaced AutoMapper with manual mapping extension methods** ([#173](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/173), [PR #178](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/178)): Removed the AutoMapper 16.1.1 dependency (which requires a commercial license for production use) and replaced all mappings with static extension methods in `Core.Mappings`. `AlarmMappingExtensions` provides `To*()` and `ApplyUpdate()` methods for all 10 alarm types with identical null-skip semantics. `EntityMappingExtensions` provides `ToModel()`, `ToEntity()`, and `ApplyTo()` methods for Human, Profile, UserGeofence, SiteSetting, WebhookDelegate, QuickPickDefinition, QuickPickAppliedState, and PwebSetting entity-model pairs. Removed AutoMapper NuGet from all 5 projects, removed `IMapper` injection from 10 controllers and 8 repositories, removed `AddAutoMapper` DI registration, and deleted `PoracleMappingProfile.cs`. No API contract or behavioral changes — all mappings produce identical results.

### Fixed
- use 'PoracleWeb.NET' branding consistently (fixes #175) ([PR #180](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/180))
- **Persist ASP.NET Core DataProtection keys to filesystem** ([#174](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/174), [PR #179](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/179)): Configured `AddDataProtection().PersistKeysToFileSystem()` to store keys in `DATA_DIR/dataprotection-keys` (defaults to `/app/data/dataprotection-keys` in Docker, `./data/dataprotection-keys` standalone). Eliminates startup warnings about ephemeral key storage and ensures keys survive container restarts. No new NuGet packages — uses the ASP.NET Core shared framework. No Dockerfile or docker-compose changes — the existing `./data:/app/data` volume mount covers the key directory.

### Removed
- **AutoMapper dependency** ([#173](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/173), [PR #178](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/178)): Removed `AutoMapper` 16.1.1 NuGet package from Api, Core.Mappings, Core.Repositories, Core.Services, and Tests projects. Eliminates the production license requirement and "Lucky Penny" startup warning.
- **Poracle server management feature** ([#176](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/176)): Removed the SSH-based admin page for checking PoracleJS server health and triggering `pm2 restart all` via SSH. Feature was designed for PoracleJS deployments and is not applicable to PoracleNG (the current authoritative backend). Removed: admin page at `/admin/poracle-servers`, three REST endpoints (`GET /api/admin/poracle/servers`, `POST .../restart`, `POST .../restart-all`), `IPoracleServerService`/`PoracleServerService`, `PoracleServerSettings`/`PoracleServerConfig`, `PoracleServerStatus` model, the `UpdateGroupMapAsync` call in the geofence approval flow (PoracleNG resolves group names via Koji parent chain automatically), `PORACLE_SERVER_N_*` env var bridge in `Program.cs`, `Poracle__Servers__N__*` env vars in `docker-compose.yml`, SSH key volume mount (`./data/ssh_key:/app/ssh_key:ro`) in `docker-compose.yml`, and `openssh-client` package from the Dockerfile. **Security benefit**: eliminates the only shell-command-execution code path in the codebase, closes command-injection surface in `RestartCommand`/`GroupMapPath` config values, and removes the need to mount SSH private keys inside the container. **Backwards compatible**: existing `.env` files containing `PORACLE_SERVER_*` or `SSH_KEY_PATH` vars are silently ignored after upgrade — no `.env` changes required. **Ops note**: operators should delete `./data/ssh_key` from the host, rotate the SSH keys previously used, and remove the PoracleWeb-specific public key from `~/.ssh/authorized_keys` on each PoracleJS server.

## [2.5.0] - 2026-04-12

### Added
- **Signup page redirect for non-registered users** ([#168](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/168), [PR #171](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/171)): New admin-configurable `signup_url` site setting (Settings > Analytics & Links) displays a green "Sign Up" button on the login page. When a non-registered user attempts to log in via Discord or Telegram and receives a `user_not_registered` or `missing_required_role` error, the signup button directs them to the configured registration page. The signup URL is served exclusively via the trusted `GET /api/settings/public` endpoint — never passed through URL fragments — to prevent open-redirect phishing attacks. Button hidden when no URL is configured.
- **Internationalized ~45 hardcoded UI strings** ([PR #171](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/171)): Extracted hardcoded English strings into i18n translation keys across all 11 supported languages: auth error messages (9 keys in `AUTH.ERR_*`), HTTP error toasts (6 keys in `ERROR.*`), test alert snackbar messages (5 keys in `TEST_ALERT.*`), admin settings messages (4 keys in `ADMIN_SETTINGS.*`), fort change dialog messages (4 keys in `FORT_CHANGES.*`), max battle level labels and messages (11 keys in `MAX_BATTLES.*`), and template selector condition labels (15 keys in `TEMPLATE.COND_*`).

### Fixed
- **Alarms silently landing on the wrong profile after PoracleNG auto-switches via active_hours scheduler** ([#167](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/167), [PR #170](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/170)): When PoracleNG's active-hours scheduler (or a bot `!profile` command) changed `current_profile_no` out-of-band, the JWT's `profileNo` claim went stale. All subsequent alarm CRUD was scoped to the old profile — users unknowingly created, edited, and deleted alarms on a profile they were no longer viewing. Fixed by adding a profile-resync check to `GET /api/auth/me`: the endpoint now compares the JWT's `profileNo` claim against `humans.current_profile_no` from the database and returns a refreshed JWT when they diverge. The frontend's `AuthService.loadCurrentUser()` picks up the new token transparently, and the dashboard shows a snackbar ("Profile switched to X by schedule") so the user knows which profile they're on.

### Changed
- **Extracted `IJwtService`**: JWT token generation was duplicated across `AuthController`, `ProfileController`, `ProfileOverviewController`, and `AdminController` (4 independent implementations). Consolidated into a shared `IJwtService` / `JwtService` singleton registered in DI. `GenerateTokenWithReplacedProfile` now filters out registered JWT claims (`exp`, `nbf`, `iat`, `iss`, `aud`) to prevent stale claim duplication — a latent bug in the previous copy-all-claims approach.

## [2.4.1] - 2026-04-12

### Fixed
- **Test alert DMs now satisfy the alarm's own filter** ([#165](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/165)): Clicking Test Alert on a "Great League rank 1-1 Tinkaton" filter previously delivered a DM with IVs 1/14/14 at L25.5 — values that didn't satisfy the filter being tested — because `TestAlertService.BuildPokemonWebhook` hardcoded IVs to 15/15/15, level to 35, CP to 2500, and shipped literal `pvp_rankings_great_league`/`_ultra_league` arrays with `rank: 1` regardless of the alarm's configured filter. PoracleNG's render pipeline then faithfully enriched the lies. Rewrote the monolithic builder as six `ITestPayloadBuilder` implementations under `Core.Services/TestAlerts/`, each reading the alarm's own filter columns and emitting matching raw webhook values aligned to the live PGAN DTS template variable set. Ported gohbem's PVP rank calculator (`Core.Services/Pvp/PvpRankCalculator` + 109-entry `CpMultiplierTable`) to resolve PVP-filter alarms to real rank-matching IV/level/CP combos — GL rank 1 Tinkaton now renders as 13/15/15 L49.5 CP 1498 with a matching single-entry PVP rank panel. Rank tables cached per `(pokemonId, form, league)` via `IPvpRankService` + `IMemoryCache` with no TTL (~16 KB per species, O(1) after the first ~1 ms sweep). Extended `MasterDataService` to expose per-species `baseAttack`/`baseDefense`/`baseStamina` from the WatWowMap Masterfile. Every builder honors the full filter field set: IV floors/ceilings, `min_iv`/`max_iv`, `min_level`/`max_level`, `min_cp`/`max_cp`, `form`, `gender`, `size`, and league-aware PVP rank windows. Also aligned raid/egg wire fields to the live PGAN DTS convention (`name`+`url`, not `gym_name`/`gym_url`), merged `egg` into the `raid` wire type (`pokemon_id=0`), fixed a latent quest-webhook `template` field collision with the in-game quest template key, and corrected invasion field names (`name`/`incident_grunt_type`/`incident_expiration`). 24 new payload assertion tests pin the filter-to-wire contract plus 19 PVP algorithm property tests. ([PR #166](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/166))
- custom geofence toggle not persisting (#163) ([PR #164](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/164))
- **Custom geofence toggle not persisting** ([#163](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/163)): Activating/deactivating a user-drawn geofence via the Geofences page toggle appeared to succeed but silently reverted on the next page load. Root cause: PoracleNG's `POST /api/humans/{id}/setAreas` handler intersects the submitted area list against fences where `userSelectable=true` (for non-admin users), and user geofences are served from PoracleWeb's feed with `userSelectable=false` so they were silently stripped. Regression introduced in v2.0.0 by the PoracleNG API proxy migration, which routed user geofence area writes through `SetAreasAsync` instead of the pre-migration direct-DB path. Introduced `IUserAreaDualWriter` — a tiny atomic-write abstraction over `PoracleContext` that commits both `humans.area` and the active `profiles.area` in a single `SaveChangesAsync` call (EF Core implicit transaction = the two writes cannot drift). `UserGeofenceService.Create`/`Delete`/`AddToProfile`/`RemoveFromProfile`/`AdminDelete` now delegate to the writer instead of making separate `IHumanRepository` + `IProfileRepository` calls. `AddToProfile` and `RemoveFromProfile` also call `ReloadGeofencesSafeAsync` manually because the direct-DB path skips PoracleNG's `HandleSetAreas` terminal reload. Also fixed a related bug where saving on the Areas page would silently strip user geofences the user had activated: `AreaController.UpdateAreas` now calls `IUserGeofenceService.PreserveOwnedAreasInHumanAsync` after `SetAreasAsync`, which hands off to the writer's bulk `AddAreasToActiveProfileAsync` method (one DB round-trip regardless of geofence count). **This is a temporary workaround — the proper fix is a new PoracleNG API endpoint that bypasses the `userSelectable` filter for trusted callers. See [`docs/poracleng-enhancement-requests.md`](docs/poracleng-enhancement-requests.md#trusted-setareas-bypass-userselectable-filter) (`trusted-set-areas` gap).** All affected sites are tagged with `HACK: trusted-set-areas` comments — when PoracleNG ships the fix, `grep -rn "HACK: trusted-set-areas" --include="*.cs"` will list every site that needs to be reverted.

## [2.4.0] - 2026-04-09

## [2.4.0] - 2026-04-09

### Added
- **Multi-language / i18n support (11 languages)**: Full internationalization of the entire UI matching the original PoracleWeb PHP language set — English, French, German, Spanish, Dutch, Italian, Portuguese, Brazilian Portuguese, Polish, Danish, Swedish. Runtime language switching via ngx-translate with no page reload. Browser language auto-detection on first visit. SVG country flag icons in the language selector menu. Admin `allowed_languages` site setting to restrict available languages. All 17 help guide sections translated with rich HTML content. Amber "English" fallback chip on untranslated sections. "Help improve translations" CTA for non-English users. 1,121 translation keys per language (12,331 total translated strings). I18nService with browser detection, localStorage persistence, computed signal reactivity, and admin-controlled filtering. ([#161](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/161), [PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))
- **Shared pipes i18n**: League name, gender display, lure name, team name, and distance display pipes now use I18nService for translated output ([PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))
- **Admin settings i18n**: All admin setting group labels and descriptions are translatable (106 keys in ADMIN_SETTINGS namespace) ([PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))
- **I18nService unit tests**: 11 tests covering init, use, browser detection, language filtering, localStorage persistence, and instant delegation ([PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))
- **MkDocs i18n documentation**: Feature page at `docs/features/internationalization.md` covering architecture, translation file structure, admin config, and contributing translations ([PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))
- **MkDocs landing page**: Custom home page with hero section, feature cards, screenshot gallery, quick start, and language flags banner ([PR #162](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/162))

## [2.3.0] - 2026-04-08

## [2.3.0] - 2026-04-08

### Added
- **Area overview map on dashboard**: Non-interactive Leaflet mini-map showing selected areas as color-coded polygons. Lazy-loaded via `@defer(on viewport)` with zero additional API calls. Click navigates to the full Areas page. ([#129](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/129), [PR #160](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/160))
- **Profile active hours / schedule management UI** ([#158](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/158)) ([#159](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/159))
  - View and edit active hours on user profiles for automatic profile switching
  - Schedule editor dialog with day picker, time picker, and weekly preview
  - Amber schedule pills on profile cards showing activation times
  - Location warning when profile has 0,0 coordinates (causes timezone bug in PoracleNG scheduler)
  - Server-side validation (day 1-7, hours 0-23, mins 0-59, max 28 entries)

### Removed
- Unused `ProfileListComponent` (was never routed)

## [2.2.0] - 2026-04-07

### Added
- **Max Battle (Dynamax) tracking alarms**: Full-stack alarm module for Dynamax and Gigantamax Max Battle tracking at Power Spots, proxied through PoracleNG's `maxbattle` CRUD API. Includes list page with card grid, add dialog (By Level / By Pokemon tabs), edit dialog, dashboard card, cleaning toggle, and admin feature flag (`disable_maxbattles`). Levels follow PoracleNG's system: 1-5 (Dynamax), 7 (Gigantamax), 8 (Legendary Gigantamax). Uses delete-then-create update pattern for PoracleNG's insert-only maxbattle handler. ([#118](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/118), [PR #137](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/137))
- **Gigantamax-only toggle**: Pokemon-based max battle alarms can filter to only Gigantamax battles, mirroring PoracleNG's `!maxbattle <pokemon> gmax` command ([PR #137](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/137))
- **Scanner-based Pokemon filter**: Max Battle "By Pokemon" tab queries the scanner DB's `station` table for species that have actually appeared in Max Battles, filtering the Pokemon selector. Gracefully falls back to showing all Pokemon when scanner DB is not configured. New reusable `allowedIds` input on `PokemonSelectorComponent`. ([PR #137](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/137))
- **Template dropdown fallback**: Template selector now shows template "1" (PoracleNG default) when no type-specific DTS templates exist, preventing empty dropdowns for new alarm types ([PR #137](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/137))
- **Fort Change tracking alarms**: New alarm type to monitor pokestop and gym changes — track name changes, relocations, image updates, removals, and new forts with fort type filtering, distance/area delivery, and clean mode support ([#119](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/119), [PR #135](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/135))
- **Max Battles quick pick support**: Admins can create Max Battle quick pick templates. All-levels mode creates 7 alarms (levels 1-5 Dynamax, 7-8 Gigantamax). Added maxbattle alarm type, category, and form fields to the admin quick pick dialog. ([#140](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/140), [PR #143](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/143))
- **Profile duplicate**: Duplicate button on profile cards creates a new profile with all alarms, areas, and location cloned from the source in one operation. Includes server-side name validation and automatic rollback if the alarm copy fails. ([#120](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/120), [PR #145](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/145))
- **Unified Profiles page with alarm overview**: View all alarms across all profiles in one place with expandable accordion panels, type-grouped alarm cards with game asset images (Pokemon sprites, raid eggs, lure/gym icons), global search, and type filter chips. Uses PoracleNG's `allProfiles` endpoint. ([#127](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/127), [PR #147](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/147))
- **Profile backup & restore**: Export a profile's alarms as a portable JSON backup file (stripped of internal IDs). Import a backup to create a new profile with all alarms restored. Profile names auto-deduplicate on import. ([PR #147](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/147))
- **Duplicate detection across profiles**: Alarms matching the same filter on multiple profiles are highlighted with an orange indicator and filterable via a Duplicates chip. ([PR #147](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/147))
- **Profile name uniqueness**: Validated client-side on add, edit, duplicate, and import dialogs with inline error messages. ([PR #147](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/147))
- **Send test alert / notification preview**: Test button on every alarm card (Pokemon, Raids, Eggs, Quests, Invasions, Lures, Nests, Gyms) sends a simulated notification through PoracleNG's test endpoint so users can preview their DTS template output. Includes per-IP rate limiting (5 req/min) and per-alarm cooldown (15s) to prevent spam. ([#122](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/122), [PR #148](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/148))
- **Weather display at user location**: Dashboard shows current in-game Pokemon GO weather with condition icon/name, boosted Pokemon type chips (color-coded per type), severity warning badge, and relative update timestamp. Reads weather from the scanner DB `weather` table via S2 level-10 cell lookup. Gracefully hidden when scanner DB is not configured. ([#128](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/128), [PR #149](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/149))
- **Per-area weather on dashboard**: Each area chip in the "Tracking areas" section displays a weather icon with a tooltip showing the condition and boosted types for that area. Uses geofence polygon centroids to resolve per-area S2 weather cells in a single batch request. ([PR #149](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/149))
- **S2 cell geometry helper**: Pure C# implementation of S2 level-10 cell ID computation from lat/lon (ported from Go S2 library), with no external NuGet dependencies. Used for weather cell lookups. ([PR #149](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/149))
- **Pokemon availability indicators from Golbat**: Pokemon selector shows which species are currently spawning via Golbat's availability API. "Live > Spawning" filter toggle in the selector filters to only active species with available-first sorting. Green dot indicators on autocomplete options and tile grid. Availability data cached server-side (5-min IMemoryCache TTL) with stale fallback. Feature is automatically enabled when Golbat API is configured (`GOLBAT_API_ADDRESS`), hidden when not. ([#133](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/133), [PR #154](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/154))
- **GeoJSON geofence export and import**: Export all geofences (admin + user) as a standard GeoJSON FeatureCollection file, and import geofences from `.geojson` files with drag-and-drop upload, client-side preview, coordinate validation, and partial-failure handling. Rate limited at 5 imports/min, 5MB file size cap, max 50 features per import. ([#130](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/130), [PR #155](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/155))
- **Login method gating**: `enable_discord` and `enable_telegram` site settings now control login button visibility and block auth attempts when disabled ([#117](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/117), [PR #136](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/136))
- **Dedicated Discord settings section** in admin page ([PR #116](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/116))

### Fixed
- **Quick pick "all invasions" fails with 400**: `grunt_type: null` sent to PoracleNG which rejects it — normalized to empty string `""` in `QuickPickService.ApplyInvasionAsync` and defensively in `InvasionService` create/update methods ([#139](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/139), [PR #141](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/141))
- **Raid card 9000 stars**: Guard `getLevelStars()` for sentinel value 9000, show "Any Level" label. Use descriptive level names (Level 6 → "Mega"). Show "All Raids" / "All Mega Raids" for level-based alarms. ([#138](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/138), [PR #144](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/144))
- **Quick pick raid level override**: PoracleNG treats `pokemon_id=0` as "everything" and overrides level filters. Quick pick now uses `pokemon_id=9000` to preserve level-based filtering (e.g., "All Mega Raids" correctly creates level 6 alarms). ([PR #144](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/144))
- **Profile rename not saving**: PoracleNG's profile update endpoint requires snake_case `profile_no`, not camelCase `profileNo`. ([PR #147](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/147))
- **Quest test alerts missing static maps** due to incorrect webhook format ([PR #153](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/153))

## [2.1.3] - 2026-04-04

### Fixed
- **Custom geofences lost when toggling areas**: `syncSelectedFromAreas()` was overwriting selected areas with only predefined names, silently dropping custom geofence subscriptions — toggling any predefined area checkbox then saving would deactivate custom geofences ([#109](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/109), [PR #113](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/113))

## [2.1.2] - 2026-04-04

### Fixed
- **Areas page layout**: Remove `max-width: 800px` constraint for consistent full-width layout matching My Geofences page ([#107](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/107), [PR #110](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/110))
- **Site title after OAuth redirect**: Load site settings after Discord callback stores JWT token — title was stuck on default "DM Alerts" because `loadOnce()` fired before the token was available ([#106](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/106), [PR #111](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/111))
- **Webhook admin actions broken**: Move user ID from path to query parameter for 8 admin endpoints — webhook IDs are URLs with slashes that broke ASP.NET Core `{id}` route matching ([#105](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/105), [PR #112](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/112))

## [2.1.1] - 2026-04-03

### Added
- **Credits and attribution**: Added credits section to README and docs homepage acknowledging PoracleJS, PoracleNG, PoracleWeb (PHP), and Kōji ([#103](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/103), [PR #104](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/104))

## [2.1.0] - 2026-04-03

### Added
- **Unified `.env` configuration**: Program.cs env var bridge auto-translates short env var names (`DB_HOST`, `JWT_SECRET`, `DISCORD_CLIENT_ID`, etc.) to .NET's `__` convention and auto-composes MySQL connection strings from `DB_HOST`/`DB_PORT`/`DB_NAME`/`DB_USER`/`DB_PASSWORD`. The same `.env` file now works for Docker, standalone, and development mode. ([#98](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/98))
- **Root-level convenience scripts**: `scripts/setup.sh` (interactive first-time setup with JWT generation and DB creation), `scripts/dev.sh` (install, api, app, start, test, lint, build, db:create, db:migrate), `scripts/docker.sh` (build, start, stop, logs, update, clean) — no more navigating deep subdirectories ([#99](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/99))
- **Standalone setup guide**: new `docs/getting-started/standalone-setup.md` with systemd, pm2, and Windows service instructions ([#102](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/102))

### Changed
- `.env.example` restructured with clear section headers, `localhost` defaults (docker-compose.yml retains its own `host.docker.internal` fallback), and Docker vs standalone host guidance ([#100](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/100))
- Configuration reference tables now show both `.env` short names and `.NET` `__` names for all settings ([#101](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/101))
- All getting-started and configuration docs updated with script references and unified `.env` approach ([#101](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/101))

### Fixed
- `DB_PORT` default mismatch: `docker-compose.yml` defaulted to `3309` while `.env.example` and Program.cs used `3306` — now consistent at `3306` ([#100](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/100))
- `CORS_ORIGIN` missing from `.env.example` — required in production but was undocumented, causing startup crashes ([#100](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/100))
- Stale `Discord__RedirectUri` reference in standalone troubleshooting docs ([#101](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/101))
- Docker docs Poracle config mount path `/app/poracle-config` corrected to `/poracle-config` ([#101](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/101))

## [2.0.0] - 2026-04-01

### Added
- **PoracleNG API proxy layer**: all alarm CRUD, human/profile management, location, and area operations now proxied through PoracleNG's REST API instead of direct database writes ([PR #88](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/88))
- `IPoracleTrackingProxy` / `PoracleTrackingProxy` — HTTP client for alarm tracking CRUD with snake_case JSON and `X-Poracle-Secret` auth
- `IPoracleHumanProxy` / `PoracleHumanProxy` — HTTP client for human/profile management via PoracleNG API
- `PoracleJsonHelper` — shared serialization helpers, uid:0 stripping, cached empty array
- `docs/poracleng-enhancement-requests.md` documenting PoracleNG API gaps
- `docs/architecture/poracleng-proxy.md` architecture documentation
- Request/response logging in `PoracleTrackingProxy` for debugging
- URL-encoding for webhook IDs in proxy path construction
- Show gym name on raid and egg alarm cards ([PR #94](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/94))

### Changed
- All alarm services (Monster, Raid, Egg, Quest, Invasion, Lure, Nest, Gym) proxy through PoracleNG API instead of writing directly to MySQL
- DashboardService uses single `GetAllTrackingAsync` call instead of 8 COUNT queries
- CleaningService uses fetch-mutate-POST pattern via PoracleNG API
- ProfileController `SwitchProfile` uses atomic PoracleNG API call (eliminates non-transactional dual-write)
- AreaController `UpdateAreas` uses atomic PoracleNG `setAreas` API call
- LocationController uses `SetLocationAsync` (removed `PoracleContext` dependency)
- AdminController enable/disable/pause/resume use PoracleNG proxy directly
- HumanService/ProfileService are proxy-first, DB only for admin bulk ops
- UserGeofenceService area mutations use proxy `SetAreasAsync` for active profile
- Service interfaces `GetByUidAsync`, `UpdateAsync`, `DeleteAsync` now require `userId` parameter
- `Poracle:ApiAddress` and `Poracle:ApiSecret` now required for ALL operations

### Removed
- 8 alarm repository classes and interfaces (MonsterRepository, RaidRepository, etc.)
- BaseRepository, PoracleUnitOfWork, IPoracleUnitOfWork
- EnsureNotNullDefaults, PVP sanitization, GruntType normalization, Template defaulting (PoracleNG handles all)

### Fixed
- Eliminated 15-hour stale state window caused by NULL template crash in PoracleNG state reload
- Profile switch and area update dual-writes are now atomic
- PoracleNG response wrapper extraction (`{"human": {...}}`, `{"profile": [...]}`)
- uid:0 in create requests caused PoracleNG to treat new alarms as updates
- Webhook ID URL encoding (slashes in URLs broke proxy routing)
- `GetAreasAsync` was calling wrong endpoint (available areas vs user's selected areas)
- Area map zoom/pan no longer resets when selecting areas ([PR #96](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/96))
- Custom geofence areas can now be removed from selected chips ([PR #95](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/95))
- Settings changes reflect immediately without page refresh ([PR #97](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/97))

## [1.3.1] - 2026-04-01

### Fixed
- **Disabled banner wording**: clarify that account disabling may be due to rate limiting, not just manual admin action ([#84](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/84), [PR #85](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/85))

## [1.3.0] - 2026-04-01

### Added
- **Admin-disabled user banner**: distinct banner for admin-disabled users with Discord support and ticket links, replacing the broken Resume button ([#82](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/82), [PR #83](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/83))
- **`adminDisable` field on UserInfo API**: frontend can now distinguish admin-disabled from self-paused users ([PR #83](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/83))

## [1.2.0] - 2026-03-31

### Added
- **Gym picker**: search and target specific gyms for team change, raid, and egg alarms with photo thumbnails and area names in search results ([#77](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/77), [PR #78](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/78))
- **Scanner gym search endpoints**: `GET /api/scanner/gyms` and `GET /api/scanner/gyms/{id}` with area resolution ([#77](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/77), [PR #78](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/78))
- **Targeted gym name on alarm cards**: gym alarm cards display the selected gym name when a specific gym is targeted ([PR #78](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/78))

### Fixed
- normalize empty-string nullable columns back to NULL before save to prevent gym_id matching failures ([#76](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/76), [PR #79](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/79))
- default GymCreate.Team to 4 (any team) to match Raid/Egg defaults ([#74](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/74), [PR #75](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/75))
## [1.1.2] - 2026-03-31


### Fixed
- normalize invasion grunt_type to lowercase and default empty to everything ([PR #73](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/73))
## [1.1.1] - 2026-03-31


### Fixed
- preserve NULL for nullable string columns in EnsureNotNullDefaults ([PR #70](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/70))
## [1.1.0] - 2026-03-30

### Added
- **PokéStop event tracking**: Kecleon, Gold Stop, and Showcase as trackable invasion types with event-specific accent colors, icons, and UICONS sprite for Kecleon; gender filter auto-hides for event types ([#65](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/65), [PR #68](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/68))

### Fixed
- **Invasion icon maps**: grunt type icon lookup keys lowercased to match DB values (backend lowercases `grunt_type` on create) ([PR #68](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/68))
- **Raid card team icon**: `team=4` (all teams) tried to load nonexistent `gym/4.png`, now maps to gray gym icon ([#66](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/66), [PR #67](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/67))
- **Raid/egg "Any Team" selector**: sent `team=0` (uncontested gym) instead of `team=4` (all teams), silencing raid notifications for affected users ([#63](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/63), [PR #64](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/64))
- remove decorative circle from page headers ([PR #62](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/62))
- DTS preview renders nested Handlebars if-blocks incorrectly ([PR #60](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/60))
## [1.0.2] - 2026-03-24

### Fixed
- PVP ranking fields (`pvp_ranking_best`, `pvp_ranking_worst`) sent with non-default values when no PVP league selected, causing Poracle to use wrong DTS template branch for all Pokemon alerts ([#57](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/57), [PR #58](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/58))

## [1.0.1] - 2026-03-24

### Added
- **Admin geofence submissions page redesign**: Region grouping, three view modes (card/list/table), sortable columns, Discord avatar display, resolved reviewer names ([#55](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/55), [PR #56](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/56))
- **Reviewer name resolution**: Backend resolves `reviewedBy` Discord IDs to display names and avatars via batch human lookup
- **Table view**: Flat ungrouped table with sortable columns (name, status, owner, region, points, created, submitted) and reviewer info
- **Region grouping**: Card and list views group geofences by region with collapsible expansion panels

### Fixed
- Map thumbnails lost when switching between card and list views — maps now properly destroyed and re-initialized on view switch

## [1.0.0] - 2026-03-24

### Added
- **PoracleNG compatibility**: officially supports both PoracleJS and PoracleNG
- **GitHub Pages docs**: pymdownx.emoji extension for Material icon rendering in Quick Links ([#53](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/53), [PR #54](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/54))

## [0.6.4] - 2026-03-24

### Added
- **Raid/Egg alarm fields**: move, evolution, exclusive, gym_id, rsvp_changes ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))
- **Gym alarm fields**: battle_changes toggle and gym_id ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))
- **Invasion**: grunt_type automatically lowercased on create for Poracle case-sensitive matching ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))

### Fixed
- Size dropdown "Any" option sent incorrect values (0 instead of -1/5), silently breaking size filters ([#49](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/49), [PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- Model defaults aligned with PHP PoracleWeb: size=-1, max_level=55, raid/egg team=4, raid move/evolution=9000 ([PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- Frontend dialog fallbacks and validators aligned with PHP defaults ([PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- pvpRankingWorst edit dialog fallback corrected from 100 to 4096
- Level input max=50 corrected to 55 in edit dialog
- Site settings update fails with EF key modification error when toggling settings

## [0.6.3] - 2026-03-24

### Fixed
- set correct defaults on MonsterCreate model to prevent silent filter rejection ([PR #48](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/48))

## [0.6.2] - 2026-03-24

### Added
- add size filter to Pokemon alarm dialogs ([PR #43](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/43))
- **Admin geofence detail view**: enriched admin geofence management page with owner display names, avatar URLs, interactive Leaflet map detail dialog, lazy-loaded map thumbnails, point count, area calculation (m²/km²), reference geofences from Poracle areas, and skeleton loading ([#42](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/42), [PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **Site settings documentation**: comprehensive GitHub Pages docs covering all 39+ admin-configurable settings organized by category with types, descriptions, and prerequisites ([#44](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/44), [PR #45](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/45))

### Changed
- extract `GetDefaultAvatarUrl` to shared `AvatarCacheService.GetAvatarOrDefault()` static method, removing duplication from `AdminController` and `AdminGeofenceController` ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- extract `GEOFENCE_STATUS_COLORS` to shared `geofence.utils.ts` constant ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))

### Fixed
- **Internal settings visible in admin UI**: hide `migration_completed` and other internal system settings from the admin settings API and block writes with `BadRequest`. Frontend defense-in-depth filtering added as well. ([#44](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/44), [PR #45](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/45))
- **Map thumbnails cleared on tab switch**: admin geofence cards now properly destroy orphaned Leaflet maps when filtered out and re-initialize them when switching back ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **Leaflet map not rendering in detail dialog**: use `dialogRef.afterOpened()` and `height !important` to prevent Material dialog animation and Leaflet global CSS from collapsing the map container ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **MySql.EntityFrameworkCore `Contains()` query failure**: replace `List<T>.Contains()` LINQ with sequential lookups for batch human ID resolution ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))

## [0.6.1] - 2026-03-23

### Added
- **Per-profile geofence toggle**: Slide toggle on each custom geofence card to activate/deactivate notifications per profile without recreating the geofence. New `POST /api/geofences/custom/{id}/activate` and `POST /api/geofences/custom/{id}/deactivate` endpoints with ownership validation. ([#36](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/36), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))

### Fixed
- **Geofence delete only removed area from active profile**: `DeleteAsync` and `AdminDeleteAsync` now remove the geofence area name from all profiles (`humans.area` + every `profiles.area` entry), not just the active one. ([#36](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/36), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Area selection not profile-scoped**: `GET /api/areas` now reads from `profiles.area` for the current profile instead of the shared `humans.area` field. `PUT /api/areas` writes to both `humans.area` and `profiles.area`. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Profile switch didn't swap areas**: `SwitchProfile` now saves `humans.area` to the old profile's `profiles.area` and loads the new profile's `profiles.area` into `humans.area`, keeping PoracleJS in sync. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Geofence area operations not profile-aware**: `AddAreaToHumanAsync` and `RemoveAreaFromHumanAsync` now update both `humans.area` and `profiles.area` to prevent area subscriptions appearing on the wrong profile. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
## [0.6.0] - 2026-03-23

### Added
- **In-app Help & User Guide page** ([PR #26](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/26))
- **EF Core migrations for PoracleWeb database**: Schema changes apply automatically on startup via `MigrateAsync()`. Includes `MariaDbHistoryRepository` to fix `GET_LOCK(-1)` incompatibility with MariaDB. ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))

### Changed
- **Migrate pweb_settings to structured tables**: Replace the generic key-value `pweb_settings` table in the Poracle DB with 4 properly structured tables in the PoracleWeb database ([#34](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/34), [PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
  - `site_settings` — typed admin settings with categories and value types
  - `webhook_delegates` — relational webhook-to-user delegation with composite unique index
  - `quick_pick_definitions` — structured quick pick presets with JSON filter columns
  - `quick_pick_applied_states` — per-user/profile applied state tracking
  - Automatic one-time data migration on first startup via `SettingsMigrationStartupService`
  - Controllers use `ISiteSettingService` + `IWebhookDelegateService` instead of generic `IPwebSettingService`
  - `QuickPickService` refactored to use dedicated repositories instead of loading all settings and filtering by key prefix
  - Significant query performance improvement: full table scans replaced with indexed lookups

### Deprecated
- **`pweb_settings` table** — The old key-value table in the Poracle DB is no longer written to. Data is automatically migrated to the new structured tables on first startup. The old `IPwebSettingService` and `PwebSettingEntity` remain registered for the migration service but should not be used for new code.

### Fixed
- **Remove hardcoded branding**, rename namespaces to Pgan.PoracleWebNet ([PR #32](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/32))
- **MkDocs build** — remove unsupported option, pin mkdocs<2 ([PR #31](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/31))
- **Security hardening** — IDOR, OAuth redirect, command injection, CORS, and code quality ([PR #28](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/28))
- **Settings endpoint security** — `GET /api/settings` now filters sensitive keys for non-admin users ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Settings upsert preserves metadata** — `PUT /api/settings/{key}` no longer overwrites `ValueType` and `Category` ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Duplicate webhook delegate handling** — `AddAsync` returns existing delegate instead of throwing 500 ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Quick pick removal with deleted definitions** — `alarm_type` stored in applied state so removal works even if the definition is later deleted ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))

### Security
- Settings API no longer serves `api_secret`, `telegram_bot_token`, `scan_db` to non-admin users
- Webhook delegates stored in proper relational table (no more arbitrary key injection via settings PUT endpoint)

## [0.5.5] - 2026-03-22

### Fixed
- **Dashboard profile card shows 'Default' when no profiles exist** — instead of the misleading "Profile 1" fallback ([#23](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/23), [PR #24](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/24))

## [0.5.4] - 2026-03-22

### Added
- **Dashboard profile quick-switch** — profile card now shows actual profile name with a dropdown menu to switch profiles without leaving the dashboard ([#19](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/19), [PR #22](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/22))

### Fixed
- **Location 404 for users without profile records** — `GET /api/location` now falls back to `humans` table coordinates when no profile exists, and `PUT /api/location` auto-creates a default profile. Affected 99% of users. ([#17](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/17), [PR #20](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/20))
- **Changelog workflow inserting entries under wrong release** — replaced grep/sed with awk scoped to the `[Unreleased]` block; fixed double-v prefix in release commit messages ([#18](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/18), [PR #21](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/21))

## [0.5.3] - 2026-03-22

### Fixed
- **Nested polygon click priority** — explicitly call `bringToFront()` on smaller polygons after rendering to ensure Leaflet SVG hit detection works correctly ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))

## [0.5.2] - 2026-03-22

### Fixed
- **Nested polygon selection on area map** — smaller polygons inside larger ones (e.g. "mikes" inside "west end") can now be clicked; polygons are sorted by area so smaller ones render on top ([#12](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/12), [PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))

### Changed
- Convert all logger calls to `[LoggerMessage]` source generators for improved performance ([#14](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/14), [PR #15](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/15))
- Fix locale-sensitive `ToString`/`Parse`/`StartsWith` calls, use typed HTTP header properties, replace generic exceptions in tests

## [0.5.1] - 2026-03-22

### Fixed
- Koji Poracle format URL missing `?name=true` — geofence names were empty in the unified feed

## [0.5.0] - 2026-03-22

### Added
- **Unified Geofence Proxy Feed** — PoracleWeb is now the single geofence source for PoracleJS ([#10](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/10), [PR #11](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/11))
  - Fetches admin geofences from Koji, resolves groups from parent chain, merges user geofences
  - PoracleJS uses a single URL — no direct Koji connection needed, no `group_map.json` required
  - 5-minute in-memory cache with invalidation on geofence changes
  - Graceful degradation: user geofences still served if Koji is down; PoracleJS `.cache/` failover if PoracleWeb is down
  - No custom code needed in PoracleJS or Koji — compatible with upstream/standard versions

### Changed
- `Koji:ProjectName` is a new required config setting
- PoracleJS `geofence.path` simplified from dual-source array to single PoracleWeb URL

## [0.4.0] - 2026-03-22

### Added
- **Admin Poracle Server Management** — Monitor and restart PoracleJS instances remotely from the admin panel
  - Multi-server support with configurable servers (name, host, SSH user)
  - Real-time health status indicators (online/offline)
  - Per-server restart via SSH + PM2 with confirm dialog
  - Restart All option with warning about alert disruption
  - Dedicated "Poracle Servers" admin page
- **User Custom Geofences** — Draw polygon boundaries on a dedicated map page for precise notification zones ([#5](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/5))
  - Dedicated "My Geofences" page with Leaflet Draw polygon tools
  - Region auto-detection from polygon centroid
  - Geofence name validation (letters, numbers, spaces, hyphens, apostrophes, max 50 chars)
  - Max 10 custom geofences per user
  - Polygon limits (3-500 points)
- **Admin Geofence Management** — Review, approve, reject, and delete user-submitted geofences
  - Status filter tabs (All, Pending, Active, Approved, Rejected)
  - Approve with optional rename, reject with reason
  - Admin delete with full cleanup (DB, Koji, user areas)
- **Discord Forum Integration** — Geofence submissions create forum posts in coverage-requests channel
  - Auto-created forum tags (Geofence - Pending / Approved / Rejected)
  - Static map image of geofence polygon in embed
  - Approval/rejection messages posted to thread, then locked and archived
- **Shared Region Selector** — Unified autocomplete component with country/state grouping
  - Replaces 3 separate region selector implementations
  - Used in area-map, geofence-name-dialog, and area-list
- **PoracleWeb Database** — Separate `poracle_web` MariaDB database for app-owned data
  - `user_geofences` table with submission workflow fields
  - `PoracleWebContext` as second EF Core DbContext
- **Unified Geofence Proxy Feed** — `GET /api/geofence-feed` serves all geofences to PoracleJS from a single endpoint
  - PoracleWeb fetches admin geofences from Koji (cached 5 minutes, invalidated on changes), resolves group names from parent chain, and merges with user geofences
  - PoracleJS uses a single URL (`geofence.path`) — no direct Koji connection needed, no `group_map.json` required
  - Graceful degradation: user geofences still served if Koji is unreachable; PoracleJS `.cache/` provides failover if PoracleWeb is down
  - User geofences served with `displayInMatches: false` for privacy
- **Submit for Review Workflow** — Users can submit geofences for admin promotion to public areas
  - Clear messaging explaining the promotion process
  - Status badges on geofence cards (Pending, Approved, Rejected)

### Changed
- PoracleWeb is now the single geofence source for PoracleJS — admin geofences from Koji and user geofences from the local DB are merged into one feed. PoracleJS `geofence.path` is a single URL (not an array). `group_map.json` is no longer needed. `Koji:ProjectName` is a new required config for the feed.
- Areas page no longer shows user-created geofences (privacy fix)
- Geofence names stored lowercase for Poracle case-sensitive matching

### Fixed
- Leaflet Draw `draw:created` event binding uses string literal instead of `L.Draw.Event.CREATED`
- `displayInMatches` set to `false` on user geofences to prevent name leaking in DMs
- Discord notification tag cache now uses static fields (persists across transient instances)
- Server-side displayName validation matches frontend regex
- Koji `RemoveGeofenceFromProjectAsync` no longer sends `__parent: 0` (caused Koji 500)
- Polygon deserialization uses clean coordinate arrays instead of `JsonElement.Clone()`

## [0.3.0] - 2026-03-21

### Fixed
- **Pokemon IV validation**: ATK/DEF/STA fields now enforce 0-15 range with error messages ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))

### Added
- **Pokemon type filter**: Filter by Pokemon type with UICONS type icons from masterfile ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))
- **Tile grid selector**: Clickable tile grid for bulk Pokemon selection when gen/type filter is active

## [0.2.0] - 2026-03-21

### Fixed
- **Alerts saving to wrong profile**: All alarm types now use profile from JWT claim instead of hardcoded profile 1 ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))
- **Location not profile-scoped**: Location reads/writes from profiles table with dual-write to humans for PoracleJS compatibility ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))

### Changed
- `ProfileNo` removed from all frontend Create DTOs — enforced server-side from JWT
- Profile switching syncs lat/lon to humans table

## [0.1.0] - 2026-03-20

### Added
- Initial release of PoracleWeb
- Discord OAuth2 and Telegram authentication
- Pokemon, Raid, Quest, Invasion, Lure, Nest, Gym alarm management
- Area selection with interactive geofence map
- Location management with geocoding
- Profile system with per-profile locations and areas
- Bulk operations (distance update, delete) on all alarm types
- Admin panel for user management
- Dark/light theme with customizable accent colors
- Onboarding wizard for new users
- Skeleton loading animations
- Rate limiting (per-IP) on auth endpoints
- Docker deployment with Watchtower auto-updates

[Unreleased]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.7.0...HEAD
[2.7.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.6.0...v2.7.0
[2.6.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.4.1...v2.6.0
[2.5.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.4.0...v2.5.0
[2.4.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.3.0...v2.4.1
[2.4.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.3.0...v2.4.0
[2.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.2.0...v2.3.0
[2.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.2.0...v2.3.0
[2.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.3...v2.2.0
[2.1.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.2...v2.1.3
[2.1.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.2...v2.1.3
[2.1.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.1...v2.1.2
[2.1.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.1...v2.1.2
[2.1.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.0...v2.1.1
[2.1.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.1.0...v2.1.1
[2.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.3.1...v2.0.0
[1.3.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.1.2...v1.3.0
[1.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.1.1...v1.2.0
[1.1.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.1.0...v1.1.2
[1.1.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.2...v1.1.1
[1.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.4...v1.0.0
[0.6.4]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.3...v0.6.4
[0.6.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.1...v0.6.3
[0.6.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.0...v0.6.1
[0.6.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.5...v0.6.0
[0.5.5]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.4...v0.5.5
[0.5.4]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.2...v0.5.3
[0.5.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/releases/tag/v0.1.0
