# PoracleNG API Proxy

All alarm tracking operations (create, read, update, delete) are proxied through the PoracleNG REST API instead of writing directly to the Poracle MySQL database. This ensures PoracleNG applies field defaults, deduplication, and immediate state reload on every mutation.

!!! warning "PoracleNG 5.1.0 or newer"
    `PoracleServerProfile.MinimumSupported` is 5.1.0 — the release that adds `override_location_label`, `override_areas` and `pvp_ranking_evolution`. Below it those columns do not exist, so per-alarm delivery scope, the PVP mega picker and the minimum time-left filter write fields nothing stores and fail silently. The [server capability probe](backend.md#server-capability-probe) reports the running version and warns an admin when it is known to be older.

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

There is an eleventh tracking type, Pokéstop Events (`incident`), and it is **not** in that table. It
has no v1 route at all, so it goes through its own `IPoracleIncidentProxy` against
`/api/v2/humans/{id}/tracking/incident`. Its rows live in the same `invasion` table as invasion rows,
but PoracleNG filters each endpoint to its own rows on every read path, so a uid from one type is
invisible to the other.

!!! warning "MaxBattle: insert-only (no upsert)"
    The PoracleNG maxbattle API handler has no diff/dedup logic — every POST creates new rows. `MaxBattleService` uses a delete-then-create pattern for updates and bulk distance changes, with error logging for atomicity recovery.

Also proxied:

- **Dashboard counts** -- `GET /api/tracking/all/{userId}` fetches all tracking in one call, counts extracted per type
- **Cleaning (auto-clean toggle)** -- fetches alarms, modifies the `clean` field, POSTs back via the proxy
- **Admin delete all alarms** -- fetches all UIDs per type, bulk deletes via the proxy
- **Bulk distance update** -- fetches alarms, modifies `distance`, POSTs back via the proxy

### Read-only calls that shape the UI

Three of PoracleNG's own endpoints are read for things other than tracking. All three degrade to a
usable default rather than failing the request:

| Call | Used for | If it fails |
|---|---|---|
| `GET /api/masterdata/monsters?locale={code}` | Pokemon names, types, form names and evolution chains, translated into the display language | Falls back to the English [WatWowMap masterfile](https://github.com/WatWowMap/Masterfile-Generator) cached server-side, so the pickers stay populated |
| `GET /api/config/poracleWeb` &rarr; `disabledHooks` | The per-type disable flags Poracle sets in its own config, honoured here as a floor under the site settings | Empty set: the local `disable_*` settings are in sole charge. Fails **open**, deliberately |
| `GET /api/config/values` &rarr; `general.disable_fort_update` | Fort changes, which PoracleNG enforces but omits from `disabledHooks` | Same, and independently of the call above, so a Poracle without this route keeps the hook list it already has |

The last row is a wart, not a design: see [PoracleNG enhancement requests](../poracleng-enhancement-requests.md).
The locale on the first row is the display language, which is why switching language re-fetches the
map — Poracle owns the translations, so this site does not carry Pokemon names of its own.

## Insert, update or duplicate

Every alarm write is a POST, and PoracleNG decides what to do with it by diffing the submitted row
against the ones already stored (`DiffTracking`). The outcome is not
obvious from the request:

| Diff result | Outcome |
|---|---|
| No differences | Duplicate. Nothing is written, reported as `alreadyPresent` |
| Exactly one difference, and it is an updatable field | **Update of that existing row**, re-keyed to a new uid |
| Anything else | New insert |

The updatable set is uniform: `clean`, `distance` and `template`, plus `slot_changes` and
`battle_changes` on gyms. Everything else identifies the alarm.

The consequence that keeps biting: an Add or an Edit that differs from a **different** alarm by exactly
one updatable field takes that alarm over. The user gets a 201 or a 200, and one alarm exists where
there were two, with the victim's radius replaced. `TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync`
mirrors the rule and refuses before the write, on create and update alike. Two or more updatable
differences genuinely coexist and must stay editable; an earlier version of the guard refused those too
and made alarms permanently uneditable.

Two qualifications:

- Pokemon edits cannot merge. On v1, `trackingMonster.go` splits rows on whether the uid is set and
  sends uid-bearing ones straight to `UpdateMonsterByUID`, never reaching the diff, so the guard skips
  them. It is the only v1 type that does this. On the v2 path the guard is upstream's: the uid-addressed
  PUT answers 409 rather than taking another rule over.
- A field PoracleWeb does not supply cannot be compared. PoracleNG fills it with its own default, so a
  null says nothing about what will be stored. That is why `TrackingFieldPreserver` merges the stored
  row in before the guard runs — see [Backend → Update pattern](backend.md#update-pattern).

## The v2 pilot

PoracleNG 5.2.0 added a second tracking surface at `/api/v2` and left v1 frozen. One call site uses it:
`MonsterService.UpdateAsync`, which writes through `PUT /api/v2/humans/{id}/tracking/pokemon/{uid}`.
Everything else stays on v1 — every read, every create, both distance endpoints, and the nine other v1
tracking types. `PoracleTrackingProxy.UpdateByUidAsync` picks the surface; callers pass a v1-shaped
object either way and never learn which was used.

**Reads stay on v1 on purpose.** v2 answers `null` for every field at its wildcard where v1 answers the
sentinel, so rebuilding a model from a v2 read would need a per-field default table matching
PoracleNG's exactly, forever. The sentinels already say what the wildcard means.

**Creates stay on v1 because v2 POST still diffs and merges.** The uid-addressed PUT does not: it 404s
when the uid is not that human's, and 409s when the replacement would exactly duplicate another rule. A
POST can still take another rule over, so `TrackingUpdateReconciler` stays, and creates stay where the
guard already works.

**A v2 PUT is a full replace.** Omitting `min_iv` wipes a stored 90, verified live against 5.2.1. That
makes `TrackingFieldPreserver` load-bearing in a stronger way than it was on v1, where PoracleNG merged.

**Pokemon uids now rotate on edit.** The v2 engine is delete-then-insert, so the replacement comes back
under a new uid. Pokemon used to be the one type whose uid survived an edit; it no longer is.
`MonsterService` calls `ITrackedUidRemapper` so quick-pick applied state follows the row rather than
pointing at a uid that is gone.

### Three shapes change at the wire

`TrackingV2Translator` rewrites a v1-shaped pokemon row into what `V2PokemonRule` accepts, once, at the
proxy boundary. Everything inside PoracleWeb.NET keeps v1's shape as its single internal currency.

| Field | v1 | v2 |
|---|---|---|
| `clean` | 3-bit mask | three booleans: `clean`, `edit`, `summary` |
| `gender` | 0-3 | `any` / `male` / `female` / `genderless` |
| `pvp_ranking_league` | any int | enum of `{0, 500, 1500, 2500}` |

`uid`, `id`, `profile_no`, `ping` and `description` have no place in a v2 rule body. `V2PokemonRule`
sets `additionalProperties: false`, so one stray property is a 422 and the write fails outright.

The translator never widens what PoracleNG accepts. A property it does not know, a gender outside 0-3,
a league outside the enum: any of those and it answers false, and the row goes to v1 instead. Refusing
outright would mean a PoracleNG newer than the translator broke every pokemon edit, and dropping the
field silently would be the #730 field-loss bug again.

### Choosing the surface

`Poracle:TrackingApiVersion` (env `PORACLE_TRACKING_API_VERSION`, mapped in `Program.cs`) takes `auto`,
`v1` or `v2`. `auto` is the default and asks the server: `SupportsV2Tracking` is
`ParsedVersion >= 5.2.0`, and an unknown or unparseable version resolves to false. An operator needs the
pin because a fork can carry the routes while reporting an older number, or the reverse, and a version
probe sees neither.

If the v2 route answers gin's plaintext `404 page not found` anyway, the proxy remembers that absence
for five minutes, the same clock the server profile is cached on, and uses v1 until it expires.

## Reading a refusal

PoracleNG speaks two error dialects and `PoracleProblemDetails.Describe` reads both:

| Surface | Status | Body |
|---|---|---|
| v1 | 400 | `{"message": "...", "status": "error"}` |
| v2 | **422**, not 400 | RFC 9457 `application/problem+json`: `{title, status, detail}`, plus an `errors[]` array on a schema violation |

Precedence is `errors[]` first, because a field error names the field that was refused; then `detail`,
`message`, `error`, `title`. `status` is never used as the message: v1's is the word `error` and v2's is
a number. Field-error locations are JSON pointers whose prefix moves with the route (`body.pokemon_id`
on the single-object PUT, `body[0].pokemon_id` on the array POST), so both prefixes are trimmed. At most
three are quoted, with a count of the rest. A body that is not JSON is echoed raw up to 300 characters.
The fallback is "Poracle rejected the alarm."

`PoracleTrackingProxy.CreateAsync` treats 422 as the caller's problem alongside 400, so a v2 validation
failure surfaces as a sentence rather than an opaque 500.

!!! warning "A v1-shaped error reader renders blank against v2"
    Anything reading `err.error.error` is reading v1's shape and shows nothing when v2 refuses. Use
    `PoracleProblemDetails.Describe` for any new v2 call site.

## When the server is too old

`PoracleUnsupportedException` carries a feature name and what the server would need, in words, and the
global `PoracleUnsupportedExceptionFilter` turns it into **409 Conflict** with
`{error, feature, requires}`. 409 rather than 403 because 403 is the `disable_*` path and keys off
`disableKey` in the body; 409 has no interceptor branch, so the message lands beside the control that
asked for it. Nothing throws it yet — the capability services hide their controls first. See
[Version compatibility](poracleng-compatibility.md#when-a-request-cannot-be-served).

## What stays on direct database access

| Operation | Reason |
|---|---|
| Admin bulk human operations (`GetAllAsync`, `DeleteUserAsync`, `UpdateAsync`) | PoracleNG has no admin-list, admin-delete, or generic update endpoints |
| Profile **rename** (`ProfileRepository.RenameAsync`) | PoracleNG's profile update answers `{"status":"ok"}` and writes nothing for `name`, while honouring `active_hours` on the same request |
| User-geofence area writes (`IUserAreaDualWriter`, `humans.area` + `profiles.area`) | `setAreas` intersects the submitted list against `userSelectable=true` fences for non-admins, so a user's own geofence is silently stripped |
| Per-alarm `override_areas` (`IUserAreaDualWriter.SetAlarmOverrideAreasAsync`) | The tracking write validates the same names against `GetAvailableAreas` and answers 400 "area not permitted", failing the whole request. Matching never consults `userSelectable`, so the name is written into the column directly |
| `schema_migrations` read (`PoracleSchemaVersionReader`) | The applied migration number is what says whether a column exists; nothing in the `/health` capability map describes alarm columns |
| Deprecated `pweb_settings` KV table (`PwebSettingRepository`, plus one `ALTER TABLE ... MODIFY COLUMN value LONGTEXT NULL` at startup) | Legacy rows PoracleNG never knew about, kept alive only so `SettingsMigrationService` can copy them into `poracle_web` |
| `poracle_web` database (geofences, settings, webhook delegates, quick picks) | Application-owned data, not managed by PoracleNG |
| Scanner database (gym search, weather) | Read-only, separate database |

The user-geofence area writes and the per-alarm `override_areas` write are tagged `HACK: trusted-set-areas` in code — `grep -rn "HACK: trusted-set-areas" --include="*.cs"` lists every reversion point. See [Backend → Areas](backend.md#areas) for the mechanism; this table and the one in [Database](database.md#poraclecontext) describe the same set.

!!! note "Single-user human/profile operations are fully proxied"
    `HumanService` reads, creates, and checks existence via `IPoracleHumanProxy` with **no DB fallback**. Location, areas, profile switch, profile CRUD, and profile copy all go through the proxy. Only admin bulk operations remain on direct DB.

## IPoracleTrackingProxy interface

```csharp
public interface IPoracleTrackingProxy
{
    Task<JsonElement> GetByUserAsync(string type, string userId);
    Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body);
    Task<TrackingUpdateResult> UpdateByUidAsync(string type, string userId, int uid, JsonElement body);
    Task DeleteByUidAsync(string type, string userId, int uid);
    Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids);
    Task<JsonElement> GetAllTrackingAsync(string userId);
    Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId);
    Task ReloadStateAsync();
}
```

Key design points:

- **`JsonElement` throughout** -- alarm data flows as raw JSON. Services deserialize with `JsonNamingPolicy.SnakeCaseLower` to map between C# PascalCase models and PoracleNG's snake_case JSON.
- **`?silent=true`** on create -- suppresses PoracleNG's DM confirmation message to the user.
- **`X-Poracle-Secret` header** -- authenticates requests to the PoracleNG API. Configured via `Poracle:ApiSecret`.
- **Updates go through `UpdateByUidAsync`** -- on v1 that is a POST carrying the `uid`, the upsert PoracleWeb.NET has always sent; on PoracleNG 5.2.0 and later, for pokemon, it is the v2 PUT. It returns the uid the rule now lives under, which may differ from the one that went in. See [The v2 pilot](#the-v2-pilot).
- **`uid:0` stripped on create** -- `PoracleJsonHelper.SerializeToElement()` removes `"uid":0` from request bodies. PoracleNG treats `uid=0` as an update target instead of a new insert; omitting `uid` tells PoracleNG to create a new row.
- **`profile_no` stripped on every alarm write** -- the same helper removes it. PoracleNG takes a submitted
  `profile_no` at face value on the pokemon type (creating a row on a profile that may not exist) while
  scoping every read to `current_profile_no`. Since the JWT claim can be stale, stamping it onto writes
  stranded alarms that were invisible and undeletable. Omitting it files each alarm under the live active
  profile.
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

No repository or entity is needed for alarm types -- the proxy handles all database interaction through PoracleNG.

## Registration

```csharp
// In ServiceCollectionExtensions.cs
services.AddHttpClient<PoracleTrackingProxy>();
services.AddScoped<IPoracleTrackingProxy>(sp => new UserOwnedOverrideAreaProxy(
    sp.GetRequiredService<PoracleTrackingProxy>(),
    sp.GetRequiredService<IUserGeofenceRepository>(),
    sp.GetRequiredService<IUserAreaDualWriter>(),
    sp.GetRequiredService<ILogger<UserOwnedOverrideAreaProxy>>()));

services.AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>();
services.AddHttpClient<IPoracleMuteProxy, PoracleMuteProxy>();
services.AddHttpClient<IPoracleIncidentProxy, PoracleIncidentProxy>();
```

There are five Poracle proxies. `IPoracleTrackingProxy` and `IPoracleHumanProxy` work v1 (with the one
v2 write described above), `IPoracleApiProxy` covers read-only config and templates, and two are v2-only:
`IPoracleMuteProxy` at `/api/v2/humans/{id}/mutes` and `IPoracleIncidentProxy` at
`/api/v2/humans/{id}/tracking/incident`.

The `HttpClient` instances are managed by the .NET HTTP client factory, providing connection pooling and DNS rotation.

### The tracking proxy is decorated

`PoracleTrackingProxy` is registered as its concrete type. What the rest of the app resolves for
`IPoracleTrackingProxy` is `UserOwnedOverrideAreaProxy` wrapping it, so every alarm service gets the
decorated instance. It intercepts `CreateAsync` only; the other six methods forward untouched.

On a create or an edit it:

1. Refuses an incoherent scope up front (`EnsureScopeIsCoherent`): a place and a set of areas cannot
   both be set, areas cannot coexist with a radius, and a place needs one. PoracleNG enforces the same
   three rules, but only against the body it receives — which by step 2 may no longer mention the areas.
2. Strips the user's own geofence names out of `override_areas` before the POST, because PoracleNG
   rejects them outright with a 400 rather than stripping them the way `setAreas` does.
3. Writes the full list into the row with `IUserAreaDualWriter.SetAlarmOverrideAreasAsync`, resolving
   the uid from the create response for a single row and by re-reading and pairing on content for a
   batch (PoracleNG returns `newUids` in its own order). A row that is not there to write to throws
   rather than leaving an alarm that quietly covers the whole profile.
4. Calls `ReloadStateAsync`, since a direct column write is not a PoracleNG mutation and would otherwise
   wait for the periodic reload.

A write that names no override area skips steps 2 to 4 entirely, so the common path costs one extra
JSON scan and no queries.
