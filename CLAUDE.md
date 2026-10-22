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
|   +-- Core.Repositories/       SiteSettingRepository, WebhookDelegateRepository,
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
|   +-- Data.Scanner/            ScannerContext for optional scanner DB
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
- **Human/Profile management** is proxied through `IPoracleHumanProxy` for single-user operations (get, create, start/stop, set location, set areas, switch profile). Admin bulk operations (get all users, the webhook list, delete user) use PoracleNG's `GET /api/v2/humans` and `DELETE /api/v2/humans/{id}` through the same proxy.
- Both proxies are registered via `AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>()` and `AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>()`.
- Authentication uses the `X-Poracle-Secret` header on every request (from `Poracle:ApiSecret` config).
- **Snake_case JSON**: Proxy classes use `JsonNamingPolicy.SnakeCaseLower` for serialization/deserialization, matching PoracleNG's wire format.
- **`?silent=true`**: Tracking create requests append `?silent=true` to suppress Discord confirmation messages from PoracleNG.
- **`TrackingCreateResult`**: PoracleNG returns `{ newUids, alreadyPresent, updates, insert }` from creates. The `TrackingCreateResult` record captures this so services can assign the UID back to the created model.
- **HumanService is proxy-first**: All single-user ops (`GetByIdAsync`, `ExistsAsync`, `CreateAsync`, `DeleteAllAlarms`, `StartAsync`, `StopAsync`, `SetLocationAsync`) go through `IPoracleHumanProxy` with no DB fallback -- proxy errors propagate to the caller. `GetHumanAsync` (and so `GetByIdAsync`/`ExistsAsync`/`GetAreasAsync`) answers null only on a 404 and throws for any other non-2xx, the `GetAdminRolesAsync` contract: answering null for every failure made an outage read as a deleted account (the purge's "already gone" 404, `/api/auth/me` signing live users out). A caller that re-reads after a write that already landed catches the throw and falls back, as it did on null (#994). Admin bulk ops (`GetAllAsync`, `DeleteUserAsync`) go through the proxy's v2 list and delete too; `HumanRepository` no longer exists.
- **ProfileService is proxy-first**: Single-user reads (`GetByUserAsync`, `GetByUserAndProfileNoAsync`) and all CRUD go through `IPoracleHumanProxy`. No DB fallback -- proxy errors propagate.
- **PoracleNG handles**: field defaults (template, PVP, size, etc.), dedup detection, immediate state reload on every mutation, grunt_type normalization, saving the active profile's areas on a profile switch.

### Repository Layer
- Repositories remain for **non-alarm** data only: all PoracleWeb-owned tables (`SiteSettingRepository`, `WebhookDelegateRepository`, `UserGeofenceRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`), and the deprecated `PwebSettingRepository`. There is no human or profile repository: every human and profile read and write, admin bulk included, goes through `IPoracleHumanProxy` (5.4.0's `/api/v2`).
- **Removed**: `HumanRepository` and `ProfileRepository` (5.4.0), 8 alarm repository classes (MonsterRepository, RaidRepository, etc.), `BaseRepository<TEntity, TModel>`, `PoracleUnitOfWork`, `IUnitOfWork`, and all alarm repository interfaces. `PoracleContext` no longer maps the eight alarm tables at all -- the DbSets, the entity classes and their `OnModelCreating` configuration are gone, so a direct alarm write is not one `DbSet` away. Nothing writes an alarm table: per-alarm `override_areas` go through PoracleNG's trusted tracking write. `EnsureNotNullDefaults()` is no longer needed for alarm writes (PoracleNG handles NULL defaults).

### Manual Mapping Extensions
- **No AutoMapper dependency.** All mappings use static extension methods in `Core.Mappings/`.
- `AlarmMappingExtensions` provides `To*()` (e.g., `model.ToMonster()`) and `ApplyUpdate()` (e.g., `model.ApplyUpdate(existing)`) methods for all 10 alarm types. `ApplyUpdate()` replicates the null-skip semantics: only non-null source properties overwrite the destination.
- `EntityMappingExtensions` provides `ToModel()`, `ToEntity()`, and `ApplyTo()` methods for `Human`, `Profile`, `PwebSetting`, `UserGeofence`, `SiteSetting`, `WebhookDelegate`, `QuickPickDefinition`, and `QuickPickAppliedState` entity-model pairs.
- `QuickPickDefinition` mappings handle `FiltersJson` (string) to `Filters` (`Dictionary<string, object?>`) JSON deserialization with `CamelCase` naming policy. `QuickPickAppliedState` mappings handle `ExcludePokemonIdsJson`/`TrackedUidsJson` to `List<int>` deserialization.
- `UserGeofence` entity-to-model mapping intentionally skips `Polygon` (populated by service layer). Model-to-entity mapping intentionally skips `Id` (auto-generated).

### Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (uses `*Update.ApplyUpdate(existing)` extension method for null-skip merge, then sends it to PoracleNG as a uid-addressed v2 PUT)
  - `PUT /distance` -- Update ALL alarms' distance for the current user/profile (fetch-mutate-POST pattern)
  - `PUT /distance/bulk` -- Update distance for specific UIDs (fetch-mutate-POST pattern)
- Both distance endpoints **skip the alarms a radius cannot apply to** and answer
  `{ updated, skippedAreaScoped: [uid], skippedPlaceScoped: [uid] }`: an alarm limited to areas cannot take
  a radius above zero, and one measured from a saved place cannot take zero. PoracleNG refuses both, so
  writing them failed the whole selection (and on max battles, which delete before re-creating, after the
  delete). `DistanceRewrite.Build` does the split; the SPA's `distanceUpdateMessage` reports it.
- Every radius is bounded at `AlarmDistance.MaxMetres` (20,037,509 m, half the Earth's circumference) on
  every `*Create`/`*Update` DTO, `BulkDistanceRequest` and `RejectInvalidDistance`. That is the physically
  impossible, not a product limit: production holds a rule at 10,000,000 m. `pokemon_id` stays uncapped.
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

### PoracleNG Geocode Proxy

Address search and reverse lookup (`LocationController.Geocode`/`ReverseGeocode`) go through
`IPoracleApiProxy.GetGeocodeForwardAsync`/`GetGeocodeReverseAsync`, which call PoracleNG's own
`/api/geocode/forward` and `/api/geocode/reverse` -- **not** a geocoder provider URL. PoracleNG already
normalises Nominatim, Photon and Google into one shape (`displayName`, `city`, `state`, `country`,
`streetName`, `streetNumber`, `zipcode`, numeric `latitude`/`longitude` on forward; the richer `Address`
struct -- `formattedAddress`, `neighbourhood`, `county`, `suburb`, `town`, `village`, `addr`, `flag` --
on reverse), so this app never parses a provider's payload and gains every provider PoracleNG supports
for free. See #845: an earlier draft of that issue proposed a `geocoder_provider` site setting and
per-provider parsing here, which was rejected as duplicating work Poracle already does and creating a
second place to configure the geocoder that can disagree with the first.

**There is no fallback for a PoracleNG without either route.** Both shipped in 5.4.0 (jfberry/PoracleNG#224), and
the code that called the configured provider directly and spoke to it as Nominatim is deleted. A plaintext
`404 page not found` (`PoracleProblemDetails.IsProblemJson` says so, the same check the v2 proxies use to
tell a missing route from a real answer) throws `PoracleUnsupportedException("Address search"...,
"PoracleNG 5.4.0")`, which the filter reports as 409. A *problem+json* 404 from `reverse` still means
"nothing at this coordinate", including every call when `[geocoding] forward_only` is set, which disables
reverse deliberately, and must never read as "too old". `LocationService` swallows the 409, so on a
too-old server the address search returns nothing.

The SPA's `GeocodingResult`/`ReverseGeocodingResult` (`core/models/index.ts`) are flat and read PoracleNG's
field names directly (`displayName`, not `display_name`; numeric `latitude`/`longitude`, not string
`lat`/`lon`) -- there is no `address.*` nesting left over from Nominatim's shape. `location-dialog`'s
`getAddressPrimary`/`getAddressSecondary`/`getPlaceIcon` build their own grouping from the flat fields;
`getPlaceIcon` infers an icon from which fields are present, since PoracleNG's forward result carries
no OSM class/type the way Nominatim's did.

`disable_nominatim` still gates both actions and keeps its name regardless of which provider or surface
answers behind it (see "Feature Gating" above).

### Areas and Profile-Scoped Storage
- Area subscriptions are **profile-scoped**. Each profile has its own set of selected areas.
- **Two storage locations** are kept in sync by PoracleNG, and PoracleWeb.NET writes neither:
  - `profiles.area` — the authoritative per-profile storage.
  - `humans.area` — the working copy for the currently active profile.
- **Reading**: `GET /api/areas` reads from the PoracleNG human proxy (`GetAreasAsync`), which returns the active profile's areas.
- **Writing**: `PUT /api/areas` calls `IUserAreaWriter.SaveSelectionAsync`, which sends **one** trusted `POST /api/v2/humans/{id}/areas`. `trusted` lifts PoracleNG's own `userSelectable` filter, which would otherwise drop a user-drawn fence (served `userSelectable=false`), so PoracleWeb.NET does the authorising itself first. A requested name must be in the union of: names `GET /v2/humans/{id}/areas` lists as `userSelectable` for this human; the user's own drawn fences (`user_geofences.koji_name`); and names already on the active profile. Anything outside the union throws `AreaNotPermittedException` before any POST, and `AreaNotPermittedExceptionFilter` answers 400 `{ error, areas }` with the offending names. Names PoracleNG has no fence for are dropped by PoracleNG even when trusted (renamed fences, `&`-escaped names, underscore spellings; 69 names across 30 users in production on 2026-10-21). That is not an error: they come back in `rejected`, are logged at debug (never warning: a save or toggle would log one per dead name), and the save stands. Comparison keys are lowercase, trimmed, `_` equal to space (PoracleNG's `NormalizeAreaKey`).
- **Profile switch**: `SwitchProfile` calls `IPoracleHumanProxy.SwitchProfileAsync()` -- a single atomic call. PoracleNG handles saving areas to the old profile and loading areas from the new profile.
- **A new profile's areas** (`NewProfileGeography`) are written `trusted` in all three paths, but only `ProfileController.Create` filters them first, through `IUserAreaWriter.FilterAllowedAsync` -- the same union. A disallowed name is dropped with a warning, not refused, because the SPA builds that list from the current profile. Both duplicates (`ProfileController`, `ProfileOverviewController`) copy the caller's own source profile's list trusted and unfiltered, a stated exception to the union: the source may be a non-active profile, whose names the "already held on the target profile" rule would not cover, and the user already holds them. Import writes no areas. Trusted is what lets a duplicated profile keep the user's drawn fences whatever `[area_security]` is set to.
- **Toggling a fence on or off** (`AddToActiveProfileAsync` / `RemoveFromActiveProfileAsync`) reads the active profile's list and posts the changed list trusted, through the same route. These two do no authorising themselves; the caller (`UserGeofenceService`) checks ownership. A trusted whole-list write also drops held names PoracleNG has no fence for, the same names a Save already drops.
- Geofence polygons come from the Poracle API, not the database.

### Custom Geofences
- User geofences are stored in `poracle_web.user_geofences` (separate database from Poracle, see "PoracleWeb Database" below).
- **Geofences are user-scoped, area subscriptions are profile-scoped.** A geofence is created once per user and shared across all profiles. Whether a profile receives notifications for that geofence is controlled by whether its `kojiName` appears in that profile's area list (see "Areas and Profile-Scoped Storage" above).
- **Per-profile toggle**: Users can activate or deactivate a geofence for the current profile via a slide toggle in the geofence list UI, without recreating the geofence. This calls `POST /api/geofences/custom/{id}/activate` or `POST /api/geofences/custom/{id}/deactivate`, which delegate to `AddToProfileAsync` / `RemoveFromProfileAsync` in `UserGeofenceService`. Both endpoints validate ownership before modifying areas. Both target the **active** profile (the JWT's `ProfileNo` is not sent). Activate retries once after a geofence reload when PoracleNG rejects the name, then reads the profile's list back: present is success (204), absent is `GeofenceActivationFailedException` and a 409.
- **Toggle visibility**: The slide toggle is hidden for `approved` geofences — once promoted to a public area in Koji, users manage them via the standard Areas page instead.
- **Creation**: `CreateAsync` stores the geofence in `user_geofences`, reloads PoracleNG's geofences, **then** adds its `kojiName` to the **current** profile's list through `IUserAreaWriter.AddToActiveProfileAsync`. The reload must come first: trusted `setAreas` refuses a name PoracleNG hasn't loaded, and posting first left every new fence inactive (#937). If the reload or the add fails, the fence row is **kept** and the returned `UserGeofence` carries `activationFailed: true`; the SPA shows `GEOFENCES.SNACK_SAVED_NOT_ACTIVE` ("Saved, but it couldn't be switched on yet. Use the toggle to retry.") and leaves the toggle off. The geofence appears as active on the creating profile and inactive on all others. GeoJSON import goes through `CreateAsync` and carries the flag, but the import dialog doesn't surface it.
- **Deletion** (user and admin): the SPA first reads `GET /api/geofences/custom/{id}/delete-impact` (admin: `GET /api/admin/geofences/{id}/delete-impact`), which dry-runs PoracleNG's `DELETE /api/v2/humans/{id}/areas?name=&all_profiles=true&dry_run=true` and answers `{ widened: {type: count}, total }`: the alarms limited to this fence alone, which would start matching every area of the profile once the name is gone. With `total > 0` the SPA shows `GeofenceDeleteDialogComponent` ("Delete those alarms too", preselected, or "Keep them, using my profile's areas"; the admin variant names the owner); with 0 it shows the plain confirm and sends `keep`. The `DELETE` endpoints **require** `?alarms=delete|keep` and answer 400 when it is absent or anything else, so a stale pre-upgrade tab can't delete alarms the user never saw. The service order is `IUserAreaWriter.RemoveEverywhereAsync`, then Koji cleanup (caught), then the `user_geofences` row, then a geofence reload. Removal is name-based and doesn't need Koji. `RemoveEverywhereAsync(delete: true)` dry-runs, bulk-deletes the broadened alarms profile by profile with `?profile=N` (bulk delete refuses `all_profiles`, and skips uids not on that profile), retries leftovers once as `incident` (Pokéstop events are reported under `invasion` but deleted as `incident`), warns about any uid still uncovered, then removes the name from every profile. **If the area removal fails, the row is kept** and nothing else changes, so a retry completes it. `AdminDeleteAsync` no longer swallows that failure; it still absorbs `AccountGoneException` (the owner is gone from PoracleNG, so there are no areas to clean). `UserPurgeService` deletes widened alarms with the account, runs each fence in its own `TryAsync`, and removes leftover fence rows after the account delete. Delete endpoints return 503 when PoracleNG is unreachable (`PoracleUnreachableExceptionFilter`, class-level on `UserGeofenceController`, per-action on the admin delete and delete-impact).
- **Rename and approval**: `RenameAsync` and `ApproveSubmissionAsync` reload PoracleNG's geofences **before** `IUserAreaWriter.RenameEverywhereAsync` (rename-everywhere on the v2 areas route), because `to` must be loaded. User rename on a definite failure (reload failed, or PoracleNG refused with 422) restores the row's previous name, group and parent, reloads, and throws `GeofenceRenameFailedException`, which the controller answers 409 ("nothing was changed"). An ambiguous failure (a throw) isn't rolled back and answers 503. Approval tries reload plus rename twice; if both fail the approval stands and the response carries `ownerAreasRenamed: false`, which the admin SPA reports as `ADMIN.SNACK_APPROVED_AREAS_NOT_MOVED`. A rename whose cleaned old and new names match is skipped.
- **PoracleWeb is the single geofence source for PoracleJS.** The `GET /api/geofence-feed` endpoint (`[AllowAnonymous]`, intended for internal network access) serves a unified feed that merges admin geofences from Koji with user-drawn geofences from the PoracleWeb database. No custom code is needed in PoracleJS or Koji -- standard upstream versions work.
- **`POST /api/geofence-feed/refresh` drops the cached Koji half** (rate limited to 20/min per IP; `GET` on that path answers 405 with `Allow: POST`. Every unrouted `/api` path answers a JSON 404 whatever the method, rather than falling through to the SPA shell as it used to, but a path a real route matches keeps its 405 or 415: the `api/{**path}` catch-all accepts every method and content type, so it would otherwise win exactly when routing rejected the real action, and `ApiFallback` re-asks routing's question, constraints included, before answering 404), so the next feed read re-fetches. The Koji collection is cached 5 minutes; a provisioning run that writes a fence into Koji and then asks PoracleNG to reload cannot wait an unknown fraction of that, because a reload against the cached list re-reads the old one and still answers `{"status":"ok"}` -- success reported, area unsubscribable. Authenticated with `X-Poracle-Secret` against `Poracle:ApiSecret`, the only secret an internal caller already holds, and it **fails closed**: with no secret configured every request is refused, since an empty configured secret matching an empty header would make this the one anonymous write on the site. See #844.
- PoracleJS `geofence.path` config is a single URL pointing to PoracleWeb (not an array, not dual Koji+PoracleWeb sources). PoracleJS does not connect to Koji directly for geofences.
- Admin geofences are fetched from Koji via the `/geofence/poracle/{projectName}` endpoint, with group names resolved from the Koji parent chain. They are served with `displayInMatches: true` and `group` populated. Results are cached for 5 minutes (`IMemoryCache` with `TimeSpan.FromMinutes(5)` TTL). The cache is invalidated when a geofence is approved/promoted to Koji.
- User geofences are served with `displayInMatches: false` and `userSelectable: false` -- names are hidden from all DMs and are not selectable on the Poracle bot's area list.
- User geofences also carry `exemptFromAreaSecurity: true` (jfberry/PoracleNG#243, built in #254; a top-level boolean on the native fence object, absent means false). A drawn fence is in no `[area_security]` community's `allowed_areas`, so with area security on the community filter can only refuse it and the reconcile prune can only delete it. The flag lifts the community filter for that fence and nothing else: it does **not** imply `userSelectable` (writes still need `trusted`) and does not touch `strict_locations`. **Never put it on the Koji half** -- those are the community system's own areas, and an exempt public area is selectable by every user in every community. Approved fences are served from Koji, so they are unmarked by construction (`GetAllActiveAsync` excludes `approved`). A server without #243 ignores the key: verified by loading a flagged feed copy into the pre-#254 `poracle-ng-next` binary on dev-01, which loaded the flagged fence exactly like an unflagged one. Writes of drawn fences are always `trusted` and gated by PoracleWeb.NET's own name check (see "Areas and Profile-Scoped Storage"), so there is no area-security probe any more.
- Parent/region geofences from Koji are excluded from the feed (they are structural, not alerting areas).
- **Graceful degradation, both halves**: Koji unreachable serves user geofences; the `poracle_web` database unreachable serves Koji's. Only the Koji half was wrapped originally, so a database blip answered 500 and took down the one geofence source PoracleJS has. **With both down the feed answers 503, deliberately, rather than 200 and an empty list** -- PoracleJS caches the last good response, so an empty success is not a degraded answer, it is an instruction to drop every geofence every user has. The check is on whether a read *failed*, not on whether the result is empty: an instance with no Koji project and no user fences has a legitimately empty feed. PoracleJS's `.cache/` is the outer failover for PoracleWeb being down entirely.
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

### Profile Numbering and Rename (PoracleNG quirks)

Two upstream behaviours PoracleWeb.NET still has to know about. Both verified directly against PoracleNG.

**PoracleNG assigns the lowest free profile number, not `max + 1`.** With profiles 0, 1, 3 a new profile is created at **2**, so the number cannot be predicted. 5.4.0's `POST /api/v2/humans/{id}/profiles` returns it, and `AddProfileAsync` reads it and throws `PoracleRequestRefusedException` if the answer carries none. The list-create-list diff (`ProfileNumbering.ResolveCreated`) that discovered it on older servers is deleted. Predicting `max + 1` produced empty create responses and copied duplicate alarms to a `profile_no` with no profile row -- orphans that later attached themselves to whatever profile was eventually created at that number. See #407.

**PoracleNG's add ignores `area`, `latitude` and `longitude`**, so create, both duplicates and import set them afterwards through `NewProfileGeography`: `POST /api/v2/humans/{id}/areas?profile=N` and `POST .../location?profile=N`. There is no direct write behind it any more, so it behaves differently from the old fallback in three ways. Names PoracleNG rejects are logged and dropped, not fatal. Create filters the area list through `IUserAreaWriter.FilterAllowedAsync`; both duplicates send the caller's own source profile's list unfiltered; import sends none. Whatever is sent is always `trusted` (no `area_security` probe; the earlier untrusted path, #838, is gone). And a refusal of either write **rolls the new profile back**: `WriteOrRollBackAsync` deletes the profile it just created and rethrows the original error, because a profile left with the active profile's map over its own alarms is worse than no profile. A rollback that itself fails is logged and swallowed so the error the caller sees is the one that caused it. The overview duplicate copies alarms with PoracleNG's own profile copy rather than re-creating each rule (which blanked every `ping`). See #993.

**PoracleNG's v1 profile update silently ignores `name`; the v2 PATCH honours it.** Rename is `PATCH /api/v2/humans/{id}/profiles/{n}` through `UpdateProfileAsync`, and `IProfileRepository.RenameAsync`, the direct write that used to cover the v1 gap, is deleted (#406, #837). Verified that PoracleNG serves the new name on its very next read, so nothing needs invalidating. The name reaches PoracleNG and nothing trims it after, so `ProfileController.Update` trims it first and sends the stored name when the incoming one is blank -- a blank name is "not renaming". **Rename sends only the `active_hours` the caller submitted**: it used to resend the stored schedule, and a profile whose stored schedule v2 would refuse (a day or hour out of range, left behind by an older server) then could not be renamed at all. A null `active_hours` is omitted, and v2 reads an omitted one as "leave unchanged".

**`ProfileNameRules.Validate` is the one name rule**, used by create, rename, both duplicates and import: required, at most 255 characters, and no control characters, line or paragraph separators, or Unicode bidi controls (U+202E made "evil" display as "live"). It deliberately does not refuse every format character, because emoji sequences are joined with U+200D. Rename judges only a name that changes, so a legacy name cannot make an unrelated edit fail.

**On the v2 PATCH, "no schedule" is sent as `[]`, not omitted.** v2 reads an omitted `active_hours` as "leave unchanged", so omitting an explicit `""` or `{}` kept the old schedule. A JSON null still means "not part of this edit" and is omitted. On create the two are equivalent and omission is kept.

**The v2 profile bodies are translated, never forwarded.** `V2AddProfileBody` and `V2UpdateProfileBody` declare `name` and `active_hours` and nothing else, under `additionalProperties: false`, and `active_hours` is the entry array rather than the JSON string we store. The callers build one body for both surfaces (with `area`, `latitude`, `longitude` and `profile_no` in it), so `PoracleHumanProxy.V2ProfileBody` keeps the two declared fields and parses the schedule, and a schedule v2 would refuse is a `PoracleRequestRefusedException`. Forwarding the body shipped once and made every profile create, duplicate, import and rename a 422 on PoracleNG `develop`; the unit tests passed because they posted `{"name":...}`, a body no caller builds. The tests now post the controllers' real shapes and check them against the schema fixture.

**Alarm writes never send `profile_no`.** PoracleNG takes a submitted `profile_no` at face value for the pokemon type -- `profile_no: 9` creates a row on a profile that does not exist -- while scoping every read to `current_profile_no`. Since the JWT claim can be stale (see "JWT profile resync"), stamping it onto writes stranded alarms that were invisible and undeletable. `PoracleJsonHelper.SerializeToElement` strips it, so PoracleNG files each alarm under the live active profile. See #411.

### Webhook Delegation Resolves Live, From Three Sources

A webhook is a Poracle human whose id is a Discord webhook URL. A **delegate** is a non-admin allowed
to manage one, which they do by impersonating it — so "who may manage what" is an authorisation
question asked on three surfaces: the `/my-webhooks` list, `POST /api/admin/impersonate`, and
`/api/auth/me`, which decides whether the nav item renders at all.

`IUserRoleResolver.ResolveAsync` is the single answer, unioning:

1. PoracleNG's `getAdministrationRoles` → `admin.discord.webhooks` (covers `discord.webhook_admins`
   **and** guild-role delegation),
2. `poracle_web.webhook_delegates` (what the admin dialog writes),
3. admins from `Poracle:AdminIds` or Poracle's config, who short-circuit before any network call.

Cached one minute per user; a degraded answer is never cached. Writes do not wait out the minute:
granting or revoking a delegate drops the cached answer for both the delegate and the webhook
(`Invalidate`), and deleting a human drops every cached answer (`InvalidateAll`), because a delegate
PoracleJS names by the webhook's *name* sits in no table that could say who else just lost access. The
cache keys carry a generation number held in the shared `IMemoryCache`, since the resolver is scoped.

This is also what re-authorises impersonation sessions on every request (see "JWT Generation"), so a
stale entry here is a minute of access an admin has just taken away. Any new writer of a grant must
invalidate too.

**All three surfaces must resolve live, and the same union.** This has now broken four separate ways,
each time because one surface disagreed with another:

| Fix | What it broke |
|---|---|
| #564 | `/my-webhooks` loaded from the admin user list, which 403s non-admins — the only people who could see the page |
| #601 | reading the claim let a revoked delegate keep access for 24 hours |
| #626 | resolving from the local table alone refused a PoracleJS-configured delegate the nav item had just offered |
| #786 | `/api/auth/me` still read the claim, so a *new* delegate could not find a page that would have let them in |
| #797 | all three agreed on the union and disagreed on what the strings in it *identify* — see below |
| v2humans | `GetAdminRolesAsync` returned null on **any** non-2xx, so a 503 read as "administers nothing" with `Resolved:true` and got cached — #656/#667 on the one source they missed |

**A degraded source must throw, not answer.** `Resolved` only protects what actually reports failure.
The admin-roles read now lives on `IPoracleHumanProxy`: a 404 means PoracleNG has no such human and
answers null; anything else non-2xx throws so the resolver records the answer as unresolved.

**Admin status never comes from the roles body.** Two `isAdmin` branches read one and neither could
ever fire — both API versions build it from the same `adminRolesResult` (`channels`, `webhooks`,
`users`), and v2's schema is `additionalProperties:false`. They are deleted; do not re-add a promotion
path from a field upstream does not send.

**A grant names a webhook; it is not necessarily its id.** PoracleNG returns whatever key the operator
wrote in `[[discord.webhook_admins]] target`, and upstream that key is the webhook's **name** — the bot
resolves it with `LookupWebhookByName` and authorises with `CanAdminWebhook(cfg, userID, nameOverride)`.
The local delegate table stores ids. `UserRoleResolver` therefore canonicalises every grant to a
`humans.id` (id first, then name, case-insensitively) before returning it, and drops what names neither:
a grant that matches no row could never match one downstream, and carrying it drew a nav item onto an
empty page. Consumers may assume `ManagedWebhooks` holds ids. See #797.

An impersonation session resolves `managedWebhooks` live, like every other surface, because `this.UserId`
is the account the whole session is acting as — the same account `my-webhooks` and `POST impersonate`
already answer for. `IsAdmin` is the carve-out, not this. Chaining a second impersonation is refused
server-side: the SPA holds one `poracle_admin_token`, so a second hop overwrites the way back out.

The `managedWebhooks` JWT claim is now a **cold fallback only**, used when a resolve is degraded so an
outage does not strip a delegate mid-session. Nothing authorises off it. Do not reintroduce it as a
source of truth.

Two carve-outs worth keeping: an impersonation session resolves nothing (`this.UserId` is the
impersonated account, so its own delegations answer a different question — the shape of #663), and
`IsAdmin` deliberately still comes from the claim, because every admin endpoint authorises off the
claim too and resolving one live without the other puts the UI and the API into disagreement.

Deleting a human purges grants in both directions (`RemoveAllForIdAsync` matches `WebhookId` **or**
`UserId`), so a recreated webhook URL does not adopt the old delegates. See #510-#512.

User-facing documentation: `docs/features/webhooks.md`.

### Rate Limiting
- Every policy is partitioned (never global), by one of two keys in `Program.cs`:
  - `IpPartitionKey` -- the client IP. Used by the anonymous endpoints, where there is no one else to key on.
  - `UserOrIpPartitionKey` -- the `userId` claim when the request carries a token, the IP otherwise. Used by
    everything authenticated, so people behind one shared NAT or corporate proxy do not share a bucket.
- `auth` policy: 30 requests per 60s **per IP** (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s **per user**, or per IP without a token (current user, profile switch, public settings).
- `test-alert` policy: 5 requests per 60s **per user**, or per IP without a token (test alert sends).
- `mutes`, `geojson-import` and `scanner-search` are per user the same way; `geofence-feed-refresh` is per IP.
- `UseRateLimiter` runs **after** `UseAuthentication`, deliberately: registered before it, the per-user key
  could not see the claim and every "per-user" policy silently fell back to the IP (#581).
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

### Feature Gating (`disable_*` Site Settings)
The `disable_mons` / `disable_raids` / `disable_quests` / `disable_invasions` / `disable_lures` / `disable_nests` / `disable_gyms` / `disable_maxbattles` / `disable_fort_changes` site settings disable entire alarm types for everyone, including admins. Eggs share `disable_raids` (no separate `disable_eggs` exists; eggs share the raid UI in the SPA). See #236 for the original bug.

**A disabled alarm type disappears completely.** The gate is **class-level** on the ten alarm
controllers and on `SummaryScheduleController`, so every action answers 403 — reads and deletes
included — and the SPA hides the sidebar item, the dashboard card and the route.

This was briefly the other way round. #784 moved the gate onto the write actions so users could still
see and delete rules of a switched-off type; #792 reverted it. An operator disabling a type means it
should be gone, and dormant rules are harmless: they cannot fire, they are not deleted, and they come
back intact when the type is re-enabled. `DisabledAlarmTypeGatingTests` fails the build if the gate
moves back onto individual actions.

The corollary is that there is nothing read-only to build around it: no banner, no `writesDisabled`
computed, no per-control `@if`. If you find yourself adding one, the page it would live on is
unreachable.

**Any `disable_*` toggle must be enforced server-side.** A toggle wired only into the SPA (nav item, route guard) is decoration, not a gate: the endpoints stay reachable by direct API call, and client state — the `siteSettings` signal — is trivially tampered with. `disable_areas`, `disable_profiles` and `disable_location` all shipped that way and were only closed later. `FeatureGateCoverageTests` now fails the build if a key is added to `DisableFeatureKeys` without a controller enforcing it.

Non-alarm features follow the same rules, minus the tracking-type dictionary: add the constant, apply `[RequireFeatureEnabled(...)]` to the controller (or to individual actions, as `UserGeofenceController` does so its reads stay open), add a `disabledFeatureGuard` to the route in `app.routes.ts` **and** the `disableKey` to the nav item — the nav entry alone is not enough.

Deliberately **not** gated: `/api/auth/me` under `disable_profiles`, so the JWT profile resync keeps working and PoracleNG's active-hours scheduler can still move a user between profiles.

`disable_nominatim` **is** implemented (#420): it gates the two geocode actions on `LocationController`, so switching it off genuinely stops address search and reverse lookup, and the location dialog hides its address search rather than firing a request that would 403 and bounce the user to the dashboard. It is gated per-action rather than per-controller because the controller itself is already gated by `disable_location`. The key predates #845 (see "PoracleNG Geocode Proxy" below), which moved the calls themselves off Nominatim specifically onto whatever PoracleNG is configured for -- the toggle keeps its original name because renaming a site setting breaks every instance that has it set.

**The SPA must not send a request a `disable_*` switch will refuse**, even one whose caller swallows the error: the error interceptor toasts the 403 before any `catchError` runs. `LocationService` and `PlacesService` check `disable_location` (and `disable_nominatim` for the two geocode calls) themselves and answer empty, or fail without a request where the caller expects an error, so the dashboard, Areas & Places and the scope picker in every add dialog stay quiet. The Areas page hides its pin card and Places section under `disable_location`, and the dashboard's quick actions, tips and the user menu carry the same `disable_*` keys as the sidebar. See #915.

`disable_geomap` and `disable_geomap_select` were removed from the admin UI and from `SettingsMigrationService` in the same change. They are legacy PoracleJS keys describing a map picker PoracleWeb does not have, so there was nothing to wire them to and inventing a meaning would have been worse than deleting them. Any rows left in `site_settings` are harmless -- nothing reads them. `disable_userlist` was never a toggle in this UI (the migration carries `admin_disable_userlist` as a legacy key only).

**Poracle's own flags are a floor under these (#769).** `UpstreamFeatureFlagService` reads `disabledHooks` from `/api/config/poracleWeb`, maps them to `disable_*` keys via `PoracleDisabledHookMap`, and `FeatureGate` treats a type as off if **either** source disables it. Cached 5 min. It **fails open**: any fault, timeout or absent field yields an empty set, because a Poracle outage disabling every alarm type for everyone is worse than the problem being solved. `GET /api/settings/upstream-disabled` exposes the resolved keys so nav, route guards and the admin toggles agree with the API.

Two traps, both found on 5.1.0, both filed upstream as jfberry/PoracleNG#195 and both fixed in 5.2.1 (jfberry/PoracleNG#197), so both are gone from every supported server. `pokestop` was in `disabledHooks` while `DisablePokestop` had no consumer in the processor, so it maps to **nothing** — mapping it to lures/invasions/quests would disable three working types. It is gone from the array now; the empty mapping costs nothing and stays. `disable_fort_update` was enforced upstream but omitted from the array; `fort` is in it as of 5.2.1 and maps like any other hook.

There used to be a second `/api/config/values` read of `general.disable_fort_update` for servers too old to report `fort`, recognised by the absence of `availableLanguages`. It went when 5.2.1 became the minimum (see "Minimum PoracleNG Is 5.4.0"); do not bring it back for an unsupported server.

**Adding a new alarm type? Wire it through all four layers:**

1. **Constant** — add to `Core/Pgan.PoracleWebNet.Core.Models/DisableFeatureKeys.cs` (both the `const string` field and the `ByTrackingType` dictionary entry).
2. **Service-layer guard** — inject `IFeatureGate` and `await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.X)` at the top of `CreateAsync`, `BulkCreateAsync`, and `UpdateAsync`. Throws `FeatureDisabledException` when disabled. Closes service-to-service bypasses (quick-pick apply, profile duplicate/import, cleaning toggle).
3. **Controller filter** — `[RequireFeatureEnabled(DisableFeatureKeys.X)]` on the controller class. Resource filter that 403s before model binding via `IFeatureGate.IsEnabledAsync`. The global `FeatureDisabledExceptionFilter` (registered in `Program.cs`) also catches `FeatureDisabledException` from any service path and returns the same 403 — both paths share `FeatureDisabledResponse.Create(disableKey)` so the wire format can't drift.
4. **Frontend** — add a `disabledFeatureGuard('disable_X')` to the route in `app.routes.ts`, and add the matching `disableKey` to the nav-item definition in `app.ts`. The 403 interceptor automatically redirects to `/dashboard` with an `ERROR.FEATURE_DISABLED` toast when the response body has a `disableKey` field.
5. **Settings migration** — add the key to `SettingsMigrationService.BooleanKeys` and `CategoryMap` (so legacy `pweb_settings` migrations carry it over) and to the admin-settings UI in `admin-settings.component.ts`.

**Caching:** `SiteSettingService.GetByKeyAsync` is wrapped in `IMemoryCache` with a 5-min TTL and explicit invalidation on writes. Without this, gate checks would add ~10 MySQL roundtrips per dashboard load. There's a documented TOCTOU window (slow read overlapping a write can leak a stale value for up to the TTL) — acceptable for admin-rare toggles.

### Quiet Periods (PoracleNG Mutes)

A quiet period is a time-boxed `(scope, value)` mute on a human, over PoracleNG's `/api/v2/humans/{id}/mutes`.
`IPoracleMuteProxy` is the **only /api/v2 caller in the codebase**: v2 is huma-generated, so its error
envelope is `{title, status, detail}` plus an `errors[]` array for schema violations. A handler that reads
`err.error.error` -- v1's shape, and what every other proxy here does -- renders blank against it.

It is **not** *Pause Alerts*, which toggles the human's `enabled` flag: account-wide, indefinite, persisted.

Four traps, all verified by calling a live instance (5.2.1, then 5.4.0) rather than reading the source:

**The store is in memory and dies with the processor.** Nothing about a mute is persisted. The UI therefore
shows a relative countdown and never an absolute expiry -- "Quiet until 3:15pm" is a promise the server
cannot keep, and a deadline that vanishes early reads as a lie where a countdown reads as time passing.
`MuteService` refetches on window focus and every time a chip appears, and the sheet states the volatility
in one plain line. Do not cache the list across a page's lifetime.

**POST canonicalises the area name; DELETE matches the stored string exactly.** `scope=area value=aberdeen`
is stored and returned as `Aberdeen` (the fence's own casing), and `DELETE ?value=aberdeen` then 404s.
PoracleWeb.NET holds area names lowercase everywhere because Poracle's matching is case-sensitive, so the
resume path must send back the value the LIST returned, and the chip must match the area row
case-insensitively. Get this wrong and the user has a quiet period nobody can lift until it expires.

**`scope=tracking` is deliberately not offered.** Tracking uids are per-table auto-increments whose ranges
overlap heavily on live data -- gym 31-121 sits wholly inside raid 59-343 and monster 4-36480 -- and
upstream's matcher compares the uid with no type qualifier. "Quiet this one rule" would silently quiet up
to ten unrelated rules and light the chip on their cards too, since the mute list is scope+value only and
cannot say which type it meant. That needed an upstream fix, not a client workaround, and
jfberry/PoracleNG#328 §6 (5.4.0) is it: GET returns `tracking_type` on a tracking mute and DELETE requires
`?tracking_type=` for that scope. We still create none, but we list and lift the bot's, so
`Mute.TrackingType` is read from the list and sent back on DELETE (SPA `trackingType` → API → upstream
`tracking_type`) **only when the list returned one** -- the same send-back-what-the-LIST-said rule as area
casing. The gate is still the right shape, not a leftover: a mute on any other scope has no type, and
the mutes DELETE rejects unknown query parameters (`422 unknown query parameter`, verified 2026-10-20). The
SPA keys a mute by scope, type and value, since raid 60 and pokemon 60 are two rows. Creating `scope=tracking`
mutes is possible now; that is a separate decision. `scope=pokestop` is
read-only here for a duller reason: neither the lure nor the invasion model carries a fort id.

**There is no capability check**, and `MuteCapabilityService` is deleted. The mute routes arrived in 5.2.0, so every supported server (5.4.0 and later) has them. `GET /api/mutes` answers `{ mutes }`, and all three actions answer `503` with a message when PoracleNG cannot be reached (`PoracleUnreachableExceptionFilter`, also on the Location controller and all eleven alarm controllers), so the SPA toasts the failure once instead of drawing a chip that claims nothing is quiet. Do not add a `Supports("mutes")` check against `/health`: 5.2.1's map has no mutes key, and absent means false by that map's own contract.

Two smaller ones. **Nobody validates a gym or station id**: `scope=gym value=abc123` is accepted verbatim,
so only ever create those from an existing alarm's `gymId`/`stationId`, never from free text. And **mutes
are per-human, not per-profile** -- switching profile does not lift them, and the copy must not imply it does.

Writes are refused while impersonating (`IsImpersonating` on `MuteController`), because `UserId` is the
inspected account and an unguarded write silences somebody else's alerts -- the #663 shape. Reads stay open
so an admin can diagnose "why am I getting nothing".

`quiet-surface-parity.spec.ts` pins the six surfaces that carry the chip and the four list components that
must not, so a future alarm type cannot quietly miss it.

### Test Alerts
- `POST /api/test-alert/{type}/{uid}` triggers a test notification for a specific alarm. Supported types: `pokemon`, `raid`, `egg`, `quest`, `invasion`, `lure`, `nest`, `gym`.
- **Parallel data fetch**: `TestAlertService` uses `Task.WhenAll` to fetch the alarm (via `IPoracleTrackingProxy`) and the human record (via `IPoracleHumanProxy`) concurrently.
- **Mock webhook payloads**: The service builds a realistic mock webhook payload per alarm type using the alarm's filter fields (e.g., pokemon_id, raid_level, quest_reward). The user's location from the human record is used as the mock event coordinates.
- **PoracleNG test endpoint**: The built payload is sent to PoracleNG's `POST /api/test` endpoint, which formats and delivers the notification to the user via their configured webhook.
- **Rate limiting**: `test-alert` policy at 5 requests per 60s, partitioned per user (per IP only when there is no token), like `auth-read`. See "Rate Limiting".
- **Controller validation**: The `type` path parameter is validated against a hardcoded set of valid alarm types before calling the service.
- **Frontend**: `TestAlertService` (Angular, `core/services/test-alert.service.ts`) tracks per-UID cooldowns (15s) client-side and deduplicates in-flight requests. Displays success/error/cooldown feedback via snackbar. Test button appears in `mat-card-actions` on all 8 alarm card types. `/api/test-alert` is in the error interceptor's silent list, so the service's own message is the only one shown and it has to cover every status (429, 404, 501's server reason, the feature-disabled 403, anything else as `TEST_ALERT.FAILED`).

### Gym Picker
- `GymPickerComponent` is a shared autocomplete component (`shared/components/gym-picker/`) for selecting a gym by name. Used in gym, raid, and egg add/edit dialogs to populate `gym_id`.
- Displays gym photo thumbnails (from `ScannerGymEntity.Url`), name, and resolved area name in the dropdown options.
- Backed by `ScannerService` (frontend, `core/services/scanner.service.ts`) which calls two scanner API endpoints:
  - `GET /api/scanner/gyms?search=term&limit=20` -- Searches gyms by name prefix in the scanner DB. Returns `GymSearchResult[]` with `id`, `name`, `url`, `lat`, `lon`, `teamId`, `area`.
  - `GET /api/scanner/gyms/{id}` -- Looks up a single gym by ID. Used to resolve the display name for an existing `gym_id` value when opening an edit dialog.
- Area names are resolved server-side via point-in-polygon against cached Koji admin geofences. The `IScannerService.PointInPolygon()` static method uses ray-casting for hit testing.
- `ScannerGymEntity` maps the `url` column for gym photo thumbnails from the scanner DB.
- The scanner DB is optional -- if not configured, the gym picker is hidden and `gym_id` can still be entered manually.

### Localized Game Data

Pokemon names, types, form names and evolution chains come from PoracleNG, which translates them from its own i18n bundle: `GET /api/masterdata/monsters?locale={code}`, proxied through `MasterDataController.GetMonsters` and consumed by the SPA's `MasterDataService` in the same `forkJoin` as items and moves. The locale is the **display** language, so switching it re-fetches and re-emits on `ready$`; an open species picker updates in place. See #771.

- **Fallback, not failure.** An unreachable Poracle, or one whose answer carries no usable names, falls back to the cached English WatWowMap masterfile (`IMasterDataService.GetMonsterDataAsync`). Moves and items have no translated equivalent upstream and stay English.
- **Type names are identity, not display text.** `IconService` keys uicons on the English name and the filter chips track by it, so `applyMonsters` resolves the English name from the stable type id (`shared/utils/pokemon-types.ts`) and keeps the translated string beside it as a label, read via `getTypeLabel()`. Substituting the localized name blanks every type icon.
- **Untranslated keys are ignored.** If Poracle's game-data locale download failed it returns the key itself (`poke_25`); those are skipped so the English name survives rather than a raw key reaching the UI.
- The lone-base-form drop rule matches `/^normal/i` because form names are now translated ("Normale" in Italian). Do **not** simplify it to "species with exactly one form": 856 species qualify and two of them, Koraidon and Miraidon, are not base forms.

### Settings That Are Projections, Not Rows

`poracle_locale` is synthesized onto the settings response from Poracle's `general.locale`; the SPA uses it as the last display-language fallback. It is **not stored**, `SettingsController.Upsert` refuses to write it, and it is declared in `PROJECTED_KEYS` so it never reaches the admin page's "Other" catch-all as an editable box. A stored row would win over the projected value, so one accidental save would pin the language default permanently. Any future projection needs the same two halves — the write refusal is the guarantee, the declaration is cosmetics. See #780, and #560 for the same mistake with retired keys.

`poracle_alert_languages` is the second, added with the same two halves: `Upsert` refuses it and it is
in `PROJECTED_KEYS`. It carries Poracle's `availableLanguages` as CSV and governs the **alert** language
menu — which codes Poracle will accept for `humans.language`, answering 422 to anything else. Not the
display language, and nothing to do with `allowed_languages`, which is this site's own restriction on
the display menu. No row is served when Poracle restricts nothing, and absent, `null` and an empty list
all mean exactly that: a Poracle with nothing configured accepts any code, so it gets the full menu.

`hidden_areas` is a real row, but one another page owns: Admin → Areas (`PUT /api/admin/areas`) writes it
and then asks Poracle to reload the geofence feed, which is what makes a change take effect. The generic
`PUT /api/settings/hidden_areas` therefore refuses it, and the SPA lists it in `MANAGED_ELSEWHERE_KEYS` so
it stays out of "Other". The same two halves as a projection, for a different reason.

Every `PUT /api/settings/{key}` is also bounded by the `site_settings` columns -- `key` 100 characters,
`category` 50, `value_type` 20, and `value` 65,535 **bytes** of UTF-8 (TEXT) -- and answers 400 over any
of them instead of the 500 a `DbUpdateException` produced. `PUT /api/admin/areas` writes the same column and
`HiddenAreas.TryValidate` applies the same byte bound (`HiddenAreas.MaxValueBytes`, which
`SettingsController` shares): the 500-name and 200-character caps alone allow ~99 KB.

### Basemaps: A Tile Provider Refusing You Still Answers 200

`BasemapService` builds every tile layer on the site; the five components call `attach(map, …)` and
nothing else. The catalogue is `shared/utils/basemaps.ts`, the settings are `basemap_*`, and the
user-facing reference is `docs/configuration/site-settings.md#maps`.

**The failure mode that keeps recurring: the refusal is drawn into the tile.** CARTO answers 200
without a key and returns a working image with `API KEY REQUIRED` across it. OpenStreetMap answers
200 without a `Referer` and returns `403 Access blocked` as a picture. Nothing logs, no health check
notices, and the browser's network tab shows success. #842 was this, and the fix for it walked into
it a second time with OSM. **Never conclude tiles work because the status code is 200 — look at the
bytes** (a refusal tile is ~7 KB, a real one much larger) **or at the image.**

A *missing* key is detectable and falls back to `FALLBACK_BASEMAP_ID`. A *wrong* key is not: the
response is byte-identical to the no-key one. Do not add code that claims otherwise.

**`Referrer-Policy: same-origin` (#383) is why OSM needs `sendReferrer`.** The header strips the
identification OSM's usage policy requires. The fix is Leaflet's per-layer `referrerPolicy`, which
overrides the document policy for those images alone — *not* relaxing the header, which exists so a
remote image host cannot learn where a private instance lives. `sendReferrer` is per provider because
OSM is the only entry needing it; Esri and CARTO return the same bytes either way, which was checked
by fetching them rather than assumed.

**Three constants decide what "default" means and they must agree** — `DEFAULT_BASEMAP_ID` (nothing
configured), `KEYED_DEFAULT_BASEMAP_ID` (a key and nothing else, which is all the pre-#863 settings
could say), `FALLBACK_BASEMAP_ID` (the chosen provider cannot be drawn). #871 happened because
#868 changed the fallback and left the default stale: the map drew OSM while the admin page reported
a missing CARTO key on an install that had never mentioned CARTO. Change one, check the other two.

**A viewer's own choice outranks the admin setting**, stored per browser. That is the feature, but it
needs the escape: the picker's first entry is *Site default*, and the active mark follows the
viewer's choice rather than what is drawn, so an override reads as one. Without it an admin who
clicked the layers button once concludes the setting does nothing — which is exactly how it was
reported.

**`ng serve` sends none of `SecurityHeaders`.** CSP and `Referrer-Policy` come from the .NET
middleware, which only runs when the API serves the SPA. Anything depending on them cannot be
reproduced *or ruled out* against the dev server; the OSM breakage looked perfect in every local
browser check and appeared the moment it deployed. Build the image and hit the API's own port.

Four tile URLs are pinned in `basemap.service.spec.ts` against ReactMap's `config/default.json`, so
the same basemap looks the same on both sites and a catalogue edit cannot drift silently.

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
- `MasterDataService` fetches monster data per display language and refetches on a language change; it no longer fetches the masterfile from GitHub in the browser. See "Localized Game Data".
- `TestAlertService` manages per-UID cooldown tracking (15s Map-based TTL) and in-flight request deduplication to prevent duplicate API calls.
- `GymPickerComponent` is a shared autocomplete component used in gym/raid/egg dialogs for gym selection with photo thumbnails and area names.
- `ActiveHoursEditorDialogComponent` is a shared dialog for editing profile schedule rules with day/time pickers and a weekly preview grid.
- `ActiveHoursChipComponent` renders compact amber schedule pills summarizing active hours on profile cards.
- `LocationWarningComponent` displays an inline warning when a profile has active hours but missing coordinates.
- `AlertDefaultsService` (`core/services/alert-defaults.service.ts`) persists the user's preferred default delivery scope for **new** alarms -- mode (`areas`/`distance`) and default radius (km, clamped 0.1-100) -- to `localStorage` (`poracle-default-alert-mode` / `poracle-default-alert-distance-km`), mirroring the theme/accent/language pattern. All nine add-alarm dialogs and the quick-pick apply dialog seed their `distanceMode`/`distanceKm` form controls from it; the `AlertDefaultsDialogComponent` (user menu -> Alert Defaults) edits it. Client-side only -- no backend/API change; existing alarms are unaffected.

### UI Patterns
- **Alarm lists**: Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance. Test button in card actions sends a sample notification via PoracleNG.
- **Bulk operations**: Select mode toggle (checklist icon) on each alarm list, bulk toolbar with Select All, Update Distance, Delete.
- **Skeleton loading**: Animated skeleton card placeholders on Pokemon, Raids, Quests pages.
- **Staggered animations**: Grid items fade in with 30ms stagger delay.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag. Steps are keyed by id and built from the `disable_*` settings: under `disable_location` / `disable_areas` the step is absent and its endpoint is not read, completion counts only the steps shown, and the alarm step links to the first enabled alarm type.
- **Active hours pills**: Amber schedule chips on profile cards showing compressed day-range + time summaries (e.g., "Mon-Fri 8:00 AM"). Uses `ActiveHoursChipComponent` with `compressDayRange` and `formatTime12h` utilities.
- **Location warning**: Inline red warning banner (`LocationWarningComponent`) displayed on profile cards when active hours are configured but the profile's coordinates are 0,0 (which defaults to UTC in PoracleNG's scheduler).
- **Admin geofence submissions**: Three view modes — **card** (map thumbnails, grouped by region), **list** (compact table grouped by region), **table** (flat sortable table with all columns). Region groups use `mat-expansion-panel` with count badges. Sortable columns in table view (name, status, owner, region, points, created, submitted). Discord avatars displayed next to owner and reviewer names. Reviewer names resolved from `reviewedByName` (batch-loaded from humans table).

## Configuration

- **Unified `.env` file**: A single `.env` file (copied from `.env.example`) configures PoracleWeb for both Docker and standalone mode. `Program.cs` loads `.env` at startup, bridges short env var names (e.g., `JWT_SECRET`, `DISCORD_CLIENT_ID`) to .NET's `__` convention (e.g., `Jwt__Secret`, `Discord__ClientId`), and auto-composes MySQL connection strings from `DB_HOST`/`DB_PORT`/`DB_NAME`/`DB_USER`/`DB_PASSWORD` (and `WEB_DB_*` for the PoracleWeb DB). Docker Compose also reads `.env` natively; the Program.cs bridge covers the standalone (`dotnet run`) case.
- **Env var bridge** (in `Program.cs`): Maps ~18 short env var names to .NET config paths. Two static helpers at the end of Program.cs: `MapEnvVar()` and `ComposeConnectionString()`.
- **`appsettings.Development.json`** (gitignored) can still be used for overrides, but `.env` is the primary configuration mechanism.
- **Poracle API** (critical for all writes): `Poracle:ApiAddress` and `Poracle:ApiSecret` are required for the application to function, against PoracleNG **5.4.0 or newer** (see "Minimum PoracleNG Is 5.4.0"). All alarm CRUD, human/profile management, area updates, and profile switches are proxied through the PoracleNG REST API. If the API is unreachable, all alarm, human, and profile operations will fail (no DB fallback). (**Deprecated**: previously also read from the `pweb_settings` table in the Poracle DB; now stored in `poracle_web.site_settings`.)
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
   and `player team leader` -- every one of them live. Connection details are in `.env`; query with
   `docker run --rm mariadb:latest mariadb --skip-ssl -h HOST -P PORT -u USER -pPASS DB -e "SQL"`.
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
When updating alarms, the frontend sends `*Update` DTOs to `PUT /{uid}`. The controller uses `model.ApplyUpdate(existing)` (null-skip extension method) to merge only non-null fields onto the existing alarm, then `TrackingFieldPreserver` copies across any stored property the typed model lacks, and the proxy sends the row as one `PUT /api/v2/humans/{id}/tracking/{type}/{uid}`. That PUT is a full replace, so a field left out is reset to its default.

### Discord API Version for Geofence Notifications
Use `discordapp.com/api/v9` (not v10) -- v10 is not supported on the `discordapp.com` domain. The `DiscordNotificationService` HttpClient is configured with base address `https://discordapp.com/api/v9/`.

### Geofence Review Embeds (Discord)
- **Never link a Poracle static map URL into an embed.** `GET /api/geofence/{area}/map` returns a *pregenerated tileserver-cache* URL that the tile cache evicts, so the embed's image 404s within hours (Discord stores only the link, and its `images-ext-1` proxy re-fetches the dead origin). Download the bytes and upload them as a message attachment (`multipart/form-data`, `payload_json` + `files[0]`, embed image `attachment://geofence-map.png`). See #391.
- **Discord folds an `attachment://` attachment into the embed**, so the message's `attachments` array comes back **empty**. There is no attachment ID to carry forward when editing, and the embed's resolved `cdn.discordapp.com` URL is a *signed, expiring* link. Editing a card therefore re-downloads and re-uploads the map rather than trying to retain it.
- **A forum post's starter message shares the thread's ID**, so the opening embed is edited with `PATCH /channels/{threadId}/messages/{threadId}` -- no separate message ID is stored.
- The map download uses a **separate unauthenticated named HttpClient** (`DiscordNotificationService.MapImageHttpClientName`) so the bot token never reaches the tileserver.
- One `BuildEmbed` builds the pending/approved/rejected card so the states can't drift. Colour is status-bearing (amber/green/red). Every piece degrades independently: failed download links the URL, failed card rewrite still posts the verdict reply, Koji outage drops the overlap line.
- `GeoMath` (area/centroid/containment) is a **hand-port of the frontend's `shared/utils/geo.utils.ts`**. Keep the two in sync -- tests derive expected values from the sphere's radius (`2πR/360`), not from the implementation.
- Use a plain human read (`IPoracleHumanProxy.GetHumanAsync`) for name lookups, **not** `GetByIdAndProfileAsync(id, 1)` -- the latter also filters on `current_profile_no`, so it finds nobody whose active profile isn't #1 (the default is 0). The same suspect pattern still exists in the approval area-swap fallback in `UserGeofenceService`.

### Poracle Area Case Sensitivity
Poracle does **case-sensitive** area matching. Geofence names stored in `humans.area`, `profiles.area`, and the `kojiName` field in `user_geofences` must always be lowercase. The `UserGeofenceService.CreateAsync()` method enforces this with `ToLowerInvariant()`. Area updates via `IPoracleHumanProxy.SetAreasAsync()` normalize to lowercase before sending to PoracleNG.

### Koji displayInMatches Limitation
Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. To ensure user geofence names are hidden from DMs, serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

### User Geofence Persistence (PoracleNG setAreas Filter)
PoracleNG's `setAreas` (v1 and `POST /api/v2/humans/{id}/areas`) intersects the submitted list against the fences where `UserSelectable == true`, for a non-admin. `GeofenceFeedController` serves user-drawn geofences with `userSelectable=false` (to hide them from the bot's `!area` picker), so an untrusted save with a drawn fence silently drops it: v1 says nothing, v2 reports the names in `rejected` beside a 200. That is what once made saving areas or toggling a fence appear to succeed and then revert (#163, and the PR #88 regression).

The answer is the v2 `trusted` flag (jfberry/PoracleNG#230, in 5.4.0), which lifts the `userSelectable` filter, plus PoracleWeb.NET's own allowed-name check in `IUserAreaWriter` (see "Areas and Profile-Scoped Storage"). Every user-geofence area mutation -- Areas save, create, activate, deactivate, delete, admin delete, rename, approval and a new profile's areas -- goes through that writer or `NewProfileGeography`, and none touches `humans.area` or `profiles.area`. The direct-DB dual writer, the `[area_security]` probe that gated the trusted path, and the `TrustedSetAreas` capability flag are deleted; there is no fallback, and servers below 5.4.0 are not supported.

**Trusted setAreas refuses any name PoracleNG has not loaded.** A fence created in the same call is unknown to PoracleNG until its geofence list is reloaded, so `CreateAsync` reloads *before* posting the name, and rename and approval reload before the rename (#937). A rejected requested name is a failure the caller handles (`activationFailed`, 409, rollback), not something to paper over: a reload can answer ok while PoracleNG keeps its cached fence list when it can't reach the feed.

**An alarm's `override_areas` are authorised here and saved by PoracleNG.** The tracking write checks names against the fences a user may select and answers 400 "area not permitted" for a drawn fence. `PoracleTrackingProxy` (and `PoracleIncidentProxy`) therefore asks `IUserAreaPolicy` to authorise each name (selectable for the user, the user's own drawn fence, on the active profile, or already on the rule being edited) and sends the write `?trusted=true`. Nothing is written to a Poracle table, and `IAlarmOverrideAreaWriter`, `AlarmOverrideAreaWriter` and `UserOwnedOverrideAreaProxy` are gone.

### Settings Migration
On first startup after upgrade, the `SettingsMigrationStartupService` automatically migrates data from the deprecated `pweb_settings` KV store (in the Poracle DB) to structured tables in the `poracle_web` database (`site_settings`, `webhook_delegates`, `quick_pick_definitions`, `quick_pick_applied_states`). This is idempotent -- safe to run multiple times. If migration fails, the app continues with existing data and logs the error. The old `pweb_settings` table is not deleted; it remains read-only as a fallback until fully decommissioned.

### MariaDB GET_LOCK Compatibility
`MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK('__EFMigrationsLock', -1)` which returns NULL on MariaDB (infinite timeout not supported), causing `System.InvalidCastException`. The `MariaDbHistoryRepository` class overrides the lock acquisition to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

### `ExecuteDeleteAsync` Is Unusable On MariaDB
`MySql.EntityFrameworkCore` emits the aliased single-table form, ``DELETE FROM `t` AS `x` WHERE …``, and MariaDB answers 1064 — it requires the multi-table ``DELETE x FROM t AS x`` once an alias is present. `ExecuteUpdateAsync` is fine; MariaDB accepts `UPDATE t AS x SET x.c = …`. Verified against MariaDB 10.8.2.

Nothing catches this before production. It compiles, and the repository tests pass because they run on **SQLite**, whose provider emits the same alias and accepts it. The OIDC session cleanup shipped this way and had never once run (#707); `QuickPickAppliedStateRepository` hit it earlier and quietly grew a load-and-`RemoveRange` workaround.

Use raw SQL with **unquoted** identifiers (so the statement also parses on SQLite for the tests), or load and `RemoveRange` when the row count is small. `NoAliasedDeleteTests` fails the build if `.ExecuteDeleteAsync(` reappears anywhere under `Core/`, `Data/` or the API project.

### Gym ID NULL vs Empty String
The `gym_id` column in Poracle alarm tables (gym, raid, egg) is a `NOT NULL` string that defaults to `""` (empty string) meaning "any gym". PoracleNG handles the null-to-empty normalization on its side. The `GymPickerComponent` emits `null` when cleared and the gym's `id` string when selected.

### Clean Field Bitmask
The alarm `clean` field is a **3-bit bitmask** in PoracleNG, not a boolean: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary (`db.IsClean/IsEdit/IsSummary` in PoracleNG; quest summary is gated by `summary_schedules`). Use `CleanFlags` (`Core.Models/CleanFlags.cs`) and the frontend twin `shared/utils/clean-flags.ts` (`AUTO_DELETE`/`EDIT`/`SUMMARY`, `isAutoDelete/isEdit/isSummary`, `compose`, `preserve(existing, mask, changes)`) for all reads/writes so bits set elsewhere (e.g. via the bot) survive a web edit. Models cap `Clean` at `[Range(0, 7)]`. UI controls exist only where PoracleNG acts on the bit: auto-delete (all types), edit-in-place (lures, raids/eggs via RSVP `rsvpChanges`, and pokemon -- see below), and daily summary (quests). **Angular templates can't parse bitwise `&`** — gate card badges via a component method (e.g. `isAutoDelete(clean)`), not inline `clean & 1`. See #292.

**Pokemon edit-in-place is capability-gated, not unconditional (jfberry/PoracleNG#246).** Before that PR
(merged into upstream `develop` 2026-10-08, not yet in a tagged release as of this writing), the
processor accepted the pokemon `edit` bit on every surface (commands, v1, v2) but never acted on it --
storable and readable, functionally inert. #246 makes it real, with a per-deployment config gate
(`[tracking] pokemon_edit`, default `true`) and a `pokemon_edit_window_mins` (default 5, `0` = no
limit): a re-send for the same encounter inside the window edits the original DM; past it, a threaded
reply is sent instead so the clean-delete on the original is undisturbed. `MonsterController.GetCapability`
(`GET /api/monsters/capability`) answers `{ edit: bool }` from `PokemonEditCapabilityService`, which
reads `tracking.pokemon_edit` off `/api/config/values` via `IPoracleApiProxy.GetPokemonEditEnabledAsync()`
-- mirrors `SummaryCapabilityService`/quest's `pokecoins` capability exactly, 5-minute cache, degrades to
`false` on any fault or absent key. The SPA's `MonsterService.editSupported` signal gates the toggle in
both pokemon dialogs; hidden means the form control still exists (seeded from the stored bit) so a toggle
a bot set earlier round-trips unchanged even when the control itself isn't shown.

**#246 also tightens `/api/v2`: `edit: true` now 422s for gym, invasion, nest, maxbattle and quest** --
alert types whose message is never tracked for editing, so the bit was never settable there (no UI, and
the bot's `!track edit` keyword was never declared for those types either). Only pokemon, lure, raid, egg
and pokestop events (incident) can carry it going forward. `TrackingV2Translator` currently writes `edit`
unconditionally for every type but fort ("all but fort" in the shape-differences table below) -- latent,
not live-broken, since nothing has ever set bit 2 for the five rejected types, but worth tightening before
an operator upgrades PoracleNG out from under it. Verified live against a PoracleNG build carrying #246
(dev-01's "next" testbed, rebuilt 2026-10-14 to commit `4931758f`): `tracking.pokemon_edit: true` on
`/api/config/values`, a v2 pokemon PUT with `edit: true` returns 200 and round-trips `clean: 2` on the v1
read, and the identical PUT against gym returns 422.

### PoracleNG Decides Insert vs Update vs Duplicate, And Reports It Before Writing

PoracleNG diffs a submitted row against the existing ones (`DiffTracking`, `processor/internal/db/diff.go`) using `diff` struct tags:

```go
totalDiffs == 0                            -> duplicate: nothing written, "alreadyPresent"
totalDiffs == 1 && nonUpdatableDiffs == 0  -> UPDATE of that existing row
otherwise                                  -> new insert
```

`diff:"update"` fields are `clean`, `distance`, `template`, plus `slot_changes` and `battle_changes` on gyms. The consequence that kept biting: **an Add that differs from a *different* alarm by exactly one updatable field takes that alarm over.** The user got 201 and one alarm existed where there were two.

PoracleWeb.NET no longer mirrors that rule. 5.4.0 (jfberry/PoracleNG#326) classifies a create before it writes, so `PoracleTrackingProxy.CreateAsync` posts `?dry_run=true` first:

- a single row reported as an update is refused with `TrackingConflictException`: "Another alarm of this type already uses those settings. Edit or remove that one instead." (409 through the global filter);
- a single row reported unchanged writes nothing and answers 200 with uid 0;
- otherwise it commits, and if the commit reports a takeover anyway (a race), the other alarm is restored from the `previous` row in the answer and the Add is refused;
- a batch (quick-pick apply, profile import) dry-runs each row alone, because PoracleNG classifies against stored rules only, and creates just the new ones. Identical rows and would-be takeovers are skipped and counted (`TakeoversSkipped`);
- max battles get the same protection. 5.4.0 merges max battles on v2 creates, so a second alarm for the same boss that differs only by radius is now refused where it used to stack. An exact duplicate keeps "You already have an identical max battle alarm.".

Edits are one v2 PUT and PoracleNG answers 409 for one that would duplicate another rule or change exactly one updatable field of it; the proxy maps it to the same message. `PoracleIncidentProxy` runs the same protocol through `IncidentCreateProtocol`.

Deleted with the mirror: `EnsureNoMergeIntoAnotherAlarmAsync`, `ReconcileAsync`, the no-op-edit check and the max battle identical-alarm pre-check. `TrackingUpdateReconciler` keeps only `EnsureBatchDoesNotTakeOverOthersAsync` (with its `CountUpdatableDifferences`) and `EnsureBatchDoesNotCollapse`, which bulk distance runs because it still writes through v1. **Do not rebuild a client-side diff for creates or edits**: it will disagree with the server, which is what #521, #531, #553 and #561 were.

**"No override" has four spellings and they are one value.** Every dialog sends the unused half of a scope as an explicit empty (`overrideAreas: []`, `overrideLocationLabel: ""`), because null means "keep what is stored" on the write path; PoracleNG stores NULL and reads it back as `override_areas: null`. The batch guard bulk distance uses must treat them as equal, and area names compare lowercased but in order.

**Lure, invasion and Pokestop events take no refusal of their own.** Up to 5.1.0 their tables carried unique keys on `lure_id` and `(grunt_type, gender)`, which PoracleWeb.NET guarded; migration 8 dropped the keys and that code went with the old minimum. **Do not reintroduce a refusal on `lure_id` or on `(grunt_type, gender)` alone**: PoracleNG stores rules that differ only by area scope or place side by side, and a refusal would leave one uneditable (the #553 shape). `LureInvasionMergeGuardTests` holds the legitimate cases.

See #462, #463, #502, #531, #553, #561.

### `/api/v2` Writes: Creates, Edits, Deletes, And The uid Is Read Back Every Time

**Every tracking create, edit, read and delete for all eleven types goes through `/api/v2`, with no v1 fallback.** The one v1 call left is `UpsertV1Async`, used only by bulk distance (`UpdateDistanceByUserAsync`, `UpdateDistanceByUidsAsync`) and the cleaning toggle, because v2 has no bulk write (305 uid-addressed PUTs at 7.8ms would replace one 20ms POST); a later release moves them to v2 PATCH. A route that answers gin's plaintext `404 page not found` raises `PoracleUnsupportedException("saving alarms", ...)`. A row the translator can't express is refused with `AlarmValidationException("PoracleNG can't store this alarm as it is: {reason}. Nothing was changed.")`, never sent to v1 and never trimmed.

**Reads go through v2 (sub-project 3a, PoracleNG 5.4.0), translated back to the v1 row.** v2 answers `null`
for a field at its wildcard where v1 answers the sentinel, and both the C# and TypeScript models are built on
the sentinels, so `TrackingV2Translator.ToV1Row` rebuilds the v1 row and `DefaultsFor` is the null-to-default
table (one entry per nullable property of each `V2*Rule` schema; a test checks it against the 5.4.0 schema
fixture in both directions). The `*Create` initialisers are ours, but where PoracleNG's own read translation
disagrees the table follows PoracleNG: fort `include_empty` null is 1 (v2's default, not v1's), raid and
max battle `level` null is 9000 with a `pokemon_id` and 90 without (90 is "every tier"), `gym_id` and
`station_id` null stay null, fort `fort_type` is `everything`, pokemon `min_iv` is -1. Enums go back to
ints, booleans to 0/1, `clean`/`edit`/`summary` to the `CleanFlags` bitmask, `change_types` to a JSON
string. **A property the type's table doesn't know is carried through verbatim**, so a future upstream field
or a field PoracleNG's own `/openapi.json` declares that PoracleWeb.NET doesn't model (a personal-shiny flag, say) survives a read; an unknown enum value throws, and the proxy logs and skips
the one rule rather than blanking the page. The write side matches: `TryTranslate` sends such a property
back on an edit when the server's own `/openapi.json` declares it, and refuses otherwise.

`GetByUserAsync` reads `/tracking/{type}` per type (validating the type first). `GetAllTrackingAsync`
reads the v2 snapshot, `GET /tracking`. `GetAllTrackingAllProfilesAsync` uses `all_profiles=true` with
`include_descriptions=true`, and **PoracleNG only enumerates profiles that have a `profiles` row**: a human
whose active profile has none (or who has none at all) gets nothing back for it. The proxy then appends the
active profile's rules from the plain snapshot, stamped with `current_profile_no`. Reads stay
active-profile scoped, as on v1. Legacy zeros in production (`pvp_ranking_best 0` on about 12,700 rules,
`pvp_ranking_worst 0`, `size 0`) come back from v2 as null and read as the defaults 1, 4096 and -1;
matching doesn't change. v2 also drops the gender on an invasion "everything"/boss rule and the `edit` bit
on gym, quest, nest, max battle and invasion rules.

**Pokéstop events (`incident`) are folded into the invasion read, as v1 did.** v2's invasion list excludes
them, so the proxy reads the incident list too and restates each rule in v1's form (`grunt_type` from
`display_type`, gender 0). Cleaning, the dashboard counts, `InvasionService` and `HumanService`'s delete-all
all see the same rows they did on v1 (`PokestopEventConsumerTests`). Writes go the other way: an event grunt
(`kecleon`, `gold-stop`, `showcase`) saved from the invasion page, a quick-pick row or a profile import goes
to the incident routes, because v2's invasion route refuses it, and an event with a gender is refused
rather than saved without it. Pokéstop events keep a role or user mention through an edit, get the same
dry-run takeover protection (`IncidentCreateProtocol`), and their bulk delete sends the uids in the body.

**Deletes are v2 only.** Single is `DELETE /tracking/{type}/{uid}`, bulk takes a `{uids}` body, and a
422 naming `query.uid` raises `PoracleUnsupportedException` ("Deleting alarms", PoracleNG 5.4.0). v2's ownership check reads the active profile, so it 404s a uid on another
profile and its bulk form skips such a uid and still answers 200 (#889). v1 ignored the profile. Three
things keep a delete from stranding a rule: an invasion delete retries leftovers as `incident`; whatever is
still left is resolved through one all-profiles read and re-issued with `?profile=N`, grouped by profile
for the bulk form (so quick-pick Remove still works after the scheduler has switched profile); and a
rule-not-found for a uid that really is gone counts as already gone and is logged, matching v1's
idempotence. Only uids that stay unresolved are logged as lost. The single-alarm case never reaches that
from the UI: each controller's `Delete` looks the uid up on the active profile first and answers 404, so a
click on a rule the scheduler just moved out of view is refused rather than half-done.

**Invasion has no grunt-name gate any more.** `IInvasionGruntNameService` and the `PoracleV2Capabilities` check it paired with are deleted, because v2 answers 422 naming an unknown grunt and 5.4.0 carries `grunt_type`. The steel grunt was being saved as `metal`, which matched nothing on v1 and which v2 refuses; `InvasionGruntTypes` and the add dialog send `steel`, `InvasionService` saves a stored `metal` rule as `steel` the next time it's written, and a profile import rewrites it before the first write. Two things the schema does not say, both established by POSTing to a running build: `V2InvasionRule.gender` says it is "ONLY valid together with `type_id`" while `grunt_type` says it "may be combined with gender", and the latter is correct; and v2 normalises a spaced name on write (`player team leader` is stored as `player_team_leader`). `genderless` has no v2 representation (the enum is `any|male|female`), so a stored gender 3 is refused with the translator's message.

**The v2 PUT is delete-then-insert, so on 5.2.x every type rotates its uid on edit; since
jfberry/PoracleNG #303 (merged into `develop`, unreleased) it keeps the rule's uid for all eleven types.**
The engine still deletes and inserts, then runs `UPDATE ... SET uid = old`. If that reassignment fails the
PUT still answers 200, with the new uid, so the response is read every time rather than the uid being
assumed either way. A returned uid equal to the addressed one means no remap: `TrackingV2Replacement`,
`MonsterService` and `TrackedUidRemapper` all treat it that way, and tests
pin it. Quick-pick applied
state is the thing that actually breaks when a uid does move and nobody follows it (#403), so every service
hands `ITrackedUidRemapper` to `TrackingV2Replacement.ApplyAsync`, the shared v2 branch each
`UpdateAsync` takes (it never returns null). Callers pass PoracleNG's type name and applied state
stores the quick pick's, and those differ for exactly one type: `pokemon` against `monster`. The remapper
translates it. Before it did, no Pokemon pick was ever remapped, and removing one after an edit left the
alarm behind (present in v2.18.0, found by driving the API against 5.2.1).

**Max battle edits are one PUT.** v1 could only delete and recreate, which left a window where the alarm existed nowhere; that path survives only in bulk distance. The v1 repair work (`ReconcileAsync`'s clean-up of the duplicate a v1 create leaves behind) went with the reconciler.

**One field table per type in `TrackingV2Translator`, never a shared one.** `V2PokemonRule` declares 28
integer filters and `V2FortRule` declares one; every rule sets `additionalProperties: false`, so a leaked
field is a 422.
`TrackingV2TypeTranslationTests` asserts each table against the schema's property list in both directions,
so a field the table leaks and a field it misses both fail the build.

Shape differences, all handled once at the wire:

| v1 | v2 | Types |
|---|---|---|
| `clean` 3-bit mask | `clean` / `edit` / `summary` booleans | all but fort |
| ^ with jfberry/PoracleNG#246 (unreleased): `edit: true` 422s | n/a | gym, invasion, nest, maxbattle, quest -- see "Clean Field Bitmask" above |
| `gender` 0-3 | `any` / `male` / `female` / `genderless` | pokemon |
| `team` 0-4 | `harmony` / `mystic` / `valor` / `instinct` / `any` | raid, egg, gym |
| `rsvp_changes` 0-2 | `none` / `rsvp` / `rsvp_only` | raid, egg |
| 0/1 columns | real booleans | `exclusive`, `slot_changes`, `battle_changes`, `gmax`, `shiny`, `include_empty` |
| `change_types` JSON string | array | fort |
| `pvp_ranking_league` any int | enum of `{0, 500, 1500, 2500}` | pokemon |

Plus `uid`, `id`, `profile_no` and `description`, which v2 has no place for and refuses outright; a target profile goes in `?profile=N`, and only profile import uses it (`CreateOnProfileAsync`), so it writes to the target profile without switching the active one. `ping` is a real field on all eleven types since jfberry/PoracleNG#249 (5.4.0), so `TrackingV2Translator` always carries it and a role or user mention survives an edit. The refusal that used to stop an edit falling back to v1 (which blanks the mention) is deleted with the fallback.

**The frontend exposes `ping` as a "Mention" field, edit dialogs only.** The field is shown when
`isWebhook` is true (the dialog's existing `AuthService.isImpersonating()` snapshot) and for nothing else:
`GET /api/config` no longer reports `PingSupported`. A personal DM alarm has nobody else to notify, so the field would be
presentation-only noise there. It is
deliberately absent from every add dialog: a single Add has no stored mention to protect, and quick-pick apply and profile import create from a template. `alarm-dialog-parity.spec.ts` -- the test that catches a control shipped in
one dialog of a pair and not the other -- carries an explicit `EDIT_ONLY` exception for `ping` on every
type it was added to, with this reasoning, rather than being silently exempted.

The v1 shape stays the single internal currency (`TrackingFieldPreserver`, `BulkUidRemap` and `QuickPickService` all build and compare it), and the translator is the only exit onto v2, which is what stops a v1-shaped row reaching a v2 body.

**Defaults are sent as absent.** A v2 rule stores nothing for a field at its wildcard, so the translator omits a property whose value equals the type's read default unless the schema's `Required` list names it. (This replaces "sentinels are sent verbatim", which was right while v1 diffed on what was stored.) A boss-specific raid or max battle (stored `level` 9000) is sent without `level`, which is why boss edits no longer depend on the older API: v2's `level` maximum is 90.

Three per-type traps, all verified live and each a 422 if ignored:

- **egg `level` is required with minimum 1**, and `Egg.Level` is a plain int defaulting to 0. Profile
  import, quick-pick apply and the cleaning fetch-mutate-POST all build eggs without one; the
  translator refuses those on a create or edit, and cleaning (v1) is unaffected.
- **fort has no `clean`, `edit` or `summary`** — no v2 field and no column. A shared clean helper applied
  blindly is a 422.
- **fort `include_empty` defaults to TRUE on v2 and FALSE on v1.** A PUT that omitted it flipped a stored
  0 to 1 and the alert text gained "including empty changes". The translator states it explicitly and
  refuses a fort row that carries none.

**The translator never changes what PoracleNG will accept, and never trims quietly.** A property it does not know, an enum outside
its range, an egg without a level: it refuses the row with the message above. Dropping the field would be #730 again.

**A v2 PUT is a full replace** — omitting `min_iv` wipes a stored 90, verified live — so
`TrackingFieldPreserver` matters more here than it did on v1, not less. When a rule names `override_areas`, the trusted write goes to the uid PoracleNG just reported, not the one addressed.

Errors are RFC 9457 problem+json at **422**, not 400, in two shapes: a schema failure carries `errors[]`
whose `location` is `body.x` on a PUT and `body[0].x` on a POST, and a semantic refusal carries only
`detail`. `PoracleProblemDetails` reads both, plus v1's `{"message":...}`.

Gating: **Version** — `PoracleServerProfile.IsSupported` (reachable and not known to be older than 5.4.0) — is informational now, since nothing falls back. **What the server publishes about itself** answers the little that is left, through `IPoracleV2SchemaService` reading the target instance's own `/openapi.json`: the `trusted` flag on `setAreas`, and which properties a rule declares (so an unmodelled one can be sent back). It is the schema-document sibling of `PoracleServerProfile`: one cached probe, typed questions, everything false when the document cannot be read. Neither is the `/health` capability map, which says nothing about v2. There is no setting to pin the surface: `Poracle:TrackingApiVersion` (`PORACLE_TRACKING_API_VERSION`) is removed and a leftover value is ignored, and the per-type "v2 route absent" flag is gone with the fallback it served. `PoracleServerProfileService.GetAsync` is single-flighted behind a static gate, because it is
registered transient, a cold cache costs a `/health` GET, and a dashboard load sends several
version-gated writes at once.

### Keep the PoracleNG Checkout Pinned To What Prod Runs

`E:/PGAN/pogogit/PoracleNG` drifts. On 2026-08-08 it was four months behind prod, and its `DiffTracking` lacked the `totalDiffs == 1` clause entirely — reading it produced three wrong fixes in one day.

PoracleNG has no version endpoint (`/health` only). To establish what prod runs: `ss -lntp | grep 3030` for the pid, `/proc/<pid>/exe` for the binary, and `git -C /source/PoracleNG rev-parse HEAD`; compare the binary mtime against the commit date. Then check out that commit locally.

**Even then, verify against the live instance.** POST directly to `/api/tracking/<type>/<id>?silent=true` with the `X-Poracle-Secret` header and read the row back. Source and running binary have disagreed before, and a unit test cannot tell you which is right.

### Validation Attributes Live On The `*Create` DTOs, Not The Domain Models

`Monster`, `Raid`, `Gym` and friends carry **no** `[Range]` or `[StringLength]` attributes; `MonsterCreate` and its siblings do. Anything that validates an alarm outside the normal model-binding path must bind or deserialize into the `*Create` DTO, or it will validate nothing and pass silently.

This has cost three separate fixes: profile import (#548), quick-pick apply (#565), and the quick-pick id length check that bounded the name but not the id generated from it (#555). All three passed their unit tests while doing nothing, because the tests asserted the code ran rather than that it rejected anything.
### Monster Filter Defaults Are Ours, Not PoracleNG's

This section used to say the `*Create` defaults "are no longer critical -- PoracleNG is the authoritative
source", and that belief wrote **15,383 rows with `pvp_ranking_best = 0`** into production. 0 is not a
rank; ranks are 1-based.

`cleanRow` fills a field that is **absent**. A non-nullable `int` on a `*Create` model is never absent --
it serialises as whatever it defaults to, and v1's `flexInt` passes an explicitly sent value straight
through. So every `*Create` default is sent on every write, and every one of them is authoritative.
`PvpRankingBest` had no initializer where `PvpRankingWorst = 4096` sat beside it; that asymmetry is the
whole bug (#895, jfberry/PoracleNG#227).

Three rules follow:

- **A bound's no-filter value is the column's default, not zero.** 0 is right for `min_iv`, `min_cp` and
  the other floors. It is wrong for `pvp_ranking_best` (1), `size` and `rarity` (-1), `costume` (9000),
  `max_rarity` (6), `max_size` (5). When adding a field, ask what the column stores for "no filter".
- **Check the sibling.** These come in pairs and the bug was one half of a pair being defaulted. The same
  drift hit `QuickPickService.BuildMonster`, which capped every applied pick at `MaxLevel = 40` -- the
  game's 2020 ceiling -- under a comment claiming it matched the add dialog's 55. 4,088 production rows.
- **Do not tighten the `[Range]` to match the default.** `PvpRankingBest` still admits 0 because 15,383
  rules hold one and have to stay editable; refusing the value would fail an edit on a rule the user did
  not break. On v1 an edit did not repair it: the preserver re-read the stored 0 and sent it back (verified on 5.1.0, 5.2.1 and 5.3.0).
  The preserver now reads through v2, where such a row comes back as the wildcard (PoracleNG #230, in 5.4.0), so it reads as 1 and an edit repairs it.

### Minimum PoracleNG Is 5.4.0

`PoracleServerProfile.MinimumSupported` is 5.4.0, since 2026-10-21 (#996). jfberry/PoracleNG#328 answered
every request this project had open upstream, and "no PoracleNG database access" is only reachable by
deleting the fallbacks for older servers rather than keeping them beside the v2 paths. The first raise
(5.1.0 to 5.2.1, 2026-10-20) deleted the lure/invasion natural-key refusals and delete-and-recreate edit
(`INaturalKeyCapabilityService`, `NaturalKeyTrackingUpdate`), `PoracleSchemaVersionReader` (the last read
of PoracleNG's `schema_migrations` and `information_schema`), the separate `general.disable_fort_update`
read, and every per-feature `>= 5.2.0` compare. The second deleted, for humans, profiles, areas listing,
places, mutes and geocoding: `HumanRepository` and `ProfileRepository`, `ProfileNumbering`'s list-and-diff,
the v1 branch of every single-human call and its per-route "v2 absent" flags,
`MuteCapabilityService`, `PlaceUpdateCapabilityService`, `QuestPokecoinCapabilityService` and
`CostumeCapabilityService` (with `GET /api/quests/capability` and `GET /api/settings/costume-capability`
and the SPA's `canEdit`/`pingSupported`/capability conditionals), `ProbePokestopEventsAsync`, the legacy
geocoder call, `Poracle:TrackingApiVersion`, and every `PoracleV2Capabilities` flag except three.
`InvasionGruntType` stays until bulk distance and cleaning move to v2 (`TrustedSetAreas` went with the area writes); `Read` also
survives, set but never read. What stayed
is anything still needed by a later sub-project (the v1 create bulk distance and the cleaning toggle use,
`pweb_settings`, `PoracleContext`) or covering a newer build or fork.

**One rule decides version-gated features: `PoracleServerProfile.IsSupported`** — PoracleNG answered and
is not known to be older than the minimum. It now decides little beyond whether the v2 tracking surface is
tried, because nothing else is optional. An unparseable version, or the `0.0.0` a locally built binary
reports, counts as current; an unreachable server does not. Anything newer than 5.4.0 is gated on the
server's own `/openapi.json` (`IPoracleV2SchemaService`), never on a version compare.

**A route a too-old server lacks is an error, not a fallback.** A plaintext `404 page not found` from a
v2 human, profile, area, place or geocode route throws `PoracleUnsupportedException(feature,
"PoracleNG 5.4.0")` (409 through `PoracleUnsupportedExceptionFilter`). A problem+json 404 keeps its
meaning: not found. Webhook ids in those paths go through `Uri.EscapeDataString`. When PoracleNG cannot be
reached at all, `PoracleUnreachableExceptionFilter` answers 503 on the Mute and Location
controllers and on all eleven alarm controllers (Pokemon, raid, egg, quest, invasion, Pokestop event, lure, nest,
gym, max battle, fort change), where a failed read used to look like an empty list.

**Older servers fail open, loudly.** `PoracleCompatibilityStartupService` logs an error at startup naming
what breaks, and the admin Versions card shows `belowMinimum`; the app keeps running, because refusing to
start would take the whole site down over something an operator fixes in minutes. Raising the floor again
follows the same rule: delete what only the old floor needed, keep what covers newer builds and forks.

### PoracleNG API Availability
The PoracleNG REST API (`Poracle:ApiAddress`) must be running and reachable for all alarm, human, profile, and area operations. If the API is down: alarm CRUD, human lookups, profile reads/writes, location updates, area updates, and profile switches all fail with no DB fallback. Only the deprecated `pweb_settings` table is still read from the Poracle database directly, for the one-time settings migration. Monitor PoracleNG uptime as a hard dependency.

### PoracleNG Response Wrappers
PoracleNG wraps its API responses in container objects. Human data is returned as `{"human": {...}}` and profile lists as `{"profile": [...]}`. The proxy classes (`PoracleHumanProxy`) must extract the inner object/array before deserializing to model types. Failing to unwrap causes deserialization to return null/empty results.

### uid:0 in Create Requests
When creating new alarms, the model's `uid` defaults to `0`. A v2 rule body has no place for `uid`, `id`, `profile_no` or `description`, so the translator drops them and a create can't be mistaken for an update. The v1 create that bulk distance and cleaning still use (`UpsertV1Async`) keeps `PoracleJsonHelper.StripUidZero()` for the same reason.

### PoracleNG active_hours String Types
PoracleNG stores `hours` and `mins` fields in `active_hours` JSON entries inconsistently -- sometimes as numbers, sometimes as strings (e.g., `"hours": "8"` instead of `"hours": 8`). The frontend `parseActiveHours` utility applies `Number()` coercion when deserializing to ensure consistent numeric types. The backend `ValidateActiveHours` method in `ProfileController` accepts both `JsonValueKind.Number` and `JsonValueKind.String` (parsing strings via `int.TryParse`) for the same reason.

### Webhook ID URL Encoding
Webhook IDs in Poracle are URLs (e.g., `http://host:port/path`). When constructing proxy API paths that include a webhook ID as a path segment, the ID must be encoded with `Uri.EscapeDataString()` to escape slashes and other special characters. Both `PoracleTrackingProxy` and `PoracleHumanProxy` apply this encoding.

### JWT Profile Desync
PoracleNG can change `current_profile_no` outside of PoracleWeb — the active-hours scheduler switches profiles on a cron, and bot commands like `!profile` update the DB directly. When this happens the JWT's `profileNo` claim goes stale, causing all alarm reads and writes to target the wrong profile. The `/api/auth/me` endpoint detects the mismatch by comparing the JWT claim against the live `current_profile_no` from `IHumanService` and returns a refreshed token (via the `Token` property on `UserInfo`) when they diverge. Frontend callers (`AuthService.loadCurrentUser`) must always check for and store the returned token.

### A Disabled Account's First 401 Is The One That Reaches Nobody

`AuthController.Me()` answers 401 for an admin-disabled account (`human.AdminDisable == 1`, not
impersonating -- see "Quiet Periods" for the unrelated per-area mute concept, and #706 for why
impersonation is exempt). That 401 lands mid-login: the Discord/OIDC/Telegram callback already minted a
token before `/api/auth/me` is ever called, so this is the first request to notice. `/api/auth/me` is in
`errorInterceptor`'s `SILENT_URL_PATTERNS` and `isAuthCallbackRoute()` is still true at that point in the
flow, so neither a toast nor the session-ending path fires -- the token survives, and
`AuthService.handleTokenFromCallback` would otherwise press on to `/dashboard` regardless of whether
`loadCurrentUser()` actually got a user back. The dashboard's own requests then 401 on that same dead
token, *outside* the callback route this time, and that is what the member actually sees: a generic
"session expired" toast, with no account anywhere explaining why. Every surface that does explain it
(the disabled banner, the Help FAQ, both from #910) sits behind the sign-in this 401 is refusing. See
#911.

The fix is a distinct machine-readable `code: "account_disabled"` on that one 401 body (not the deleted-
account 401 a row away, which stays prose-only and unhandled by any of this), carrying `supportUrl`
alongside it. `support_url` is otherwise read by signed-in callers only
(`SettingsController.UserVisibleKeys`) -- the one caller who needs it here is not signed in by the time
it matters, so the value travels inside the 401 body rather than through the normal settings fetch.
`AuthService` stores it in a private signal (`_disabledAccountInfo`) the instant `loadCurrentUser()` sees
the code, and `handleTokenFromCallback` reads it right after awaiting that call: if set, it clears the
session outright (the token is useless regardless) and sends the browser to `/login#error=account_disabled
&support_url=...` instead of `/dashboard`. This reuses the exact mechanism `LoginComponent` already has
for every other pre-token-issuance auth failure (`missing_required_role`, `not_in_guild`, etc.), just fed
by a failure that happens after a token exists rather than before one is issued.

**That redirect is a real `window.location.href` assignment, not `router.navigate({ fragment })` --
verified live in a real browser, not just jsdom.** Every existing `/login#error=...` is built by the
backend (`AuthController`'s `Redirect($"{frontendUrl}/login#error=...")`); this is the one place a
redirect like it is built client-side, and the first thing tried was the in-app Router's `fragment`
option. The Router's own fragment serialization re-escapes a literal `%`, so an already-percent-encoded
`support_url` (`...%3A%2F%2F...`, one `URLSearchParams.toString()` pass) came back out of
`router.navigate` double-encoded (`...%253A%252F%252F...`), and the link `LoginComponent` rendered
pointed at a broken, still-percent-encoded string instead of the real URL. Every unit test for this
passed regardless, because they mock `Router.navigate` and only assert what string it was called with --
none of them exercise the Router's actual URL-building. Caught by driving the real `CallbackComponent` →
`AuthService` → `Router` → `LoginComponent` chain in a live `ng serve` instance with Playwright,
intercepting `/api/auth/me` rather than mocking any application code. The one-encode/one-decode contract
(`URLSearchParams.toString()` written, `new URLSearchParams(location.hash)` read) now lives in its own
pure function, `disabledAccountFragment`, exported solely so it can be pinned by a fast unit test without
going near `window.location` or the Router at all.

`account_disabled` is deliberately not one of `LoginComponent`'s `errorKeys` map entries: that map renders
a one-line translated string, and this needs the same richer markup the dashboard banner already has
(`BANNER.DISABLED_ACCOUNT`'s bold "why", plus a conditional `BANNER.DISABLED_SUPPORT` link) -- both
already translated in all eleven locales as part of #910, reused verbatim rather than duplicated under a
new key.

### JWT Generation (IJwtService)
JWT token generation is centralized in `IJwtService` / `JwtService` (singleton). Three methods: `GenerateToken(UserInfo)` for fresh tokens, `GenerateImpersonationToken(UserInfo, impersonatedBy)` for admin impersonation, and `GenerateTokenWithReplacedProfile(ClaimsPrincipal, profileNo)` for profile switches. The latter filters out registered JWT claims (`exp`, `nbf`, `iat`, `iss`, `aud`) before copying to prevent stale claim duplication. All controllers (`AuthController`, `ProfileController`, `ProfileOverviewController`, `AdminController`) use this service — no inline JWT generation.

**A re-issue never moves `exp`, not even by a second.** `GenerateTokenWithReplacedProfile` backs four paths -- profile switch, `/api/auth/me` resync, overview duplicate and overview import -- and writes the original expiry instant back. It used to round the remaining lifetime up to whole minutes and count it from now, with a one-minute floor, so each re-issue pushed the expiry later and one a minute renewed a session forever; inside JwtBearer's five-minute clock skew the floor revived an expired token outright. That is how a revoked delegate kept an impersonation token alive. The only path that extends an ordinary session is OIDC refresh (`POST /api/auth/oidc/refresh`), which is intended: it spends a provider refresh token and re-validates the user live every time.

**An impersonation token is re-authorised on every request.** `ImpersonationAuthority`, hooked into JwtBearer's `OnTokenValidated`, asks `IUserRoleResolver` whether `impersonatedBy` is still an admin or still delegated the impersonated webhook, and fails authentication if not. The SPA answers that 401 by restoring the impersonator's own token. **The error interceptor judges each 401 by the bearer that request carried**, not by what is in storage when it lands: a page load sends about nine requests, so a revoked session comes back as a burst, and judging them against storage let the second 401 find the stash already spent and sign out the person just returned to their own account (#922). A 401 for a token that is no longer current, or for a request sent with none, is dropped silently. The refresh interceptor retries below the error interceptor, so it records the bearer a retry used (`TokenStoreService.noteRetriedWith`). Ending a session drops the tokens at once and clears the user only after the login navigation settles (`endSession`), the same order `logout()` uses, because clearing the user first swaps the shell's layout and rebuilds the page without a token. **An unresolved answer keeps the session** (fails open), for the #656 reason: one PoracleNG or `poracle_web` blip must not end every impersonation on the site. Because the resolver never caches an unresolved answer, an outage made every impersonated request re-ask every source and wait out its timeouts: measured with PoracleNG's address black-holed, ~200 s per request against 0.01 s for an ordinary session (a refused connection costs nothing). `ImpersonationRoleProbe`, a singleton used by this check alone, bounds it: a resolve gets two seconds, runs in its own DI scope so one that overruns cannot race the request's `DbContext`, is shared by concurrent requests, and once an answer comes back unresolved or late the impersonator is not re-asked for fifteen seconds, a pause that ends the moment a late resolve answers. Revocation therefore lands within fifteen seconds of the sources recovering, usually on the next request. The resolver's own cache is untouched, so its other consumers still never see a cached degraded answer.

**A Pokestop event sent to `/api/invasions` is refused where the events page exists.** Those rows live in the invasion table but the invasion list hides them, so the create answered 201 with a Location that 404s, or 200 with uid 0 beside an event rule the invasion guard (which reads the filtered list) could not see. The controller refuses with a pointer to Pokestop Events; service paths (quick picks, import, duplicate) still write them, since the rows are real and listed there.

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
| IUpstreamFeatureFlagService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUpstreamFeatureFlagService.cs` |
| UpstreamFeatureFlagService | `Core/Pgan.PoracleWebNet.Core.Services/UpstreamFeatureFlagService.cs` |
| PoracleDisabledHookMap | `Core/Pgan.PoracleWebNet.Core.Models/PoracleDisabledHookMap.cs` |
| Pokemon type id-to-name table | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/pokemon-types.ts` |
| Basemap catalogue | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/basemaps.ts` |
| BasemapService | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/basemap.service.ts` |
| IPoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs` |
| IPoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs` |
| PoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs` |
| PoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs` |
| PoracleJsonHelper | `Core/Pgan.PoracleWebNet.Core.Services/PoracleJsonHelper.cs` |
| IPoracleV2SchemaService (what the server's own /openapi.json declares) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleV2SchemaService.cs` |
| PoracleV2SchemaService | `Core/Pgan.PoracleWebNet.Core.Services/PoracleV2SchemaService.cs` |
| PoracleV2Capabilities | `Core/Pgan.PoracleWebNet.Core.Models/PoracleV2Capabilities.cs` |
| IInvasionGruntNameService (grunt names v2 will accept) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IInvasionGruntNameService.cs` |
| InvasionGruntNameService | `Core/Pgan.PoracleWebNet.Core.Services/InvasionGruntNameService.cs` |
| TrackingV2Translator (v1 row -> /api/v2 body) | `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Translator.cs` |
| TrackingV2Replacement (the v2 branch each UpdateAsync takes) | `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Replacement.cs` |
| PoracleProblemDetails (RFC 9457 + v1 errors) | `Core/Pgan.PoracleWebNet.Core.Services/PoracleProblemDetails.cs` |
| Repositories (non-alarm) | `Core/Pgan.PoracleWebNet.Core.Repositories/` |
| SiteSettingRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/SiteSettingRepository.cs` |
| WebhookDelegateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/WebhookDelegateRepository.cs` |
| QuickPickDefinitionRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickDefinitionRepository.cs` |
| QuickPickAppliedStateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickAppliedStateRepository.cs` |
| IUserAreaWriter | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaWriter.cs` |
| IUserAreaPolicy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaPolicy.cs` |
| UserAreaWriter | `Core/Pgan.PoracleWebNet.Core.Services/UserAreaWriter.cs` |
| UserAreaPolicy | `Core/Pgan.PoracleWebNet.Core.Services/UserAreaPolicy.cs` |
| OverrideScope | `Core/Pgan.PoracleWebNet.Core.Services/OverrideScope.cs` |
| Service Layer | `Core/Pgan.PoracleWebNet.Core.Services/` |
| UserGeofenceService | `Core/Pgan.PoracleWebNet.Core.Services/UserGeofenceService.cs` |
| SiteSettingService | `Core/Pgan.PoracleWebNet.Core.Services/SiteSettingService.cs` |
| WebhookDelegateService | `Core/Pgan.PoracleWebNet.Core.Services/WebhookDelegateService.cs` |
| QuickPickService | `Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs` |
| PwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Services/PwebSettingService.cs` |
| KojiService | `Core/Pgan.PoracleWebNet.Core.Services/KojiService.cs` |
| DiscordNotificationService | `Core/Pgan.PoracleWebNet.Core.Services/DiscordNotificationService.cs` |
| GeoMath (area/centroid/containment) | `Core/Pgan.PoracleWebNet.Core.Services/GeoMath.cs` |
| GeofenceSubmissionPost Model | `Core/Pgan.PoracleWebNet.Core.Models/GeofenceSubmissionPost.cs` |
| IPwebSettingService (deprecated) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPwebSettingService.cs` |
| IScannerService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IScannerService.cs` |
| ScannerService | `Core/Pgan.PoracleWebNet.Core.Services/ScannerService.cs` |
| GymSearchResult Model | `Core/Pgan.PoracleWebNet.Core.Models/GymSearchResult.cs` |
| Test Alert Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/TestAlertController.cs` |
| ITestAlertService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/ITestAlertService.cs` |
| Mute Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/MuteController.cs` |
| IPoracleMuteProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleMuteProxy.cs` |
| PoracleMuteProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleMuteProxy.cs` |
| Mute / MuteScopes Models | `Core/Pgan.PoracleWebNet.Core.Models/Mute.cs`, `MuteScopes.cs` |
| Mute Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/mute.service.ts` |
| Quiet Chip / Sheet / List Sheet | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/quiet-chip/`, `quiet-sheet/`, `quiet-list-sheet/` |
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
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and manual mapping extensions. Alarm service tests mock `IPoracleTrackingProxy` (returning `JsonElement` payloads) instead of repositories. Human/Profile/Area controller tests mock `IPoracleHumanProxy`. Key test classes: `MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`, `HumanServiceTests`, `DashboardServiceTests`, `CleaningServiceTests`, `AreaControllerTests`, `ProfileControllerTests`, `AdminControllerTests`, `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`, `SettingsControllerTests`, `PwebSettingServiceTests`, `QuickPickServiceSecurityTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `TestAlertControllerTests`, `TestAlertServiceTests`, `ActiveHoursValidationTests`, `MappingExtensionTests`, `DataProtectionConfigurationTests`, `DiscordNotificationServiceTests`, `GeoMathTests`.
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
