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
**Code refs:** Each alarm service's `UpdateDistanceByUserAsync` and `UpdateDistanceByUidsAsync` methods

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

**Current behavior:** PoracleWeb.NET fetches all alarms of a type via the proxy, sets the auto-delete bit of each row's `clean` bitmask, and POSTs them back. The bit is set read-modify-write, so edit-in-place and summary bits set from the bot survive the toggle.

**What's needed:** A PoracleNG endpoint to batch-toggle the clean flag:
```
PUT /api/tracking/{type}/{id}/clean
Body: { "clean": 1 }
// Sets the given bits on all alarms of {type} for user {id} on their active profile,
// leaving the bits it was not given alone
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
**Priority:** Low (resolved)  
**Code refs:** `ProfileController.cs:Delete`

**Status: Resolved, verified upstream.** `ProfileController.Delete` proxies to
`DELETE /api/profiles/{id}/byProfileNo/{n}` and PoracleNG cascades. `SQLHumanStore.DeleteProfile`
(`processor/internal/store/human_sql.go`) deletes the `profiles` row, deletes every tracking row for that
`(id, profile_no)`, and when the deleted profile was the active one moves `current_profile_no` to the
lowest remaining profile, copying that profile's area and coordinates into `humans`. Deleting profile 1
while it is the only profile is the single exception: the row goes, the tracking stays.

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

**Status: answered upstream, answered incompletely, still blocked.** Filed as
[jfberry/PoracleNG#215](https://github.com/jfberry/PoracleNG/issues/215) and given a `trusted` flag in
[PR #217](https://github.com/jfberry/PoracleNG/pull/217). Building against that branch showed the flag does
not close this gap, on three counts:

1. **It lifts more than the `userSelectable` filter.** `settableAreaNames` folds the community area
   restriction behind the same `unrestricted := admin || trusted`, while the comment beside the flag says it
   lifts `userSelectable` only. Reproduced on a build of `develop` with `[area_security] enabled = true` and
   a human restricted to a five-area community: an untrusted request stored `["erina"]` and rejected
   `Aberdeen`; the same request with `trusted: true` stored both. Since `setAreas` is a whole-list replace,
   the one call that needs the flag is also the one carrying the user's requested admin areas -- so using it
   would hand any user every area on the instance. Filed as
   [#228](https://github.com/jfberry/PoracleNG/issues/228).
2. **There is no profile target.** `POST /v2/humans/{id}/areas` takes `id` and nothing else and writes
   `CurrentProfileNo`, so `RemoveAreaFromAllProfilesAsync` and `RenameAreaInAllProfilesAsync` -- which must
   span every profile when a drawn fence is deleted or renamed -- have no API form at all.
3. **Per-rule `override_areas` is still refused.** `POST /v2/humans/{id}/tracking/{type}` with an
   `override_areas` naming a `userSelectable=false` fence answers `422 "area not permitted"`, and the
   tracking write has no `trusted` of its own. `SetAlarmOverrideAreasAsync` and `UserOwnedOverrideAreaProxy`
   stay regardless.

[PR #230](https://github.com/jfberry/PoracleNG/pull/230) addresses all three. Its first two halves check out;
its `trusted` fix over-corrects, so under `area_security` the flag now rejects the very fences it exists to
admit -- a drawn fence is in no community's `allowed_areas`, and the community filter strips it immediately
after the lift adds it. Reported on that PR. **Re-test the mixed case before adopting.**

Three of the six `IUserAreaDualWriter` methods would be mechanically replaceable once the above lands, and
should not be: each is a single `SaveChangesAsync` spanning `humans.area` and the active `profiles.area`,
and `setAreas` replaces the whole list, so going through it turns each into read-modify-write. That is a
correctness regression bought for no deletion while the other three methods keep the writer alive anyway.

**Workaround (HACK):** `UserGeofenceService` delegates user-geofence area mutations to `IUserAreaDualWriter`, a tiny atomic-write abstraction that holds the Poracle `DbContext` and commits both `humans.area` and the active `profiles.area` in a single `SaveChangesAsync` call. The single-SaveChanges guarantees EF Core wraps both writes in one implicit transaction — `humans.area` and `profiles.area` cannot drift, even if the process crashes between reads. `AreaController.UpdateAreas` additionally calls `IUserGeofenceService.PreserveOwnedAreasInHumanAsync` after the proxy `SetAreasAsync` call to re-add any user-owned geofences that PoracleNG stripped; this hands off to the writer's bulk `AddAreasToActiveProfileAsync` so the merge costs one DB round-trip regardless of how many geofences the user owns. These are the only direct-DB writes left on the area path, and are tagged `HACK: trusted-set-areas`; `grep -rn "HACK: trusted-set-areas" --include="*.cs"` lists every one. `IProfileRepository.RenameAsync` and `HumanRepository.DeleteUserAsync` are direct writes for their own reasons, covered under the v2 findings below.

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
**Priority:** Low (fixed upstream)

**Status: Fixed.** `processor/internal/db/monsters.go` selects `COALESCE(template, '') AS template` on
PoracleNG `main`, matching every other tracking query file, so a NULL `template` no longer crashes the
state reload for everyone. `ping` is still selected raw, but PoracleNG's initial schema declares it
`NOT NULL` on every tracking table it creates, so there is no second crash vector to close.

The March 31, 2026 incident this tracked is still why alarm writes go through the API instead of the
database. That reasoning does not depend on the query being defensive.

---

### available_languages, readable since 5.2.1

**Filed as [jfberry/PoracleNG#194](https://github.com/jfberry/PoracleNG/issues/194), fixed by
[PR #197](https://github.com/jfberry/PoracleNG/pull/197) — closed.**

`POST /api/humans/{id}/setLanguage` refuses any language absent from `general.available_languages`, and
until 5.2.1 nothing exposed that list, so the alert-language menu had to offer all eleven of this site's
languages and let the write fail.

`GET /api/config/poracleWeb` now carries `availableLanguages`. `PoracleApiProxy` reads it,
`SettingsController` projects it as `poracle_alert_languages`, and `AlertLanguageService.restrictTo`
narrows the menu to codes Poracle will accept. Absent or empty means unrestricted — which is also what a
server older than 5.2.1 sends, and both accept any code, so the two need no telling apart.

---

### disabledHooks, corrected in 5.2.1

**Filed as [jfberry/PoracleNG#195](https://github.com/jfberry/PoracleNG/issues/195), fixed by
[PR #197](https://github.com/jfberry/PoracleNG/pull/197) — closed.**

`fort` is in the `disabledHooks` array as of 5.2.1 and `PoracleDisabledHookMap` maps it like any other
hook. `pokestop`, which was in the array while nothing in the processor read the flag, is gone from it and
the config field is deprecated.

Both halves leave a tail for older servers. `UpstreamFeatureFlagService` still reads
`general.disable_fort_update` from `GET /api/config/values`, but only when the config response did not
carry `availableLanguages` — the discriminator for pre-5.2.1, since both fields arrived in the same
release. And `pokestop` still maps to nothing, because a 5.1.0 server still sends it and the obvious
reading would switch off lures, invasions and quests on a flag that does nothing.

`disable_showcase` is the same shape one release later: enforced, absent from `disabledHooks`, and read
separately by `IPoracleApiProxy.GetShowcaseDisabledAsync`. Filed as
[#210](https://github.com/jfberry/PoracleNG/issues/210) and fixed in PR #217, which is on `develop` only.

---

## v2 findings, filed upstream as #208-#216 { #v2-findings-for-an-upstream-report }

Nine things found while planning the v1-to-v2 migration against **PoracleNG 5.2.1**, each confirmed by
calling a running instance on 2026-08-24 rather than by reading source. All nine were filed as
jfberry/PoracleNG#208 through #216, accepted, and fixed in
[PR #217](https://github.com/jfberry/PoracleNG/pull/217), merged 2026-08-31 -- onto `develop`.
**No released build carries any of it**, so every consequence below is still live and every workaround is
still the one running.

Eight of the nine are below; the ninth, `disable_showcase` missing from `disabledHooks`, sits with its
twin under [disabledHooks](#disabledhooks-corrected-in-521).

### `active_hours.day` is bounded 0-6 while the scheduler reads ISO 1-7

[#208](https://github.com/jfberry/PoracleNG/issues/208) -- fixed on `develop`: `day` is ISO 1-7 and
`day: 0` is refused.

The v2 schema declared `day` as `minimum: 0, maximum: 6` and the migration guide documented it as
"0 = Sunday". `isoDow` in `processor/cmd/processor/profiles.go` uses Monday 1 through Sunday 7, and nothing
translated between them, so through v2 a Sunday schedule could not be expressed at all and the value that
*was* accepted matched no weekday. Any client following the guide wrote schedules that never fire.

**Here:** profile writes stay on v1 and `ActiveHoursValidator` enforces 1-7, which is what the fix settles
on. Nothing to change.

### v2 invasion reads omit the targeting field for a named grunt, so GET then PUT is impossible

[#209](https://github.com/jfberry/PoracleNG/issues/209) -- fixed on `develop`, and larger than reported: 41
of the 59 `grunt_type` values in shipped data had no read representation, not the four probed.

v2 required exactly one of `type_id`, `grunt_id`, `everything`, `boss` on a write. A rule whose
`grunt_type` is a type name round-tripped (`water` becomes `type_id: 11`); a rule whose `grunt_type` is a
named grunt (`blanche`, `player team leader`, `npc 0`) came back carrying no targeting field of any kind, so
handing a v2 read back to a v2 write answered 422. The fix admits `grunt_type` itself to the one-of set, and
a read emits exactly the field the rule is stored as.

**Here: adopted**, in PoracleWeb.NET #894. Invasion has a `TrackingV2Translator` entry and its edits go to
v2 -- behind a second gate the other types do not have, because `grunt_type` exists only on a server carrying
the fix and even there only for names that server's grunt masterdata lists. That second condition is not a
formality: 32 of 201 invasion rules in production carry a name v2 refuses (`kecleon`, `gold-stop` and
`showcase`, which are Pokestop events, and `metal`, which the game data calls `steel`), and all 32 are
editable today because v1's read returns them where v2's does not. `InvasionGruntNameService` reads the
accepted names from the server rather than shipping a table.

Two things the schema does not say, both established by calling a running build: it contradicts itself on
gender -- `V2InvasionRule.gender` says "ONLY valid together with `type_id`" while `grunt_type` says it "may
be combined with gender", and the latter is correct -- and it normalises a spaced name on write, so
`player team leader` is stored as `player_team_leader`. Worth knowing before adopting the fix: it also stops
`type_id`, `everything` and `boss` being emitted on a read.

### `override_areas` is not validated, on either version or either surface

[#211](https://github.com/jfberry/PoracleNG/issues/211) -- fixed on `develop`, and the reason the re-test
found nothing is now known.

A non-admin's `override_areas` was stored verbatim on 5.1.0 v1, on 5.2.1 v1 and on 5.2.1 v2 -- for a real
user-drawn fence carrying `userSelectable: false`, and for a fence name that does not exist at all. The
`setAreas` filter on the same human, in the same session, stripped the same fence name, so the human was
demonstrably non-admin and the filter demonstrably live. Method in
[the verification note](poracleng-v2-review.md#override_areas-re-test-2026-08-24).

The check was not weak, it never ran: `validateOverrideFields` gates on `oc.permitted != nil`, `permitted`
is built from `deps.AreaLogic`, and `processor/cmd/processor/main.go` never sets `AreaLogic` -- still true
on `main` today. Dead code on every tracking write, v1 and v2, all eleven types.

**Here:** `UserOwnedOverrideAreaProxy` sends PoracleNG the filtered list and writes the full list to the row
afterwards, which stores the right value whether or not validation runs. The fix makes v1 tracking writes
reject an unpermitted `override_areas` too, so the class's premise becomes true again the moment a build
carrying #217 ships. Keep it.

### `language` validation depends on configuration the client cannot read

[#216](https://github.com/jfberry/PoracleNG/issues/216) -- fixed on `develop`: the code is validated against
the loaded locales, and the lowercasing is documented.

`POST /api/v2/humans/{id}/language` accepted `"zz"` with 200 and stored it, while the same call is refused on
a deployment that sets `general.available_languages`. The write also lowercases, so `"DE"` stores `de`.

**Here:** `AlertLanguageService.load` matches `humans.language` case-insensitively against the languages this
UI ships and stores it back in the UI's own casing, so a stored `pt-br` still selects the `pt-BR` row.

### `/health` carries no applied-migration number

[#212](https://github.com/jfberry/PoracleNG/issues/212) -- closed deliberately without adding one. Migrations
are mandatory at startup, so a database can only be ahead of its binary, never behind; and publishing the
number would make PoracleNG's migration *numbering* a public contract. PR #217 answers the concrete question
instead, with a `costume` capability flag on `/health`, `GET /api/v2/activity` and
`GET /api/masterdata/costumes`.

**Here:** `PoracleSchemaVersionReader` keeps reading `schema_migrations` from the Poracle database directly.

### No list-humans and no delete-human, on either version

[#214](https://github.com/jfberry/PoracleNG/issues/214) -- fixed on `develop`: `GET /api/v2/humans` with
`?type=` and `?id=a,b,c`, and `DELETE /api/v2/humans/{id}`.

Every human route was single-`{id}`: `GET /api/humans`, `GET /api/v2/humans` and `DELETE /api/v2/humans/{id}`
all answered 404.

**Here:** all three still keep `HumanRepository.GetAllAsync`, `GetByIdsAsync` and `DeleteUserAsync` -- and
therefore `PoracleContext` -- alive, and the list alone does not release them. Its item carries seven fields
(`admin_disable`, `current_profile_no`, `enabled`, `id`, `language`, `name`, `type`) and the admin grid also
renders `disabled_date` and `notes`. Both come back from `GET /v2/humans/{id}` for a single human and neither
from the list, and per-id is not an option against 2,333 humans. Filed as
[#229](https://github.com/jfberry/PoracleNG/issues/229) and added in
[PR #230](https://github.com/jfberry/PoracleNG/pull/230), which also confirms `type=webhook` is the exact
stored value `GetWebhooksAsync` would filter on.

`GetByIdsAsync` and `DeleteUserAsync` would work against the list as it already stands, and are deliberately
not moved ahead of `GetAllAsync`: neither deletes anything while the repository survives for the third
method, so moving them early buys a capability-gated second path and no removal. `ExistsAsync` is a separate
case and stays regardless -- `UserPurgeService` reads the database on purpose, because the proxy cannot tell
an account that is gone from a Poracle that is unreachable, and the caller turns that into a 404.

### Profile create returns no `profile_no`, and nothing can rename a profile

[#213](https://github.com/jfberry/PoracleNG/issues/213) -- fixed on `develop`: create returns the created
profile, and `PATCH` is a real PATCH that can rename.

`POST /api/v2/humans/{id}/profiles` answered `{"status":"ok"}`, and PoracleNG assigns the lowest free number
rather than max + 1, so the caller could not predict it. `PATCH .../profiles/{n}` required `active_hours` and
refused every other property, `name` included.

**Here: adopted**, in PoracleWeb.NET #891, behind `IPoracleV2SchemaService` -- which reads the target
instance's own `/openapi.json` and answers whether it carries each capability. Version would not do: the
branch reports `5.3.0`, no release carries it, and a fork that cherry-picks one fix reports whatever it likes.

Both fallbacks stay for released servers, and the rename gate is load-bearing rather than an optimisation:
`PATCH .../profiles/{n}` exists on 5.2.1, where `V2UpdateProfileBody` declares `active_hours` alone under
`additionalProperties: false`, so sending a name there is a 422 rather than an ignored field.
`ProfileNumbering.ResolveCreated` cannot be replaced by arithmetic either way, because PoracleNG assigns the
lowest free number.

### v2 `setAreas` keeps the v1 `userSelectable` filter

[#215](https://github.com/jfberry/PoracleNG/issues/215) -- answered on `develop`, incompletely. `setAreas`
reports what it stored against what it rejected, and takes an opt-in `trusted` flag. The flag does **not**
lift the `userSelectable` filter only, whatever the comment beside it says: it also bypasses the community
area restriction, which makes it unusable for the one call that needs it. See
[#228](https://github.com/jfberry/PoracleNG/issues/228) and the
[trusted setAreas](#trusted-setareas-bypass-userselectable-filter) gap above for the reproduction and for the
two further blockers the flag does not address.

Re-confirmed on 5.2.1 rather than taken from source: `POST /api/v2/humans/{id}/areas` with
`["<a user-drawn fence>", "aberdeen"]` stored `["aberdeen"]` -- silently, 200, exactly as v1 does.

**Here:** this is the [trusted setAreas](#trusted-setareas-bypass-userselectable-filter) ask above, the
keystone for deleting `IUserAreaDualWriter`. Every `HACK: trusted-set-areas` site stays -- not merely until a
release carries the flag, but until the flag's scope, a profile target and a trusted per-rule override write
all land together.

---

### Tracking create has no upsert path for natural-key types

**Status: closed by a schema change in 5.2.1.** Migration `000008_drop_tracking_unique_keys` (upstream
commit `5a3886a`, 2026-08-18, three days before the 5.2.1 version bump) drops both keys.

`lure` and `invasion` were the only types PoracleWeb tracks that carried a unique index over a natural key:

```
lure       lure_tracking(id, profile_no, lure_id)
invasion   invasion_tracking(id, profile_no, gender, grunt_type)
```

`HandleCreateLure` / `HandleCreateInvasion` treat a row as "already present" only when **every** field
matches, so changing a field *outside* the natural key -- distance, template or clean on a lure -- was not
recognised as an existing row, and the handler attempted an `INSERT` that collided with the index:

```
Tracking API: insert lure: Error 1062 (23000):
Duplicate entry '<id>-<profile_no>-<lure_id>' for key 'lure_tracking'
```

PoracleNG answered `500 {"message":"database error"}` and the edit was discarded, so the only way to edit
these two types was to delete the row first. That is what `NaturalKeyTrackingUpdate` does, at the cost of
rotating the `uid` on every edit and orphaning anything holding the old one -- quick-pick applied state
tracks uids. `ITrackedUidRemapper` moves that state across, so the rotation is handled rather than merely
known about.

**Here, still:** `LureService` tries the v2 PUT first and only falls back to delete-then-create when that
write is declined. `InvasionService` has no v2 path at all (see #209 above), so it takes the fallback every
time -- which on a 5.2.1-migrated database pays the uid rotation to avoid a collision that can no longer
happen.

Pokemon rotates too, for a different reason: on 5.2.0 and later its edits go through
`PUT /api/v2/humans/{id}/tracking/pokemon/{uid}`, whose engine is delete-then-insert. It used to be the one
type whose uid survived an edit.

---

## Summary Table

| Gap | Priority | Workaround in use | Status |
|-----|----------|-------------------|--------|
| Bulk distance update | High | Fetch all, modify, POST back | Open |
| Bulk clean toggle | High | Fetch all, modify, POST back | Open |
| Trusted setAreas | High | Direct-DB dual write (`IUserAreaDualWriter`) | **Still blocked.** Flag on `develop` ([#215](https://github.com/jfberry/PoracleNG/issues/215)) bypasses the community filter too ([#228](https://github.com/jfberry/PoracleNG/issues/228)); no profile target; per-rule override still refused |
| Dashboard counts | Medium | Single `GET /api/tracking/all` call | Open, returns full payloads |
| Admin delete all alarms | Medium | Fetch UIDs per type, bulk delete each | Open |
| Admin list / delete human | Medium | `HumanRepository` direct DB | Routes on `develop` ([#214](https://github.com/jfberry/PoracleNG/issues/214)); list projection too thin to back the admin grid until [#229](https://github.com/jfberry/PoracleNG/issues/229) lands |
| v2 pokemon catch-all unwritable | Medium | `pokemon_id = 0` rules take the v1 write path | [#227](https://github.com/jfberry/PoracleNG/issues/227): `minimum: 1` refuses a rule PoracleNG itself stores. Our half of it -- writing `pvp_ranking_best = 0` -- fixed in PoracleWeb.NET #895 |
| Profile delete cascade | -- | None needed | PoracleNG cascades; verified upstream |
| Atomic profile switch | Low | Already in PoracleNG | **Adopted** |
| Atomic area update | Low | Already in PoracleNG | **Adopted** (admin areas only) |
| NULL field defaults | Low | Handled by PoracleNG `cleanRow()` | Resolved by the proxy migration |
| monsters.go COALESCE | -- | None needed | Fixed upstream; `template` is COALESCE'd on `main` |
| available_languages not readable | -- | None needed | **Adopted** on 5.2.1 ([#194](https://github.com/jfberry/PoracleNG/issues/194)) |
| disabledHooks omits fort | Low | Second config call, pre-5.2.1 servers only | Fixed in 5.2.1 ([#195](https://github.com/jfberry/PoracleNG/issues/195)) |
| Natural-key create collision | -- | Delete-then-create (`NaturalKeyTrackingUpdate`) | Keys dropped by 5.2.1 migration 8 |
