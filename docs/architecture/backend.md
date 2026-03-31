# Backend Patterns

## Repository layer

`BaseRepository<TEntity, TModel>` uses expression-based filters and AutoMapper projections.

### EnsureNotNullDefaults

Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method sets null strings to `""` before saving to avoid constraint violations.

!!! note "Two-phase string normalization"
    `EnsureNotNullDefaults()` performs two complementary steps using cached reflection via `NullabilityInfoContext`:

    1. **Non-nullable strings**: coerces `null` → `""` for `NOT NULL` columns that EF Core maps as `string`.
    2. **Nullable strings**: coerces `""` → `null` for `string?` properties (e.g., `gym_id`, `template`).

    Property lists are cached in static fields (`WritableNonNullableStringProperties` and `WritableNullableStringProperties`) so reflection runs once per entity type. The second step protects against `MySql.EntityFrameworkCore` converting `null` to empty string on INSERT, which would break Poracle's `NULL` vs empty-string semantics (e.g., `gym_id IS NULL` means "general alarm").

### Targeted distance updates

`UpdateDistanceByUidsAsync()` does targeted distance-only SQL updates without touching other fields. Use this for bulk distance operations instead of the generic `UpdateAsync`.

## AutoMapper update models

All `*Update` models (MonsterUpdate, RaidUpdate, etc.) use **nullable `int?`** properties so partial updates don't zero out unset fields.

```csharp
// The mapping profile skips null properties
.ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))
```

!!! danger "Full object spread required"
    When calling the PUT `/{uid}` endpoint from the frontend, always spread the full alarm object:

    ```typescript
    // ✅ Correct — preserves all existing fields
    this.http.put(`/api/pokemon/${uid}`, { ...alarm, distance });

    // ❌ Wrong — zeros out clean, template, filter settings
    this.http.put(`/api/pokemon/${uid}`, { distance });
    ```

## AutoMapper create model defaults

`*Create` models (MonsterCreate, RaidCreate, etc.) must have **property defaults that match the PHP PoracleWeb defaults** used by the original project. Without these defaults, AutoMapper maps C# zero-values onto the entity, overwriting the database column defaults that Poracle expects.

Key defaults on create models:

| Property | Default | Notes |
|---|---|---|
| `MaxIv` | 100 | |
| `MaxCp` | 9000 | |
| `MaxLevel` | 55 | |
| `MaxWeight` | 9000000 | |
| `MaxAtk` | 15 | |
| `MaxDef` | 15 | |
| `MaxSta` | 15 | |
| `PvpRankingWorst` | 4096 | |
| `Size` | -1 | Means "any size" |
| `MaxSize` | 5 | |
| `Team` (Raid/Egg) | 4 | Means "any team" |
| `Move` (Raid) | 9000 | Means "any move" |
| `Evolution` (Raid) | 9000 | Means "any evolution" |

!!! danger "Forgetting a default silently breaks filters"
    If a create model property defaults to `0` (C#'s `int` default) instead of the expected Poracle default, AutoMapper writes `0` to the entity. The alarm is saved but the filter is silently too restrictive or non-functional — e.g., `MaxIv=0` means nothing ever matches.

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

The `/distance/bulk` endpoint does a targeted `SetDistance()` on matching entities, bypassing AutoMapper entirely — safe for bulk operations.

## Poracle API proxy

`IPoracleApiProxy` / `PoracleApiProxy` wraps HttpClient calls to the external Poracle REST API.

- Used for: fetching config, areas/geofences, templates, sending commands
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`

### Config parsing

`PoracleConfig` is parsed from Poracle's JSON configuration. The `defaultTemplateName` field can be a number or string — deserialization handles both via `JsonElement`.

## Areas

User areas are stored as JSON arrays in the `humans.area` column:

```json
["west end", "downtown"]
```

Geofence polygons come from the Poracle API (via the unified feed), not the database.

## Service lifetimes

| Service | Lifetime | Reason |
|---|---|---|
| Most services | **Scoped** | Per-request |
| `MasterDataService` | **Singleton** | Cached game data |

!!! warning "DbContext is not thread-safe"
    `DashboardService` runs sequential DB queries (not `Task.WhenAll`) because it uses a single scoped `DbContext` instance.

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
