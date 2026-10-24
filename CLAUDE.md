# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a full-stack web application for configuring Pokemon GO DM notification alarms through the Poracle bot system. Compatible with both [PoracleJS](https://github.com/KartulUdus/PoracleJS) and [PoracleNG](https://github.com/jfberry/PoracleNG). Users authenticate via Discord OAuth2 or Telegram and manage alert filters (Pokemon, Raids, Quests, etc.) that Poracle uses to send personalized notifications.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider -- `MySql.EntityFrameworkCore`, NOT Pomelo)
- **Frontend**: Angular 21, standalone components with `inject()` and signals, Angular Material 21 (Material Design 3)
- **Mapping**: Manual extension methods (`AlarmMappingExtensions`, `EntityMappingExtensions`) for Entity-to-Model and DTO-to-Model conversions (alarm CRUD uses JSON via PoracleNG proxy)
- **Maps**: Leaflet 1.9 for interactive geofence display
- **Auth**: JWT bearer tokens, Discord OAuth2, Telegram Bot Login
- **Testing**: Jest (frontend), xUnit (backend)

## Solution Structure

Top-level folders (full tree and the per-file lookup table are in `docs/file-locations.md`):

- `Core/` -- `Core.Abstractions` (interfaces), `Core.Models` (DTOs, not EF entities), `Core.Mappings` (manual mapping extensions), `Core.Repositories`, `Core.Services` (business logic and the PoracleNG proxies)
- `Data/` -- `Data` (`PoracleWebContext`, entities, configurations, migrations) and `Data.Scanner` (optional `ScannerContext`)
- `Applications/` -- `Web.Api` (ASP.NET Core host, controllers, filters, DI configuration) and `Web.App/ClientApp` (Angular SPA: `core/`, `modules/`, `shared/`)
- `Tests/` -- `Pgan.PoracleWebNet.Tests`, xUnit backend tests
- `scripts/` -- `setup.sh`, `dev.sh`, `docker.sh`

Detailed tree and lookup table of where things live: `docs/file-locations.md`.

## Key Patterns

### Repository Layer
- Repositories remain for **non-alarm** data only: all PoracleWeb-owned tables (`SiteSettingRepository`, `WebhookDelegateRepository`, `UserGeofenceRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`). There is no human or profile repository: every human and profile read and write, admin bulk included, goes through `IPoracleHumanProxy` (5.4.0's `/api/v2`).
- **Removed**: `HumanRepository` and `ProfileRepository` (5.4.0), 8 alarm repository classes (MonsterRepository, RaidRepository, etc.), `BaseRepository<TEntity, TModel>`, `PoracleUnitOfWork`, `IUnitOfWork`, and all alarm repository interfaces. `PoracleContext` itself is gone: PoracleWeb.NET has no connection to Poracle's database, so a direct alarm write is not one `DbSet` away. Nothing writes an alarm table: per-alarm `override_areas` go through PoracleNG's trusted tracking write. `EnsureNotNullDefaults()` is no longer needed for alarm writes (PoracleNG handles NULL defaults).

### Manual Mapping Extensions
- **No AutoMapper dependency.** All mappings use static extension methods in `Core.Mappings/`.
- `AlarmMappingExtensions` provides `To*()` (e.g., `model.ToMonster()`) and `ApplyUpdate()` (e.g., `model.ApplyUpdate(existing)`) methods for all 10 alarm types. `ApplyUpdate()` replicates the null-skip semantics: only non-null source properties overwrite the destination.
- `EntityMappingExtensions` provides `ToModel()`, `ToEntity()`, and `ApplyTo()` methods for `Human`, `Profile`, `UserGeofence`, `SiteSetting`, `WebhookDelegate`, `QuickPickDefinition`, and `QuickPickAppliedState` entity-model pairs.
- `QuickPickDefinition` mappings handle `FiltersJson` (string) to `Filters` (`Dictionary<string, object?>`) JSON deserialization with `CamelCase` naming policy. `QuickPickAppliedState` mappings handle `ExcludePokemonIdsJson`/`TrackedUidsJson` to `List<int>` deserialization.
- `UserGeofence` entity-to-model mapping intentionally skips `Polygon` (populated by service layer). Model-to-entity mapping intentionally skips `Id` (auto-generated).

### Poracle API Proxy (Config/Read-Only)
- `IPoracleApiProxy` / `PoracleApiProxy` wraps HttpClient calls to the external Poracle REST API for **read-only** operations: fetching config, geofence definitions, templates, sending commands.
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`.
- **Not used for alarm CRUD or human/profile writes** -- those go through `IPoracleTrackingProxy` and `IPoracleHumanProxy` respectively (see "PoracleNG API Proxy Layer" above).

### Poracle Config Parsing
- `PoracleConfig` is parsed from Poracle's JSON configuration.
- Handles mixed types: `defaultTemplateName` can be a number or string in Poracle's config. Use `JsonElement` or careful deserialization.

### Service Lifetimes
- Most services are **scoped** (per-request). `MasterDataService` is a **singleton** (cached game data).
- `DashboardService` now uses a single `GetAllTrackingAsync` call to PoracleNG instead of 8 separate DB count queries.
- Swagger/OpenAPI is available in the development environment.

## Fixing Defects Without Causing Them

About one in five defects found in this codebase's audit sweeps was caused by an earlier fix in the
same campaign. They are not random -- they cluster into two shapes, and both are cheap to prevent.

### Shape 1: tightening a rule without enumerating who depended on the loose one

The repeated failure. A constraint gets added, the bad case is verified refused, and nobody checks
which legitimate cases are now refused too.

| The fix | What it broke |
|---|---|
| #601 resolved delegated webhooks live | delegates configured in PoracleJS got an empty page and a 403 (#626) |
| #604 validated quick-pick filters at save time | seeding died on two built-ins that carry empty filters on purpose (#637) |
| #616 cleared the admin token on a 401 | left an impersonation banner whose Stop button did nothing (#627) |
| #531 guarded alarm-edit collisions | refused uneditable alarms (#553), then swallowed `gym_id` (#575), then ignored first-match ordering (#606) |

**Before adding any validation, allowlist, guard or refusal:**

1. **Query production for what currently satisfies the loose rule.** An invasion `gruntType` allowlist
   built from `InvasionGruntTypes.All` would have refused `blanche`, `candela`, `spark`, `npc 0`-`npc 10`
   and `player team leader` -- every one of them live. Query production read-only, for example through
   `docker run --rm mariadb:latest mariadb --skip-ssl -h HOST -P PORT -u USER -pPASS DB -e "SELECT ..."`.
   If you cannot enumerate what the loose rule permits, you cannot safely tighten it.
2. **Prefer refusing the impossible over allowing only the known.** "No control characters, no more than
   the column holds" ages well. "One of these thirty values" does not.
3. **Find the other callers.** `SeedDefaultsAsync` reaches `SaveAdminPickAsync`; quick-pick apply reaches
   `BulkCreateAsync`. A guard added for the interactive path fires on every internal path too.

### Shape 2: fixing one path and leaving its siblings

`bulkDelete` was hardened in #603 and `bulkUpdateDistance` was not (#641). The single-pick delete cleared
applied state and the reseed did not (#630). One edit dialog sent `''` and nine sent `null` (#639).

**Before committing, grep for the shape you just fixed.** There are ten alarm types, ten list components,
ten edit dialogs, eleven locale files. A fix that touches one of a set of ten is incomplete until you have
looked at the other nine and can say in the PR why they are fine, or fixed them too.

### Tests: assert what must still work, not only what must now fail

A test written alongside a fix encodes the fix's own model of the world, so it passes whether or not the
fix is right. Three fixes validated the domain model instead of the `*Create` DTO the controller binds,
passed their tests, and did nothing (#548, #555, #565). `monster.service.spec.ts` went further and
**asserted the broken request shape**, so the suite was actively defending the bug (#640).

- Every guard needs a **legitimate-case-still-passes** test alongside the refusal test. The grunt-type
  theory names five values found in live data; the seeding test names the two presets that were being
  dropped. Those are the tests that catch shape 1.
- **Watch a new test fail before you fix the code.** A test that has never been red proves nothing.
- When an existing test contradicts a fix, work out which one is wrong before changing either. Sometimes
  the test is enshrining the defect.

### Verify upstream behaviour by calling it, not by reading it

Three fixes were derived from a PoracleNG checkout four months adrift from production (#521, #531, #553).
Pin the checkout first -- see the memory note `project_poracleng_checkout_must_match_prod` -- and then,
where it is cheap, confirm by POSTing to the running instance rather than trusting the source read.
Reading `DiffAndClassify` incrementally produced a guard that was right about the part just read and wrong
about the part not yet read, twice.

### What "clean" means

A sweep returning nothing does not mean the code is defect-free -- it means that set of lenses is
exhausted. Every sweep should include a **regression lens**: an agent that reads the diffs merged since
the last sweep and asks only "what did these fixes break, and which siblings did they miss?" That is the
lens which catches the one-in-five, and it is the one most easily forgotten.

## Common Issues

### Discord API
- Use `discordapp.com` (not `discord.com`) for API calls -- `discord.com` is blocked by Cloudflare in some server environments.
- `AvatarCacheService` fetches avatars sequentially with delays to avoid rate limiting.

### Poracle Config Mixed Types
`defaultTemplateName` in Poracle's config can be a number (e.g., `1`) or a string (e.g., `"default"`). Deserialization must handle both.

## Branching

| Branch | Purpose |
|---|---|
| `main` | Released code. Only moves when a release is merged. Publishing the release builds `:latest`, which prod's watchtower auto-deploys within its 60s poll -- the merge itself ships nothing. `docker-publish.yml` also has an SSH deploy step, but it is inert here: it exits early unless `DEPLOY_HOST` and `DEPLOY_SSH_KEY` are set, and this repo has neither. A plain `git clone` lands here, so self-hosters get released code. |
| `develop` | Integration. **Open pull requests against this.** Publishes `:beta` on every merge, which the dev instance auto-deploys. |
| `experimental/*` | Unofficial release lines built on released `main`. Never merged back. Publish only GitHub **prereleases**, keep their own `CHANGELOG-<name>.md`, and take `main` through the merge PRs `sync-experimental.yml` opens after each full release (merge commit, never squash or rebase). |

Cutting a release: merge `develop` into `main`, then publish a GitHub release. `release-changelog.yml` opens a PR promoting `[Unreleased]` to the new version section, and `docker-publish.yml` pushes `:latest`.

`ci.yml` and `changelog.yml` run for pushes and PRs on `main`, `develop` and `experimental/**` -- a workflow filtered to fewer branches means PRs into the others run with no checks at all and merge looking green (this happened to #394 while it was stacked on another branch).

**Prereleases skip every release workflow** (#962): `docker-publish`, `docs`, `release-changelog` and `sync-experimental` skip a release marked pre-release and any tag containing a hyphen, so an experimental build cannot move `:latest`, redeploy the docs site from its branch, or cut `main`'s CHANGELOG. Promoting a prerelease to a full release later sends `released`, not `published`, and runs none of them; publish a fresh full release instead.

## Build & Run

### Using convenience scripts (recommended)

```bash
# First-time setup (interactive wizard)
./scripts/setup.sh

# Development
./scripts/dev.sh install            # Install frontend (npm) dependencies
./scripts/dev.sh api                # Start .NET API (http://localhost:5048)
./scripts/dev.sh app                # Start Angular dev server (http://localhost:4200)
./scripts/dev.sh start              # Start both API + Angular in parallel
./scripts/dev.sh test               # Run all tests (backend + frontend)
./scripts/dev.sh lint               # Run ESLint + Prettier checks
./scripts/dev.sh build              # Production build (API + Angular → publish/)
./scripts/dev.sh db:create          # Create the poracle_web database
./scripts/dev.sh db:migrate <Name>  # Add a new EF Core migration

# Docker
./scripts/docker.sh build           # Build Docker image
./scripts/docker.sh start           # Start containers
./scripts/docker.sh stop            # Stop containers
./scripts/docker.sh logs            # Tail container logs
./scripts/docker.sh update          # Rebuild and recreate
./scripts/docker.sh clean           # No-cache rebuild
```

### Manual commands

```bash
# Build entire solution (from solution root)
dotnet build

# Run API (starts on http://localhost:5048)
cd Applications/Pgan.PoracleWebNet.Api
dotnet run

# Run Angular dev server (starts on http://localhost:4200)
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm install
npm start              # alias for ng serve
npm run watch          # watch mode for development
npm run build          # production build

# Run frontend tests (Jest)
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm test

# Run backend tests (xUnit)
dotnet test

# Lint and format
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm run lint           # ESLint check
npm run prettier-check # Prettier check
npx eslint --fix src/  # Auto-fix lint
npm run prettier-format # Auto-format

# Docker — build from source
docker build -t poracleweb.net:latest .
docker compose up -d

# Docker — force clean rebuild
docker build --no-cache -t poracleweb.net:latest .
docker compose up -d --force-recreate

# EF Core Migrations — add a new migration after model changes
dotnet ef migrations add <MigrationName> \
  --context PoracleWebContext \
  --project Data/Pgan.PoracleWebNet.Data \
  --startup-project Applications/Pgan.PoracleWebNet.Api \
  --output-dir Migrations/PoracleWeb

# EF Core Migrations — generate SQL script (for review)
dotnet ef migrations script \
  --context PoracleWebContext \
  --project Data/Pgan.PoracleWebNet.Data \
  --startup-project Applications/Pgan.PoracleWebNet.Api
```

## Development Setup

1. **Clone the repo** and either run `./scripts/setup.sh` (interactive wizard) or manually copy `.env.example` to `.env` and fill in your values.
2. **Create the `poracle_web` database** — run `./scripts/dev.sh db:create`, or manually:
   ```sql
   CREATE DATABASE poracle_web CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
   ```
3. **Configuration**: All settings are read from `.env` at the project root. `Program.cs` loads the file, bridges short env var names to .NET config paths, and auto-composes the PoracleWeb connection string from the `WEB_DB_*` variables. You can also use `appsettings.Development.json` (gitignored) for overrides if preferred.
4. **Run the app** — `./scripts/dev.sh start` (runs both API + Angular), or individually:
   - `./scripts/dev.sh api` — starts the .NET API on http://localhost:5048
   - `./scripts/dev.sh app` — starts the Angular dev server on http://localhost:4200
   - On first startup, `MigrateAsync()` applies pending EF Core migrations and `SettingsMigrationStartupService` seeds default settings.

### Adding new PoracleWeb tables
1. Add the entity class to `Data/Pgan.PoracleWebNet.Data/Entities/`
2. Add a `DbSet<>` to `PoracleWebContext`
3. Optionally add an `IEntityTypeConfiguration<>` in `Data/Configurations/`
4. Create a migration: `./scripts/dev.sh db:migrate <Name>` (or manually: `dotnet ef migrations add <Name> --context PoracleWebContext --project Data/Pgan.PoracleWebNet.Data --startup-project Applications/Pgan.PoracleWebNet.Api --output-dir Migrations/PoracleWeb`)
5. The migration applies automatically on next app startup via `MigrateAsync()`

## Production Setup (Docker)

1. **Build the image**: `./scripts/docker.sh build` (or `docker build -t poracleweb.net:latest .`)
2. **Configure `.env`** with production values (`WEB_DB_*`, secrets, Koji API, Discord bot token). The same `.env` format works for both Docker and standalone — `Program.cs` bridges the env vars at startup.
3. **Ensure the `poracle_web` database exists** in MariaDB/MySQL (tables are created automatically on startup)
4. **Start**: `./scripts/docker.sh start` (or `docker compose up -d`)
5. **On first start**, the app will:
   - Run EF Core migrations to create all `poracle_web` tables
6. **Subsequent starts** skip both steps (migrations already applied, defaults already seeded)
7. **Updates**: `./scripts/docker.sh update` (or `docker build -t poracleweb.net:latest . && docker compose up -d --force-recreate`)
   - New migrations (if any) apply automatically on startup

## Code Style

- **Prettier**: 140-char print width, single quotes, 2-space indent, configured in `.prettierrc`
- **ESLint**: Configured with Angular, perfectionist (sorted class members), and Prettier plugins
- **EditorConfig**: 2-space indent, UTF-8, in `ClientApp/.editorconfig`

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, dialogs, and utilities (including `geo.utils.spec.ts`, `user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `region-selector.component.spec.ts`, `geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`, `geofence-submissions.component.spec.ts`, `test-alert.service.spec.ts`, `active-hours.utils.spec.ts`, `active-hours-chip.component.spec.ts`, `active-hours-editor-dialog.component.spec.ts`, `location-warning.component.spec.ts`).
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and manual mapping extensions. Alarm service tests mock `IPoracleTrackingProxy` (returning `JsonElement` payloads) instead of repositories. Human/Profile/Area controller tests mock `IPoracleHumanProxy`. Key test classes: `MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`, `HumanServiceTests`, `DashboardServiceTests`, `CleaningServiceTests`, `AreaControllerTests`, `ProfileControllerTests`, `AdminControllerTests`, `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`, `SettingsControllerTests`, `QuickPickServiceSecurityTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `TestAlertControllerTests`, `TestAlertServiceTests`, `ActiveHoursValidationTests`, `MappingExtensionTests`, `DataProtectionConfigurationTests`, `DiscordNotificationServiceTests`, `GeoMathTests`.
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.

## Path-scoped rules index

Subsystem guidance lives in `.claude/rules/` and loads only when Claude works with matching files. It is all still binding; read the file when you touch that area.

- `.claude/rules/geofences.md` -- Custom/user geofences, Koji, the geofence feed, review embeds, setAreas filter. Loads for: *Geofence*, *geofence*, *Koji*, GeoJson*, GeoMath*, ...
- `.claude/rules/areas-profiles.md` -- Area storage, profiles, profile numbering and rename. Loads for: *Area*, *area*, *Profile*, *profile*, UserRoleResolver*
- `.claude/rules/destinations-delegation.md` -- People vs destinations, webhook delegation, impersonation. Loads for: *Webhook*, *webhook*, Destination*, Impersonation*, HumanService*, ...
- `.claude/rules/poracleng-api.md` -- PoracleNG proxy layer, minimum version, response wrappers, small API quirks. Loads for: Poracle*, IPoracle*, Tracking*, ITracking*, MasterData*, ...
- `.claude/rules/poracleng-v2-writes.md` -- `/api/v2` creates/edits/deletes, insert vs update, validation on Create DTOs. Loads for: Poracle*, IPoracle*, Tracking*, V2Schema*, *Create.cs, ...
- `.claude/rules/alarm-filters-bulk.md` -- Bulk operations, clean bitmask, rule repair, monster/gym filter defaults. Loads for: *Bulk*, *bulk*, *Clean*, *clean*, RuleRepair*, ...
- `.claude/rules/feature-gating-settings.md` -- `disable_*` feature gating, site settings, settings projections. Loads for: *Feature*, *feature*, *SiteSetting*, *Setting*, *settings*, ...
- `.claude/rules/quiet-periods.md` -- Quiet periods (PoracleNG mutes) and summaries. Loads for: *Mute*, *mute*, *quiet*, *Quiet*, *Summary*
- `.claude/rules/test-alerts.md` -- Test alerts and payload builders. Loads for: *TestAlert*, *test-alert*, TestAlerts/**, *TestPayload*
- `.claude/rules/auth-jwt-security.md` -- JWT, disabled-account 401, rate limiting, admin self-protection, OIDC. Loads for: *Jwt*, *jwt*, AuthController*, auth.*, auth/**, ...
- `.claude/rules/location-maps.md` -- Geocode proxy, pin/place usage, basemaps, gym picker, scanner DB. Loads for: *Location*, *location*, *Basemap*, *basemap*, *Gym*, ...
- `.claude/rules/angular-ui.md` -- Angular and UI patterns, failure messages, localized game data. Loads for: Applications/Pgan.PoracleWebNet.App/ClientApp/src/**, ErrorInterceptor*, MasterData*, masterdata*
- `.claude/rules/database-mariadb.md` -- PoracleWeb DB, MariaDB/MySQL provider traps, repositories, seeding. Loads for: Data/**, Core.Repositories/**, Core.Abstractions/Repositories/**, SettingsMigration*, *Repository*.cs, ...
- `.claude/rules/configuration.md` -- .env, appsettings, env var bridge, Koji/Golbat/Discord/DataProtection config. Loads for: .env.example, docker-compose.yml.example, Dockerfile, appsettings*.json, Program.cs, ...
