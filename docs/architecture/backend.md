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

## Repository layer (non-alarm entities)

`HumanRepository` is used only for **admin bulk operations** (`GetAllAsync`, `DeleteUserAsync`, `UpdateAsync`) that lack PoracleNG API equivalents. Single-user human reads and writes go through `IPoracleHumanProxy`. `poracle_web`-owned entities (`SiteSettingRepository`, `WebhookDelegateRepository`, `QuickPickDefinitionRepository`, `QuickPickAppliedStateRepository`) use their own dedicated repository classes.

!!! note "`BaseRepository` removed"
    The generic `BaseRepository<TEntity, TModel>` and all alarm repository classes have been removed. `EnsureNotNullDefaults()` is no longer needed -- PoracleNG handles NULL defaults for alarm writes, and the remaining repositories handle null normalization as needed.

## AutoMapper (non-alarm entities only)

AutoMapper is used for `humans` and `profiles` entities. Alarm tracking data flows as raw JSON through the PoracleNG API proxy and does not use AutoMapper.

All `*Update` models for non-alarm entities use **nullable `int?`** properties so partial updates don't zero out unset fields.

```csharp
// The mapping profile skips null properties
.ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))
```

## Alarm field defaults

PoracleNG's `cleanRow()` function applies field defaults on every create/update. PoracleWeb no longer needs to manage alarm defaults directly. However, the frontend still sends sensible initial values to avoid confusing the user when the add dialog opens:

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

- Registered via `AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>()`
- Used by: all alarm services, `DashboardService`, `CleaningService`

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

## Areas

User areas are managed through `IPoracleHumanProxy.SetAreasAsync()`. PoracleNG handles the dual-write to both `humans.area` and `profiles.area` internally.

Geofence polygons come from the Poracle API (via the unified feed), not the database.

## Location

`LocationController` uses `IPoracleHumanProxy.SetLocationAsync()` to set the user's location. No direct DB access or transactions are needed -- PoracleNG handles the write and state reload atomically.

## Service lifetimes

| Service | Lifetime | Reason |
|---|---|---|
| Most services | **Scoped** | Per-request |
| `MasterDataService` | **Singleton** | Cached game data |

!!! info "DashboardService uses the proxy"
    `DashboardService` calls `IPoracleTrackingProxy.GetAllTrackingAsync()` to fetch all alarm types in a single API call, then counts each type from the response. No direct DB queries.

## Profiles

- `humans.current_profile_no` (not `profile_no`) tracks the active profile
- All alarm tables reference `profile_no` to filter by active profile

## Scanner service

The scanner DB (`ScannerDb` connection string) is optional. When not configured, `IScannerService` is not registered and scanner endpoints return appropriate fallback responses.

### Gym search endpoints

`ScannerController` exposes two gym endpoints backed by `RdmScannerService`:

| Endpoint | Purpose |
|---|---|
| `GET /api/scanner/gyms?search=term&limit=20` | Search gyms by name (LIKE `%term%`). Minimum 2-character query, limit capped at 50. |
| `GET /api/scanner/gyms/{id}` | Return a single gym by its ID. |

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

### RdmGymEntity.Url

The `RdmGymEntity` in the scanner context maps the `url` column from the `gym` table, providing gym photo thumbnail URLs to `GymSearchResult`.

### PointInPolygon

`IScannerService` declares a static `PointInPolygon(double lat, double lon, double[][] polygon)` method using the ray-casting algorithm. The method tests if a point lies inside a polygon (where each entry is `[lat, lon]`) and returns `false` for degenerate polygons with fewer than 3 vertices. Used by `ScannerController` to determine which Koji geofence area a gym belongs to.

## Rate limiting

Auth endpoints use **per-IP** partitioned rate limiting:

| Policy | Limit | Window |
|---|---|---|
| `auth` | 30 requests | 60 seconds |
| `auth-read` | 120 requests | 60 seconds |

Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.

!!! danger "Never use global rate limiting for auth"
    Global (non-partitioned) `AddFixedWindowLimiter` for auth causes cascading login failures — multiple users share one bucket.
