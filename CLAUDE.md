# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a full-stack web application for configuring Pokemon GO DM notification alarms through the Poracle bot system. Compatible with both [PoracleJS](https://github.com/KartulUdus/PoracleJS) and [PoracleNG](https://github.com/jfberry/PoracleNG). Users authenticate via Discord OAuth2 or Telegram and manage alert filters (Pokemon, Raids, Quests, etc.) that Poracle uses to send personalized notifications.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider -- `MySql.EntityFrameworkCore`, NOT Pomelo)
- **Frontend**: Angular 21, standalone components with `inject()` and signals, Angular Material 21 (Material Design 3)
- **Mapping**: AutoMapper for Entity-to-Model conversions (Human, Profile, PoracleWeb-owned tables only -- alarm CRUD uses JSON via PoracleNG proxy)
- **Maps**: Leaflet 1.9 for interactive geofence display
- **Auth**: JWT bearer tokens, Discord OAuth2, Telegram Bot Login
- **Testing**: Jest (frontend), xUnit (backend)

## Solution Structure

```
Pgan.PoracleWebNet.slnx
|
+-- Core/
|   +-- Core.Abstractions/       Interfaces: IRepository, IService, IPoracleTrackingProxy,
|   |                            IPoracleHumanProxy
|   +-- Core.Models/             DTOs passed between layers (not EF entities)
|   +-- Core.Mappings/           AutoMapper PoracleMappingProfile (Human, Profile,
|   |                            PoracleWeb-owned tables; alarm Create/Update DTOs)
|   +-- Core.Repositories/       HumanRepository, ProfileRepository,
|   |                            SiteSettingRepository, WebhookDelegateRepository,
|   |                            QuickPickDefinitionRepository, QuickPickAppliedStateRepository
|   +-- Core.Services/           Business logic (MonsterService, DashboardService,
|   |                            UserGeofenceService, KojiService, SiteSettingService,
|   |                            WebhookDelegateService, QuickPickService,
|   |                            DiscordNotificationService, PoracleServerService,
|   |                            PoracleTrackingProxy, PoracleHumanProxy, etc.)
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
|                                geofence-approval-dialog, gym-picker
|                                utils/: geo.utils (point-in-polygon, centroid)
|
+-- Tests/
    +-- Pgan.PoracleWebNet.Tests/  xUnit backend tests (controllers, services, mappings)
```

## Key Patterns

### PoracleNG API Proxy Layer
- **All alarm CRUD** (Monster, Raid, Egg, Quest, Invasion, Lure, Nest, Gym) is proxied through PoracleNG's REST API via `IPoracleTrackingProxy`. Services no longer use repositories or direct DB writes for alarm operations.
- **Human/Profile management** is proxied through `IPoracleHumanProxy` for single-user operations (get, create, start/stop, set location, set areas, switch profile). Admin bulk operations (get all users, delete user) still use `IHumanRepository` directly.
- Both proxies are registered via `AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>()` and `AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>()`.
- Authentication uses the `X-Poracle-Secret` header on every request (from `Poracle:ApiSecret` config).
- **Snake_case JSON**: Proxy classes use `JsonNamingPolicy.SnakeCaseLower` for serialization/deserialization, matching PoracleNG's wire format.
- **`?silent=true`**: Tracking create requests append `?silent=true` to suppress Discord confirmation messages from PoracleNG.
- **`TrackingCreateResult`**: PoracleNG returns `{ newUids, alreadyPresent, updates, insert }` from creates. The `TrackingCreateResult` record captures this so services can assign the UID back to the created model.
- **HumanService is hybrid**: Proxy-first for single-user ops (`GetByIdAsync`, `ExistsAsync`, `CreateAsync`, `DeleteAllAlarms`), with try/catch fallback to direct DB on proxy failure. Admin bulk ops (`GetAllAsync`, `DeleteUserAsync`) remain direct DB because PoracleNG has no admin-list or admin-delete endpoints.
- **PoracleNG handles**: field defaults (template, PVP, size, etc.), dedup detection, immediate state reload on every mutation, grunt_type normalization, area dual-writes on profile switch.

### Repository Layer
- Repositories remain for **non-alarm** data: `HumanRepository`, `ProfileRepository` (direct DB for admin/fallback), and all PoracleWeb-owned tables (`SiteSettingRepository`, `WebhookDelegateRepository`, `UserGeofenceRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`).
- **Removed**: 8 alarm repository classes (MonsterRepository, RaidRepository, etc.), `BaseRepository<TEntity, TModel>`, `PoracleUnitOfWork`, `IUnitOfWork`, and all alarm repository interfaces. `EnsureNotNullDefaults()` is no longer needed for alarm writes (PoracleNG handles NULL defaults).

### AutoMapper
- AutoMapper is still used for **Entity-to-Model** conversions on `Human`, `Profile`, and PoracleWeb-owned tables (`UserGeofence`, `SiteSetting`, `WebhookDelegate`, `QuickPickDefinition`, `QuickPickAppliedState`).
- Alarm `*Create` and `*Update` model mappings remain in the profile for controller-level DTO mapping, but are no longer used for Entity writes.
- The `ForAllMembers` null-skip condition on `*Update` mappings is still active for DTO merging.

### Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (full object, sent to PoracleNG as a create-with-uid which performs an upsert)
  - `PUT /distance` -- Update ALL alarms' distance for the current user/profile (fetch-mutate-POST pattern)
  - `PUT /distance/bulk` -- Update distance for specific UIDs (fetch-mutate-POST pattern)
- **CleaningService** uses a fetch-mutate-POST workaround: fetches all alarms of a type, sets the `clean` flag on each, and POSTs them back. PoracleNG has no dedicated bulk-clean endpoint yet.

### Settings Architecture
- Site settings are stored in typed columns in `poracle_web.site_settings` with categories (branding, features, alarms, admin, etc.) and typed values.
- **Deprecated**: The `pweb_settings` KV store in the Poracle DB (`PwebSettingEntity` / `IPwebSettingService`) is deprecated. Settings, webhook delegates, and quick picks previously stored as key-prefixed JSON blobs in `pweb_settings` are migrated to dedicated structured tables in the `poracle_web` database.
- Webhook delegation uses the `poracle_web.webhook_delegates` relational table with a composite unique constraint (user + webhook), replacing the old `webhook_delegates:` key prefix pattern in `pweb_settings`.
- Quick pick definitions use `poracle_web.quick_pick_definitions` with JSON columns for filter definitions. Applied state is tracked in `poracle_web.quick_pick_applied_states` per user/profile, replacing the old `quick_pick:`, `user_quick_pick:`, and `qp_applied:` key prefix patterns.
- On first startup after upgrade, `SettingsMigrationStartupService` automatically migrates data from `pweb_settings` to the structured tables. This is idempotent and safe to run multiple times.

### Poracle API Proxy (Config/Read-Only)
- `IPoracleApiProxy` / `PoracleApiProxy` wraps HttpClient calls to the external Poracle REST API for **read-only** operations: fetching config, geofence definitions, templates, sending commands.
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`.
- **Not used for alarm CRUD or human/profile writes** -- those go through `IPoracleTrackingProxy` and `IPoracleHumanProxy` respectively (see "PoracleNG API Proxy Layer" above).

### Poracle Config Parsing
- `PoracleConfig` is parsed from Poracle's JSON configuration.
- Handles mixed types: `defaultTemplateName` can be a number or string in Poracle's config. Use `JsonElement` or careful deserialization.

### Areas and Profile-Scoped Storage
- Area subscriptions are **profile-scoped**. Each profile has its own set of selected areas.
- **Two storage locations** are kept in sync by PoracleNG:
  - `profiles.area` — the authoritative per-profile storage.
  - `humans.area` — the working copy for the currently active profile.
- **Reading**: `GET /api/areas` reads from the PoracleNG human proxy (`GetAreasAsync`), which returns the active profile's areas.
- **Writing**: `PUT /api/areas` calls `IPoracleHumanProxy.SetAreasAsync()` -- a single atomic call. PoracleNG handles the dual-write to both `humans.area` and `profiles.area` internally.
- **Profile switch**: `SwitchProfile` calls `IPoracleHumanProxy.SwitchProfileAsync()` -- a single atomic call. PoracleNG handles saving areas to the old profile and loading areas from the new profile.
- **Important**: Area dual-writes are now PoracleNG's responsibility. PoracleWeb no longer writes to `humans.area` or `profiles.area` directly for standard area operations. Custom geofence activate/deactivate still uses `SetAreasAsync` through the proxy.
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
- All alarm tables reference `profile_no` to filter by active profile. PoracleNG scopes tracking queries to the active profile automatically.
- **Area storage**: Each profile stores its own area list in `profiles.area`. The active profile's areas are also mirrored in `humans.area` for PoracleNG compatibility (see "Areas and Profile-Scoped Storage").
- **Profile switch lifecycle** (`ProfileController.SwitchProfile`):
  1. Calls `IPoracleHumanProxy.SwitchProfileAsync(userId, profileNo)` -- a single atomic call that handles saving/loading areas and updating `current_profile_no`.
  2. Issues a new JWT with the updated `profileNo`.
- Profile CRUD (add, update, delete, list) is proxied through `IPoracleHumanProxy`.
- **Custom geofences and profiles**: Geofences are user-scoped (not profile-scoped), but their area subscriptions are profile-scoped. A user can activate/deactivate a geofence per profile via the toggle UI without affecting other profiles. Deleting a geofence removes it from all profiles.

### Rate Limiting
- Auth endpoints use **per-IP** partitioned rate limiting (not global).
- `auth` policy: 30 requests per 60s per IP (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s per IP (current user, profile switch).
- Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

### Gym Picker
- `GymPickerComponent` is a shared autocomplete component (`shared/components/gym-picker/`) for selecting a gym by name. Used in gym, raid, and egg add/edit dialogs to populate `gym_id`.
- Displays gym photo thumbnails (from `RdmGymEntity.Url`), name, and resolved area name in the dropdown options.
- Backed by `ScannerService` (frontend, `core/services/scanner.service.ts`) which calls two scanner API endpoints:
  - `GET /api/scanner/gyms?search=term&limit=20` -- Searches gyms by name prefix in the scanner DB. Returns `GymSearchResult[]` with `id`, `name`, `url`, `lat`, `lon`, `teamId`, `area`.
  - `GET /api/scanner/gyms/{id}` -- Looks up a single gym by ID. Used to resolve the display name for an existing `gym_id` value when opening an edit dialog.
- Area names are resolved server-side via point-in-polygon against cached Koji admin geofences. The `IScannerService.PointInPolygon()` static method uses ray-casting for hit testing.
- `RdmGymEntity` maps the `url` column for gym photo thumbnails from the scanner DB.
- The scanner DB is optional -- if not configured, the gym picker is hidden and `gym_id` can still be entered manually.

### Service Lifetimes
- Most services are **scoped** (per-request). `MasterDataService` is a **singleton** (cached game data).
- `DashboardService` now uses a single `GetAllTrackingAsync` call to PoracleNG instead of 8 separate DB count queries.
- Swagger/OpenAPI is available in the development environment.

### Angular Patterns
- All components are **standalone** (no NgModules).
- Uses `inject()` function instead of constructor injection.
- Uses Angular signals for reactive state where applicable.
- Lazy-loaded routes in `app.routes.ts`.
- Services in `core/services/` use `HttpClient` to call the .NET API (including `ScannerService` for gym search).
- `GymPickerComponent` is a shared autocomplete component used in gym/raid/egg dialogs for gym selection with photo thumbnails and area names.

### UI Patterns
- **Alarm lists**: Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance.
- **Bulk operations**: Select mode toggle (checklist icon) on each alarm list, bulk toolbar with Select All, Update Distance, Delete.
- **Skeleton loading**: Animated skeleton card placeholders on Pokemon, Raids, Quests pages.
- **Staggered animations**: Grid items fade in with 30ms stagger delay.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag.
- **Admin geofence submissions**: Three view modes — **card** (map thumbnails, grouped by region), **list** (compact table grouped by region), **table** (flat sortable table with all columns). Region groups use `mat-expansion-panel` with count badges. Sortable columns in table view (name, status, owner, region, points, created, submitted). Discord avatars displayed next to owner and reviewer names. Reviewer names resolved from `reviewedByName` (batch-loaded from humans table).

## Configuration

- **Secrets**: `appsettings.Development.json` (gitignored) holds all connection strings, JWT secret, Discord/Telegram credentials, Poracle API address/secret.
- **Docker**: Environment variables configured in `.env` file, mapped in `docker-compose.yml`.
- **Poracle API** (critical for all writes): `Poracle:ApiAddress` and `Poracle:ApiSecret` are required for the application to function. All alarm CRUD, human/profile management, area updates, and profile switches are proxied through the PoracleNG REST API. If the API is unreachable, alarm operations will fail (HumanService falls back to direct DB for reads only). (**Deprecated**: previously also read from the `pweb_settings` table in the Poracle DB; now stored in `poracle_web.site_settings`.)
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
Many Poracle DB columns are `NOT NULL` with empty-string defaults but EF Core maps them as `string?`. For alarm writes, this is handled by PoracleNG. For remaining direct DB writes (Human, Profile, PoracleWeb-owned tables), repositories handle null normalization as needed.

### Discord API
- Use `discordapp.com` (not `discord.com`) for API calls -- `discord.com` is blocked by Cloudflare in some server environments.
- `AvatarCacheService` fetches avatars sequentially with delays to avoid rate limiting.

### Poracle Config Mixed Types
`defaultTemplateName` in Poracle's config can be a number (e.g., `1`) or a string (e.g., `"default"`). Deserialization must handle both.

### Scanner DB is Optional
The `ScannerDb` connection string is optional. If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses. The gym search endpoints (`GET /api/scanner/gyms?search=` and `GET /api/scanner/gyms/{id}`) gracefully return empty results when the scanner DB is unavailable, and the `GymPickerComponent` hides itself in the UI.

### Bulk Update Preserving Fields
When updating alarms, the frontend still sends full alarm objects to `PUT /{uid}`. The backend now proxies these to PoracleNG's create endpoint (which performs an upsert when a `uid` is present). PoracleNG handles field defaults, so the risk of zeroing out fields is lower than with direct DB writes, but sending the full object remains best practice.

### Discord API Version for Geofence Notifications
Use `discordapp.com/api/v9` (not v10) -- v10 is not supported on the `discordapp.com` domain. The `DiscordNotificationService` HttpClient is configured with base address `https://discordapp.com/api/v9/`.

### Poracle Area Case Sensitivity
Poracle does **case-sensitive** area matching. Geofence names stored in `humans.area`, `profiles.area`, and the `kojiName` field in `user_geofences` must always be lowercase. The `UserGeofenceService.CreateAsync()` method enforces this with `ToLowerInvariant()`. Area updates via `IPoracleHumanProxy.SetAreasAsync()` normalize to lowercase before sending to PoracleNG.

### Koji displayInMatches Limitation
Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. To ensure user geofence names are hidden from DMs, serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

### Settings Migration
On first startup after upgrade, the `SettingsMigrationStartupService` automatically migrates data from the deprecated `pweb_settings` KV store (in the Poracle DB) to structured tables in the `poracle_web` database (`site_settings`, `webhook_delegates`, `quick_pick_definitions`, `quick_pick_applied_states`). This is idempotent -- safe to run multiple times. If migration fails, the app continues with existing data and logs the error. The old `pweb_settings` table is not deleted; it remains read-only as a fallback until fully decommissioned.

### MariaDB GET_LOCK Compatibility
`MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK('__EFMigrationsLock', -1)` which returns NULL on MariaDB (infinite timeout not supported), causing `System.InvalidCastException`. The `MariaDbHistoryRepository` class overrides the lock acquisition to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

### Gym ID NULL vs Empty String
The `gym_id` column in Poracle alarm tables (gym, raid, egg) is a `NOT NULL` string that defaults to `""` (empty string) meaning "any gym". PoracleNG handles the null-to-empty normalization on its side. The `GymPickerComponent` emits `null` when cleared and the gym's `id` string when selected.

### Monster Filter Defaults
PoracleNG applies `cleanRow` defaults (template, PVP ranking, size, max values, etc.) on every create/update, so PoracleWeb no longer needs to maintain its own set of `*Create` model defaults for alarm filter fields. The `*Create` models still exist for DTO mapping but their field defaults are no longer critical -- PoracleNG is the authoritative source for filter defaults.

### PoracleNG API Availability
The PoracleNG REST API (`Poracle:ApiAddress`) must be running and reachable for all alarm, human, profile, and area operations. If the API is down: alarm CRUD fails entirely; `HumanService` falls back to direct DB reads for human lookups (proxy-first with try/catch); profile switches and area updates fail. Monitor PoracleNG uptime as a hard dependency.

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
| Gym Picker Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/gym-picker/` |
| Scanner Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/scanner.service.ts` |
| Geo Utilities | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/geo.utils.ts` |
| AutoMapper Profile | `Core/Pgan.PoracleWebNet.Core.Mappings/PoracleMappingProfile.cs` |
| IPoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs` |
| IPoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs` |
| PoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs` |
| PoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs` |
| Repositories (non-alarm) | `Core/Pgan.PoracleWebNet.Core.Repositories/` |
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
| RdmScannerService | `Core/Pgan.PoracleWebNet.Core.Services/RdmScannerService.cs` |
| IScannerService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IScannerService.cs` |
| GymSearchResult Model | `Core/Pgan.PoracleWebNet.Core.Models/GymSearchResult.cs` |
| Scanner Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/ScannerController.cs` |
| Scanner DB Context | `Data/Pgan.PoracleWebNet.Data.Scanner/` |
| Poracle Servers Page | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/admin/poracle-servers/` |
| Abstractions | `Core/Pgan.PoracleWebNet.Core.Abstractions/` |
| Backend Tests | `Tests/Pgan.PoracleWebNet.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Docker Config | `Dockerfile`, `docker-compose.yml`, `.env.example` |

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, dialogs, and utilities (including `geo.utils.spec.ts`, `user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `region-selector.component.spec.ts`, `geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`, `geofence-submissions.component.spec.ts`).
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and AutoMapper mappings. Alarm service tests mock `IPoracleTrackingProxy` (returning `JsonElement` payloads) instead of repositories. Human/Profile/Area controller tests mock `IPoracleHumanProxy`. Key test classes: `MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`, `HumanServiceTests`, `DashboardServiceTests`, `CleaningServiceTests`, `AreaControllerTests`, `ProfileControllerTests`, `AdminControllerTests`, `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`, `SettingsControllerTests`, `PwebSettingServiceTests`, `QuickPickServiceSecurityTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`.
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
