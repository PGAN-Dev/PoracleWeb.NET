# PoracleNG v2 API Review & PoracleWeb.NET Migration Plan

*Lead architect synthesis of architecture, db-elimination, tracking-types, performance, security, testing, optimization, and source-verification (researcher) reviews of PoracleNG v2 ([issue #138](https://github.com/jfberry/PoracleNG/issues/138) / [PR #139](https://github.com/jfberry/PoracleNG/pull/139), "huma" framework).*

> Companion to [`poracleng-enhancement-requests.md`](poracleng-enhancement-requests.md). That doc tracks v1-era workaround gaps; this doc evaluates whether v2 closes them and what we must still ask for. **PR #139 is OPEN — wire shapes are not yet frozen.**

---

## Executive Summary

PoracleNG v2 is a strong, well-shaped API surface that PoracleWeb.NET should adopt. The full human-scoped **snapshot** (`GET /api/v2/humans/{id}/tracking` → `{human, tracking, profiles, locations, summaries}`) collapses our dashboard/bootstrap reads into one round-trip, the **wrapper-free typed bodies** delete reams of `JsonElement` unwrap plumbing, and **strict typing + RFC 9457** errors are exactly the defense-in-depth that the March 31 NULL-template incident demanded.

But the headline goal of this review — **eliminate all direct access to the Poracle DB** — is **NOT achievable on v2 as currently designed.** The researcher verified against PR #139 source that the three load-bearing direct-DB touchpoints are each blocked by a missing endpoint or an unbypassable filter:

1. **Trusted setAreas (the `IUserAreaDualWriter` HACK) — NOT closed.** `v2_humans.go registerV2HumanSetAreas` mirrors v1 `HandleSetAreas`: for non-admins it skips every fence where `!f.UserSelectable`. Our user-drawn geofences are served `userSelectable=false`, so their names are still silently dropped. The new per-rule `override_areas` field does **not** rescue this — `validateOverrideFields` (tracking.go) checks each override against `GetAvailableAreas`, which for non-admins also excludes `userSelectable=false` fences (`bot/area_logic.go`), returning a 400. **This is the single most important blocker and it requires a new PoracleNG capability.**

2. **Admin list-all-humans, batch name/avatar resolve, full-purge delete — NONE exist in v2.** All v2 human endpoints are single-`{id}`-scoped. These three keep `HumanRepository` (and therefore `PoracleContext` and the entire Poracle-DB `Data` dependency) alive.

Net consequence: **without three explicit asks to jfberry, the keystone deletion (`PoracleContext` + the 10 alarm entities, ~800 LOC) cannot happen.** What we *can* delete unconditionally on v2 is the proxy unwrap helpers, `StripUidZero`, `PoracleJsonHelper`'s coercion machinery, and the dead `ProfileRepository` / dead `HumanRepository` methods — a real but smaller win (~600–700 LOC).

**Recommended posture:** adopt v2 behind a `Poracle:ApiVersion` flag, migrate reads first (snapshot), then writes after a strict-payload audit, while sending jfberry the three High-priority asks below (trusted setAreas, admin list, batch resolve). Treat v2 wire shapes as **not yet frozen** — PR #139 is still OPEN.

> **Source-verified against branch `huma-api-migration` (PR #139):** the `userSelectable` filter in `registerV2HumanSetAreas` (`v2_humans.go:518-522`), the `override_areas` → `GetAvailableAreas` gate (`tracking.go:266,322`), the absence of any admin list/delete-human endpoint (full `RegisterV2Humans` set), the `active_hours` `day` 0-6/Sun=0 schema with no cross-midnight (`v2_profiles.go:43,71`), and `blocked_alerts` as a read-only field on the human GET. **Correction vs our older gap-tracker:** `monsters.go` now already does `COALESCE(template, '') AS template` on this branch — the template crash vector is closed; only `ping` is still selected raw (see ask #4).

---

## What v2 Gets Right (for us)

- **Full snapshot in one call.** `GET /api/v2/humans/{id}/tracking` (+ `?all_profiles=true`) folds human, areas, profiles, locations, all tracking, and summaries into one typed response — replaces our `/auth/me` + `/api/dashboard` + `/api/areas` + `/api/profiles` + `/api/locations` fan-out. Confirmed to cover `DashboardService`'s count needs.
- **No wrappers.** Typed bodies directly — deletes the `{human:{...}}` / `{profile:[...]}` / `{type:[...]}` / `{status:ok}` unwrap branches in `PoracleHumanProxy`/`PoracleTrackingProxy` and the `DeserializeHuman`/`DeserializeProfiles` hand-parsers.
- **Strict types, no coercion.** Game-master ids as ints, fixed categories as string enums, flags as bools. Lets us drop the defensive `JsonValueKind` switches (and the IDE0072 suppressions) and the `PropertyNameCaseInsensitive` tolerance.
- **`clean` split into independent booleans (`clean`/`edit`/`summary`).** Genuinely better than the 3-bit bitmask we compose client-side — we want it, provided bot-set bits survive partial writes.
- **POST-array create keeps the `{created,updated,unchanged}` diff** — matches today's `TrackingCreateResult` flow; we depend on the created UID round-tripping for the optimistic UI.
- **RFC 9457 problem+json** — a structured error contract we can map to typed exceptions, directly addressing the class of failure that motivated the proxy migration.
- **No `uid:0` sentinel** — explicit POST(create)/PUT(replace)/DELETE removes the `StripUidZero` magic.
- **Saved-locations editor** (`PUT /{label}`) and **typed `active_hours`** are net-new capabilities we can surface.

---

## Direct-DB Elimination Scorecard

| # | Touchpoint (file) | What it does | v2 status | What's needed to eliminate |
|---|---|---|---|---|
| 1 | `UserAreaDualWriter` + 6 `UserGeofenceService` callsites + `AreaController.UpdateAreas` merge-back | Direct dual-write of `humans.area` + `profiles.area` because setAreas strips `userSelectable=false` names; forces manual `ReloadGeofencesSafeAsync` | **NO** (verified: v2 setAreas mirrors v1; `override_areas` blocked by same filter) | **Trusted setAreas variant** (secret-gated, bypasses `userSelectable`, per-profile + all-profiles, triggers internal reloadState) — ask #1 |
| 2 | `HumanRepository.GetAllAsync` → `AdminController.GetAllUsers` | Admin user-list enumerates whole humans table | **NO** | `GET /api/v2/humans` paginated admin list — ask #2 |
| 3 | `HumanRepository.GetByIdsAsync` → `UserGeofenceService.GetAllWithDetailsAsync` | Batch-resolve owner/reviewer names for admin geofence submissions UI | **NO** | Batch human resolve endpoint — ask #3 |
| 4 | `HumanRepository.DeleteUserAsync` → `AdminController.DeleteUser` | Full account purge | **NO** | `DELETE /api/v2/humans/{id}` cascade — ask #4 |
| 5 | `UserGeofenceService` non-active-profile cleanup (`RemoveAreaFromAllProfilesAsync`, spans every `profiles.area`) | Remove deleted geofence from all profiles | **NO** | Trusted setAreas with `all_profiles` targeting — folded into ask #1 |
| 6 | `HumanRepository.GetByIdAndProfileAsync` (UserGeofenceService submission display name, :292) | Read human name for Discord forum post | **PARTIAL** | `GET /api/v2/humans/{id}` covers the read; swap now (independent of trusted-areas) |
| 7 | `LocationController.UpdateLanguage` → `HumanService.UpdateAsync` → `HumanRepository.UpdateAsync` | Generic direct-DB human update for language | **YES** | `POST /api/v2/humans/{id}/language` exists — swap now |
| 8 | `ProfileRepository` (all methods) + `ProfileService.Create/Update/Delete` | Dead code — controllers already use proxy | **YES** | Delete outright; v2 profile endpoints exist |
| 9 | `HumanRepository.GetByIdAsync`/`ExistsAsync`/`CreateAsync` | Dead — service uses proxy | **YES** | Delete dead methods |
| 10 | ~~`HumanRepository.DeleteAllAlarmsByUserAsync`~~ | Dead — service loops via proxy | **DONE** | Deleted in #707 (its `ExecuteDeleteAsync` calls could not run on MariaDB anyway); one-shot purge endpoint is still a nice-to-have (ask #6) |
| 11 | `DashboardService.GetAllTrackingAsync` (proxy, not direct DB) | Fetches full payloads to count | **YES** | Snapshot/counts — perf win, not DB elimination |

**Verdict:** rows 6–11 close on v2 (some are pure dead-code deletion). Rows 1–5 — the entire reason `PoracleContext` still exists — require **four new PoracleNG capabilities.** Until those land, `HumanRepository` shrinks to ~3 admin methods that still pin `PoracleContext`, and `UserAreaDualWriter` stays in full.

---

## Answering the maintainer's 6 open questions

- **Q1 (collection scoping):** Keep the **human-scoped sub-resource** `/api/v2/humans/{id}/tracking[/{type}][/{uid}]?profile={n}`. PoracleWeb is always single-human-scoped; a flat `?user=&profile=` collection would force `user` into every query and lose the ownership boundary. We do **not** need a `/users/{id}/` alias.
- **Q2 (create response):** **Keep `{created,updated,unchanged}`** — we depend on the created UID round-tripping. **Enhancement:** return the *full resulting rule objects* in each bucket (with applied defaults), not just UIDs, so we can hydrate cards without a follow-up GET.
- **Q3 (int/enum split):** **Correct as-is.** Keep `team`/`gender`/`rsvp_changes`/`fort_type` as string enums, game ids as ints, flags as bools. The `clean`→3-booleans split is good; confirm all three are independently settable and bot-set bits survive partial writes.
- **Q4 (invasion two-axis + incident):** Two-axis fits our UI (we already carry `typeId`). **Caveat:** moving from `grunt_type` *string* to integer `type_id`/`grunt_id` requires a **published id dictionary** so our Angular table doesn't drift. Confirm `incident.display_type` is a *different* dictionary than invasion `type_id`, and that read-back returns the same axis we wrote.
- **Q5 (discrete action endpoints + typed active_hours):** Discrete endpoints suit us. Typed `active_hours` is good — but **day numbering flips from our 1-7 Mon-Sun to v2's 0-6 Sun=0**, and v2 forbids cross-midnight ranges. Document `day=0=Sunday` and `step`/`end_hours`/`end_mins` semantics authoritatively.
- **Q6 (v1 dependencies missing in v2):** Yes — admin list-all-humans, admin full-purge delete, and batch human resolve. These are hard blockers to a DB-free PoracleWeb.

---

## New Capabilities to Build (unblocked by v2)

- **`incident` alarm type** — genuinely new (facade over invasion, `display_type` int). Full four-layer wiring per CLAUDE.md (model + `IncidentCreate/Update`, controller with `[RequireFeatureEnabled]`, `DisableFeatureKeys` entry, Angular module/route/guard/nav). **Do not under-scope as "another invasion"** — `display_type` is a different dictionary. Effort: L.
- **`fort` type** — we already have `FortChange` model/UI; reconcile `include_empty` default (v2 = TRUE, ours = 0) and convert int flags to bools. Effort: S–M.
- **`maxbattle`** — model/UI exist; add `gmax` bool + `move` int coverage. Effort: S.
- **`pokemon.pvp_ranking_evolution`** (int 0/2/3) — additive field on `MonsterCreate/Update`. Effort: S.
- **Saved-locations editor** — v2 `PUT /{id}/locations/{label}` enables in-place edit of saved locations. Effort: S.
- **`blocked_alerts`** — read-only per-user authz signal (derived from Discord roles). Consume from the snapshot to hide/disable alarm-type nav + add-dialogs per user (distinct from global `disable_*` gates). Map `monster`→`pokemon`. Effort: M.

---

## Risks & Gotchas

1. **`override_areas` is a trap.** Per-*rule*, not the human/profile area subscription list, and source-verified to be gated by the same `userSelectable` filter. Adopting it expecting a filter-bypass would silently reintroduce the geofence-persistence regression. **Do not delete `UserAreaDualWriter` until a confirmed trusted human-level areas op exists.**
2. **PUT full-replace footgun.** v2 PUT resets omitted fields to defaults — incompatible with our `ApplyUpdate` null-skip merge. A naive partial PUT silently zeroes IV/CP/PvP/template — echoing the NULL-template incident. Route single-field edits through POST-array-diff or send the complete object.
3. **`active_hours` day off-by-one.** 1-7 Mon-Sun → 0-6 Sun=0 is a silent, high-blast-radius corruption. v2 also bans cross-midnight ranges. Needs a translation shim **and dedicated round-trip tests** (the existing suite tests string coercion `'09'`/`'00'` and 1-7 numbering — these *invert* under v2 and must be rewritten, not find-replaced).
4. **Strict 422 rejection.** Unknown fields and wrong types hard-fail. Our snake_case proxy currently sends ints for enums and string-coerced hours in places — a full payload audit is mandatory before flipping writes.
5. **Snapshot payload bloat.** For power users (500+ alarms) the snapshot is hundreds of KB. Do **not** use it for the lightweight badge path — use a counts projection/selective includes, keep `include_descriptions` OFF except on the Profiles-overview page, add client-side dedupe + ETag/304 if offered.
6. **RFC 9457 reflected input.** `errors[].value`/`detail` echo submitted input — sanitize at the proxy boundary before surfacing to the SPA.
7. **Trust model unchanged.** `X-Poracle-Secret` = full impersonation of any human id. If admin list/delete/resolve are added but reachable without the secret (e.g. via public `/docs`), they become mass-enumeration/deletion vulns. **Verify secret-gating before adopting.**
8. **PR #139 is OPEN.** Wire shapes may shift; pin a vendored `openapi.json` as a golden contract fixture and treat shapes as not-yet-frozen.
9. **`monsters.go` COALESCE — `template` fixed on the v2 branch, `ping` still raw.** Verified on `huma-api-migration`: `COALESCE(template, '') AS template, clean, ping,` — the template DoS vector (one NULL row crashing state reload for everyone) is closed there. `ping` remains raw; if nullable it's the same crash class. Confirm the template fix is in the release line we actually deploy (our older gap-tracker still lists it as live), and COALESCE `ping` for parity.

---

## Appendix A — Prioritized API Change Requests to PoracleNG (feedback for issue #138)

The three **High** asks (trusted setAreas, admin list, batch resolve) are the gating set for full DB elimination. The `monsters.go` item is now **Medium** — `template` is already COALESCE'd on the v2 branch (verified), leaving only a `ping` parity nit.

| # | Priority | Ask | Proposed shape |
|---|---|---|---|
| 1 | **High** | **Trusted setAreas variant (bypass userSelectable filter) — keystone blocker.** Only thing that lets us delete `IUserAreaDualWriter` + 6 callsites + the merge-back + manual reloads. The filter is a browser-hack defense, meaningless against a caller holding `X-Poracle-Secret`. | `POST /api/v2/humans/{id}/areas?trusted=true&profile={n\|all}` body `{areas:[...], mode:add\|remove\|replace}`; secret-gated; runs `reloadState` internally. **Alternative (most defensible): per-fence `ownedBy:humanId`** so the intersection admits owned fences regardless of `userSelectable`. |
| 2 | **High** | **Admin list-all-humans (paginated).** Keeps `HumanRepository.GetAllAsync` → `PoracleContext` alive. | `GET /api/v2/humans?limit=&offset=&search=&community=` → `{humans:[{id,name,type,enabled,admin_disable,current_profile_no,language,last_checked,disabled_date,notes}], total}`. Secret-gated. Must include `last_checked`/`disabled_date` (admin grid shows them). |
| 3 | **High** | **Batch human display-name/avatar resolution.** `GetByIdsAsync` resolves N owner+reviewer ids in one query for the admin submissions UI; per-id fan-out is a perf regression. | `GET /api/v2/humans?ids=a,b,c` → `[{id,name,type,avatar?}]` or `POST /api/v2/humans/resolve {ids:[...]}`. Minimal projection. Secret-gated. |
| 4 | Medium | **`monsters.go` COALESCE parity — `template` already fixed on `huma-api-migration`, `ping` still raw.** Verified: line 97 now reads `COALESCE(template, '') AS template, clean, ping,`, so the original template crash vector (the incident that motivated our proxy migration) is **closed** there. But `ping` is still selected raw — if nullable, that's the same `converting NULL to string` crash class. | Confirm `ping` is `NOT NULL` in schema, or `COALESCE(ping,'') AS ping` for parity with the other tracking files. Also confirm the template fix is in the release line we deploy, not only the v2 branch. |
| 5 | Medium | **Admin delete-human (full purge).** Last admin write with no v2 mapping. | `DELETE /api/v2/humans/{id}` → `{deleted:{human,profiles,tracking}}`; cascades all profiles/tracking/locations/roles. Secret-gated, idempotent. |
| 6 | Medium | **Return full rule objects in `{created,updated,unchanged}`.** Eliminates our post-create re-fetch. | Each bucket returns full rule (incl. uid + applied defaults). Confirm POST-array upsert preserves omitted fields (vs PUT full-replace). |
| 7 | Medium | **Publish integer dictionaries** (invasion `type_id`/`grunt_id`, incident `display_type`). Our hardcoded Angular tables must match exactly. | In `openapi.json` or `/api/v2/dictionaries`. Confirm read-back returns the same axis written; clarify whether invasion `type_id` == incident `display_type` dictionary. |
| 8 | Medium | **Document sentinel→null mapping + `active_hours` day convention.** `distance=0`='use areas' is meaningful, not absent. | Annotate each field's null-meaning/omit-default in OpenAPI; flag `distance:0`. Document `day=0-6 (0=Sunday)`, `step`/`end_*`, no-wrap. Echo-on-read. |
| 9 | Medium | **Bulk field-update (PATCH) for distance/clean.** Replaces O(N) fetch-modify-POST; PUT full-replace makes per-uid updates more dangerous. | `PATCH /api/v2/humans/{id}/tracking/{type}?profile=N` body `{uids?:[...]\|all:true, distance?, clean?, edit?, summary?}` → `{updated:[uids]}`. |
| 10 | Medium | **Surface `blocked_alerts` in the snapshot + confirm reject semantics.** Per-user authz we should respect. | Include in `GET …/tracking`; document `monster=pokemon`/`specificgym`/`specificstation`; confirm POST of a blocked type returns 403/422 (not silent-unchanged). |
| 11 | Medium | **Versioned `/api/v2/openapi.json` as a golden contract fixture.** Our mocked tests can't catch wire drift (the NULL-template blind spot). | semver/etag, published per release, so we vendor it and add a build-time contract test. |
| 12 | Low | **Confirm profile-DELETE cascade + single-call all-types purge.** | Document `DELETE …/profiles/{n}` cascades tracking, reassigns `current_profile_no`, refreshes `humans.area`, rejects last profile (422). Add `DELETE …/tracking?all_profiles=true`. |
| 13 | Low | **Sanitize/document RFC 9457 error echo.** `errors[].value`/`detail` can reflect attacker input. | Document they contain only caller-submitted values; optional `X-Poracle-Verbose-Errors:false`. |

---

## Appendix B — PoracleWeb.NET Migration Sequence

**Phase 0 — Unconditional, no API dependency (do now):**

| Step | Detail | Effort |
|---|---|---|
| 0a | Swap `LocationController.UpdateLanguage` to proxy `SetLanguageAsync` (→ `POST /api/v2/humans/{id}/language`). Removes the last live `HumanRepository.UpdateAsync` caller. | S |
| 0b | Swap `UserGeofenceService:292` display-name read to proxy `GetHumanAsync`. | S |
| 0c | Delete dead `ProfileRepository`/`IProfileRepository` + `ProfileService` CRUD; dead `HumanRepository` methods (`GetByIdAsync`/`ExistsAsync`/`CreateAsync`; `DeleteAllAlarmsByUserAsync` already gone in #707) + `EnsureNotNullDefaults`. Update test mocks. Keep only `GetAllAsync`/`GetByIdsAsync`/`DeleteUserAsync` until v2 admin endpoints land. | M |

**Phase 1 — v2 read path (behind `Poracle:ApiVersion` flag):**

| Step | Detail | Effort |
|---|---|---|
| 1a | Collapse `IPoracleTrackingProxy` + `IPoracleHumanProxy` into one `IPoracleV2Client`. Point reads at `/api/v2`, deserialize typed bodies, delete all unwrap branches + hand-parsers. Keep v1 as runtime fallback. | L |
| 1b | RFC 9457 → typed `PoracleApiException`; handle 400→422; **sanitize `errors[].value`/`detail`** before surfacing to SPA. | M |
| 1c | Add `GetSnapshotAsync`; re-point `DashboardService`, `AreaController.GetSelectedAreas`, profile/location reads, `/auth/me` resync at the cached snapshot. `include_descriptions` OFF; counts projection for the badge path. | M |

**Phase 2 — v2 write path (after strict-payload audit):**

| Step | Detail | Effort |
|---|---|---|
| 2a | Strict-payload audit across all create/update builders: correct JSON types, no unknown fields, int↔enum translation, sentinel↔null mapping (**preserve `distance=0`=use-areas**). | L |
| 2b | Keep POST-array upsert; **never adopt PUT full-replace for partial edits**. Verify `CleanFlags` bit preservation on the 3-boolean path. Drop `StripUidZero`. | M |
| 2c | `active_hours` 1-7↔0-6 shim + typed-schema validator rewrite; update Angular day picker + utilities; drop string coercion. **Rewrite (don't find-replace) the active_hours tests.** | L |
| 2d | Vendor `openapi.json` contract test; re-point proxy/service tests at `/api/v2`; shared typed-fixture builder to replace ~150 snake_case `JsonElement` fixtures. | L |

**Phase 3 — gated on API asks landing:**

| Step | Detail | Effort |
|---|---|---|
| 3a | **GATED on trusted setAreas:** replace all 6 `UserAreaDualWriter` callsites + merge-back, delete the writer + interface + tests + manual reloads. Until then, keep the HACK + add a regression-lock test proving v2 setAreas still strips `userSelectable=false`. | L |
| 3b | **GATED on admin endpoints:** re-point `AdminController.GetAllUsers`/`DeleteUser`/`GetAllWithDetailsAsync` at proxy; delete `HumanRepository`, then **`PoracleContext`, the 10 Poracle-DB entities, the connection string, and the Human/Profile half of `EntityMappingExtensions`.** Keep `PoracleWebContext` untouched. | L |

**Phase 4 — new capabilities:** `incident` (four-layer, distinct dictionary), invasion two-axis, `pvp_ranking_evolution`, `fort.include_empty` reconcile, saved-locations editor, `blocked_alerts` consumption. Effort: L.

**Phase 5 — gated on bulk PATCH:** replace fetch-modify-POST across 8 alarm services + `CleaningService` with one PATCH; reconcile `poracleng-enhancement-requests.md` with v2. Effort: M.
