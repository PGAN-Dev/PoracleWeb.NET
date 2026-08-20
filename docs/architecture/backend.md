# Backend Patterns

## Alarm services (PoracleNG API proxy)

All alarm tracking services (`MonsterService`, `RaidService`, `EggService`, `QuestService`, `InvasionService`, `LureService`, `NestService`, `GymService`, `FortChangeService`, `MaxBattleService`) use `IPoracleTrackingProxy` to proxy CRUD operations through the PoracleNG REST API. They do **not** use repositories or direct database access.

See [PoracleNG API Proxy](poracleng-proxy.md) for the full architecture, request flow, and how to add new alarm types.

### JSON serialization

Alarm data is serialized/deserialized with `JsonNamingPolicy.SnakeCaseLower` to match PoracleNG's snake_case field names:

```csharp
private static readonly JsonSerializerOptions SnakeCaseOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};
```

### Update pattern

PoracleNG's tracking POST endpoint handles both creates and updates. When the request body includes a `uid` field, it updates the existing alarm. Services use the same `CreateAsync` proxy method for both operations.

An edit therefore sends the whole row, and the body is built by serializing the typed model — so every column PoracleWeb has no property for arrives absent and PoracleNG stores the default over what the user had. `TrackingFieldPreserver.PreserveStoredFieldsAsync` runs first on every update: it re-reads the stored row and copies across any property the submitted body lacks. Before it existed, editing an alarm on the web reset `override_location_label`, `override_areas` and `pvp_ranking_evolution` set from the bot (#730). A read failure returns the body untouched rather than failing the edit.

The merge runs *before* the collision guards, because `TrackingUpdateReconciler.CountUpdatableDifferences` only compares properties present in the submission — an unmodelled property could not tell two alarms apart, so the guard refused edits PoracleNG would have accepted. See [PoracleNG API Proxy](poracleng-proxy.md#insert-update-or-duplicate) for what the guards are mirroring.

## Repository layer (non-alarm entities)

`HumanRepository` is used only for **admin bulk operations** (`GetAllAsync`, `DeleteUserAsync`, `UpdateAsync`) that lack PoracleNG API equivalents. Single-user human reads and writes go through `IPoracleHumanProxy`. `poracle_web`-owned entities (`SiteSettingRepository`, `WebhookDelegateRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`) use their own dedicated repository classes.

!!! note "`BaseRepository` removed"
    The generic `BaseRepository<TEntity, TModel>` and all alarm repository classes have been removed. `EnsureNotNullDefaults()` is no longer needed -- PoracleNG handles NULL defaults for alarm writes, and the remaining repositories handle null normalization as needed.

## Mapping extensions

Mapping is done with static extension methods in `Core.Mappings/`. There is no AutoMapper dependency.

`AlarmMappingExtensions` covers the alarm DTOs: `To*()` builds a model from a `*Create` DTO (`create.ToMonster()`), and `ApplyUpdate()` merges a `*Update` DTO onto an existing model (`update.ApplyUpdate(existing)`).

`EntityMappingExtensions` covers `Human`, `Profile`, and the `poracle_web`-owned entities (user geofences, site settings, webhook delegates, quick picks) with `ToModel()`, `ToEntity()`, and `ApplyTo()`.

All `*Update` models use **nullable** properties so partial updates don't zero out unset fields. `ApplyUpdate` skips nulls explicitly:

```csharp
public static void ApplyUpdate(this MonsterUpdate src, Monster dest)
{
    if (src.Ping != null) dest.Ping = src.Ping;
    if (src.Distance != null) dest.Distance = src.Distance.Value;
    // ... one guarded assignment per field
}
```

## Alarm field defaults

PoracleNG's `cleanRow()` function applies field defaults on every create/update. PoracleWeb.NET no longer needs to manage alarm defaults directly. However, the frontend still sends sensible initial values to avoid confusing the user when the add dialog opens:

| Property | Frontend default | Notes |
|---|---|---|
| `max_iv` | 100 | |
| `max_cp` | 9000 | |
| `max_level` | 55 | |
| `size` | -1 | Means "any size" |
| `team` (Raid/Egg/Gym) | 4 | Means "any team" |
| `move` (Raid) | 9000 | Means "any move" |
| `evolution` (Raid) | 9000 | Means "any evolution" |

!!! info "Defaults are now enforced server-side"
    Even if the frontend sends incomplete data, PoracleNG's `cleanRow()` fills in proper defaults. This eliminates the class of bugs where missing C# model defaults caused silent filter breakage.

## Raid level service

`IRaidLevelService` / `RaidLevelService` is a singleton that serves the canonical Pokémon GO raid-type vocabulary to the frontend, mirroring the [WatWowMap masterfile](https://github.com/WatWowMap/Masterfile-Generator) without the locale-blind English strings leaking into the UI. The implementation returns a baked-in snapshot of 19 levels (1-Star through Coordinated 2) via `GET /api/masterdata/raid-levels`, with each entry exposing `{ value, category, name, namePlural }`. A `TODO` in `GetAllAsync` documents the upgrade path to a live masterfile fetch with on-disk caching under `DATA_DIR`; the wire contract will not change. The frontend `RaidLevelService` caches the response in a signal and falls back to a baked-in `KNOWN_LEVELS` constant on fetch error so the level picker always works, even offline.

PoracleNG accepts any positive integer as a raid/egg level, so the picker's `+ Add` affordance lets users alarm on levels that haven't been added to the canonical list yet. The `[Range(0, int.MaxValue)]` attribute on the alarm `Create`/`Update` DTOs ensures custom integers and the `9000` "any" sentinel pass server-side validation.

## Test alert service

`TestAlertService` lets users trigger a sample notification for any configured alarm. It uses `Task.WhenAll` to fetch the alarm (via `IPoracleTrackingProxy`) and the human record (via `IPoracleHumanProxy`) in parallel. It then constructs a realistic mock webhook payload based on the alarm's filter fields (e.g., `pokemon_id`, `raid_level`, `quest_reward`) using the user's location as the event coordinates. The payload is sent to PoracleNG's `POST /api/test` endpoint, which formats and delivers the notification. Rate-limited at 5 requests per 60s per IP via the `test-alert` policy.

## Fort change and Max Battle services

`FortChangeService` and `MaxBattleService` follow the same `IPoracleTrackingProxy` pattern as all other alarm services. They proxy CRUD operations through PoracleNG's tracking endpoints with no direct database access. Each has the standard three distance endpoints (`PUT /{uid}`, `PUT /distance`, `PUT /distance/bulk`).

## Invasion service

### GruntType case normalization

`InvasionService.CreateAsync()` and `BulkCreateAsync()` call `ToLowerInvariant()` on the `GruntType` field before saving. This matches Poracle's case-sensitive matching behavior — grunt types must be lowercase for notifications to fire correctly.

## Bulk operations

Each alarm controller has three distance endpoints:

| Endpoint | Purpose |
|---|---|
| `PUT /{uid}` | Update a single alarm (full object) |
| `PUT /distance` | Update ALL alarms' distance for the current user/profile |
| `PUT /distance/bulk` | Update distance for specific UIDs: `{ uids: number[], distance: number }` |

All three endpoints go through the PoracleNG API proxy. Bulk distance updates fetch all alarms via `GET`, modify the distance field in memory, then POST the updated alarms back. This is a workaround until PoracleNG adds dedicated bulk distance endpoints (see [enhancement requests](../poracleng-enhancement-requests.md)).

## Poracle API proxies

### IPoracleTrackingProxy (alarm tracking)

Proxies all alarm CRUD operations to PoracleNG's `/api/tracking/*` endpoints. Authenticated via `X-Poracle-Secret` header. See [PoracleNG API Proxy](poracleng-proxy.md) for full details.

- Registered as the concrete `PoracleTrackingProxy`, then decorated — what the container resolves for `IPoracleTrackingProxy` is `UserOwnedOverrideAreaProxy` wrapping it
- Used by: all alarm services, `DashboardService`, `CleaningService`, each of which therefore gets the decorated instance

```csharp
services.AddHttpClient<PoracleTrackingProxy>();
services.AddScoped<IPoracleTrackingProxy>(sp => new UserOwnedOverrideAreaProxy(
    sp.GetRequiredService<PoracleTrackingProxy>(),
    sp.GetRequiredService<IUserGeofenceRepository>(),
    sp.GetRequiredService<IUserAreaDualWriter>(),
    sp.GetRequiredService<ILogger<UserOwnedOverrideAreaProxy>>()));
```

The decorator only touches `CreateAsync`; everything else forwards untouched. See [Per-alarm areas](#per-alarm-areas).

### IPoracleHumanProxy (human/profile management)

Proxies single-user human and profile operations to PoracleNG's `/api/humans/*` and `/api/profiles/*` endpoints. Handles user reads, creation, location setting, area updates, profile switching, and profile CRUD.

- Registered via `AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>()`
- Used by: `HumanService`, `LocationController`, `AreaController`, `ProfileController`, `UserGeofenceService`
- URL-encodes user IDs with `Uri.EscapeDataString()` -- critical for webhook IDs that contain slashes

### IPoracleApiProxy (config, areas, templates)

Wraps HttpClient calls for non-tracking Poracle API operations.

- Used for: fetching config, areas/geofences, templates, sending commands
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`

### Config parsing

`PoracleConfig` is parsed from Poracle's JSON configuration. The `defaultTemplateName` field can be a number or string — deserialization handles both via `JsonElement`.

## Server capability probe

`IPoracleServerProfileService` / `PoracleServerProfileService` asks the PoracleNG instance what it is and
what it can store. PoracleWeb previously assumed 5.1.0 and never checked, so on an older server the
per-alarm scope, the PVP mega picker and the minimum-time filter wrote fields nothing stored and failed
without a word.

Two reads, answering different questions:

- `GET {Poracle:ApiAddress}/health` — the release number and PoracleNG's own capability map. It is
  unauthenticated, so the probe carries no secret and still works when the API key is wrong, which is
  itself worth knowing: "reachable but every write 401s" and "not running" look identical otherwise. The
  map is read key-by-key rather than into a fixed type, because it is upstream's and it grows.
- `SELECT version FROM schema_migrations` on `PoracleContext`, via `PoracleSchemaVersionReader`. The
  capability map covers bot and template-editor features and says nothing about alarm columns; the
  applied migration number is what answers "can this server store that filter". 5.1.0 sits at migration
  5. A missing table or a permission error reports an unknown schema, which unlocks nothing.

`PoracleServerProfile.MinimumSupported` is **5.1.0** — where `override_location_label`, `override_areas`
and `pvp_ranking_evolution` arrive. `IsBelowMinimum` is true only when the server is *known* to be older:
unreachable, unparseable, or the `0.0.0` a locally built binary reports are all unknown rather than too
old, so the banner is not shown on a guess. `Supports(capability)` defaults a missing key to false, per
PoracleNG's map contract; `HasSchema(n)` answers false on an unknown schema.

The profile is cached in `IMemoryCache` for five minutes and the HttpClient's timeout is five seconds, so
an unreachable server answers "unknown" quickly instead of stalling the admin page.
`GET /api/admin/server-profile` serves it (admin only); `?refresh=true` invalidates the cache and the
GitHub update check first.

## Areas

User areas are managed through `IPoracleHumanProxy.SetAreasAsync()`, and PoracleNG handles the dual-write
to both `humans.area` and `profiles.area` — **for admin areas**.

User-drawn geofences are the exception. PoracleWeb serves them with `userSelectable=false` to keep them
off the bot's area picker, and PoracleNG's `HandleSetAreas` silently strips any name whose fence is not
user-selectable. So every user-geofence area mutation goes through `IUserAreaDualWriter`, which writes
both tables directly in a single `SaveChangesAsync`, and `AreaController.UpdateAreas` calls
`PreserveOwnedAreasInHumanAsync` after `SetAreasAsync` to re-add what was stripped. Every such site is
tagged `HACK: trusted-set-areas` — `grep -rn "HACK: trusted-set-areas" --include="*.cs"` lists them.

Geofence polygons come from the Poracle API (via the unified feed), not the database.

### Per-alarm areas

An alarm can confine itself to named areas of its own, stored in the row's `override_areas` column. That
is the same `userSelectable` problem one layer down, and it fails harder: PoracleNG's tracking write
validates every entry against `GetAvailableAreas` and answers **400 "area not permitted"**, failing the
whole request, where `setAreas` merely strips silently.

Matching never consults `userSelectable` — `resolveOverride` hands the rule's areas to `areaOverlap`,
a name comparison against the fences the spawn fell in — so a name written straight into the column
matches exactly like a permitted one. `UserOwnedOverrideAreaProxy`, the decorator over
`IPoracleTrackingProxy`, does that:

1. `EnsureScopeIsCoherent` refuses an incoherent scope before anything is written. A row cannot carry
   both a place and a set of areas; areas cannot coexist with a radius; a place needs one. PoracleNG
   enforces the same three rules, but only on the body it is sent, and it never sees the stripped names.
2. Any of the user's own geofence names are removed from `override_areas` for the POST. A list left
   empty drops the property entirely, so PoracleNG sees no override rather than an empty one.
3. The full list is written into the row by `IUserAreaDualWriter.SetAlarmOverrideAreasAsync`, a raw
   `UPDATE` scoped by both `id` and `uid`. An empty list stores NULL, not `[]` — `parseOverrideAreas`
   reads `""` back as no override, while `[]` would be a list matching nothing.
4. `ReloadStateAsync` is called afterwards, because a direct column write is not a PoracleNG mutation
   and would otherwise wait for the periodic reload.

Writes that name no override at all skip all of this. Tagged `HACK: trusted-set-areas` like the rest.

## Location

`LocationController` uses `IPoracleHumanProxy.SetLocationAsync()` to set the user's location. No direct DB access or transactions are needed -- PoracleNG handles the write and state reload atomically.

### Saved places

The same controller owns the places an alarm can be anchored to instead of the profile pin. PoracleNG
stores them in `user_locations`, keyed by (human, label), and they are reached only through
`IPoracleHumanProxy`.

| Endpoint | Behaviour |
|---|---|
| `GET /api/location/places` | Returns `SavedPlaces { Default, Named }`. `Default` is the profile pin every alarm falls back to, null when the user has never set one. |
| `POST /api/location/places` | Saves a place, then returns the updated set. PoracleNG reports a rejected label inside a 200 because its endpoint answers per row, so the refusal is unwrapped and returned as a 400 the SPA can show against the field. |
| `DELETE /api/location/places/{label}` | 204 on success. **409** with `referencingRules` when alarms still point at the label — PoracleNG refuses rather than orphaning it, and naming the alarms is the difference between "could not delete" and knowing what to repoint. Carried as `PlaceInUseException`. |

A label is what an alarm's `override_location_label` refers to. The whole controller carries `[RequireFeatureEnabled(DisableFeatureKeys.Location)]`, so `disable_location` takes the pin, the places API, the static and distance maps and weather with it.

## Service lifetimes

| Service | Lifetime | Reason |
|---|---|---|
| Most services | **Scoped** | Per-request |
| `MasterDataService` | **Singleton** | Cached game data |
| `RaidLevelService` | **Singleton** | Stateless canonical-list provider; future live masterfile fetch will cache here |

!!! info "DashboardService uses the proxy"
    `DashboardService` calls `IPoracleTrackingProxy.GetAllTrackingAsync()` to fetch all alarm types in a single API call, then counts each type from the response. No direct DB queries.

## Profiles

- `humans.current_profile_no` (not `profile_no`) tracks the active profile
- All alarm tables reference `profile_no` to filter by active profile

### Active hours

The `Profile` model includes an `ActiveHours` (`string?`) property representing a JSON array of time-window rules stored in the `active_hours` column of the `profiles` table. `ProfileService.DeserializeProfiles()` extracts `active_hours` from the PoracleNG proxy's `JsonElement` response and maps it onto the model.

`ProfileController` includes `active_hours` in the proxy payload for Create, Update, and Duplicate
endpoints. Validation lives in `Core.Models/ActiveHoursValidator`, shared with `SummaryScheduleController`
so the two cannot drift:

- Each entry must specify `day` (1--7), `hours` (0--23), `mins` (0--59)
- Maximum 28 entries per profile (four per day across seven days)
- Returns a `400 Bad Request` with details on validation failure

It accepts `hours` and `mins` as either numbers or strings, because PoracleNG stores them inconsistently.

## Scanner service

The scanner DB (`ScannerDb` connection string) is optional. When not configured, `IScannerService` is not registered and scanner endpoints return appropriate fallback responses.

### Gym search endpoints

`ScannerController` exposes two gym endpoints backed by `ScannerService`:

| Endpoint | Purpose |
|---|---|
| `GET /api/scanner/gyms?search=term&limit=20` | Search gyms by name prefix (`term%`, index-sargable). User input is escaped for LIKE wildcards (`%`, `_`, `\`). Search length 2--100 chars; `limit` clamped to `[1, 50]`. |
| `GET /api/scanner/gyms/{id}` | Return a single gym by its ID (max 128 chars). |

Both endpoints are rate-limited under the `scanner-search` policy (60 requests/min per IP).

Both endpoints resolve the gym's area name by running point-in-polygon checks against cached Koji admin geofences (via `IKojiService.GetAdminGeofencesAsync()`). The first matching fence name is set on the result's `Area` property.

Graceful fallback: if the scanner DB is unreachable or the query fails, the search endpoint returns an empty array and the single-gym endpoint returns 404. If `IKojiService` is unavailable, area resolution is skipped (gym is returned without an area name).

### GymSearchResult model

`GymSearchResult` in `Core.Models` carries the gym data returned by both endpoints:

| Property | Type | Notes |
|---|---|---|
| `Id` | `string` | Scanner gym ID |
| `Name` | `string?` | Gym name from scanner DB |
| `Url` | `string?` | Photo thumbnail URL from scanner DB |
| `Lat` | `double` | Latitude |
| `Lon` | `double` | Longitude |
| `TeamId` | `int?` | Controlling team (0 = neutral) |
| `Area` | `string?` | Resolved at request time via point-in-polygon, not stored |

### ScannerGymEntity.Url

The `ScannerGymEntity` in the scanner context maps the `url` column from the `gym` table, providing gym photo thumbnail URLs to `GymSearchResult`.

### PointInPolygon

`IScannerService` declares a static `PointInPolygon(double lat, double lon, double[][] polygon)` method using the ray-casting algorithm. The method tests if a point lies inside a polygon (where each entry is `[lat, lon]`) and returns `false` for degenerate polygons with fewer than 3 vertices. Used by `ScannerController` to determine which Koji geofence area a gym belongs to.

## GeoMath

`Core.Services/GeoMath.cs` holds the polygon maths used outside the scanner path: `AreaSqKm` (spherical excess / Girard's theorem, R = 6371 km), `Centroid` (vertex mean), `Contains` (ray casting), and `DescribeArea` (plain-language size band). It backs the geofence review card's size, location and overlap fields.

!!! warning "Keep in sync with the frontend"
    `GeoMath` is a hand-port of the frontend's `shared/utils/geo.utils.ts`. If the two drift, the area a user sees while drawing a geofence stops matching the one an admin sees in the review thread. The tests derive their expected values from the sphere's radius (`2πR/360`) rather than from the implementation, so a port mistake fails rather than being enshrined.

## Discord notifications

`IDiscordNotificationService` opens and maintains the geofence review thread. Two mechanics are easy to get wrong:

- **The map must be uploaded, not linked.** Poracle's `GET /api/geofence/{area}/map` returns a *pregenerated tileserver-cache* URL that the cache evicts, so an embed linking it goes blank within hours. The bytes are downloaded (via a separate unauthenticated named `HttpClient`, so the bot token never reaches the tileserver) and posted as a message attachment.
- **Editing the card re-uploads the map.** Discord folds an `attachment://` attachment into the embed, so the message reports an empty `attachments` array — there is no ID to carry forward, and the embed's resolved `cdn.discordapp.com` URL is a signed link that expires. A forum post's starter message shares the thread's ID, so the opening embed is edited with `PATCH /channels/{threadId}/messages/{threadId}`.

One `BuildEmbed` produces the pending, approved and rejected cards so they cannot drift apart, and each piece degrades on its own: a failed download links the URL, a failed card rewrite still posts the verdict reply, and a Koji outage just omits the overlap line.

## Golbat API proxy

### IGolbatApiProxy (Pokemon availability)

Proxies requests to Golbat's `GET /api/pokemon/available` endpoint, which returns currently spawning species with per-species/form counts. Authentication uses the `X-Golbat-Secret` header (per-request, not on DefaultRequestHeaders).

- Registered conditionally via `AddHttpClient<IGolbatApiProxy, GolbatApiProxy>()` — only when `Golbat:ApiAddress` is configured
- Response parsing handles both flat arrays `[1,2,3]` and object arrays `[{"id":1,"form":0,"count":100}]`, deduplicating by Pokemon ID
- On error: returns empty list (never throws), logs warning

### IPokemonAvailabilityService (caching layer)

Caches Golbat availability data in `IMemoryCache` with a 5-minute absolute expiration. Maintains a `_lastKnownGood` fallback — if Golbat goes down after a successful fetch, the stale data is served rather than returning empty.

- Registered as **singleton** (only when Golbat is configured)
- Cache key: `golbat_available_pokemon`
- `PokemonAvailabilityController` uses nullable DI injection (`IPokemonAvailabilityService? = null`) — when Golbat is not configured, the endpoint returns `{ available: [], enabled: false }`

## Weather data

Weather data is served via `IScannerService` from the scanner DB (`ScannerWeatherEntity`). `ScannerService` fetches weather cells using S2 cell geometry (`S2CellHelper`) and returns `WeatherData` models with cell polygons and gameplay weather conditions. The `LocationController` exposes weather data alongside the user's location. Weather is optional -- when the scanner DB is not configured, weather endpoints return empty results.

## Rate limiting

Sensitive endpoints use **per-IP** partitioned rate limiting:

| Policy | Limit | Window | Applied to |
|---|---|---|---|
| `auth` | 30 requests | 60 seconds | Login / callback / token exchange |
| `auth-read` | 120 requests | 60 seconds | Current user, profile switch |
| `test-alert` | 5 requests | 60 seconds | Test-alert sends |
| `geojson-import` | 5 requests | 60 seconds | Admin GeoJSON import |
| `scanner-search` | 60 requests | 60 seconds | Scanner gym search / lookup |

Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.

!!! danger "Never use global rate limiting for auth"
    Global (non-partitioned) `AddFixedWindowLimiter` for auth causes cascading login failures — multiple users share one bucket.
