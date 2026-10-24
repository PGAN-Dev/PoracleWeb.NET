---
paths:
  - "**/*Bulk*"
  - "**/*bulk*"
  - "**/*Clean*"
  - "**/*clean*"
  - "**/RuleRepair*"
  - "**/*rule-repair*"
  - "**/MonsterService*"
  - "**/GymService*"
  - "**/ScannerController*"
  - "**/Pokemon*"
  - "**/pokemon*"
  - "**/Monster*"
  - "**/TrackingFieldPreserver*"
  - "**/*Alarm*"
  - "**/*alarm*"
---

## Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (uses `*Update.ApplyUpdate(existing)` extension method for null-skip merge, then sends it to PoracleNG as a uid-addressed v2 PUT)
  - `PUT /distance` -- Update ALL alarms' distance for the current user/profile (one v2 PATCH per type)
  - `PUT /distance/bulk` -- Update distance for specific UIDs (one v2 PATCH per type)
- Both distance endpoints send `PATCH /api/v2/humans/{id}/tracking/{type}` with `{uids, set: {distance}}`
  (Pokestop event rules held in the invasion table go to `incident`'s PATCH). Only the one setting travels,
  so nothing else a rule holds is re-sent and every rule keeps its uid.
- **Every PATCH is sent `?trusted=true`**, from both `PoracleTrackingProxy.PatchAsync` and
  `PoracleIncidentProxy.PatchAsync`. PoracleNG re-validates the whole stored rule on a PATCH, and untrusted
  its area check allows only `userSelectable` fences, so every rule limited to a user's own drawn fence came
  back `invalid`: cleaning silently skipped them and bulk distance mislabelled them. Trusted grants nothing
  new, because a PATCH set has no `override_areas`; the only areas re-checked are the ones already stored,
  which `IUserAreaPolicy` allows as "already on the rule".
- They answer `{ updated, skippedAreaScoped: [uid], skippedPlaceScoped: [uid], skippedConflict: [uid] }`.
  An alarm limited to areas cannot take a radius above zero and one measured from a saved place cannot take
  zero: `DistanceRewrite.Plan` splits those off before `DistanceRewrite.ApplyAsync` sends the rest, as
  PoracleNG would refuse them. PoracleNG also answers per rule: `conflict` (at the new radius the rule would
  clash with another) lands in `skippedConflict`; `invalid` lands there too, with a warning in the log,
  because a rule is only sent when its scope takes the radius, so its scope can't be the reason;
  `not_found` is logged. One refused rule never fails the rest. The SPA's `distanceUpdateMessage` reports
  all three.
- Every radius is bounded at `AlarmDistance.MaxMetres` (20,037,509 m, half the Earth's circumference) on
  every `*Create`/`*Update` DTO, `BulkDistanceRequest` and `RejectInvalidDistance`. That is the physically
  impossible, not a product limit: production holds a rule at 10,000,000 m. `pokemon_id` stays uncapped.
- **CleaningService** sends one v2 PATCH per type naming the uids and the clean bit to change, so the edit-in-place and summary bits a rule already holds survive the toggle. Pokestop event rules in the invasion table go to `incident`'s PATCH. A rule PoracleNG refuses keeps its old setting and is left out of the count; the service logs a warning naming it and the reason, since nothing on screen does.
- **`GET /api/cleaning/status` grew a `counts` member** beside the per-type booleans: `{ pokemon: true, ..., counts: { pokemon: { clean, total }, ... } }`. Each boolean still means "every rule of this type is clean", so the old reading survives, but the response is no longer a `Dictionary<string, bool>`; a client that deserialised it as one breaks on `counts`. Additive on purpose: the page reads `counts` to show a partly clean type instead of reporting it as off, and treats a missing `counts` as the old server.

## Rule Repair (PoracleNG 5.4.0)

`Admin > Server settings > Rule repair` (`AdminRuleRepairController`, `RuleRepairJob`, `RuleRepairClassifier`, SPA `rule-repair-card`) finds stored rules 5.4.0's v2 reads differently: invasion `grunt_type` `metal` (now `steel`), raids with `pokemon_id` 0, and raids/max battles for any boss with level unconfirmed (v2 reports null). Rules v2 cannot express are listed as `CannotEdit` and never written.

The scan reads every human through v2 and never writes. A repair is a uid-addressed `PUT /api/v2/humans/{id}/tracking/{type}/{uid}?silent=true&trusted=true&profile=N`, preceded by the same request with `dry_run=true`; it's written only when the dry run answers `updated`, and `unchanged` counts as already fine. A 409 leaves the rule alone and lists it with a "would duplicate another rule" reason: under `CannotEdit` when the scan's any-tier confirmation hits it, in `Applied.Conflicts` when apply does. Apply re-reads each human's rules and re-classifies, so a rule changed since the scan is judged as it is now. Trusted, because it restores what PoracleNG stored and must not go through `IUserAreaPolicy`. Just before each real PUT the stored v2 rule is logged at Information (`LogRepairWriting`), so a bad repair can be reversed from the log.

One job at a time per process (a second start answers 409 with the current state), admin-only and refused while impersonating, and the start is logged with the admin's id. State lives in memory, so a restart mid-run loses it; scanning again is safe. A run stops after five requests in a row that got no response or a 502/503/504, or when PoracleNG is too old. A scan that skipped every human fails, whatever the reasons, naming the most common one: an empty list there would read as "nothing to repair". The card never says "nothing" while any user was skipped.

The full status can run to thousands of rows, so the card polls `GET /api/admin/rule-repair?summary=true` (lists empty, `repairableCount`/`cannotEditCount`/`skippedCount` kept) while a run is going and reads the full status once when it ends. The card caps every list at 100 rows per group. User docs: `docs/getting-started/upgrading-to-poracleng-5-4.md`.

## Bulk Update Preserving Fields
When updating alarms, the frontend sends `*Update` DTOs to `PUT /{uid}`. The controller uses `model.ApplyUpdate(existing)` (null-skip extension method) to merge only non-null fields onto the existing alarm, then `TrackingFieldPreserver` copies across any stored property the typed model lacks, and the proxy sends the row as one `PUT /api/v2/humans/{id}/tracking/{type}/{uid}`. That PUT is a full replace, so a field left out is reset to its default. Bulk distance and cleaning are the opposite: a PATCH names the one setting to change and leaves everything else alone.

## Gym ID NULL vs Empty String
The `gym_id` column in Poracle alarm tables (gym, raid, egg) is nullable with a NULL default, and NULL means "any gym" (`000001_initial_schema.up.sql`). The v2 API stores an omitted, null or empty `gym_id` as NULL and returns null when unset (`v2_gym.go`), and the matcher treats any non-NULL value as a specific gym. The `GymPickerComponent` emits `null` when cleared and the gym's `id` string when selected.

## Clean Field Bitmask
The alarm `clean` field is a **3-bit bitmask** in PoracleNG, not a boolean: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary (`db.IsClean/IsEdit/IsSummary` in PoracleNG; quest summary is gated by `summary_schedules`). Use `CleanFlags` (`Core.Models/CleanFlags.cs`) and the frontend twin `shared/utils/clean-flags.ts` (`AUTO_DELETE`/`EDIT`/`SUMMARY`, `isAutoDelete/isEdit/isSummary`, `compose`, `preserve(existing, mask, changes)`) for all reads/writes so bits set elsewhere (e.g. via the bot) survive a web edit. Models cap `Clean` at `[Range(0, 7)]`. UI controls exist only where PoracleNG acts on the bit: auto-delete (all types), edit-in-place (lures, raids/eggs via RSVP `rsvpChanges`, and pokemon -- see below), and daily summary (quests). **Angular templates can't parse bitwise `&`** — gate card badges via a component method (e.g. `isAutoDelete(clean)`), not inline `clean & 1`. See #292.

**Pokemon edit-in-place is capability-gated, not unconditional (jfberry/PoracleNG#246).** Before that PR
(merged into upstream `develop` 2026-10-08, not yet in a tagged release as of this writing), the
processor accepted the pokemon `edit` bit on every surface (commands, v1, v2) but never acted on it --
storable and readable, functionally inert. #246 makes it real, with a per-deployment config gate
(`[tracking] pokemon_edit`, default `true`) and a `pokemon_edit_window_mins` (default 5, `0` = no
limit): a re-send for the same encounter inside the window edits the original DM; past it, a threaded
reply is sent instead so the clean-delete on the original is undisturbed. `MonsterController.GetCapability`
(`GET /api/monsters/capability`) answers `{ edit: bool }` from `PokemonEditCapabilityService`, which
reads `tracking.pokemon_edit` off `/api/config/values` via `IPoracleApiProxy.GetPokemonEditEnabledAsync()`
-- mirrors `SummaryCapabilityService`/quest's `pokecoins` capability exactly, 5-minute cache, degrades to
`false` on any fault or absent key. The SPA's `MonsterService.editSupported` signal gates the toggle in
both pokemon dialogs; hidden means the form control still exists (seeded from the stored bit) so a toggle
a bot set earlier round-trips unchanged even when the control itself isn't shown.

**#246 also tightens `/api/v2`: `edit: true` now 422s for gym, invasion, nest, maxbattle and quest** --
alert types whose message is never tracked for editing, so the bit was never settable there (no UI, and
the bot's `!track edit` keyword was never declared for those types either). Only pokemon, lure, raid, egg
and pokestop events (incident) can carry it going forward. `TrackingV2Translator` currently writes `edit`
unconditionally for every type but fort ("all but fort" in the shape-differences table below) -- latent,
not live-broken, since nothing has ever set bit 2 for the five rejected types, but worth tightening before
an operator upgrades PoracleNG out from under it. Verified live against a PoracleNG build carrying #246
(dev-01's "next" testbed, rebuilt 2026-10-14 to commit `4931758f`): `tracking.pokemon_edit: true` on
`/api/config/values`, a v2 pokemon PUT with `edit: true` returns 200 and round-trips `clean: 2` on the v1
read, and the identical PUT against gym returns 422.

## Monster Filter Defaults Are Ours, Not PoracleNG's

This section used to say the `*Create` defaults "are no longer critical -- PoracleNG is the authoritative
source", and that belief wrote **15,383 rows with `pvp_ranking_best = 0`** into production. 0 is not a
rank; ranks are 1-based.

`cleanRow` fills a field that is **absent**. A non-nullable `int` on a `*Create` model is never absent --
it serialises as whatever it defaults to, and v1's `flexInt` passes an explicitly sent value straight
through. So every `*Create` default is sent on every write, and every one of them is authoritative.
`PvpRankingBest` had no initializer where `PvpRankingWorst = 4096` sat beside it; that asymmetry is the
whole bug (#895, jfberry/PoracleNG#227).

Three rules follow:

- **A bound's no-filter value is the column's default, not zero.** 0 is right for `min_iv`, `min_cp` and
  the other floors. It is wrong for `pvp_ranking_best` (1), `size` and `rarity` (-1), `costume` (9000),
  `max_rarity` (6), `max_size` (5). When adding a field, ask what the column stores for "no filter".
- **Check the sibling.** These come in pairs and the bug was one half of a pair being defaulted. The same
  drift hit `QuickPickService.BuildMonster`, which capped every applied pick at `MaxLevel = 40` -- the
  game's 2020 ceiling -- under a comment claiming it matched the add dialog's 55. 4,088 production rows.
- **Do not tighten the `[Range]` to match the default.** `PvpRankingBest` still admits 0 because 15,383
  rules hold one and have to stay editable; refusing the value would fail an edit on a rule the user did
  not break. On v1 an edit did not repair it: the preserver re-read the stored 0 and sent it back (verified on 5.1.0, 5.2.1 and 5.3.0).
  The preserver now reads through v2, where such a row comes back as the wildcard (PoracleNG #230, in 5.4.0), so it reads as 1 and an edit repairs it.
