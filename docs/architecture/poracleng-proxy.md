# PoracleNG API Proxy

All alarm tracking operations (create, read, update, delete) are proxied through the PoracleNG REST API instead of writing directly to the Poracle MySQL database. This ensures PoracleNG applies field defaults, deduplication, and immediate state reload on every mutation.

## Why we migrated

On March 31, 2026, a NULL `template` column written directly by PoracleWeb.NET crashed PoracleNG's state reload for 15 hours. PoracleNG's Go SQL scanner cannot handle `NULL` in the `template` column of the `monsters` table, causing the entire state reload to fail. All users received stale alarm state and unwanted DM floods until PoracleNG was manually restarted.

Direct database writes bypass PoracleNG's `cleanRow()` function, which applies proper defaults for every field (template defaults to the config's `defaultTemplateName`, ping defaults to `""`, etc.). By proxying all writes through PoracleNG's API, we eliminate this entire class of data integrity bugs.

## Request flow

```
Frontend (Angular)
    |
    v
ASP.NET Core Controllers  (/api/pokemon, /api/raids, etc.)
    |
    v
Alarm Services  (MonsterService, RaidService, etc.)
    |
    v
IPoracleTrackingProxy  (PoracleTrackingProxy)
    |  HTTP + X-Poracle-Secret header
    v
PoracleNG REST API  (/api/tracking/*)
    |
    v
MySQL (Poracle DB)  +  State Reload
```

## What goes through the proxy

All alarm tracking CRUD for these types:

| Type | PoracleNG tracking type | Service |
|---|---|---|
| Pokemon | `pokemon` | `MonsterService` |
| Raids | `raid` | `RaidService` |
| Eggs | `egg` | `EggService` |
| Quests | `quest` | `QuestService` |
| Invasions | `invasion` | `InvasionService` |
| Lures | `lure` | `LureService` |
| Nests | `nest` | `NestService` |
| Gyms | `gym` | `GymService` |
| Fort Changes | `fort` | `FortChangeService` |
| Max Battles | `maxbattle` | `MaxBattleService` |

!!! warning "MaxBattle: insert-only (no upsert)"
    The PoracleNG maxbattle API handler has no diff/dedup logic — every POST creates new rows. `MaxBattleService` uses a delete-then-create pattern for updates and bulk distance changes, with error logging for atomicity recovery.

Also proxied:

- **Dashboard counts** -- `GET /api/tracking/all/{userId}` fetches all tracking in one call, counts extracted per type
- **Cleaning (auto-clean toggle)** -- fetches alarms, modifies the `clean` field, POSTs back via the proxy
- **Admin delete all alarms** -- fetches all UIDs per type, bulk deletes via the proxy
- **Bulk distance update** -- fetches alarms, modifies `distance`, POSTs back via the proxy

## What stays on direct database access

| Operation | Reason |
|---|---|
| Admin bulk human operations (`GetAllAsync`, `DeleteUserAsync`, `UpdateAsync`) | PoracleNG has no admin-list, admin-delete, or generic update endpoints |
| `poracle_web` database (geofences, settings, webhook delegates, quick picks) | Application-owned data, not managed by PoracleNG |
| Scanner database (gym search) | Read-only, separate database |

!!! note "Single-user human/profile operations are fully proxied"
    `HumanService` reads, creates, and checks existence via `IPoracleHumanProxy` with **no DB fallback**. Location, areas, profile switch, profile CRUD, and profile copy all go through the proxy. Only admin bulk operations remain on direct DB.

## IPoracleTrackingProxy interface

```csharp
public interface IPoracleTrackingProxy
{
    Task<JsonElement> GetByUserAsync(string type, string userId);
    Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body);
    Task DeleteByUidAsync(string type, string userId, int uid);
    Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids);
    Task<JsonElement> GetAllTrackingAsync(string userId);
    Task ReloadStateAsync();
}
```

Key design points:

- **`JsonElement` throughout** -- alarm data flows as raw JSON. Services deserialize with `JsonNamingPolicy.SnakeCaseLower` to map between C# PascalCase models and PoracleNG's snake_case JSON.
- **`?silent=true`** on create -- suppresses PoracleNG's DM confirmation message to the user.
- **`X-Poracle-Secret` header** -- authenticates requests to the PoracleNG API. Configured via `Poracle:ApiSecret`.
- **Updates use POST** -- PoracleNG's tracking POST endpoint handles both creates and updates. When the request body includes a `uid` field, PoracleNG updates the existing alarm instead of creating a new one.
- **`uid:0` stripped on create** -- `PoracleJsonHelper.SerializeToElement()` removes `"uid":0` from request bodies. PoracleNG treats `uid=0` as an update target instead of a new insert; omitting `uid` tells PoracleNG to create a new row.
- **URL-encoding for user IDs** -- Both `PoracleTrackingProxy` and `PoracleHumanProxy` use `Uri.EscapeDataString()` on user IDs in URL paths. Webhook IDs are full URLs containing slashes that would break routing without encoding.

## snake_case JSON serialization

PoracleNG's API uses snake_case field names (`pokemon_id`, `min_iv`, `max_cp`). PoracleWeb.NET's C# models use PascalCase (`PokemonId`, `MinIv`, `MaxCp`). The shared `PoracleJsonHelper` class provides a centralized `SnakeCaseOptions` instance:

```csharp
// PoracleJsonHelper.cs
public static readonly JsonSerializerOptions SnakeCaseOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};
```

All alarm services use `PoracleJsonHelper.SerializeToElement()` for serialization (which also strips `uid:0`) and `PoracleJsonHelper.DeserializeList<T>()` for deserialization.

## PoracleNG response wrapper format

PoracleNG wraps certain responses in container objects:

- **Human responses**: `GET /api/humans/one/{id}` returns `{ "human": { ... }, "status": "ok" }`. `PoracleHumanProxy.GetHumanAsync()` unwraps the `"human"` property.
- **Profile responses**: `GET /api/profiles/{id}` returns a JSON array or object depending on the endpoint.
- **Tracking responses**: `GET /api/tracking/{type}/{id}` returns an array of alarm objects.

When adding new proxy methods, check the actual PoracleNG response shape and unwrap accordingly.

## Active hours pass-through

The `active_hours` field is a JSON-encoded string stored in the `profiles` table. It passes through the proxy with no special handling — `IPoracleHumanProxy` uses raw `JsonElement` pass-through for profile payloads, so `active_hours` is included automatically in GET responses and accepted in create/update request bodies.

No proxy code changes were needed to support active hours. PoracleNG's profile scheduler evaluates these rules at notification time — PoracleWeb.NET only manages the data (validation, display, editing).

## Known gaps and workarounds

These operations lack dedicated PoracleNG endpoints and use fetch-modify-repost workarounds:

| Operation | Workaround | Impact |
|---|---|---|
| Bulk distance update | Fetch all alarms, modify distance, POST back | Extra round-trip; scales linearly with alarm count |
| Bulk clean toggle | Fetch all alarms, modify clean flag, POST back | Same as above |
| Dashboard counts | Single `GET /api/tracking/all/{userId}` call | Returns full alarm payloads just to count them |
| Admin delete all alarms | Fetch UIDs per type, bulk delete each | Multiple API calls instead of one |

See [PoracleNG Enhancement Requests](../poracleng-enhancement-requests.md) for the full gap analysis and proposed endpoints.

## How to add a new alarm type

1. Create a new service class following the pattern in `MonsterService.cs`:
    - Inject `IPoracleTrackingProxy`
    - Define the `TrackingType` constant (must match PoracleNG's tracking type name)
    - Define `SnakeCaseOptions` for JSON serialization
    - Implement `GetByUserAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, etc.
2. Add the type key to `PoracleTrackingProxy.ResolveResponseKey()` if the response property name differs from the type name.
3. Register the service in `ServiceCollectionExtensions.cs`.
4. Create the corresponding controller under `Controllers/`.

No repository, entity, or AutoMapper mapping is needed for alarm types -- the proxy handles all database interaction through PoracleNG.

## Registration

```csharp
// In ServiceCollectionExtensions.cs
services.AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>();
services.AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>();
```

The `HttpClient` instances are managed by the .NET HTTP client factory, providing connection pooling and DNS rotation.
