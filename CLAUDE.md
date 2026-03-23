# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a full-stack web application for configuring Pokemon GO DM notification alarms through the Poracle bot system. Users authenticate via Discord OAuth2 or Telegram and manage alert filters (Pokemon, Raids, Quests, etc.) that Poracle uses to send personalized notifications.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider -- `MySql.EntityFrameworkCore`, NOT Pomelo)
- **Frontend**: Angular 21, standalone components with `inject()` and signals, Angular Material 21 (Material Design 3)
- **Mapping**: AutoMapper for Entity-to-Model conversions
- **Maps**: Leaflet 1.9 for interactive geofence display
- **Auth**: JWT bearer tokens, Discord OAuth2, Telegram Bot Login
- **Testing**: Jest (frontend), xUnit (backend)

## Solution Structure

```
Pgan.PoracleWebNet.slnx
|
+-- Core/
|   +-- Core.Abstractions/       Interfaces: IRepository, IService, IUnitOfWork
|   +-- Core.Models/             DTOs passed between layers (not EF entities)
|   +-- Core.Mappings/           AutoMapper PoracleMappingProfile
|   +-- Core.Repositories/       BaseRepository<TEntity, TModel> implementations,
|   |                            SiteSettingRepository, WebhookDelegateRepository,
|   |                            QuickPickDefinitionRepository, QuickPickAppliedStateRepository
|   +-- Core.Services/           Business logic (MonsterService, DashboardService,
|   |                            UserGeofenceService, KojiService, SiteSettingService,
|   |                            WebhookDelegateService, QuickPickService,
|   |                            DiscordNotificationService, PoracleServerService, etc.)
|   +-- Core.UnitsOfWork/        PoracleUnitOfWork wrapping DbContext.SaveChangesAsync()
|
+-- Data/
|   +-- Data/                    PoracleContext (EF Core), PoracleWebContext (app-owned DB),
|   |                            Entities/ (incl. UserGeofenceEntity, SiteSettingEntity,
|   |                            WebhookDelegateEntity, QuickPickDefinitionEntity,
|   |                            QuickPickAppliedStateEntity),
|   |                            Configurations/ (EF Core entity type configurations)
|   +-- Data.Scanner/            RdmScannerContext for optional scanner DB
|
+-- Applications/
|   +-- Web.Api/                 ASP.NET Core host
|   |   +-- Controllers/         20+ API controllers (REST, all under /api/)
|   |   |                        incl. UserGeofenceController, AdminGeofenceController,
|   |   |                        GeofenceFeedController
|   |   +-- Configuration/       DI registration, JwtSettings, DiscordSettings, KojiSettings, etc.
|   |   +-- Services/            Background services: AvatarCacheService, DtsCacheService,
|   |                            SettingsMigrationStartupService
|   +-- Web.App/
|       +-- ClientApp/           Angular 21 SPA
|           +-- src/app/
|               +-- core/        Guards (auth, admin), services, interceptors, models
|               +-- modules/     Feature modules: auth, dashboard, pokemon, raids,
|               |                quests, invasions, lures, nests, gyms, areas,
|               |                profiles, cleaning, quick-picks, admin, geofences
|               +-- shared/      Reusable components: area-map, pokemon-selector,
|                                template-selector, delivery-preview, location-dialog,
|                                discord-avatar, alarm-info, confirm-dialog,
|                                language-selector, distance-dialog, onboarding,
|                                region-selector, geofence-name-dialog,
|                                geofence-approval-dialog
|                                utils/: geo.utils (point-in-polygon, centroid)
|
+-- Tests/
    +-- Pgan.PoracleWebNet.Tests/  xUnit backend tests (controllers, services, mappings)
```

## Key Patterns

### Repository Layer
- `BaseRepository<TEntity, TModel>` uses expression-based filters and AutoMapper projections.
- `EnsureNotNullDefaults()` method handles MySQL `NOT NULL` text columns that EF Core maps as nullable strings. Call this before saving to avoid constraint violations.
- `UpdateDistanceByUidsAsync()` does targeted distance-only SQL updates without touching other fields -- use this for bulk distance operations instead of the generic `UpdateAsync`.

### AutoMapper Update Models
- All `*Update` models (MonsterUpdate, RaidUpdate, etc.) use **nullable `int?`** properties so partial updates don't zero out unset fields.
- The mapping profile uses `.ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))` to skip null properties.
- **Important**: When calling the PUT `/{uid}` endpoint from the frontend, always spread the full alarm object (`{ ...alarm, distance }`) rather than sending a partial `{ distance }`. This ensures existing values like `clean`, `template`, and filter settings are preserved.

### Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (full object)
  - `PUT /distance` -- Update ALL alarms' distance for the current user/profile
  - `PUT /distance/bulk` -- Update distance for specific UIDs: `{ uids: number[], distance: number }`. This does a targeted `SetDistance()` on matching entities, bypassing AutoMapper entirely -- safe for bulk operations.

### Settings Architecture
- Site settings are stored in typed columns in `poracle_web.site_settings` with categories (branding, features, alarms, admin, etc.) and typed values.
- **Deprecated**: The `pweb_settings` KV store in the Poracle DB (`PwebSettingEntity` / `IPwebSettingService`) is deprecated. Settings, webhook delegates, and quick picks previously stored as key-prefixed JSON blobs in `pweb_settings` are migrated to dedicated structured tables in the `poracle_web` database.
- Webhook delegation uses the `poracle_web.webhook_delegates` relational table with a composite unique constraint (user + webhook), replacing the old `webhook_delegates:` key prefix pattern in `pweb_settings`.
- Quick pick definitions use `poracle_web.quick_pick_definitions` with JSON columns for filter definitions. Applied state is tracked in `poracle_web.quick_pick_applied_states` per user/profile, replacing the old `quick_pick:`, `user_quick_pick:`, and `qp_applied:` key prefix patterns.
- On first startup after upgrade, `SettingsMigrationStartupService` automatically migrates data from `pweb_settings` to the structured tables. This is idempotent and safe to run multiple times.

### Poracle API Proxy
- `IPoracleApiProxy` / `PoracleApiProxy` wraps HttpClient calls to the external Poracle REST API.
- Used for: fetching config, areas/geofences, templates, sending commands.
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`.

### Poracle Config Parsing
- `PoracleConfig` is parsed from Poracle's JSON configuration.
- Handles mixed types: `defaultTemplateName` can be a number or string in Poracle's config. Use `JsonElement` or careful deserialization.

### Areas and Profile-Scoped Storage
- Area subscriptions are **profile-scoped**. Each profile has its own set of selected areas.
- **Two storage locations** are kept in sync:
  - `profiles.area` — the authoritative per-profile storage. Each row in the `profiles` table stores a JSON array of area names for that specific profile (e.g., `["west end", "downtown"]`).
  - `humans.area` — PoracleJS's working copy for the **currently active** profile. PoracleJS reads this field for notification matching. It is always a mirror of the active profile's `profiles.area`.
- **Reading**: `GET /api/areas` reads from `profiles.area` for the current profile (identified by `profileNo` in the JWT). Falls back to `humans.area` if the profile record is missing (legacy users).
- **Writing**: `PUT /api/areas` writes to **both** `humans.area` and `profiles.area` for the current profile. All internal area mutations (`AddAreaToHumanAsync`, `RemoveAreaFromHumanAsync`) also dual-write to both tables.
- **Profile switch**: `SwitchProfile` saves `humans.area` to the old profile's `profiles.area`, then loads the new profile's `profiles.area` into `humans.area`. This keeps PoracleJS in sync without requiring PoracleJS to understand profiles.
- **Important**: Never write to `humans.area` alone — always update `profiles.area` in tandem, or the change will be lost on the next profile switch.
- Geofence polygons come from the Poracle API, not the database.

### Custom Geofences
- User geofences are stored in `poracle_web.user_geofences` (separate database from Poracle, see "PoracleWeb Database" below).
- **Geofences are user-scoped, area subscriptions are profile-scoped.** A geofence is created once per user and shared across all profiles. Whether a profile receives notifications for that geofence is controlled by whether its `kojiName` appears in that profile's area list (see "Areas and Profile-Scoped Storage" above).
- **Per-profile toggle**: Users can activate or deactivate a geofence for the current profile via a slide toggle in the geofence list UI, without recreating the geofence. This calls `POST /api/geofences/custom/{id}/activate` or `POST /api/geofences/custom/{id}/deactivate`, which delegate to `AddToProfileAsync` / `RemoveFromProfileAsync` in `UserGeofenceService`. Both endpoints validate ownership before modifying areas.
- **Toggle visibility**: The slide toggle is hidden for `approved` geofences — once promoted to a public area in Koji, users manage them via the standard Areas page instead.
- **Creation**: `CreateAsync` stores the geofence in `user_geofences` and adds its `kojiName` to the **current** profile's area list (both `humans.area` and `profiles.area`). The geofence appears as active on the creating profile and inactive on all others.
- **Deletion**: `DeleteAsync` removes the geofence's `kojiName` from **all** profiles (`humans.area` for the active profile + every `profiles.area` entry), then deletes the `user_geofences` row and reloads Poracle geofences. `AdminDeleteAsync` does the same but looks up the owning user's actual `CurrentProfileNo` instead of hardcoding a profile number.
- **PoracleWeb is the single geofence source for PoracleJS.** The `GET /api/geofence-feed` endpoint (`[AllowAnonymous]`, intended for internal network access) serves a unified feed that merges admin geofences from Koji with user-drawn geofences from the PoracleWeb database. No custom code is needed in PoracleJS or Koji -- standard upstream versions work.
- PoracleJS `geofence.path` config is a single URL pointing to PoracleWeb (not an array, not dual Koji+PoracleWeb sources). PoracleJS does not connect to Koji directly for geofences.
- Admin geofences are fetched from Koji via the `/geofence/poracle/{projectName}` endpoint, with group names resolved from the Koji parent chain. They are served with `displayInMatches: true` and `group` populated. Results are cached for 5 minutes (`IMemoryCache` with `TimeSpan.FromMinutes(5)` TTL). The cache is invalidated when a geofence is approved/promoted to Koji.
- User geofences are served with `displayInMatches: false` and `userSelectable: false` -- names are hidden from all DMs and are not selectable on the Poracle bot's area list.
- Parent/region geofences from Koji are excluded from the feed (they are structural, not alerting areas).
- **Graceful degradation**: If Koji is unreachable, the feed endpoint logs the error and still serves user geofences from the local DB. PoracleJS's built-in `.cache/` directory provides additional failover -- if PoracleWeb itself is down, PoracleJS falls back to its last cached geofence data.
- `group_map.json` is not needed in PoracleJS -- group names are resolved automatically from the Koji parent chain by PoracleWeb.
- On admin approval, the geofence polygon is pushed to Koji with `isPublic: true` (`userSelectable: true`), making it a proper public area.
- Koji is used only for admin/approved public geofences; user-drawn private geofences remain in the PoracleWeb database.
- Geofence names (the `kojiName` field) are always **lowercase** because Poracle does case-sensitive area matching and area lists store names in lowercase.
- Geofence names are auto-generated from the user-provided display name (lowercased). Collisions are resolved by appending a numeric suffix.
- Maximum 10 custom geofences per user. Polygons limited to 500 points.
- Discord forum posts are created on submission via the bot API (`discordapp.com/api/v9`). Forum tags (Pending, Approved, Rejected) are auto-created.
- On approval/rejection, the Discord forum thread is updated with a status message, tagged, locked, and archived.
- Geofence statuses: `active` (private, user-only), `pending_review` (submitted for admin review), `approved` (promoted to Koji as public), `rejected` (remains private with review notes).
- `KojiService` fetches region (parent) geofences from Koji's reference API and serves them to the frontend `region-selector` component for auto-detection of which region a drawn polygon belongs to.

### Poracle Server Management
- Servers configured via `Poracle:Servers` array in appsettings (name, host, API address, SSH user).
- Health check pings each server's API endpoint to determine online/offline status.
- Restart executes `ssh user@host "pm2 restart all"` via `System.Diagnostics.Process`.
- SSH key mounted as read-only volume at `/app/ssh_key` (path configurable via `Poracle:SshKeyPath`).

### PoracleWeb Database
- Second `DbContext`: `PoracleWebContext` using `ConnectionStrings:PoracleWebDb`.
- Separate `poracle_web` MariaDB/MySQL database for application-owned data (not managed by PoracleJS).
- Does **not** modify the Poracle DB schema -- the Poracle database remains exclusively managed by PoracleJS.
- Tables:
  - `user_geofences` -- user-drawn custom geofence polygons.
  - `site_settings` -- typed admin-configurable settings with categories (replaces `pweb_settings` KV store).
  - `webhook_delegates` -- relational user-to-webhook delegation mappings with composite unique constraint.
  - `quick_pick_definitions` -- structured quick pick alarm presets (global and user-scoped) with JSON filter columns.
  - `quick_pick_applied_states` -- tracks which quick picks users have applied per profile, with tracked alarm UIDs.
- Schema managed via **EF Core migrations** (`Database.MigrateAsync()` on startup). New tables are created automatically.
- `MariaDbHistoryRepository` overrides the default MySQL migration lock to use `GET_LOCK(3600)` instead of `GET_LOCK(-1)`, working around a `MySql.EntityFrameworkCore` bug where MariaDB returns NULL for infinite-timeout locks.
- Design-time factory (`PoracleWebContextDesignTimeFactory`) enables `dotnet ef migrations add` without a running app.
- Migrations are stored in `Data/Pgan.PoracleWebNet.Data/Migrations/PoracleWeb/`.

### Profiles
- `humans.current_profile_no` (not `profile_no`) tracks the active profile.
- All alarm tables reference `profile_no` to filter by active profile.
- **Area storage**: Each profile stores its own area list in `profiles.area`. The active profile's areas are also mirrored in `humans.area` for PoracleJS compatibility (see "Areas and Profile-Scoped Storage").
- **Profile switch lifecycle** (`ProfileController.SwitchProfile`):
  1. Saves current `humans.area` → old profile's `profiles.area` (preserves outgoing profile)
  2. Loads new profile's `profiles.area` → `humans.area` (activates incoming profile)
  3. Updates `humans.current_profile_no`, lat/lng
  4. Issues a new JWT with the updated `profileNo`
- **Custom geofences and profiles**: Geofences are user-scoped (not profile-scoped), but their area subscriptions are profile-scoped. A user can activate/deactivate a geofence per profile via the toggle UI without affecting other profiles. Deleting a geofence removes it from all profiles.

### Rate Limiting
- Auth endpoints use **per-IP** partitioned rate limiting (not global).
- `auth` policy: 30 requests per 60s per IP (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s per IP (current user, profile switch).
- Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

### Service Lifetimes
- Most services are **scoped** (per-request). `MasterDataService` is a **singleton** (cached game data).
- `DashboardService` runs sequential DB queries (not `Task.WhenAll`) because it uses a single scoped `DbContext` instance which is not thread-safe.
- Swagger/OpenAPI is available in the development environment.

### Angular Patterns
- All components are **standalone** (no NgModules).
- Uses `inject()` function instead of constructor injection.
- Uses Angular signals for reactive state where applicable.
- Lazy-loaded routes in `app.routes.ts`.
- Services in `core/services/` use `HttpClient` to call the .NET API.

### UI Patterns
- **Alarm lists**: Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance.
- **Bulk operations**: Select mode toggle (checklist icon) on each alarm list, bulk toolbar with Select All, Update Distance, Delete.
- **Skeleton loading**: Animated skeleton card placeholders on Pokemon, Raids, Quests pages.
- **Staggered animations**: Grid items fade in with 30ms stagger delay.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag.

## Configuration

- **Secrets**: `appsettings.Development.json` (gitignored) holds all connection strings, JWT secret, Discord/Telegram credentials, Poracle API address/secret.
- **Docker**: Environment variables configured in `.env` file, mapped in `docker-compose.yml`.
- **Poracle API**: Address comes from `appsettings.json` `Poracle:ApiAddress`. (**Deprecated**: previously also read from the `pweb_settings` table in the Poracle DB; now stored in `poracle_web.site_settings`.)
- **Site Settings**: Admin-configurable settings (custom title, feature flags, etc.) are stored in `poracle_web.site_settings`. On first startup after upgrade, `SettingsMigrationStartupService` migrates any existing data from the deprecated `pweb_settings` table automatically.
- **Discord Bot Token**: Sourced from PoracleJS server's `config/local-discord.json`.
- **Admin IDs**: Comma-separated Discord user IDs in `Poracle:AdminIds`.
- **PoracleWeb DB**: `ConnectionStrings:PoracleWebDb` -- connection string for the `poracle_web` database (user geofences, site settings, webhook delegates, quick picks).
- **Koji Geofence API**:
  - `Koji:ApiAddress` -- Koji geofence server URL (e.g., `http://localhost:8080`).
  - `Koji:BearerToken` -- Koji API authentication token.
  - `Koji:ProjectId` -- Koji project ID for admin-promoted geofences.
  - `Koji:ProjectName` -- Koji project name used for the `/geofence/poracle/{name}` endpoint to fetch admin geofences. Settings class: `KojiSettings`.
- **Discord Geofence Forum**: `Discord:GeofenceForumChannelId` -- Discord forum channel ID where geofence submission threads are created.
- **Poracle Servers**: `Poracle:Servers` -- array of PoracleJS server configs (name, host, API address, SSH user) for multi-server management.
- **SSH Key Path**: `Poracle:SshKeyPath` -- path to SSH private key inside the container (default `/app/ssh_key`).
- **PoracleJS config**: `geofence.path` in PoracleJS config is a single URL pointing to the PoracleWeb unified feed endpoint (e.g., `"http://poracleweb:8082/api/geofence-feed"`). PoracleWeb fetches admin geofences from Koji internally and merges them with user geofences.

## Common Issues

### MySQL Provider
Pomelo MySQL provider (`Pomelo.EntityFrameworkCore.MySql`) is **incompatible** with EF Core 10. This project uses `MySql.EntityFrameworkCore` (Oracle's official provider). Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

### NULL String Columns
Many Poracle DB columns are `NOT NULL` with empty-string defaults but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method in `BaseRepository` sets null strings to `""` before saving.

### Discord API
- Use `discordapp.com` (not `discord.com`) for API calls -- `discord.com` is blocked by Cloudflare in some server environments.
- `AvatarCacheService` fetches avatars sequentially with delays to avoid rate limiting.

### Poracle Config Mixed Types
`defaultTemplateName` in Poracle's config can be a number (e.g., `1`) or a string (e.g., `"default"`). Deserialization must handle both.

### Scanner DB is Optional
The `ScannerDb` connection string is optional. If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses.

### Bulk Update Preserving Fields
When updating alarms in bulk, **never** send partial objects to `PUT /{uid}`. AutoMapper maps all fields from the Update model onto the existing entity -- `int` properties default to `0` when absent from JSON, which overwrites existing values like `clean` and filter settings. Use the dedicated `PUT /distance/bulk` endpoint for distance changes, or spread the full alarm: `{ ...alarm, distance }`.

### Discord API Version for Geofence Notifications
Use `discordapp.com/api/v9` (not v10) -- v10 is not supported on the `discordapp.com` domain. The `DiscordNotificationService` HttpClient is configured with base address `https://discordapp.com/api/v9/`.

### Poracle Area Case Sensitivity
Poracle does **case-sensitive** area matching. Geofence names stored in `humans.area`, `profiles.area`, and the `kojiName` field in `user_geofences` must always be lowercase. The `UserGeofenceService.CreateAsync()` method enforces this with `ToLowerInvariant()`. All area mutation methods (`AddAreaToHumanAsync`, `RemoveAreaFromHumanAsync`, `AreaController.UpdateAreas`) normalize to lowercase before storing.

### Koji displayInMatches Limitation
Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. To ensure user geofence names are hidden from DMs, serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

### Settings Migration
On first startup after upgrade, the `SettingsMigrationStartupService` automatically migrates data from the deprecated `pweb_settings` KV store (in the Poracle DB) to structured tables in the `poracle_web` database (`site_settings`, `webhook_delegates`, `quick_pick_definitions`, `quick_pick_applied_states`). This is idempotent -- safe to run multiple times. If migration fails, the app continues with existing data and logs the error. The old `pweb_settings` table is not deleted; it remains read-only as a fallback until fully decommissioned.

### MariaDB GET_LOCK Compatibility
`MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK('__EFMigrationsLock', -1)` which returns NULL on MariaDB (infinite timeout not supported), causing `System.InvalidCastException`. The `MariaDbHistoryRepository` class overrides the lock acquisition to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

## Build & Run

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

1. **Clone the repo** and copy `.env.example` to `.env`, fill in database credentials and Discord/Telegram secrets.
2. **Create the `poracle_web` database** in MariaDB/MySQL (empty — tables are created automatically):
   ```sql
   CREATE DATABASE poracle_web CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
   ```
3. **Configure connection strings** in `appsettings.Development.json` (gitignored):
   - `ConnectionStrings:PoracleDb` — the Poracle database (managed by PoracleJS)
   - `ConnectionStrings:PoracleWebDb` — the `poracle_web` database (owned by this app)
4. **Run the app** — `dotnet run` from `Applications/Pgan.PoracleWebNet.Api`. On first startup:
   - `MigrateAsync()` applies all pending EF Core migrations, creating the `poracle_web` tables
   - `SettingsMigrationStartupService` migrates data from the old `pweb_settings` table (if any exists)
5. **Run the Angular dev server** — `npm start` from `ClientApp/` (proxies API calls to the .NET backend)

### Adding new PoracleWeb tables
1. Add the entity class to `Data/Pgan.PoracleWebNet.Data/Entities/`
2. Add a `DbSet<>` to `PoracleWebContext`
3. Optionally add an `IEntityTypeConfiguration<>` in `Data/Configurations/`
4. Create a migration: `dotnet ef migrations add <Name> --context PoracleWebContext --project Data/Pgan.PoracleWebNet.Data --startup-project Applications/Pgan.PoracleWebNet.Api --output-dir Migrations/PoracleWeb`
5. The migration applies automatically on next app startup via `MigrateAsync()`

## Production Setup (Docker)

1. **Build the image**: `docker build -t poracleweb.net:latest .`
2. **Configure `.env`** with production values (DB hosts, secrets, Koji API, Discord bot token)
3. **Ensure the `poracle_web` database exists** in MariaDB/MySQL (tables are created automatically on startup)
4. **Start**: `docker compose up -d`
5. **On first start**, the app will:
   - Run EF Core migrations to create all `poracle_web` tables
   - Migrate settings data from `pweb_settings` (Poracle DB) to the new structured tables
6. **Subsequent starts** skip both steps (migrations already applied, sentinel key set)
7. **Updates**: `docker build -t poracleweb.net:latest . && docker compose up -d --force-recreate`
   - New migrations (if any) apply automatically on startup

## Code Style

- **Prettier**: 140-char print width, single quotes, 2-space indent, configured in `.prettierrc`
- **ESLint**: Configured with Angular, perfectionist (sorted class members), and Prettier plugins
- **EditorConfig**: 2-space indent, UTF-8, in `ClientApp/.editorconfig`

## File Locations

| What | Path |
|---|---|
| EF Core Entities | `Data/Pgan.PoracleWebNet.Data/Entities/` |
| EF Core DbContext (Poracle) | `Data/Pgan.PoracleWebNet.Data/PoracleContext.cs` |
| EF Core DbContext (PoracleWeb) | `Data/Pgan.PoracleWebNet.Data/PoracleWebContext.cs` |
| User Geofence Entity | `Data/Pgan.PoracleWebNet.Data/Entities/UserGeofenceEntity.cs` |
| Site Setting Entity | `Data/Pgan.PoracleWebNet.Data/Entities/SiteSettingEntity.cs` |
| Webhook Delegate Entity | `Data/Pgan.PoracleWebNet.Data/Entities/WebhookDelegateEntity.cs` |
| Quick Pick Definition Entity | `Data/Pgan.PoracleWebNet.Data/Entities/QuickPickDefinitionEntity.cs` |
| Quick Pick Applied State Entity | `Data/Pgan.PoracleWebNet.Data/Entities/QuickPickAppliedStateEntity.cs` |
| PwebSetting Entity (deprecated) | `Data/Pgan.PoracleWebNet.Data/Entities/PwebSettingEntity.cs` |
| Entity Configurations | `Data/Pgan.PoracleWebNet.Data/Configurations/` |
| MariaDb History Repository | `Data/Pgan.PoracleWebNet.Data/MariaDbHistoryRepository.cs` |
| Design-Time Context Factory | `Data/Pgan.PoracleWebNet.Data/PoracleWebContextDesignTimeFactory.cs` |
| EF Core Migrations (PoracleWeb) | `Data/Pgan.PoracleWebNet.Data/Migrations/PoracleWeb/` |
| API Controllers | `Applications/Pgan.PoracleWebNet.Api/Controllers/` |
| Geofence Feed Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/GeofenceFeedController.cs` |
| Admin Geofence Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/AdminGeofenceController.cs` |
| User Geofence Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/UserGeofenceController.cs` |
| Area Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/AreaController.cs` |
| Profile Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/ProfileController.cs` |
| DI Registration | `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs` |
| Settings Classes | `Applications/Pgan.PoracleWebNet.Api/Configuration/` (JwtSettings, DiscordSettings, KojiSettings, PoracleServerSettings, etc.) |
| Settings Migration Service | `Applications/Pgan.PoracleWebNet.Api/Services/SettingsMigrationStartupService.cs` |
| Angular App Root | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/` |
| Angular Routes | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/app.routes.ts` |
| Angular Services | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/` |
| Angular Guards | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/guards/` |
| Shared Components | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/` |
| Feature Modules | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/` |
| Geofences Module | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/geofences/` |
| Admin Geofence Submissions | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/admin/geofence-submissions/` |
| Region Selector Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/region-selector/` |
| Geofence Name Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/geofence-name-dialog/` |
| Geofence Approval Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/geofence-approval-dialog/` |
| Geo Utilities | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/geo.utils.ts` |
| AutoMapper Profile | `Core/Pgan.PoracleWebNet.Core.Mappings/PoracleMappingProfile.cs` |
| Repository Base | `Core/Pgan.PoracleWebNet.Core.Repositories/` |
| SiteSettingRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/SiteSettingRepository.cs` |
| WebhookDelegateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/WebhookDelegateRepository.cs` |
| QuickPickDefinitionRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickDefinitionRepository.cs` |
| QuickPickAppliedStateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickAppliedStateRepository.cs` |
| Service Layer | `Core/Pgan.PoracleWebNet.Core.Services/` |
| UserGeofenceService | `Core/Pgan.PoracleWebNet.Core.Services/UserGeofenceService.cs` |
| SiteSettingService | `Core/Pgan.PoracleWebNet.Core.Services/SiteSettingService.cs` |
| WebhookDelegateService | `Core/Pgan.PoracleWebNet.Core.Services/WebhookDelegateService.cs` |
| QuickPickService | `Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs` |
| PwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Services/PwebSettingService.cs` |
| KojiService | `Core/Pgan.PoracleWebNet.Core.Services/KojiService.cs` |
| DiscordNotificationService | `Core/Pgan.PoracleWebNet.Core.Services/DiscordNotificationService.cs` |
| IPoracleServerService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/` |
| IPwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPwebSettingService.cs` |
| PoracleServerService | `Core/Pgan.PoracleWebNet.Core.Services/PoracleServerService.cs` |
| Poracle Servers Page | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/admin/poracle-servers/` |
| Abstractions | `Core/Pgan.PoracleWebNet.Core.Abstractions/` |
| Backend Tests | `Tests/Pgan.PoracleWebNet.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Docker Config | `Dockerfile`, `docker-compose.yml`, `.env.example` |

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, dialogs, and utilities (including `geo.utils.spec.ts`, `user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `region-selector.component.spec.ts`, `geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`).
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and AutoMapper mappings (including `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`, `AreaControllerTests`, `ProfileControllerTests`, `SettingsControllerTests`, `AdminControllerTests`, `PwebSettingServiceTests`, `QuickPickServiceSecurityTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`).
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
