# PoracleNG API Proxy

All alarm tracking operations (create, read, update, delete) are proxied through the PoracleNG REST API instead of writing directly to the Poracle MySQL database. This ensures PoracleNG applies field defaults, deduplication, and immediate state reload on every mutation.

!!! warning "PoracleNG 5.4.0 or newer"
    `PoracleServerProfile.MinimumSupported` is 5.4.0. Against anything older PoracleWeb.NET still runs, but the [server capability probe](backend.md#server-capability-probe) logs an error at startup and warns an admin, and the human, profile, area, place and mute calls answer with a message naming the version they need. See [Version compatibility](poracleng-compatibility.md).

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
PoracleNG REST API  (/api/tracking/*, /api/v2/humans/*)
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
told apart only by `grunt_type` carrying an event name. PoracleNG's two v2 endpoints each filter to
their own rows; **the v1 invasion read does not**, so `InvasionService` repeats the partition — and
skips it when the Pokéstop Events page is unavailable, so those rows stay visible somewhere. See
[Database](database.md#poraclecontext).

!!! warning "MaxBattle: insert-only on v1"
    PoracleNG's v1 maxbattle handler has no diff/dedup logic — every POST creates new rows, so `MaxBattleService` deletes and recreates, logging enough to recover if the second half fails. A single-rule edit escapes that on a server carrying the v2 PUT, which replaces the row in place; bulk distance changes still delete and recreate, because they stay on v1.

Also proxied:

- **Dashboard counts** -- `GET /api/tracking/all/{userId}` fetches all tracking in one call, counts extracted per type
- **Cleaning (auto-clean toggle)** -- fetches alarms, modifies the `clean` field, POSTs back via the proxy
- **Admin delete all alarms** -- fetches all UIDs per type, bulk deletes via the proxy
- **Bulk distance update** -- fetches alarms, modifies `distance`, POSTs back via the proxy

### Read-only calls that shape the UI

PoracleNG's own config and master data are read for things other than tracking. Each read degrades to a
usable default rather than failing the request:

| Call | Used for | If it fails |
|---|---|---|
| `GET /api/masterdata/monsters?locale={code}` | Pokemon names, types, form names and evolution chains, translated into the display language | Falls back to the English [WatWowMap masterfile](https://github.com/WatWowMap/Masterfile-Generator) cached server-side, so the pickers stay populated |
| `GET /api/config/poracleWeb` &rarr; `disabledHooks` | The per-type disable flags Poracle sets in its own config, honoured here as a floor under the site settings | Empty set: the local `disable_*` settings are in sole charge. Fails **open**, deliberately |
| `GET /api/config/values` &rarr; `general.disable_showcase` | Whether the Pokéstop Events page exists at all | Only `true` hides the page; absent or unreadable leaves it on, because every supported PoracleNG serves the v2 `incident` route |
| `GET /api/config/values` &rarr; `tracking.quest_summary_enabled` | Whether quest summary delivery is offered | Treated as off, so the control is hidden rather than saving into a delivery nothing performs |

Fort changes arrive in `disabledHooks` as `fort`
([jfberry/PoracleNG#197](https://github.com/jfberry/PoracleNG/issues/197)). A second read of
`general.disable_fort_update` covered servers before 5.2.1; it is gone.

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
- Lures, invasions and Pokéstop events take this same guard and nothing stricter. Up to PoracleNG 5.1.0
  their tables carried a unique key on the natural key (`lure_id`; `grunt_type` and `gender`), so
  PoracleWeb.NET refused any second rule on it and edited by deleting the row first. Migration 8 dropped
  the keys, and that code went when 5.2.1 became the minimum. A refusal on the natural key alone would
  now refuse rules PoracleNG stores side by side: an area-scoped twin, a place-measured one, two
  updatable fields apart.

<a id="the-v2-pilot"></a>

## The v2 write path

PoracleNG 5.2.0 added a second tracking surface at `/api/v2` and left v1 frozen. **Nine of the ten v1
tracking types send an edit through it**: `PUT /api/v2/humans/{id}/tracking/{type}/{uid}`. Reads,
creates and both distance endpoints stay on v1 for every type.

Invasion writes through v2 as well, since #841, but behind a second gate the other types do not have.
`grunt_type` — its one targeting field, and the only one PoracleWeb.NET ever holds — exists solely on a
server carrying PoracleNG PR #217, and even there a rule goes to v1 unless that server's own grunt
masterdata lists the name. That second condition is not a formality: 32 of 201 invasion rules in
production carry a name v2 refuses (`kecleon`, `gold-stop` and `showcase`, which are Pokéstop events, and
`metal`, which the game data calls `steel`), and all 32 are editable today because v1's read returns them
where v2's does not.

Pokéstop Events are the exception in the other direction: v2 is their only surface, so they have their
own proxy rather than a fallback.

**Reads and deletes go through v2.** `TrackingV2Translator.ToV1Row` rebuilds the v1 row from a v2 rule:
enums back to integers, booleans to 0/1, the three clean booleans to the `CleanFlags` bitmask, and every
`null` to a default from `DefaultsFor`, which follows PoracleNG's own read translation where it differs
from ours (fort `include_empty` null is 1, raid and max battle `level` null is 9000 with a pokemon and 90
without). A property the table doesn't know is carried through, so an upstream addition or the fork's
`personal_shiny` isn't dropped, and an edit sends it back when the server's `/openapi.json` declares it.
The whole-profile reads use the v2 snapshot; the all-profiles read adds the active profile's rules itself
when that profile has no `profiles` row, since PoracleNG only lists profiles that have one. Pokéstop
event rules come from `incident` and are folded into the invasion list as v1 returned them. Deletes use
the uid route, a `{uids}` body for bulk; a uid on a profile other than the active one is looked up with one
all-profiles read and deleted with `?profile=N`, and an invasion delete retries leftovers as `incident`.

**Creates stay on v1 because v2 POST still diffs and merges.** The uid-addressed PUT does not: it 404s
when the uid is not that human's, and 409s when the replacement would exactly duplicate another rule. A
POST can still take another rule over, so `TrackingUpdateReconciler` stays, and creates stay where the
guard already works.

**A v2 PUT is a full replace.** Omitting `min_iv` wipes a stored 90, verified live against 5.2.1. That
makes `TrackingFieldPreserver` load-bearing in a stronger way than it was on v1, where PoracleNG merged.

**The uid is read back on every edit.** The v2 engine is delete-then-insert, so on 5.2.x the replacement
comes back under a new uid. Since jfberry/PoracleNG #303 (unreleased) the PUT keeps the rule's uid, but a
failed reassignment still answers 200 with a new one, so the response is always read. Pokemon used to be
the one type whose uid survived an edit; on 5.2.x it no longer is.
`TrackingV2Replacement.TryApplyAsync` calls `ITrackedUidRemapper` for the nine types routed through it,
and `MonsterService` does the same after its own update, but only when the uid actually moved, so
quick-pick applied state follows the row rather than pointing at a uid that is gone.

**A workaround disappears when the PUT is available.** Max battles are insert-only on v1 and have to be
deleted and recreated; a uid-addressed replace skips the window where the alarm exists nowhere. Max
battles keep that v1 path for a server without the route.

### What changes at the wire

`TrackingV2Translator` rewrites a v1-shaped row into what that type's `V2*Rule` accepts, once, at the
proxy boundary. Everything inside PoracleWeb.NET keeps v1's shape as its single internal currency.

Each type has its own field table, never a shared one: `V2PokemonRule` declares 28 integer filters,
`V2FortRule` declares one and has no `clean` field at all. Four kinds of field change shape:

| v1 | v2 |
|---|---|
| `clean`, a 3-bit mask | three booleans — `clean`, `edit`, `summary` |
| 0/1 columns: `exclusive`, `slot_changes`, `battle_changes`, `gmax`, `shiny`, `include_empty` | real booleans |
| `team`, `gender` and `rsvp_changes` as integers | string enums |
| `change_types`, stored as a JSON string | an array |

`uid`, `id`, `profile_no` and `description` have no place in a v2 rule body — they are addressing and
presentation, and v2 reconstructs all of it. Every `V2*Rule` sets `additionalProperties: false`, so one
stray property is a 422 and the write fails outright.

Sentinels go across verbatim. The migration guide says to omit them, but a write carrying `level: 9000`
or `costume: 9000` stores exactly what v1 stores — verified on 5.2.1 by writing through both surfaces
and diffing the v1 read, which came back byte-identical but for the rotated uid. Only the v2 *response*
reports them as null.

**The translator never widens what PoracleNG accepts, and never narrows it quietly.** A property it does
not know, a gender outside 0-3, a `pvp_ranking_league` outside `{0, 500, 1500, 2500}`, an egg with no
level: any of those and it answers false, and the row goes to v1 instead. Refusing outright would mean a
PoracleNG newer than the translator broke every edit, and dropping the field silently would be the #730
field-loss bug again.

`ping` is the one that catches people out. It's a real column on every type and the v1 body carries it,
and v2 declares it on every rule (jfberry/PoracleNG#249), so a mention survives an edit that goes through
v2. v1 doesn't: its write handler blanks `ping` unconditionally, create or update, sent or omitted
(verified live on 5.4.0). An edit that would **fall back to v1** while the row holds a mention is
therefore refused with an `AlarmValidationException` (`RefuseIfV1WouldDropPing`, run by
`TryReplaceV2Async` whenever it is about to answer "use v1"), whatever the reason for the fallback: an
untranslatable field, an invasion v2 won't take, a route remembered as absent. An empty ping is
always fine. The SPA shows the Mention field in edit dialogs for webhooks and nowhere else; it asks the
server nothing first.

### Choosing the surface

There is no setting for it. `PoracleServerProfile.IsSupported` (PoracleNG answered and is not known to be
older than 5.4.0; an unparseable version or a local build's `0.0.0` counts as current, an unreachable
server does not) decides whether the proxy tries v2 at all. `Poracle:TrackingApiVersion` and
`PORACLE_TRACKING_API_VERSION` are gone, and a leftover value is ignored.

If a v2 tracking route answers gin's plaintext `404 page not found` anyway, the proxy remembers that
absence **per type** for five minutes, the same clock the server profile is cached on, and uses v1 until
it expires. Per type rather than per surface: one shared flag would let a single missing route drop every
other type back to v1, and for `incident`, whose only surface is v2, that would mean the type vanishing
rather than degrading. This tracking-only fallback goes when tracking moves to v2 entirely.

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
asked for it. `PoracleHumanProxy` and the areas and geocode calls on `PoracleApiProxy` throw it for a
plaintext 404 from a route 5.4.0 added; a problem+json 404 keeps its ordinary meaning. See
[Version compatibility](poracleng-compatibility.md#when-a-request-cannot-be-served).

When PoracleNG cannot be reached at all, `PoracleUnreachableExceptionFilter` answers 503 with a message on
the mute and location controllers and all eleven alarm controllers.

## What stays on direct database access

| Operation | Reason |
|---|---|
| Per-alarm `override_areas` (`IAlarmOverrideAreaWriter.SetAlarmOverrideAreasAsync`) | The tracking write validates the same names against `GetAvailableAreas` and answers 400 "area not permitted", failing the whole request. Matching never consults `userSelectable`, so the name is written into the column directly |
| Deprecated `pweb_settings` KV table (`PwebSettingRepository`, plus one `ALTER TABLE ... MODIFY COLUMN value LONGTEXT NULL` at startup) | Legacy rows PoracleNG never knew about, kept alive only so `SettingsMigrationService` can copy them into `poracle_web` |
| `poracle_web` database (geofences, settings, webhook delegates, quick picks) | Application-owned data, not managed by PoracleNG |
| Scanner database (gym search, weather) | Read-only, separate database |

The per-alarm `override_areas` write is tagged `HACK: trusted-set-areas` in code — `grep -rn "HACK: trusted-set-areas" --include="*.cs"` lists every reversion point. See [Backend → Areas](backend.md#areas) for the mechanism; this table and the one in [Database](database.md#poraclecontext) describe the same set.

!!! note "Every human and profile operation is proxied"
    `HumanService` and `ProfileService` read and write a single human through `IPoracleHumanProxy` with **no DB fallback**, and so do the admin-bulk operations: the full user list, the webhook list, the batch read that resolves geofence owners' names, and user deletion use `GET /api/v2/humans` (with `?type=` and `?id=a,b,c`) and `DELETE /api/v2/humans/{id}`. `HumanRepository` and `ProfileRepository` no longer exist. The existence check the purge runs first reads the proxy too: its human read answers null only for a 404 and throws for anything else, the same contract as `GetAdminRolesAsync`, so an unreachable PoracleNG is not reported to the admin as "already gone" ([#994](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/994)).

    **A new profile's areas and location** are written by `NewProfileGeography` through `POST /api/v2/humans/{id}/areas?profile=N` and `.../location?profile=N`, because PoracleNG's add ignores both ([#993](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/993)). Names PoracleNG rejects are logged and dropped, create first filters the list through the same allowed-name check as the Areas page (`IUserAreaWriter.FilterAllowedAsync`), both duplicates send the caller's own source profile's list unfiltered, import sends none, and whatever is sent is always `trusted`, so the profile keeps the user's drawn fences whatever `area_security` is, and a refusal of either write deletes the profile just created and returns the original error, since there is no direct write left to repair it with.

    **Rename** is `PATCH /api/v2/humans/{id}/profiles/{n}`, and it sends only the `active_hours` the caller submitted, so renaming a profile whose stored schedule v2 would refuse still works.

## Which human operations use /api/v2

All of them, with no fallback. A route 5.4.0 added that answers gin's plaintext `404 page not found` is a
`PoracleUnsupportedException`, not a retry on v1.

| Operation | Route |
|---|---|
| Get human | `GET /v2/humans/{id}` |
| Create human | `POST /v2/humans` |
| Admin list / delete | `GET /v2/humans`, `DELETE /v2/humans/{id}` |
| Enable / disable | `POST .../enable`, `.../disable` |
| Admin disable | `POST .../admin-disable` `{disabled}` |
| Set location | `POST .../location` `{latitude,longitude}` |
| Switch profile | `POST .../profile` `{profile_no}` |
| Set language | `POST .../language` |
| List / add / move / delete saved place | `GET`, `POST .../locations`, `PUT`, `DELETE .../locations/{label}` |
| Admin roles | `GET .../admin-roles` |
| Save areas, toggle a fence, create | `POST .../areas` `{areas, trusted: true}`, after `IUserAreaWriter` has checked every name (see [Backend → Areas](backend.md#areas)). Names PoracleNG has no fence for come back in `rejected` and are not an error |
| Delete a fence | `DELETE .../areas?name=&all_profiles=true`, preceded by a `dry_run=true` call that reports the alarms scoped to that fence alone. Alarms the user chose to delete go first, per profile, through the tracking bulk delete with `?profile=N` |
| Rename a fence | `PATCH .../areas?from=&to=&all_profiles=true&trusted=true`, after a geofence reload so `to` is loaded |
| Available areas | `GET .../areas` |
| List profiles | `GET .../profiles`, translated back to v1's `{"profile":[...]}` with `area` and `active_hours` as strings |
| Create / rename profile | `POST .../profiles` (returns the number), `PATCH .../profiles/{n}` |
| Delete profile | `DELETE .../profiles/{n}` |
| Copy profile | `POST .../profiles/{to}/copy` `{from_profile}` |
| A new profile's geography | `POST .../areas?profile=N`, `POST .../location?profile=N` |

Three things are worth knowing about how a missing route is told from a missing record:

- **The content type is the only thing separating them.** gin answers a route it does not have with the plaintext `404 page not found`; the v2 surface answers a missing human or place with problem+json at the same status. `PoracleProblemDetails.IsProblemJson` is consulted before concluding anything.
- **Every webhook id in a path goes through `Uri.EscapeDataString`**, because the id is a URL.
- **Bodies differ from v1 in more than shape.** `admin-disable` renames `state` to `disabled`. The location writes take `latitude`/`longitude`, matching what every read answers (jfberry/PoracleNG#303). The profile create and update bodies are translated, never forwarded: `V2AddProfileBody` and `V2UpdateProfileBody` declare `name` and `active_hours` under `additionalProperties: false`, and `active_hours` is the entry array rather than the JSON string stored here.

The profile list restates the v2 answer in v1's shape before `ProfileService` sees it: the wrapper key changes from `profile` to `profiles`, and `active_hours` and `area` arrive as arrays. Copy inverts its arguments (destination in the path, source as `from_profile`) and replaces the destination's rules, which the tests pin. Both surfaces' copy replaces, so copying from a profile with no rules empties the destination. Delete cascades the profile's rules and moves `current_profile_no` off a deleted active profile.

A `?profile=N` write for a profile number that does not exist answers 200 and writes nothing, on both routes, so a geography write can't report "no such profile"; callers pass the number PoracleNG has just assigned.

## IPoracleTrackingProxy interface

```csharp
public interface IPoracleTrackingProxy
{
    Task<JsonElement> GetByUserAsync(string type, string userId);
    Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body);
    Task<TrackingUpdateResult> UpdateByUidAsync(string type, string userId, int uid, JsonElement body);
    Task<TrackingUpdateResult?> TryReplaceV2Async(string type, string userId, int uid, JsonElement body);
    Task DeleteByUidAsync(string type, string userId, int uid);
    Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids);
    Task<JsonElement> GetAllTrackingAsync(string userId);
    Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId);
    Task ReloadStateAsync();
}
```

Key design points:

- **`JsonElement` throughout** -- alarm data flows as raw JSON. Services deserialize with `JsonNamingPolicy.SnakeCaseLower` to map between C# PascalCase models and PoracleNG's snake_case JSON.
- **`?silent=true`** on create, update **and delete** -- suppresses PoracleNG's DM confirmation. Without it on the delete routes, clearing a list DMed the user one filter dump per row removed: a bulk delete, a cleaning reset and an admin delete-all could each produce dozens.
- **`X-Poracle-Secret` header** -- authenticates requests to the PoracleNG API. Configured via `Poracle:ApiSecret`.
- **Two update entry points.** `UpdateByUidAsync` tries v2 and falls back to the v1 POST carrying the `uid`; `MonsterService` uses it. The other eight v2 types go through `TrackingV2Replacement.TryApplyAsync`, the only service-layer caller of `TryReplaceV2Async`, because each wraps its own v1 path in guards a successful v2 replace must skip — the max-battle delete-then-create, the reconcile of a stray insert. Both return the uid the rule now lives under, which may differ from the one that went in. See [The v2 write path](#the-v2-write-path).
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

- **Human responses**: `GET /api/humans/one/{id}` returns `{ "human": { ... }, "status": "ok" }`, and `GET /api/v2/humans/{id}` returns the same wrapper without the `status`. `PoracleHumanProxy.GetHumanAsync()` unwraps the `"human"` property either way.
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
    sp.GetRequiredService<IAlarmOverrideAreaWriter>(),
    sp.GetRequiredService<ILogger<UserOwnedOverrideAreaProxy>>()));

services.AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>();
services.AddHttpClient<IPoracleMuteProxy, PoracleMuteProxy>();
services.AddHttpClient<IPoracleIncidentProxy, PoracleIncidentProxy>();
```

There are five Poracle proxies. `IPoracleTrackingProxy` and `IPoracleHumanProxy` each speak both
versions, preferring v2 where it exists and falling back to v1 where it does not; `IPoracleApiProxy`
covers read-only config and templates on v1; and two are v2-only, `IPoracleMuteProxy` at
`/api/v2/humans/{id}/mutes` and `IPoracleIncidentProxy` at `/api/v2/humans/{id}/tracking/incident`.

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
3. Writes the full list into the row with `IAlarmOverrideAreaWriter.SetAlarmOverrideAreasAsync`, resolving
   the uid from the create response for a single row and by re-reading and pairing on content for a
   batch (PoracleNG returns `newUids` in its own order). A row that is not there to write to throws
   rather than leaving an alarm that quietly covers the whole profile.
4. Calls `ReloadStateAsync`, since a direct column write is not a PoracleNG mutation and would otherwise
   wait for the periodic reload.

A write that names no override area skips steps 2 to 4 entirely, so the common path costs one extra
JSON scan and no queries.
