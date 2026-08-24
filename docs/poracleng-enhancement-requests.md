# PoracleNG API Enhancement Requests

This document tracks PoracleNG API gaps that require workarounds in PoracleWeb.NET. Each gap is referenced by inline `HACK`/`TODO` comments throughout the codebase.

## Background

PoracleWeb.NET now proxies all alarm tracking writes through the PoracleNG REST API (see [PoracleNG API Proxy](architecture/poracleng-proxy.md)). This migration was prompted by a March 31, 2026 incident where a NULL `template` column written directly by PoracleWeb.NET crashed PoracleNG's state reload for 15 hours.

The migration is complete for the ten alarm types that have a v1 tracking route. The eleventh, Pokéstop Events (`incident`), exists only on PoracleNG's `/api/v2` and never touches `IPoracleTrackingProxy` — its CRUD goes through `IPoracleIncidentProxy` instead. Some operations lack dedicated PoracleNG endpoints and use fetch-modify-repost workarounds that are less efficient. The gaps listed below are these operations.

---

## Gaps

### Bulk Distance Update

**ID:** `bulk-distance-update`  
**Priority:** High  
**Code refs:** Each alarm service's `UpdateDistanceAsync` and `UpdateDistanceBulkAsync` methods

**Current behavior:** PoracleWeb.NET fetches all alarms of a type via the proxy, modifies the `distance` field in memory, and POSTs them back. Two variants:
1. Update ALL alarms of a type for a user/profile
2. Update specific alarms by UID list

**What's needed:** A PoracleNG endpoint that accepts a batch distance update:
```
PUT /api/tracking/{type}/{id}/distance
Body: { "distance": 500 }
// Updates all alarms of {type} for user {id} on their active profile

PUT /api/tracking/{type}/{id}/distance/bulk
Body: { "uids": [1, 2, 3], "distance": 500 }
// Updates specific alarm UIDs
```

**Workaround without enhancement:** Fetch all alarms via GET, then POST each one back with the distance field changed. Very inefficient for users with hundreds of alarms.

---

### Bulk Clean Toggle

**ID:** `bulk-clean-toggle`  
**Priority:** High  
**Code refs:** `CleaningService.cs`

**Current behavior:** PoracleWeb.NET fetches all alarms of a type via the proxy, sets the `clean` field (0 or 1), and POSTs them back. Used for the "auto-clean" feature that deletes alarms after they fire.

**What's needed:** A PoracleNG endpoint to batch-toggle the clean flag:
```
PUT /api/tracking/{type}/{id}/clean
Body: { "clean": 1 }
// Sets clean=1 on all alarms of {type} for user {id} on their active profile
```

**Workaround without enhancement:** Fetch all alarms via GET, then POST each back with the clean field changed. Same inefficiency as bulk distance.

---

### Dashboard Counts

**ID:** `dashboard-counts`  
**Priority:** Medium  
**Code refs:** `DashboardService.cs`

**Current behavior:** `DashboardService` calls `IPoracleTrackingProxy.GetAllTrackingAsync()` which returns full alarm payloads for all types. Counts are extracted from the response arrays.

**What's needed:** A PoracleNG endpoint that returns alarm counts per type:
```
GET /api/tracking/counts/{id}
Response: { "pokemon": 537, "raid": 27, "egg": 17, "quest": 38, ... }
```

**Workaround without enhancement:** Single `GET /api/tracking/all/{id}` call returns full payloads for all types. Works but returns complete alarm objects just to count them.

---

### Admin Delete All Alarms

**ID:** `admin-delete-all-alarms`  
**Priority:** Medium  
**Code refs:** `HumanService.cs:DeleteAllAlarmsByUserAsync`

**Current behavior:** PoracleWeb.NET fetches all UIDs per alarm type via the proxy, then bulk-deletes each type sequentially.

**What's needed:** A PoracleNG admin endpoint:
```
DELETE /api/tracking/all/{id}
// Deletes ALL tracking for user {id} across all alarm types and profiles
```

**Workaround without enhancement:** Loop through each alarm type, GET all UIDs, then POST bulk delete for each. Slow and not atomic.

---

### Profile Delete Cascade

**ID:** `profile-delete-cascade`  
**Priority:** Medium  
**Code refs:** `ProfileController.cs:Delete`

**Current behavior:** PoracleWeb.NET only deletes the `profiles` row. It does NOT:
- Delete alarm records scoped to that profile (`monsters`, `raid`, `egg`, etc. with matching `profile_no`)
- Reassign `humans.current_profile_no` if the active profile is deleted
- Remove the profile's areas from `humans.area`

**What's needed:** Confirm that PoracleNG's `DELETE /api/profiles/{id}/byProfileNo/{n}` cascades:
1. Deletes all alarm rows with matching `(id, profile_no)`
2. Reassigns `humans.current_profile_no` to profile 1 (or another valid profile) if the active profile is deleted
3. Updates `humans.area` if the deleted profile was active

If PoracleNG already handles this, PoracleWeb.NET can simply proxy the call.

---

### Atomic Profile Switch

**ID:** `atomic-profile-switch`  
**Priority:** Low (adopted)  
**Code refs:** `ProfileController.cs:SwitchProfile`, `PoracleHumanProxy.cs:SwitchProfileAsync`

**Status: Adopted.** `ProfileController.SwitchProfile` now calls `IPoracleHumanProxy.SwitchProfileAsync(userId, profileNo)` -- a single atomic call. PoracleNG handles saving the old profile's areas and loading the new profile's areas internally. The multi-step dual-write has been removed.

---

### Atomic Area Update

**ID:** `atomic-area-update`  
**Priority:** Low (partially adopted)  
**Code refs:** `AreaController.cs:UpdateAreas`, `PoracleHumanProxy.cs:SetAreasAsync`

**Status: Partially adopted.** `AreaController.UpdateAreas` calls `IPoracleHumanProxy.SetAreasAsync(userId, areas)` for admin-area writes. PoracleNG handles the dual-write to both `humans.area` and `profiles.area` internally. However, user-drawn geofence names are silently stripped by PoracleNG's `userSelectable` intersection filter -- see the **Trusted setAreas (bypass userSelectable filter)** gap below.

---

### Trusted setAreas (bypass userSelectable filter)

**ID:** `trusted-set-areas`  
**Priority:** High  
**Code refs:** `IUserAreaDualWriter.cs`, `UserAreaDualWriter.cs`, `UserGeofenceService.cs:CreateAsync`, `UserGeofenceService.cs:DeleteAsync`, `UserGeofenceService.cs:AdminDeleteAsync`, `UserGeofenceService.cs:AddToProfileAsync`, `UserGeofenceService.cs:RemoveFromProfileAsync`, `UserGeofenceService.cs:PreserveOwnedAreasInHumanAsync`, `AreaController.cs:UpdateAreas`

**Current behavior:** PoracleNG's `POST /api/humans/{id}/setAreas` handler (`processor/internal/api/humans.go:HandleSetAreas`) intersects the submitted area list against fences where `UserSelectable == true` for non-admin users. Any area whose fence has `userSelectable=false` is silently dropped -- no error, no warning. PoracleWeb.NET's `GeofenceFeedController.cs` serves user-drawn custom geofences with `userSelectable: false` (to hide them from the Poracle bot's `!area` picker and from other users' views), so every `SetAreasAsync` call that contains a user-drawn geofence name loses that name. This is the root cause of [#163](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/163) -- "custom geofence toggle doesn't persist."

**What's needed:** Any of the following would resolve the gap:
1. **`POST /api/humans/{id}/setAreas?trusted=true`** -- query flag that skips the userSelectable intersection but still honors community-membership filtering. The caller already holds the `X-Poracle-Secret`, so trust is established.
2. **`POST /api/humans/{id}/setAreasTrusted`** -- separate endpoint with the same body shape.
3. **Per-fence ownership:** add an `ownedBy: humanId` field to the fence definition. PoracleNG's intersection allows selection when `ownedBy == request user ID`, regardless of `userSelectable`.

Option 1 is the smallest surface area change. The filter exists to stop users from selecting restricted admin fences via a browser hack; since PoracleWeb.NET writes are already gated behind the shared secret and user geofences are owned by the requesting user, the filter is not a meaningful defense in this path.

**Workaround (HACK):** `UserGeofenceService` delegates user-geofence area mutations to `IUserAreaDualWriter`, a tiny atomic-write abstraction that holds the Poracle `DbContext` and commits both `humans.area` and the active `profiles.area` in a single `SaveChangesAsync` call. The single-SaveChanges guarantees EF Core wraps both writes in one implicit transaction — `humans.area` and `profiles.area` cannot drift, even if the process crashes between reads. `AreaController.UpdateAreas` additionally calls `IUserGeofenceService.PreserveOwnedAreasInHumanAsync` after the proxy `SetAreasAsync` call to re-add any user-owned geofences that PoracleNG stripped; this hands off to the writer's bulk `AddAreasToActiveProfileAsync` so the merge costs one DB round-trip regardless of how many geofences the user owns. These are the only remaining direct-DB writes in the alarm / human / area code path and are tagged with `HACK: trusted-set-areas` comments.

Because the direct-DB writes skip PoracleNG's `HandleSetAreas` handler, they also skip its terminal `reloadState(deps)` call — so `AddToProfileAsync`, `RemoveFromProfileAsync`, and `PreserveOwnedAreasInHumanAsync` each call `ReloadGeofencesSafeAsync` manually to ask PoracleNG to refresh its in-memory state. Without the manual reload, a toggle would only take effect on the next organic state reload (potentially minutes). These manual reload calls are part of the same `HACK: trusted-set-areas` surface area and are removed together when the workaround is reverted.

**Regression history:** Before PR #88 (v2.0.0), the direct-DB path was the only path. The proxy migration routed user geofence area writes through `setAreas`, which introduced the silent-strip bug. The direct-DB code is the restored pre-#88 behavior, scoped specifically to user geofence names.

---

### NULL Field Defaults

**ID:** `null-field-defaults`  
**Priority:** Low (resolved)

**Status: Resolved.** `BaseRepository` and `EnsureNotNullDefaults()` have been removed. All alarm writes go through the PoracleNG API, which applies proper defaults via `cleanRow()`. Remaining direct-DB repositories (`HumanRepository` for admin ops, `poracle_web`-owned tables) handle null normalization as needed.

---

### PoracleNG monsters.go COALESCE Gap

**ID:** `monsters-go-coalesce`  
**Priority:** High (bug in PoracleNG)  
**Not a PoracleWeb.NET code ref — this is a PoracleNG bug**

**Issue:** In `/source/PoracleNG/processor/internal/db/monsters.go`, the SQL query selects `template` and `ping` as raw columns without `COALESCE`:
```sql
template, clean, ping
```

Every other tracking query file (quests.go, invasions.go, raids.go, gyms.go, lures.go, nests.go, forts.go, tracking_queries.go) correctly uses:
```sql
COALESCE(template, '1') AS template
```

When `template IS NULL` in the database, Go's `database/sql` scanner crashes with:
```
sql: Scan error on column index 25, name "template": converting NULL to string is unsupported
```

This crashes the **entire state reload**, freezing PoracleNG on stale data for all users until restarted.

**Fix:** Add `COALESCE(template, '1') AS template, clean, COALESCE(ping, '') AS ping` to the monsters query in `monsters.go`, matching all other tracking files.

---

### available_languages is enforced but not readable

**Filed upstream:** [jfberry/PoracleNG#194](https://github.com/jfberry/PoracleNG/issues/194)

`POST /api/humans/{id}/setLanguage` rejects any language absent from `general.available_languages`
with `400 "language is not available"` (`internal/api/humans.go`). Nothing exposes that list:
`/api/config/poracleWeb` does not carry it, and `/api/config/values` is driven by `configSchema`,
which does not declare it.

**Consequence here:** the alert-language menu offers all 11 of this site's languages. On a Poracle
that restricts the list, choosing an unlisted one fails the write and the user sees a generic error
with no reason. Filtering that menu is blocked until the codes are readable.

**Workaround:** none. The menu is unfiltered.

### disabledHooks omits fort, and carries an inert pokestop

**Filed upstream:** [jfberry/PoracleNG#195](https://github.com/jfberry/PoracleNG/issues/195)

The `disabledHooks` array on `/api/config/poracleWeb` is built from ten flags. `disable_fort_update`
is enforced by the processor, the bot, and `!tracked`, but is not one of them — so a client reading
the array concludes fort changes are enabled when they are not. Meanwhile `pokestop` is in the array
and nothing in the processor reads it.

**Consequence here:** [feature gating](configuration/site-settings.md) needs a second call to
`GET /api/config/values` purely to learn `general.disable_fort_update`, and `pokestop` is
deliberately mapped to nothing. Mapping it to lures, invasions and quests — the obvious reading,
since those arrive on the pokestop webhook — would disable three working types on a flag that does
nothing.

**Workaround:** the second config call, degraded independently so a Poracle without that route keeps
the hook list already in hand.

---

## v2 findings, for an upstream report

Nine things found while planning the v1-to-v2 migration against **PoracleNG 5.2.1**. Every one below
was confirmed by calling a running 5.2.1 instance on 2026-08-24, not by reading source; the requests
above were mostly derived from source and are older. Two of them decide whether whole surfaces can
move to v2 at all.

### `active_hours.day` is bounded 0-6 while the scheduler reads ISO 1-7

The v2 schema declares `day` as `minimum: 0, maximum: 6` and the migration guide documents it as
"0 = Sunday". The scheduler does not agree: `isoDow` in `processor/cmd/processor/profiles.go` uses ISO
weekdays, Monday 1 through Sunday 7, and nothing translates between the two.

```
PATCH /api/v2/humans/{id}/profiles/1  {"active_hours":[{"day":7,...}]}
  -> 422 "expected number <= 6"  at body.active_hours[0].day
PATCH /api/v2/humans/{id}/profiles/1  {"active_hours":[{"day":0,...}]}
  -> 200
```

So through v2 a Sunday schedule cannot be expressed at all, and the value that *is* accepted matches
no weekday. The official migration guide propagates the error, so any client following it writes
schedules that never fire.

**Consequence here:** v2 `active_hours` writes are blocked. PoracleWeb's own 1-7 validation is correct
and stays.

### v2 invasion reads omit the targeting field for a named grunt, so GET then PUT is impossible

v2 requires exactly one of `type_id`, `grunt_id`, `everything`, `boss` on a write. For a rule whose
`grunt_type` is a *type* name it returns `type_id` and round-trips fine. For a rule whose `grunt_type`
is a named grunt it returns **no targeting field of any kind**:

| stored `grunt_type` | v1 read | v2 read |
|---|---|---|
| `water` | `grunt_type: "water"` | `type_id: 11` |
| `blanche` | `grunt_type: "blanche"` | *(nothing)* |
| `player team leader` | `grunt_type: "player team leader"` | *(nothing)* |
| `npc 0` | `grunt_type: "npc 0"` | *(nothing)* |

Handing a v2 read straight back to a v2 write therefore fails:

```
PUT /api/v2/humans/{id}/tracking/invasion/757
  -> 422 "exactly one of type_id, grunt_id, everything, boss must be set"
```

PoracleNG holds the forward name-to-id map and exposes no endpoint for it, and PoracleWeb stores only
the name, so the id cannot be reconstructed on the client either. **Consequence here:** invasion is
excluded from the v2 migration in both directions until a read returns `grunt_id`.

### `override_areas` is not validated, on either version or either surface

This is the one that most needs a second opinion, because it contradicts what PoracleWeb was built
around. See [the verification note](poracleng-v2-review.md#override_areas-re-test-2026-08-24) for the
full method. In short: a non-admin's `override_areas` was stored verbatim on 5.1.0 v1, on 5.2.1 v1 and
on 5.2.1 v2 — for a real user-drawn fence carrying `userSelectable: false`, and for a fence name that
does not exist at all. The `setAreas` filter on the same human, in the same session, stripped the same
fence name, so the human was demonstrably non-admin and the filter was demonstrably live.

There is a privacy edge if this holds: a crafted call can scope a rule to another user's private
geofence name, which leaks nothing by itself but does let one account key alerts off another's area.
Unreachable through PoracleWeb's UI, since #544 stopped those names being listed anywhere.

### `language` validation depends on configuration the client cannot read

`POST /api/v2/humans/{id}/language` accepted `"zz"` with 200 and stored it. On a deployment that
configures `general.available_languages` the same call is refused — which is
[#194](https://github.com/jfberry/PoracleNG/issues/194) above, still open. The write also lowercases:
`"DE"` stores `de`. Worth documenting, since a client sending a stored casing back gets a different
string than it sent.

### `/health` carries no applied-migration number

`/health` returns capabilities, status and version. It does not say which schema migration has been
applied, and that is a different fact from the version: a 5.2.1 binary pointed at a database whose
migrations did not run reports 5.2.1 and behaves like an older one. PoracleWeb reads
`schema_migrations` directly for exactly this reason — it is the last read-only dependency
`PoracleContext` has. One integer on `/health` would delete `PoracleSchemaVersionReader`, its
interface, its registration and that dependency.

### No list-humans and no delete-human, on either version

```
GET    /api/v2/humans              -> 404
GET    /api/v2/humans?type=webhook -> 404
GET    /api/humans                 -> 404
DELETE /api/v2/humans/{id}         -> 404
```

Every human route is single-`{id}`. The admin user list and account deletion therefore keep
`HumanRepository` and its direct database access alive. A `?type=webhook` filter alone would close the
delegated-webhook half of it.

### Profile create returns no `profile_no`

`POST /api/v2/humans/{id}/profiles` answers `{"status":"ok"}`. PoracleNG assigns the lowest free
number rather than max + 1, so the caller cannot predict it and must snapshot the list, create, re-read
and diff — on names that are not unique. Returning the created resource, as the rest of v2 does, would
remove that dance.

### PATCH profile cannot write name, area or coordinates

```
PATCH /api/v2/humans/{id}/profiles/1  {"name":"renamed"}
  -> 422  "expected required property active_hours to be present"  at body
  -> 422  "unexpected property"                                    at body.name
```

`active_hours` is required and is the only writable field. There is no rename endpoint on either
version, so `IProfileRepository.RenameAsync`'s direct database write stays.

### v2 `setAreas` keeps the v1 `userSelectable` filter

Re-confirmed on 5.2.1 rather than taken from source. `POST /api/v2/humans/{id}/areas` with
`["<a user-drawn fence>", "aberdeen"]` stored `["aberdeen"]` — silently, 200, no warning, exactly as
v1 does. This is the [trusted setAreas](#trusted-setareas-bypass-userselectable-filter) ask above, and v2
does not close it.

---

## Summary Table

| Gap | Priority | Workaround in Use | Status |
|-----|----------|-------------------|--------|
| Bulk distance update | High | Fetch all, modify, POST back | Working but inefficient |
| Bulk clean toggle | High | Fetch all, modify, POST back | Working but inefficient |
| monsters.go COALESCE | High | PoracleNG bug -- needs fix upstream | PoracleNG fix needed |
| Dashboard counts | Medium | Single GET /api/tracking/all call | Working (returns full payloads) |
| Admin delete all alarms | Medium | Fetch UIDs per type, bulk delete each | Working |
| Profile delete cascade | Medium | Unknown | Need verification |
| Atomic profile switch | Low | Already in PoracleNG | **Adopted** |
| Atomic area update | Low | Already in PoracleNG | **Adopted** |
| NULL field defaults | Low | Handled by PoracleNG cleanRow() | Resolved by migration |
| available_languages not readable | Medium | None -- alert-language menu is unfiltered | [Filed upstream (#194)](https://github.com/jfberry/PoracleNG/issues/194) |
| disabledHooks omits fort | Low | Second call to /api/config/values | [Filed upstream (#195)](https://github.com/jfberry/PoracleNG/issues/195) |

### Tracking create has no upsert path for natural-key types

`lure` and `invasion` are the only tracking tables carrying a unique index over a natural key:

```
lure       lure_tracking(id, profile_no, lure_id)
invasion   invasion_tracking(id, profile_no, gender, grunt_type)
```

Every other type is unique on `PRIMARY(uid)` alone.

`HandleCreateLure` / `HandleCreateInvasion` treat a row as "already present" only when **every** field matches. Changing a field *outside* the natural key — distance, template or clean on a lure — is therefore not recognised as an existing row, so the handler attempts an `INSERT` that collides with the unique index:

```
Tracking API: insert lure: Error 1062 (23000):
Duplicate entry '<id>-<profile_no>-<lure_id>' for key 'lure_tracking'
```

PoracleNG answers `500 {"message":"database error"}` and the edit is discarded. Reproduced against a dev instance:

| Request | Result |
|---|---|
| create a lure with an untracked `lure_id` | `200 insert:1` |
| re-post the identical row | `200 alreadyPresent:1` |
| re-post with only `distance` changed | **`500 database error`** |
| `DELETE byUid` then re-post | `200 insert:1` |

So the only way to edit these two types is to delete the row first, which is what PoracleWeb now does (`NaturalKeyTrackingUpdate`). The cost is that the `uid` rotates on every edit, which in turn orphans anything holding the old uid — quick-pick applied state tracks uids, for example.

Pokemon now rotates too, for a different reason. On PoracleNG 5.2.0 and later its edits go through the v2 `PUT /api/v2/humans/{id}/tracking/pokemon/{uid}`, whose engine is delete-then-insert, so the replacement comes back under a new uid. It used to be the one type whose uid survived an edit. `ITrackedUidRemapper` moves the applied state either way, so the orphaning is handled rather than merely known about — but this request no longer covers only lures and invasions.

**Request:** make the create handler upsert when the natural key matches an existing row for the same `(id, profile_no)`, updating the non-key columns in place and returning `updates: 1` with the existing uid. That matches how the uid-only types already behave and would let PoracleWeb drop the delete-then-create workaround along with the uid churn it causes.
