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

```
Pgan.PoracleWebNet.slnx
|
+-- Core/
|   +-- Core.Abstractions/       Interfaces: IRepository, IService, IPoracleTrackingProxy,
|   |                            IPoracleHumanProxy, ITestAlertService,
|   |                            IGolbatApiProxy, IPokemonAvailabilityService
|   +-- Core.Models/             DTOs passed between layers (not EF entities)
|   +-- Core.Mappings/           Static extension methods: AlarmMappingExtensions (alarm
|   |                            Create/Update DTOs), EntityMappingExtensions (Human, Profile,
|   |                            PoracleWeb-owned tables)
|   +-- Core.Repositories/       HumanRepository, ProfileRepository,
|   |                            SiteSettingRepository, WebhookDelegateRepository,
|   |                            QuickPickDefinitionRepository, QuickPickAppliedStateRepository
|   +-- Core.Services/           Business logic (MonsterService, DashboardService,
|   |                            UserGeofenceService, KojiService, SiteSettingService,
|   |                            WebhookDelegateService, QuickPickService,
|   |                            DiscordNotificationService,
|   |                            PoracleTrackingProxy, PoracleHumanProxy,
|   |                            TestAlertService, GolbatApiProxy,
|   |                            PokemonAvailabilityService, etc.)
|
+-- Data/
|   +-- Data/                    PoracleContext (EF Core), PoracleWebContext (app-owned DB),
|   |                            Entities/ (incl. UserGeofenceEntity, SiteSettingEntity,
|   |                            WebhookDelegateEntity, QuickPickDefinitionEntity,
|   |                            QuickPickAppliedStateEntity),
|   |                            Configurations/ (EF Core entity type configurations)
|   +-- Data.Scanner/            ScannerDbContext for optional scanner DB
|
+-- Applications/
|   +-- Web.Api/                 ASP.NET Core host
|   |   +-- Controllers/         20+ API controllers (REST, all under /api/)
|   |   |                        incl. UserGeofenceController, AdminGeofenceController,
|   |   |                        GeofenceFeedController, TestAlertController
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
|                                geofence-approval-dialog, gym-picker,
|                                active-hours-chip, active-hours-editor-dialog,
|                                location-warning
|                                utils/: geo.utils (point-in-polygon, centroid)
|                                models/: active-hours.models (interfaces, utilities)
|
+-- scripts/
|   +-- setup.sh                 Interactive first-time setup wizard
|   +-- dev.sh                   Dev convenience commands (install, api, app, start, test, lint, build, db:create, db:migrate)
|   +-- docker.sh                Docker convenience commands (build, start, stop, logs, update, clean)
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
- **HumanService is proxy-first**: All single-user ops (`GetByIdAsync`, `ExistsAsync`, `CreateAsync`, `DeleteAllAlarms`, `StartAsync`, `StopAsync`, `SetLocationAsync`) go through `IPoracleHumanProxy` with no DB fallback -- proxy errors propagate to the caller. Admin bulk ops (`GetAllAsync`, `DeleteUserAsync`) remain direct DB because PoracleNG has no admin-list or admin-delete endpoints.
- **ProfileService is proxy-first**: Single-user reads (`GetByUserAsync`, `GetByUserAndProfileNoAsync`) and all CRUD go through `IPoracleHumanProxy`. No DB fallback -- proxy errors propagate.
- **PoracleNG handles**: field defaults (template, PVP, size, etc.), dedup detection, immediate state reload on every mutation, grunt_type normalization, area dual-writes on profile switch.

### Repository Layer
- Repositories remain for **non-alarm** data: `HumanRepository` (admin bulk ops only -- `GetAllAsync`, `DeleteUserAsync`), `ProfileRepository` (admin bulk ops and non-active profile cleanup in `UserGeofenceService`), and all PoracleWeb-owned tables (`SiteSettingRepository`, `WebhookDelegateRepository`, `UserGeofenceRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`). Single-user human and profile reads/writes are fully proxied through `IPoracleHumanProxy`.
- **Removed**: 8 alarm repository classes (MonsterRepository, RaidRepository, etc.), `BaseRepository<TEntity, TModel>`, `PoracleUnitOfWork`, `IUnitOfWork`, and all alarm repository interfaces. `EnsureNotNullDefaults()` is no longer needed for alarm writes (PoracleNG handles NULL defaults).

### Manual Mapping Extensions
- **No AutoMapper dependency.** All mappings use static extension methods in `Core.Mappings/`.
- `AlarmMappingExtensions` provides `To*()` (e.g., `model.ToMonster()`) and `ApplyUpdate()` (e.g., `model.ApplyUpdate(existing)`) methods for all 10 alarm types. `ApplyUpdate()` replicates the null-skip semantics: only non-null source properties overwrite the destination.
- `EntityMappingExtensions` provides `ToModel()`, `ToEntity()`, and `ApplyTo()` methods for `Human`, `Profile`, `PwebSetting`, `UserGeofence`, `SiteSetting`, `WebhookDelegate`, `QuickPickDefinition`, and `QuickPickAppliedState` entity-model pairs.
- `QuickPickDefinition` mappings handle `FiltersJson` (string) to `Filters` (`Dictionary<string, object?>`) JSON deserialization with `CamelCase` naming policy. `QuickPickAppliedState` mappings handle `ExcludePokemonIdsJson`/`TrackedUidsJson` to `List<int>` deserialization.
- `UserGeofence` entity-to-model mapping intentionally skips `Polygon` (populated by service layer). Model-to-entity mapping intentionally skips `Id` (auto-generated).

### Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (uses `*Update.ApplyUpdate(existing)` extension method for null-skip merge, then sends to PoracleNG as a create-with-uid which performs an upsert)
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
- **Writing**: `PUT /api/areas` calls `IPoracleHumanProxy.SetAreasAsync()` and then `IUserGeofenceService.PreserveOwnedAreasInHumanAsync()`. The proxy call is atomic for admin fences, but PoracleNG silently strips any area whose fence has `userSelectable=false` (its allowed-areas filter in `HandleSetAreas`). Since user-drawn geofences are served from PoracleWeb's feed with `userSelectable=false`, the preserve step re-adds any user-owned geofence names from the request via direct DB so the save doesn't nuke them.
- **Profile switch**: `SwitchProfile` calls `IPoracleHumanProxy.SwitchProfileAsync()` -- a single atomic call. PoracleNG handles saving areas to the old profile and loading areas from the new profile.
- **Important**: Area dual-writes are PoracleNG's responsibility only for admin areas. Custom geofence activate/deactivate writes directly to `humans.area` and the active `profiles.area` via `IUserAreaDualWriter` (atomic single-`SaveChangesAsync`), bypassing `SetAreasAsync`. Going through `SetAreasAsync` is impossible for user geofences because of PoracleNG's `userSelectable=true`-only intersection filter for non-admins -- see "User geofence persistence" in Common Issues.
- Geofence polygons come from the Poracle API, not the database.

### Custom Geofences
- User geofences are stored in `poracle_web.user_geofences` (separate database from Poracle, see "PoracleWeb Database" below).
- **Geofences are user-scoped, area subscriptions are profile-scoped.** A geofence is created once per user and shared across all profiles. Whether a profile receives notifications for that geofence is controlled by whether its `kojiName` appears in that profile's area list (see "Areas and Profile-Scoped Storage" above).
- **Per-profile toggle**: Users can activate or deactivate a geofence for the current profile via a slide toggle in the geofence list UI, without recreating the geofence. This calls `POST /api/geofences/custom/{id}/activate` or `POST /api/geofences/custom/{id}/deactivate`, which delegate to `AddToProfileAsync` / `RemoveFromProfileAsync` in `UserGeofenceService`. Both endpoints validate ownership before modifying areas.
- **Toggle visibility**: The slide toggle is hidden for `approved` geofences — once promoted to a public area in Koji, users manage them via the standard Areas page instead.
- **Creation**: `CreateAsync` stores the geofence in `user_geofences` and adds its `kojiName` to the **current** profile's area list via `IUserAreaDualWriter.AddAreaToActiveProfileAsync` — one atomic `SaveChangesAsync` commits both `humans.area` and the current `profiles.area` row. The geofence appears as active on the creating profile and inactive on all others.
- **Deletion**: `DeleteAsync` removes the geofence's `kojiName` from **all** profiles via `IUserAreaDualWriter.RemoveAreaFromAllProfilesAsync` (one atomic `SaveChangesAsync` spanning `humans.area` and every `profiles.area` row), then deletes the `user_geofences` row and reloads Poracle geofences. `AdminDeleteAsync` does the same. Going through the proxy is not viable because PoracleNG's `setAreas` intersects with `userSelectable=true` fences only and would drop the write.
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
- **Active hours**: Each profile has an optional `active_hours` JSON string that controls PoracleNG's profile scheduler -- PoracleNG automatically switches to/from the profile based on these time rules.
  - **Data format**: JSON array of `{ "day": N, "hours": N, "mins": N }` entries where `day` is 1-7 (Mon-Sun), `hours` is 0-23, `mins` is 0-59. Maximum 28 entries (4 per day). Stored as a JSON string in `profiles.active_hours`.
  - **Backend**: `Profile.ActiveHours` (string?) property. `ProfileService.DeserializeProfiles` extracts `active_hours` from proxy JSON via `GetStringPropOrNull`. Create/Update/Duplicate endpoints on `ProfileController` and `ProfileOverviewController` include `active_hours` in the proxy payload.
  - **Validation**: `ProfileController.ValidateActiveHours` (internal static) validates the JSON structure: day 1-7, hours 0-23, mins 0-59, max 28 entries, accepts both string and int types for hours/mins fields (PoracleNG stores these inconsistently).
  - **Frontend**: `ProfileService.updateActiveHours()` sends the updated schedule. `ActiveHoursChipComponent` renders compact amber schedule pills on profile cards. `ActiveHoursEditorDialogComponent` provides a day picker (circular buttons + Weekdays/Weekends/Every Day presets), time picker, grouped rules list, and mini weekly preview grid.
  - **Location warning**: `LocationWarningComponent` shows an inline red warning when active hours are set but the profile has 0,0 coordinates, since PoracleNG uses the profile's location for timezone calculations and 0,0 defaults to UTC.
- **JWT profile resync**: PoracleNG can change `current_profile_no` out-of-band (active-hours scheduler, bot `!profile` command). `GET /api/auth/me` detects when the JWT's `profileNo` claim differs from the DB value and returns a refreshed JWT with the corrected profile number, preventing alarm CRUD from targeting a stale profile. The dashboard shows a snackbar notification when this resync occurs.

### Rate Limiting
- Auth endpoints use **per-IP** partitioned rate limiting (not global).
- `auth` policy: 30 requests per 60s per IP (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s per IP (current user, profile switch).
- `test-alert` policy: 5 requests per 60s per IP (test alert sends).
- Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

### Test Alerts
- `POST /api/test-alert/{type}/{uid}` triggers a test notification for a specific alarm. Supported types: `pokemon`, `raid`, `egg`, `quest`, `invasion`, `lure`, `nest`, `gym`.
- **Parallel data fetch**: `TestAlertService` uses `Task.WhenAll` to fetch the alarm (via `IPoracleTrackingProxy`) and the human record (via `IPoracleHumanProxy`) concurrently.
- **Mock webhook payloads**: The service builds a realistic mock webhook payload per alarm type using the alarm's filter fields (e.g., pokemon_id, raid_level, quest_reward). The user's location from the human record is used as the mock event coordinates.
- **PoracleNG test endpoint**: The built payload is sent to PoracleNG's `POST /api/test` endpoint, which formats and delivers the notification to the user via their configured webhook.
- **Rate limiting**: `test-alert` policy at 5 requests per 60s per IP, using the same per-IP partitioned pattern as `auth` and `auth-read`.
- **Controller validation**: The `type` path parameter is validated against a hardcoded set of valid alarm types before calling the service.
- **Frontend**: `TestAlertService` (Angular, `core/services/test-alert.service.ts`) tracks per-UID cooldowns (15s) client-side and deduplicates in-flight requests. Displays success/error/cooldown feedback via snackbar. Test button appears in `mat-card-actions` on all 8 alarm card types.

### Gym Picker
- `GymPickerComponent` is a shared autocomplete component (`shared/components/gym-picker/`) for selecting a gym by name. Used in gym, raid, and egg add/edit dialogs to populate `gym_id`.
- Displays gym photo thumbnails (from `ScannerGymEntity.Url`), name, and resolved area name in the dropdown options.
- Backed by `ScannerService` (frontend, `core/services/scanner.service.ts`) which calls two scanner API endpoints:
  - `GET /api/scanner/gyms?search=term&limit=20` -- Searches gyms by name prefix in the scanner DB. Returns `GymSearchResult[]` with `id`, `name`, `url`, `lat`, `lon`, `teamId`, `area`.
  - `GET /api/scanner/gyms/{id}` -- Looks up a single gym by ID. Used to resolve the display name for an existing `gym_id` value when opening an edit dialog.
- Area names are resolved server-side via point-in-polygon against cached Koji admin geofences. The `IScannerService.PointInPolygon()` static method uses ray-casting for hit testing.
- `ScannerGymEntity` maps the `url` column for gym photo thumbnails from the scanner DB.
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
- Services in `core/services/` use `HttpClient` to call the .NET API (including `ScannerService` for gym search, `TestAlertService` for test notifications).
- `TestAlertService` manages per-UID cooldown tracking (15s Map-based TTL) and in-flight request deduplication to prevent duplicate API calls.
- `GymPickerComponent` is a shared autocomplete component used in gym/raid/egg dialogs for gym selection with photo thumbnails and area names.
- `ActiveHoursEditorDialogComponent` is a shared dialog for editing profile schedule rules with day/time pickers and a weekly preview grid.
- `ActiveHoursChipComponent` renders compact amber schedule pills summarizing active hours on profile cards.
- `LocationWarningComponent` displays an inline warning when a profile has active hours but missing coordinates.

### UI Patterns
- **Alarm lists**: Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance. Test button in card actions sends a sample notification via PoracleNG.
- **Bulk operations**: Select mode toggle (checklist icon) on each alarm list, bulk toolbar with Select All, Update Distance, Delete.
- **Skeleton loading**: Animated skeleton card placeholders on Pokemon, Raids, Quests pages.
- **Staggered animations**: Grid items fade in with 30ms stagger delay.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag.
- **Active hours pills**: Amber schedule chips on profile cards showing compressed day-range + time summaries (e.g., "Mon-Fri 8:00 AM"). Uses `ActiveHoursChipComponent` with `compressDayRange` and `formatTime12h` utilities.
- **Location warning**: Inline red warning banner (`LocationWarningComponent`) displayed on profile cards when active hours are configured but the profile's coordinates are 0,0 (which defaults to UTC in PoracleNG's scheduler).
- **Admin geofence submissions**: Three view modes — **card** (map thumbnails, grouped by region), **list** (compact table grouped by region), **table** (flat sortable table with all columns). Region groups use `mat-expansion-panel` with count badges. Sortable columns in table view (name, status, owner, region, points, created, submitted). Discord avatars displayed next to owner and reviewer names. Reviewer names resolved from `reviewedByName` (batch-loaded from humans table).

## Configuration

- **Unified `.env` file**: A single `.env` file (copied from `.env.example`) configures PoracleWeb for both Docker and standalone mode. `Program.cs` loads `.env` at startup, bridges short env var names (e.g., `JWT_SECRET`, `DISCORD_CLIENT_ID`) to .NET's `__` convention (e.g., `Jwt__Secret`, `Discord__ClientId`), and auto-composes MySQL connection strings from `DB_HOST`/`DB_PORT`/`DB_NAME`/`DB_USER`/`DB_PASSWORD` (and `WEB_DB_*` for the PoracleWeb DB). Docker Compose also reads `.env` natively; the Program.cs bridge covers the standalone (`dotnet run`) case.
- **Env var bridge** (in `Program.cs`): Maps ~18 short env var names to .NET config paths. Two static helpers at the end of Program.cs: `MapEnvVar()` and `ComposeConnectionString()`.
- **`appsettings.Development.json`** (gitignored) can still be used for overrides, but `.env` is the primary configuration mechanism.
- **Poracle API** (critical for all writes): `Poracle:ApiAddress` and `Poracle:ApiSecret` are required for the application to function. All alarm CRUD, human/profile management, area updates, and profile switches are proxied through the PoracleNG REST API. If the API is unreachable, all alarm, human, and profile operations will fail (no DB fallback). (**Deprecated**: previously also read from the `pweb_settings` table in the Poracle DB; now stored in `poracle_web.site_settings`.)
- **Site Settings**: Admin-configurable settings (custom title, feature flags, etc.) are stored in `poracle_web.site_settings`. On first startup after upgrade, `SettingsMigrationStartupService` migrates any existing data from the deprecated `pweb_settings` table automatically.
- **Discord Bot Token**: Sourced from PoracleJS server's `config/local-discord.json`.
- **Admin IDs**: Comma-separated Discord user IDs in `Poracle:AdminIds`.
- **PoracleWeb DB**: `ConnectionStrings:PoracleWebDb` -- connection string for the `poracle_web` database (user geofences, site settings, webhook delegates, quick picks). Auto-composed by `Program.cs` from `WEB_DB_HOST`, `WEB_DB_PORT`, `WEB_DB_NAME`, `WEB_DB_USER`, `WEB_DB_PASSWORD` env vars when the full connection string is not set.
- **Golbat API** (optional): `Golbat:ApiAddress` and `Golbat:ApiSecret` enable Pokemon availability indicators in the Pokemon selector. When configured, `IGolbatApiProxy` and `IPokemonAvailabilityService` are registered in DI. When not configured, the feature is hidden. Settings class: `GolbatSettings`. Env vars: `GOLBAT_API_ADDRESS`, `GOLBAT_API_SECRET`.
- **Koji Geofence API**:
  - `Koji:ApiAddress` -- Koji geofence server URL (e.g., `http://localhost:8080`).
  - `Koji:BearerToken` -- Koji API authentication token.
  - `Koji:ProjectId` -- Koji project ID for admin-promoted geofences.
  - `Koji:ProjectName` -- Koji project name used for the `/geofence/poracle/{name}` endpoint to fetch admin geofences. Settings class: `KojiSettings`.
- **Discord Geofence Forum**: `Discord:GeofenceForumChannelId` -- Discord forum channel ID where geofence submission threads are created.
- **DataProtection**: Keys are persisted to `DATA_DIR/dataprotection-keys` (Docker: `/app/data/dataprotection-keys`, standalone: `./data/dataprotection-keys`). Configured in `ServiceCollectionExtensions.cs` via `AddDataProtection().PersistKeysToFileSystem().SetApplicationName("Pgan.PoracleWebNet.Api")`. Uses the existing `DATA_DIR` env var (set in Dockerfile, read via `configuration["DATA_DIR"]`) with a fallback to `Path.Combine(Directory.GetCurrentDirectory(), "data")` for local dev. No additional env vars or NuGet packages needed.
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
When updating alarms, the frontend sends `*Update` DTOs to `PUT /{uid}`. The controller uses `model.ApplyUpdate(existing)` (null-skip extension method) to merge only non-null fields onto the existing alarm, then proxies to PoracleNG's create endpoint (which performs an upsert when a `uid` is present). PoracleNG handles field defaults, so the risk of zeroing out fields is lower than with direct DB writes, but sending the full object remains best practice.

### Discord API Version for Geofence Notifications
Use `discordapp.com/api/v9` (not v10) -- v10 is not supported on the `discordapp.com` domain. The `DiscordNotificationService` HttpClient is configured with base address `https://discordapp.com/api/v9/`.

### Poracle Area Case Sensitivity
Poracle does **case-sensitive** area matching. Geofence names stored in `humans.area`, `profiles.area`, and the `kojiName` field in `user_geofences` must always be lowercase. The `UserGeofenceService.CreateAsync()` method enforces this with `ToLowerInvariant()`. Area updates via `IPoracleHumanProxy.SetAreasAsync()` normalize to lowercase before sending to PoracleNG.

### Koji displayInMatches Limitation
Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. To ensure user geofence names are hidden from DMs, serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

### User Geofence Persistence (PoracleNG setAreas Filter)
PoracleNG's `POST /api/humans/{id}/setAreas` handler (`HandleSetAreas` in `processor/internal/api/humans.go`) intersects the submitted area list against `st.Fences` where `UserSelectable == true` (for non-admin users). Any name whose fence has `userSelectable=false` is silently dropped — no error, no warning, just stripped from the result. Because PoracleWeb's `GeofenceFeedController` serves user-drawn geofences with `userSelectable=false` (to hide them from the Poracle bot's `!area` picker), calling `SetAreasAsync` with a user geofence name always results in that name being dropped.

**Workaround**: All user-geofence area mutations (`CreateAsync`, `DeleteAsync`, `AddToProfileAsync`, `RemoveFromProfileAsync`, `AdminDeleteAsync`, `PreserveOwnedAreasInHumanAsync`) delegate to `IUserAreaDualWriter` (`Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IUserAreaDualWriter.cs`), a tiny atomic-write abstraction over `PoracleContext`. Every method commits both `humans.area` and the active `profiles.area` in a single `SaveChangesAsync` call — EF Core wraps that in an implicit transaction, so the two writes cannot drift. In addition, `AreaController.UpdateAreas` calls `PreserveOwnedAreasInHumanAsync` after `SetAreasAsync` to re-add any user-owned geofence names from the submitted list via the writer's bulk `AddAreasToActiveProfileAsync`. Without these workarounds, saving areas or toggling a geofence would silently fail to persist — the UI would show optimistic success but a refresh would reveal the geofence is gone (regression from PR #88 v2.0.0, fixed in v2.4.1 by restoring the pre-migration direct-DB path for this specific case). All sites are tagged `HACK: trusted-set-areas` — `grep -rn "HACK: trusted-set-areas" --include="*.cs"` lists every reversion point.

### Settings Migration
On first startup after upgrade, the `SettingsMigrationStartupService` automatically migrates data from the deprecated `pweb_settings` KV store (in the Poracle DB) to structured tables in the `poracle_web` database (`site_settings`, `webhook_delegates`, `quick_pick_definitions`, `quick_pick_applied_states`). This is idempotent -- safe to run multiple times. If migration fails, the app continues with existing data and logs the error. The old `pweb_settings` table is not deleted; it remains read-only as a fallback until fully decommissioned.

### MariaDB GET_LOCK Compatibility
`MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK('__EFMigrationsLock', -1)` which returns NULL on MariaDB (infinite timeout not supported), causing `System.InvalidCastException`. The `MariaDbHistoryRepository` class overrides the lock acquisition to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

### Gym ID NULL vs Empty String
The `gym_id` column in Poracle alarm tables (gym, raid, egg) is a `NOT NULL` string that defaults to `""` (empty string) meaning "any gym". PoracleNG handles the null-to-empty normalization on its side. The `GymPickerComponent` emits `null` when cleared and the gym's `id` string when selected.

### Monster Filter Defaults
PoracleNG applies `cleanRow` defaults (template, PVP ranking, size, max values, etc.) on every create/update, so PoracleWeb no longer needs to maintain its own set of `*Create` model defaults for alarm filter fields. The `*Create` models still exist for DTO mapping (via `AlarmMappingExtensions.To*()` methods) but their field defaults are no longer critical -- PoracleNG is the authoritative source for filter defaults.

### PoracleNG API Availability
The PoracleNG REST API (`Poracle:ApiAddress`) must be running and reachable for all alarm, human, profile, and area operations. If the API is down: alarm CRUD, human lookups, profile reads/writes, location updates, area updates, and profile switches all fail with no DB fallback. Only admin bulk operations (`GetAllAsync`, `DeleteUserAsync`) and non-active profile cleanup in `UserGeofenceService` use direct DB. Monitor PoracleNG uptime as a hard dependency.

### PoracleNG Response Wrappers
PoracleNG wraps its API responses in container objects. Human data is returned as `{"human": {...}}` and profile lists as `{"profile": [...]}`. The proxy classes (`PoracleHumanProxy`) must extract the inner object/array before deserializing to model types. Failing to unwrap causes deserialization to return null/empty results.

### uid:0 in Create Requests
When creating new alarms, the model's `uid` defaults to `0`. PoracleNG treats `uid: 0` as an update request (looking for an existing row with uid 0) rather than an insert. The `PoracleJsonHelper.StripUidZero()` method removes `uid` properties with value `0` from the JSON payload before sending to PoracleNG, ensuring the request is treated as an insert.

### PoracleNG active_hours String Types
PoracleNG stores `hours` and `mins` fields in `active_hours` JSON entries inconsistently -- sometimes as numbers, sometimes as strings (e.g., `"hours": "8"` instead of `"hours": 8`). The frontend `parseActiveHours` utility applies `Number()` coercion when deserializing to ensure consistent numeric types. The backend `ValidateActiveHours` method in `ProfileController` accepts both `JsonValueKind.Number` and `JsonValueKind.String` (parsing strings via `int.TryParse`) for the same reason.

### Webhook ID URL Encoding
Webhook IDs in Poracle are URLs (e.g., `http://host:port/path`). When constructing proxy API paths that include a webhook ID as a path segment, the ID must be encoded with `Uri.EscapeDataString()` to escape slashes and other special characters. Both `PoracleTrackingProxy` and `PoracleHumanProxy` apply this encoding.

### JWT Profile Desync
PoracleNG can change `current_profile_no` outside of PoracleWeb — the active-hours scheduler switches profiles on a cron, and bot commands like `!profile` update the DB directly. When this happens the JWT's `profileNo` claim goes stale, causing all alarm reads and writes to target the wrong profile. The `/api/auth/me` endpoint detects the mismatch by comparing the JWT claim against the live `current_profile_no` from `IHumanService` and returns a refreshed token (via the `Token` property on `UserInfo`) when they diverge. Frontend callers (`AuthService.loadCurrentUser`) must always check for and store the returned token.

### JWT Generation (IJwtService)
JWT token generation is centralized in `IJwtService` / `JwtService` (singleton). Three methods: `GenerateToken(UserInfo)` for fresh tokens, `GenerateImpersonationToken(UserInfo, impersonatedBy)` for admin impersonation, and `GenerateTokenWithReplacedProfile(ClaimsPrincipal, profileNo)` for profile switches. The latter filters out registered JWT claims (`exp`, `nbf`, `iat`, `iss`, `aud`) before copying to prevent stale claim duplication. All controllers (`AuthController`, `ProfileController`, `ProfileOverviewController`, `AdminController`) use this service — no inline JWT generation.

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
3. **Configuration**: All settings are read from `.env` at the project root. `Program.cs` loads the file, bridges short env var names to .NET config paths, and auto-composes MySQL connection strings from the `DB_*` / `WEB_DB_*` variables. You can also use `appsettings.Development.json` (gitignored) for overrides if preferred.
4. **Run the app** — `./scripts/dev.sh start` (runs both API + Angular), or individually:
   - `./scripts/dev.sh api` — starts the .NET API on http://localhost:5048
   - `./scripts/dev.sh app` — starts the Angular dev server on http://localhost:4200
   - On first startup, `MigrateAsync()` applies pending EF Core migrations and `SettingsMigrationStartupService` migrates data from the old `pweb_settings` table (if any exists).

### Adding new PoracleWeb tables
1. Add the entity class to `Data/Pgan.PoracleWebNet.Data/Entities/`
2. Add a `DbSet<>` to `PoracleWebContext`
3. Optionally add an `IEntityTypeConfiguration<>` in `Data/Configurations/`
4. Create a migration: `./scripts/dev.sh db:migrate <Name>` (or manually: `dotnet ef migrations add <Name> --context PoracleWebContext --project Data/Pgan.PoracleWebNet.Data --startup-project Applications/Pgan.PoracleWebNet.Api --output-dir Migrations/PoracleWeb`)
5. The migration applies automatically on next app startup via `MigrateAsync()`

## Production Setup (Docker)

1. **Build the image**: `./scripts/docker.sh build` (or `docker build -t poracleweb.net:latest .`)
2. **Configure `.env`** with production values (DB hosts, secrets, Koji API, Discord bot token). The same `.env` format works for both Docker and standalone — `Program.cs` bridges the env vars at startup.
3. **Ensure the `poracle_web` database exists** in MariaDB/MySQL (tables are created automatically on startup)
4. **Start**: `./scripts/docker.sh start` (or `docker compose up -d`)
5. **On first start**, the app will:
   - Run EF Core migrations to create all `poracle_web` tables
   - Migrate settings data from `pweb_settings` (Poracle DB) to the new structured tables
6. **Subsequent starts** skip both steps (migrations already applied, sentinel key set)
7. **Updates**: `./scripts/docker.sh update` (or `docker build -t poracleweb.net:latest . && docker compose up -d --force-recreate`)
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
| IJwtService / JwtService | `Applications/Pgan.PoracleWebNet.Api/Configuration/IJwtService.cs`, `JwtService.cs` |
| Settings Classes | `Applications/Pgan.PoracleWebNet.Api/Configuration/` (JwtSettings, DiscordSettings, KojiSettings, etc.) |
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
| Alarm Mapping Extensions | `Core/Pgan.PoracleWebNet.Core.Mappings/AlarmMappingExtensions.cs` |
| Entity Mapping Extensions | `Core/Pgan.PoracleWebNet.Core.Mappings/EntityMappingExtensions.cs` |
| IPoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs` |
| IPoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs` |
| PoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs` |
| PoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs` |
| PoracleJsonHelper | `Core/Pgan.PoracleWebNet.Core.Services/PoracleJsonHelper.cs` |
| Repositories (non-alarm) | `Core/Pgan.PoracleWebNet.Core.Repositories/` |
| SiteSettingRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/SiteSettingRepository.cs` |
| WebhookDelegateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/WebhookDelegateRepository.cs` |
| QuickPickDefinitionRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickDefinitionRepository.cs` |
| QuickPickAppliedStateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickAppliedStateRepository.cs` |
| IUserAreaDualWriter | `Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IUserAreaDualWriter.cs` |
| UserAreaDualWriter | `Core/Pgan.PoracleWebNet.Core.Repositories/UserAreaDualWriter.cs` |
| Service Layer | `Core/Pgan.PoracleWebNet.Core.Services/` |
| UserGeofenceService | `Core/Pgan.PoracleWebNet.Core.Services/UserGeofenceService.cs` |
| SiteSettingService | `Core/Pgan.PoracleWebNet.Core.Services/SiteSettingService.cs` |
| WebhookDelegateService | `Core/Pgan.PoracleWebNet.Core.Services/WebhookDelegateService.cs` |
| QuickPickService | `Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs` |
| PwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Services/PwebSettingService.cs` |
| KojiService | `Core/Pgan.PoracleWebNet.Core.Services/KojiService.cs` |
| DiscordNotificationService | `Core/Pgan.PoracleWebNet.Core.Services/DiscordNotificationService.cs` |
| IPwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPwebSettingService.cs` |
| IScannerService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IScannerService.cs` |
| ScannerService | `Core/Pgan.PoracleWebNet.Core.Services/ScannerService.cs` |
| GymSearchResult Model | `Core/Pgan.PoracleWebNet.Core.Models/GymSearchResult.cs` |
| Test Alert Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/TestAlertController.cs` |
| ITestAlertService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/ITestAlertService.cs` |
| TestAlertService | `Core/Pgan.PoracleWebNet.Core.Services/TestAlertService.cs` |
| TestAlertRequest Model | `Core/Pgan.PoracleWebNet.Core.Models/TestAlertRequest.cs` |
| Test Alert Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/test-alert.service.ts` |
| Scanner Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/ScannerController.cs` |
| Scanner DB Context | `Data/Pgan.PoracleWebNet.Data.Scanner/` |
| GolbatSettings | `Applications/Pgan.PoracleWebNet.Api/Configuration/GolbatSettings.cs` |
| IGolbatApiProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IGolbatApiProxy.cs` |
| GolbatApiProxy | `Core/Pgan.PoracleWebNet.Core.Services/GolbatApiProxy.cs` |
| IPokemonAvailabilityService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPokemonAvailabilityService.cs` |
| PokemonAvailabilityService | `Core/Pgan.PoracleWebNet.Core.Services/PokemonAvailabilityService.cs` |
| PokemonAvailability Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/PokemonAvailabilityController.cs` |
| Pokemon Availability Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/pokemon-availability.service.ts` |
| Active Hours Models | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/models/active-hours.models.ts` |
| Active Hours Chip Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/active-hours-chip/` |
| Active Hours Editor Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/active-hours-editor-dialog/` |
| Location Warning Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/location-warning/` |
| Active Hours Utilities | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/active-hours.utils.spec.ts` |
| Abstractions | `Core/Pgan.PoracleWebNet.Core.Abstractions/` |
| DataProtection Configuration Tests | `Tests/Pgan.PoracleWebNet.Tests/Configuration/DataProtectionConfigurationTests.cs` |
| Backend Tests | `Tests/Pgan.PoracleWebNet.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Scripts | `scripts/` (`setup.sh`, `dev.sh`, `docker.sh`) |
| Docker Config | `Dockerfile`, `docker-compose.yml.example` (copy to `docker-compose.yml`), `.env.example` |

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, dialogs, and utilities (including `geo.utils.spec.ts`, `user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `region-selector.component.spec.ts`, `geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`, `geofence-submissions.component.spec.ts`, `test-alert.service.spec.ts`, `active-hours.utils.spec.ts`, `active-hours-chip.component.spec.ts`, `active-hours-editor-dialog.component.spec.ts`, `location-warning.component.spec.ts`).
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and manual mapping extensions. Alarm service tests mock `IPoracleTrackingProxy` (returning `JsonElement` payloads) instead of repositories. Human/Profile/Area controller tests mock `IPoracleHumanProxy`. Key test classes: `MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`, `HumanServiceTests`, `DashboardServiceTests`, `CleaningServiceTests`, `AreaControllerTests`, `ProfileControllerTests`, `AdminControllerTests`, `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`, `SettingsControllerTests`, `PwebSettingServiceTests`, `QuickPickServiceSecurityTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `TestAlertControllerTests`, `TestAlertServiceTests`, `ActiveHoursValidationTests`, `MappingExtensionTests`, `DataProtectionConfigurationTests`.
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
