# PoracleNG 5.4.0 Tracking Reads and Deletes on v2 (Sub-project 3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every tracking read and delete uses PoracleNG's `/api/v2`. Callers keep receiving exactly the v1-shaped rows they get today, so nothing above the proxy changes.

**Architecture:** A read direction added to `TrackingV2Translator` turns each v2 rule into today's v1 row. `null` becomes our own "no filter" default for that field, and every property the translator doesn't model is carried through unchanged, so a later PUT can't wipe it. `PoracleTrackingProxy`'s three read methods call the v2 per-type list or the v2 snapshot and translate. Both delete methods become v2-only. Creates and edits stay as they are (sub-project 3b); bulk distance and cleaning stay as they are (3c).

**Tech Stack:** .NET 10 / xUnit + Moq, PoracleNG 5.4.0 (develop `715c0d7ea`).

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md`, "Sub-project 3". Approach A: translate at the proxy, the v1 shape stays the internal currency. It is phased by layer (3a reads/deletes, 3b creates/edits, 3c bulk, 3d repair and docs) rather than by type, as agreed with the user on 2026-10-21.

## Global Constraints

### v2 read shapes, verified on the 5.4.0 bed (2026-10-22)

| Route | Returns |
|---|---|
| `GET /api/v2/humans/{id}/tracking/{type}[?profile=N][&include_descriptions=true]` | `{ "rules": [ … ] }`; each rule has `uid`. No `id`, and no `profile_no` unless `all_profiles` is used. |
| `GET /api/v2/humans/{id}/tracking[?profile=N\|all_profiles=true][&include_descriptions=true]` | `{ human, locations, mutes, profiles, summaries, tracking }`. `tracking` is keyed by the 11 type names (`egg, fort, gym, incident, invasion, lure, maxbattle, nest, pokemon, quest, raid`), each an array or null. With `all_profiles=true` every rule also carries `profile_no`. |

### Read translation (v2 rule → v1 row), per type, exactly inverting the write side in `TrackingV2Translator`

- **Enums back to ints:** `gender` (pokemon `any/male/female/genderless` → 0–3; invasion `any/male/female` → 0–2), `team` (`harmony/mystic/valor/instinct/any` → 0–4, using the write side's map), `rsvp_changes` (`none/rsvp/rsvp_only` → 0–2).
- **Booleans to 0/1:** `exclusive`, `slot_changes`, `battle_changes`, `gmax`, `shiny`, `include_empty`.
- **`clean`/`edit`/`summary` to the bitmask** with `CleanFlags`, null counting as false. Fort has none of the three.
- **`change_types`:** the array becomes a JSON string.
- **Identity fields:** `uid`, and `profile_no` when present, are kept. `id` is set to the human id passed in. `description` is kept when present.
- **`null` → the default** in the table below.
- **Any property not in the type's table is copied through verbatim.** That covers future upstream fields and the PGAN fork's `personal_shiny` / `personal_background` (1,111 production rules). The write side must keep sending these back; it already copies unknown properties only where the field table allows, so add the carry-through to the write side too, gated on the server's own `/openapi.json` declaring the property (it declines today, which 3b removes).

### Null → default table (the `*Create` initialisers are ours; where they and the column default differ, the column default wins, per CLAUDE.md "Monster Filter Defaults Are Ours")

| Type | Defaults |
|---|---|
| pokemon | `max_iv 100`, `max_cp 9000`, `max_level 55`, `max_weight 9000000`, `max_atk 15`, `max_def 15`, `max_sta 15`, `pvp_ranking_worst 4096`, `pvp_ranking_best 1`, `costume 9000`, `size -1`, `max_size 5`, `rarity -1`, `max_rarity 6`, `min_iv -1` *(confirm the stored wildcard for `min_iv` against PoracleNG's read translation; `OmitWhenEquals` treats -1 as "no filter")*, every other integer `0`, `form 0`, `gender 0`, `pokemon_id` as stored |
| raid | `team 4`, `costume 9000`, `move 9000`, `evolution 9000`, `pokemon_id 9000`, `level 9000` *(v2 nulls the sentinel)*, others `0` |
| egg | `team 4`, others `0` (level is required, so never null) |
| maxbattle | `pokemon_id 9000`, `level 9000`, `move 9000`, `evolution 9000`, others `0` |
| gym | `team 4`, others `0` |
| fort | `include_empty false` *(v1's default; v2's is true, so a null must not become true)*, `change_types "[]"` |
| quest, invasion, lure, nest, incident | `0` / empty string per the `*Create` model |
| all | `template ""`, `ping ""`, `override_location_label ""`, `override_areas null` |

**Required test: every nullable property of every `V2*Rule` schema in `Tests/Pgan.PoracleWebNet.Tests/Fixtures/poracleng-openapi-5.4.0.json` has a default entry, and every default entry names a real schema property.** Where this table and PoracleNG's own read translation disagree on a field, follow PoracleNG's translation (`processor/internal/api/v2_*.go`) and record the difference in the task report.

### Other rules

- **Legacy zeros in production** (`pvp_ranking_best 0`: 12,734 rules; `pvp_ranking_worst 0`: 6; `size 0`: 2) read back from v2 as null, so they become the defaults 1/4096/-1. Matching doesn't change. The equivalence check in Task 6 whitelists exactly these.
- **Profile scoping:** today's reads are active-profile only. Keep that. Don't start honouring the ignored `profileNo` parameters in services (that would change results). `GetAllTrackingAllProfilesAsync` uses `all_profiles=true&include_descriptions=true`.
- **Invasion and incident:** v2's `invasion` list excludes Pokéstop events (`kecleon`, `gold-stop`, `showcase`; 38 production rules), which v1's invasion list included. Every consumer that relied on seeing them in invasion reads must be checked:
  - `InvasionService.OwnRowPredicateAsync` / `BelongsToPokestopEventsAsync`
  - `CleaningService` partition
  - `DashboardService` invasion filter
  - `HumanService.DeleteAllAlarmsByUserAsync`
  - `QuickPickService`

  Each must either read `incident` too or demonstrably not need it.
- **Errors:**
  - A v2 read for a human PoracleNG doesn't have → `AccountGoneException`, as today.
  - A plaintext 404 → `PoracleUnsupportedException(feature, "PoracleNG 5.4.0")`.
  - A delete of a uid that isn't there (problem+json 404) → treated as already gone and logged, matching v1's old idempotence. No exception.
- **House rules:**
  - Write "PoracleWeb.NET" in full. No Claude trailers or links.
  - Preserve line endings (`git diff --numstat`).
  - Keep the CHANGELOG under `[Unreleased]`.
  - Every refusal gets a legitimate-case test.
  - Watch new tests fail first.
  - Backend tests: `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS= dotnet test Tests/Pgan.PoracleWebNet.Tests --no-restore`.

## Review Focus

1. **A rule carrying a property we don't model** (the fork's `personal_shiny`, or a future upstream field). It must reach the v1 row and survive the next edit's preserver → PUT. Pinned in Tasks 1 and 3.
2. **Pokéstop events disappearing from a consumer** that read them through the v1 invasion list: the Cleaning page toggle, dashboard counts, delete-all-alarms. Pinned in Task 4.
3. **Profile overview and export** grouping rules by profile. `profile_no` must come through on the all-profiles read. Pinned in Task 2.
4. **Dashboard counts must be identical before and after** for the same human. Pinned by the Task 6 equivalence run.
5. **A delete of a uid on another profile** (controllers 404 first today, #889). It must not silently "succeed" and leave the rule. Pinned in Task 3.

---

### Task 1: Read translation and the defaults table

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Translator.cs`.
- Create: `Tests/Pgan.PoracleWebNet.Tests/Services/TrackingV2ReadTranslationTests.cs`.
- Modify: `Tests/Pgan.PoracleWebNet.Tests/Services/TrackingV2TypeTranslationTests.cs`. Replace the hard-coded 5.2.1 `V2Schema` with the property lists read from the 5.4.0 fixture, including `ping` and `summary`.

**Interfaces (Produces):**
- `internal static JsonElement ToV1Row(string type, JsonElement v2Rule, string humanId)`. Throws `InvalidOperationException` naming the type and property for an enum string it doesn't know. Never silently drops a property.
- `internal static IReadOnlyDictionary<string, JsonElement> DefaultsFor(string type)`.

**Steps:**
- [ ] **Step 1: Write failing tests.**
  - Every `V2*Rule` nullable property has a default, and every default names a real property: both directions, against the 5.4.0 fixture.
  - Round trip `v1 → TryTranslate → ToV1Row` gives back the original row for one full representative row per type. Include the sentinels (raid 9000s, maxbattle 9000s, costume 9000), the enum values, a clean mask using all three bits on pokemon, fort `change_types`, and a row with `override_areas`.
  - An unknown property (`"personal_shiny": true`) passes through unchanged.
  - A v2 rule with `profile_no` keeps it.
  - `id` is set from `humanId`.
  - An unknown enum string throws.
- [ ] **Step 2: Run them and watch them fail.**
- [ ] **Step 3: Implement.** Before relying on the table, check PoracleNG's read translation (`E:/PGAN/pogogit/PoracleNG`, `origin/develop`, `processor/internal/api/v2_*.go`) and record any field where it differs from the table above.
- [ ] **Step 4: Run them and confirm they pass.**
- [ ] **Step 5: Commit** `feat: translate v2 tracking rules back to the v1 row shape`.

### Task 2: Reads through v2

**Files:** Modify `PoracleTrackingProxy.cs` (`GetByUserAsync` :85, `GetAllTrackingAsync`, `GetAllTrackingAllProfilesAsync`). Test `PoracleTrackingProxyTests.cs` and `PoracleTrackingProxyV2Tests.cs`.

**Interfaces:**
- **Unchanged signatures:** `GetByUserAsync(type, userId)` returns a v1-shaped JSON array, exactly as before.
- **Snapshot reads:** `GetAllTrackingAsync(userId)` and `GetAllTrackingAllProfilesAsync(userId)` must return the same JSON object shape v1's `/api/tracking/all/{id}` and `/allProfiles/{id}` returned. Read those routes' response shape from the existing tests and the consumers (`DashboardService`, `CleaningService`, `ProfileOverviewService`). Build it from the v2 snapshot, translating each type's array with `ToV1Row`, and map type keys to whatever keys v1 used. Include `human`/`profiles` only if a consumer reads them from this call.
- **Feature names for `PoracleUnsupportedException`:** "Alarm list" and "All alarms".

**Steps:**
- [ ] **Step 1: Write failing tests.**
  - Each read requests only its v2 URL. `GetAllTrackingAllProfilesAsync` uses `all_profiles=true&include_descriptions=true`.
  - The output equals the v1 shape for a scripted v2 response, including `profile_no` per rule on the all-profiles read and `description` when present.
  - "human not found" → `AccountGoneException`; plaintext 404 → `PoracleUnsupportedException`.
  - An empty `rules`, or a null type in the snapshot, gives an empty array.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** Delete the v1 read code paths and the tests that pin v1 read URLs. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat: tracking reads go through PoracleNG v2`.

### Task 3: Deletes through v2 only

**Files:** Modify `PoracleTrackingProxy.cs` (`DeleteByUidAsync` :221 / `TryDeleteByUidV2Async`, `BulkDeleteByUidsAsync` :241 / `TryBulkDeleteByUidsV2Async`, `V2BulkDeleteMaxUidQueryLength`). Test `PoracleTrackingProxyV2Tests.cs`.

**Interfaces (signatures unchanged):**
- `DeleteByUidAsync` → `DELETE /api/v2/humans/{id}/tracking/{type}/{uid}?silent=true`.
- `BulkDeleteByUidsAsync` → `DELETE /api/v2/humans/{id}/tracking/{type}?silent=true` with `{"uids":[…]}` in the body. No length cap.
- When v2 deletes fewer than requested, log the missing uids at Information. Don't fall back.
- A problem+json 404 for a missing rule is "already gone", logged, with no exception.

**Steps:**
- [ ] **Step 1: Write failing tests.**
  - Each delete sends only its v2 request. Bulk uses the body, including a 3,000-character uid list.
  - A partial bulk delete logs and doesn't call v1.
  - A rule-not-found 404 on a single delete doesn't throw. "human not found" → `AccountGoneException`. Plaintext 404 → `PoracleUnsupportedException`.
  - Review Focus 5: a controller `Delete` of a uid that isn't on the active profile still answers 404 before any proxy call (existing behaviour, assert it remains).
  - Review Focus 1: a rule read with `personal_shiny: true`, then edited through `TrackingFieldPreserver` → `TryReplaceV2Async`, carries `personal_shiny` on the PUT. This needs the write side's carry-through, gated on the schema declaring the property (see Global Constraints). Implement that carry-through here.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** Delete the v1 delete code, the 2,000-character constant and the partial-delete fallback. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat: tracking deletes go through PoracleNG v2 only`.

### Task 4: Consumers that relied on events in the invasion list

**Files:**
- Check and adjust as needed: `InvasionService.cs`, `CleaningService.cs`, `DashboardService.cs`, `HumanService.cs` (`DeleteAllAlarmsByUserAsync`), `QuickPickService.cs`, `PokestopEventService.cs`.
- Test: their test classes.

**Steps:**
- [ ] **Step 1: List every consumer.** For each one that read Pokéstop events (`kecleon`, `gold-stop`, `showcase` grunt types) through the invasion list, write a test that pins today's user-visible result:
  - the Cleaning page toggle applies to events too, if it does today;
  - dashboard event counts appear where they did;
  - delete-all removes event rules.
- [ ] **Step 2: Run; fail where behaviour changed.** **Step 3: Implement** by reading `incident` (through `IPoracleIncidentProxy`) where needed. Remove the now-dead `BelongsToPokestopEventsAsync` filtering only if nothing needs it. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `fix: Pokéstop event rules keep their place in cleaning, counts and delete-all on v2 reads`.

### Task 5: CLAUDE.md and CHANGELOG

- [ ] **Step 1: Rewrite the tracking docs.**
  - CLAUDE.md "`/api/v2` Writes" section: replace "Reads stay on v1 …" and "Deletes fall back to v1 …" with how reads and deletes work now. Name the null↔default table, the unknown-field carry-through, and the snapshot.
  - Remove "Reads stay on v1" from `docs/architecture/poracleng-proxy.md` and `poracleng-compatibility.md`.
  - CHANGELOG `[Unreleased]` → Changed: "Alarm lists, counts and deletes read and write through PoracleNG's v2 API."
- [ ] **Step 2:** `mkdocs build --strict` passes.
- [ ] **Step 3: Commit** `docs: tracking reads and deletes go through PoracleNG v2`.

### Task 6: Verify on the 5.4.0 bed

- [ ] **Step 1:** Full backend suite and `npm test` green.
- [ ] **Step 2: Equivalence run.**
  - For every human in `poracle_dev_540` (2,332, prod-like data) and every type, fetch v1 `GET /api/tracking/{type}/{id}` and v2 `GET /api/v2/humans/{id}/tracking/{type}` from `:3046`. Apply `ToV1Row` through a small test harness (a console or xUnit `[Fact(Skip=…)]` run locally against the bed, or the same code path via a test endpoint; don't add a production endpoint for it).
  - The two must be equal per rule, ignoring key order, apart from the whitelisted legacy zeros (Global Constraints).
  - Do the same for `/api/tracking/all` versus the snapshot path, and `/allProfiles` versus `all_profiles=true`.
  - Report counts and every non-whitelisted difference.
  - v1 routes still exist on PoracleNG 5.4.0 for this comparison; the app no longer calls them.
- [ ] **Step 3: Branch instance.** Build the branch image on dev-01 and run `instance.sh up sp3a 5197 540 poracleweb.net:sp3a`. Then run:
  - the harness, expecting 258/260;
  - the e2eC alarm flows: list, edit, delete and bulk delete for every type; dashboard counts; profile overview; export; and the Cleaning page.

  Clean up probes and run `instance.sh down sp3a`.
- [ ] **Step 4:** Stop. Push and PR happen at the finishing step.
