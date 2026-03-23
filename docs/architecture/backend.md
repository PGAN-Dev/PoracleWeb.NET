# Backend Patterns

## Repository layer

`BaseRepository<TEntity, TModel>` uses expression-based filters and AutoMapper projections.

### EnsureNotNullDefaults

Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method sets null strings to `""` before saving to avoid constraint violations.

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

## Rate limiting

Auth endpoints use **per-IP** partitioned rate limiting:

| Policy | Limit | Window |
|---|---|---|
| `auth` | 30 requests | 60 seconds |
| `auth-read` | 120 requests | 60 seconds |

Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.

!!! danger "Never use global rate limiting for auth"
    Global (non-partitioned) `AddFixedWindowLimiter` for auth causes cascading login failures — multiple users share one bucket.
