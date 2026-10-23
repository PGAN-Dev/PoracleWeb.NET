# PoracleNG 5.4.0: v2 only, no Poracle database

**Date:** 2026-10-21
**Tracker:** #996 (sub-issues #838, #860, #993, #994, #995)
**Upstream:** jfberry/PoracleNG#328, tested end to end on 2026-10-21 and signed off on the PR. It ships as 5.4.0.

## Goal

PoracleWeb.NET talks to PoracleNG only through `/api/v2` and PoracleNG's unversioned routes (config, masterdata, geofence reload, test alerts, geocode, health), and never opens a connection to Poracle's database. When this is done, `PoracleContext`, `ConnectionStrings:PoracleDb` and the `DB_*` settings no longer exist.

Success means:

- No v1 tracking, human or profile call remains (`/api/tracking`, `/api/humans`, `/api/profiles`).
- No direct read or write of a Poracle table remains, and a test fails the build if one comes back.
- Every feature that works against PoracleNG 5.2.1 today works against 5.4.0, verified on the dev-01 5.4.0 bed (`poracle-ng-540`, :3046) by the API harness, an e2e pass and a browser pass.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Minimum PoracleNG | **5.4.0**, hard | "No DB access" is only reachable if the fallbacks for older servers are deleted, not kept beside the v2 paths. Self-hosters must upgrade PoracleNG first. |
| Where the work lands | **Directly on `develop`** | Chosen over a long-lived integration branch. Consequence: `develop` can't be released until 5.4.0 is tagged and prod runs it, and any prod fix in the meantime is a hotfix branched from `main`. |
| Tracking shape | **Translate at the proxy** (approach A) | Models and the SPA keep today's shape. `TrackingV2Translator` maps v2's `null` to our own "no filter" default on read and back to `null` on write. Keeps "the v1 shape is the single internal currency". Null-native end to end (approach B) would rewrite 20 dialogs, every guard and quick-pick storage at once. |
| Deleting a user fence that alarms are limited to | **Ask in the delete dialog** | Default: delete those alarms too. Option: keep them, widened to the profile's areas. Admin delete gets the same choice. |
| Rebrand (Chimecho) | **Not in this release** | It would add rename noise to the riskiest release so far. Do it as the next release. |

## Before any sub-project merges

1. Repoint `poracleweb-dev` (the `:beta` instance on dev-01) from PoracleNG 5.2.1 (:3042) to the 5.4.0 bed (:3046) and its database `poracle_dev_540`. The first commit requiring 5.4.0 otherwise breaks it at startup.
2. Fold open PR #992 (upstream status doc) into sub-project 1 and close it.

## Sub-project 1: 5.4.0 minimum, human and profile fallbacks deleted

- `PoracleServerProfile.MinimumSupported` becomes 5.4.0. The startup check keeps its behaviour (log an error, stay up) so an admin can still reach the UI, which already shows "below minimum".
- Check each `PoracleV2Capabilities` flag against the 5.4.0 bed's `/openapi.json` before deleting its branch. Delete these flags and their false branches: `ProfileRename`, `ProfileCreateReturnsNumber`, `AdminHumanRoutes`, `SetAreasReportsRejected`, `AvailableAreas`, `ProfileList`, `ProfileDelete`, `ProfileCopy`, `ProfileScopedGeography`, `V2LocationLatitudeLongitude`, `TrackingPing`. `TrustedSetAreas` stays until sub-project 2, `InvasionGruntType` until sub-project 3.
- `PoracleHumanProxy.TryV2Async` no longer falls back to v1. A plaintext 404 from a v2 human route is an error. `CreateHumanAsync` moves to `POST /v2/humans`.
- Delete: `ProfileNumbering`'s list-and-diff, `ProfileRepository` (rename and new-profile geography), `HumanRepository`, the per-route "v2 absent" cache flags, the 5.1.0 Pokestop Events probe in `UpstreamFeatureFlagService`, the legacy geocoder fallback, and `Poracle:TrackingApiVersion` / `PORACLE_TRACKING_API_VERSION`.
- The `IsSupported`-gated features (mutes, place move, quest pokecoins, costume) are always on. Delete `MuteCapabilityService`, `PlaceUpdateCapabilityService`, `QuestPokecoinCapabilityService` and `CostumeCapabilityService`, the API fields that only report them, and the SPA conditionals that hide their controls. The ping field keeps only its "webhook only" condition.
- Keeps, for later sub-projects: `IUserAreaDualWriter`, all tracking v1 paths, tolerance for `area` arriving as a JSON string (v1 reads still produce it), `pweb_settings`.

## Sub-project 2: area writes on v2 (#838)

**Authorisation.** `trusted` lets PoracleNG accept fences that aren't user-selectable, so PoracleWeb.NET authorises names first. A name may be written if it is any of:

1. listed with `userSelectable: true` for this human by `GET /v2/humans/{id}/areas` (`V2AreaInfo` carries the flag),
2. one of the user's own drawn fences (`user_geofences.koji_name` for this user),
3. already held by the user on that profile.

Anything else is refused with its name in the error. Rule 3 exists so legacy names can't be stripped by an ordinary save. Before merging, query production for held names outside rules 1 and 2 and record the count in the PR.

**Operations.**

| Operation | v2 call | Replaces |
|---|---|---|
| Areas page save | `POST /v2/humans/{id}/areas {areas, trusted:true}` | `SetAreasAsync` + `PreserveOwnedAreasInHumanAsync` |
| Create fence | save row, reload geofences, read active list, post list + name | `AddAreaToActiveProfileAsync` |
| Activate / deactivate on a profile | read that profile's list, add or remove, post with `trusted` and `?profile=` | `AddAreaToActiveProfileAsync`, `RemoveAreaFromActiveProfileAsync` |
| Delete fence (user or admin) | `DELETE …/areas?name=&all_profiles=true&dry_run=true`, then per the user's choice bulk-delete the `broadened` uids, then the real DELETE | `RemoveAreaFromAllProfilesAsync` |
| Rename / approval | `PATCH …/areas?from=&to=&all_profiles=true&trusted=true`, after a reload so `to` is loaded | `RenameAreaInAllProfilesAsync` |

**Delete dialog.** New endpoint `GET /api/geofences/custom/{id}/delete-impact` returns the dry run's `broadened` count by type. When it's above zero the dialog offers "Delete those alarms too" (default) or "Keep them, using my profile's areas". Alarms whose override also names other areas lose only this name either way (upstream behaviour).

**Failure handling.** If PoracleNG rejects a new fence because it hasn't loaded it, the fence stays saved and inactive, and the response says so; the UI shows "Saved, but it couldn't be switched on yet. Use the toggle to retry." No direct-DB fallback. A 422 "area not permitted" becomes a 400 with PoracleNG's reason.

**Deleted.** `IUserAreaDualWriter` / `UserAreaDualWriter`, the `TrustedSetAreas` flag, `IAreaSecurityPolicyService` and its "confirmed off" gate (the 2026-10-21 bed run showed `trusted` correct with `area_security` on), and every `HACK: trusted-set-areas` site except `UserOwnedOverrideAreaProxy`, which waits for v2 creates in sub-project 3.

**Verify first.** That bulk DELETE reaches uids on non-active profiles (v2 tracking deletes are profile-scoped; check `?all_profiles=true`).

## Sub-project 3: tracking on v2 (#860)

**Reads.** `GetByUserAsync` uses `GET /v2/humans/{id}/tracking/{type}`; the dashboard and cleaning use the snapshot `GET /v2/humans/{id}/tracking` (`?all_profiles=true` where needed). `TrackingV2Translator` gains a read direction to the v1-shaped row: enums to ints (gender, team, rsvp), booleans to 0/1, `clean`/`edit`/`summary` to the bitmask, `change_types` to a JSON string, and `null` to our default for that field. The defaults table comes from the `*Create` DTO initialisers. A test checks every nullable property in each v2 rule schema has an entry, both directions.

**Writes.** A value equal to our default is sent as `null`. This also fixes boss-specific raid and max-battle edits, which today send `level: 9000` past v2's maximum of 90 and fall back to v1.

**Creates.** `POST ?dry_run=true` first. Anything in `updated` means the Add would take over another rule: refuse with today's message. `unchanged`: answer as an identical Add does today. Otherwise commit. If the commit reports `updated` (a write landed between the calls), restore the other rule from `previous` with a PUT and refuse. Bulk creates (quick-pick apply, import, duplicate) dry-run the batch and commit only the safe rules. Deleted: `EnsureNoMergeIntoAnotherAlarmAsync`, `CountUpdatableDifferences`, `UpdatableFieldsByType`, the reconciler's v1 clean-up, and lure's and invasion's own sibling checks.

**Edits.** `TryReplaceV2Async` loses every v1 fallback. User fences in `override_areas` go with `?trusted=true` after sub-project 2's authorisation; `UserOwnedOverrideAreaProxy` is deleted.

**Bulk distance and cleaning.** `PATCH …/tracking/{type} {uids, set}`. Upstream's `invalid` refusals map onto `skippedAreaScoped` / `skippedPlaceScoped`; `conflict` refusals become a new `skippedConflict` list and one more line in the SPA's message. Deleted: max battle's delete-then-recreate, `DistanceRewrite`'s write-back, `EnsureBatchDoesNotCollapse`, `EnsureBatchDoesNotTakeOverOthersAsync`, cleaning's fetch-mutate-POST.

**Deletes.** Bulk DELETE sends the uid list in the body. Deleted: the 2,000-character fallback and the v1 fallback for a uid not on the active profile (#889). The controllers' active-profile check stays.

**Rows v2 can't represent.** Without v1 these become uneditable. Known: eggs with level 0, invasion gender 3, PvP league outside 0/500/1500/2500, 32 invasion rows (`metal` for `steel`; `kecleon`/`gold-stop`/`showcase` filed in the invasion table). First step: a production inventory per type. Then per case: repair by v2 PUT where the meaning is clear, ask jfberry where it isn't, or show the rule read-only with the reason. Never a direct DB write.

**Order.** One PR per type, each moving read, create, edit, bulk and delete together, each followed by an e2e run on the 5.4.0 bed: nest, gym, fort, lure, quest, egg, raid, max battle, invasion with Pokestop events, pokemon. `InvasionGruntType` and the grunt-name check go with invasion. The uid remapper stays: v2 still reports a new uid if keeping the old one fails.

## Sub-project 4: no Poracle database

- **`pweb_settings`.** Delete `PwebSettingEntity`, its repository and service, the legacy read in `SettingsMigrationService` and the startup `ALTER TABLE` (`Program.cs`). The upgrade guide says an install older than 0.6.0 must start any 2.x release once first, and that admins may drop the table. We don't drop it.
- **`PoracleContext`.** Delete it, its entities and registration, `ConnectionStrings:PoracleDb`, the `DB_*` bridge in `Program.cs` (`WEB_DB_*` stays), the startup check requiring it, its `setup.sh` questions, and its lines in `.env.example` and the compose example. An install still setting `DB_*` gets one info line at startup saying it's unused.
- **Guard.** `NoPoracleDatabaseTests` fails the build if `PoracleContext`, a `PoracleDb` connection string or a `DB_HOST` mapping appears under `Core/`, `Data/` or the API project.

## Docs (mkdocs site and CLAUDE.md)

The site deploys only from `main`, so each sub-project's PR updates its own pages alongside the code.

| Sub-project | Pages |
|---|---|
| 1 | `index.md`, `getting-started/*` (5.4.0 requirement); `architecture/poracleng-compatibility.md` (matrix becomes "requires 5.4.0, and why"); `features/quiet-periods.md` |
| 2 | `features/custom-geofences/*` (delete dialog with an anonymised screenshot, "saved but not switched on"); `koji-and-regions.md` (rename, approval) |
| 3 | `features/alarms.md` (overlap refusals, "skipped, would clash"); `architecture/poracleng-proxy.md`, `backend.md` |
| 4 | `configuration/reference.md` and setup pages (no `DB_*`); `architecture/database.md`, `overview.md`; `troubleshooting.md` ("PoracleNG below 5.4.0", "fence saved but not switched on") |

New page "Upgrading to PoracleNG 5.4.0" in the nav: upgrade PoracleNG first, `DB_*` unused, `pweb_settings` may be dropped, the 0.6.0 step, and what users will notice. The release notes link to it. `poracleng-enhancement-requests.md` and `poracleng-v2-review.md` move to an "Upstream history" nav section with a status refresh.

CLAUDE.md is pruned in each PR: the 5.1.0/5.2.1 passages (1), "User Geofence Persistence" and "Areas and Profile-Scoped Storage" (2), the guard and v1-shape sections (3), "NULL String Columns", the dual-write explanations and the `DB_*` prod-query recipe (4).

Before the release: a full docs audit against the code, and `mkdocs build --strict`.

## Testing, every sub-project

- Unit tests post the request shapes the callers really build and check them against a 5.4.0 openapi fixture.
- Every deleted fallback gets a test that the v2 call is the only call made.
- Every new refusal has a legitimate-case-still-passes test beside it (CLAUDE.md, "Fixing Defects Without Causing Them"). Watch each new test fail first.
- After each PR: the API harness (`smoke.py`) and the relevant e2e scripts (`/root/pweb-smoke/e2eB|e2eC|e2eD`) against `pweb-e2e540` and :3046.
- Before release: a browser pass over every page, and a regression lens over everything merged.

## Release

Blocked on: 5.4.0 tagged upstream, and prod's PoracleNG fork carrying it (the fork merge of #328 has 12 conflicts with the personal-shiny work and is its own task). Order on release day: PoracleNG first, then PoracleWeb.NET.

## Out of scope

- The rebrand. `docs/superpowers/plans/2026-10-20-rebrand.md` (untracked) lists `pweb_settings` and `PwebSetting*` among identifiers frozen forever; sub-project 4 deletes them, so that plan needs a refresh before it runs.
- Moving the unversioned routes; jfberry confirmed they are stable and get no v2 copies.
- `!script` dropping overrides (jfberry/PoracleNG#329), upstream's to fix.
