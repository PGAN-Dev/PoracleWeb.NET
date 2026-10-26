---
paths:
  - "**/Poracle*"
  - "**/IPoracle*"
  - "**/Tracking*"
  - "**/V2Schema*"
  - "**/*Create.cs"
  - "**/*Update.cs"
  - "**/Pgan.PoracleWebNet.Core.Services/*Service.cs"
  - "**/Pgan.PoracleWebNet.Api/Controllers/**"
  - "**/Pgan.PoracleWebNet.Tests/Services/**"
  - "**/Pgan.PoracleWebNet.Tests/Controllers/**"
---

## PoracleNG Decides Insert vs Update vs Duplicate, And Reports It Before Writing

PoracleNG diffs a submitted row against the existing ones (`DiffTracking`, `processor/internal/db/diff.go`) using `diff` struct tags:

```go
totalDiffs == 0                            -> duplicate: nothing written, "alreadyPresent"
totalDiffs == 1 && nonUpdatableDiffs == 0  -> UPDATE of that existing row
otherwise                                  -> new insert
```

`diff:"update"` fields are `clean`, `distance`, `template`, plus `slot_changes` and `battle_changes` on gyms. The consequence that kept biting: **an Add that differs from a *different* alarm by exactly one updatable field takes that alarm over.** The user got 201 and one alarm existed where there were two.

PoracleWeb.NET no longer mirrors that rule. 5.4.0 (jfberry/PoracleNG#326) classifies a create before it writes, so `PoracleTrackingProxy.CreateAsync` posts `?dry_run=true` first:

- a single row reported as an update is refused with `TrackingConflictException`: "Another alarm of this type already uses those settings. Edit or remove that one instead." (409 through the global filter). The exception's `ExistingUid` is the stored rule the dry run named (`updated[0].previous.uid`, else `updated[0].uid`), and the 409 body carries it as `existingUid` beside `trackingType`; the SPA's add dialogs keep a refused single Add open, name that rule through the list that opened them, and offer "Edit that one" (`shared/utils/add-outcome.ts`). A batch Add closes as before;
- a single row reported unchanged writes nothing and answers 200 with uid 0;
- otherwise it commits, and if the commit reports a takeover anyway (a race), the other alarm is restored from the `previous` row in the answer and the Add is refused;
- a batch (quick-pick apply, profile import) dry-runs each row alone, because PoracleNG classifies against stored rules only, and creates just the new ones. Identical rows and would-be takeovers are skipped and counted (`TakeoversSkipped`);
- max battles get the same protection. 5.4.0 merges max battles on v2 creates, so a second alarm for the same boss that differs only by radius is now refused where it used to stack. An exact duplicate keeps "You already have an identical max battle alarm.", and carries the stored rule's uid as `existingUid`: the dry run reports the duplicate under `unchanged` with that rule's uid, which `TrackingCreateResult.ExistingUid` passes on, so the dialog offers **Edit that one** as for the other types.

Edits are one v2 PUT and PoracleNG answers 409 for one that would duplicate another rule or change exactly one updatable field of it; the proxy maps it to the same message. `PoracleIncidentProxy` runs the same protocol through `IncidentCreateProtocol`.

Deleted with the mirror: `EnsureNoMergeIntoAnotherAlarmAsync`, `ReconcileAsync`, the no-op-edit check and the max battle identical-alarm pre-check. `TrackingUpdateReconciler` is gone too; its last two methods guarded the bulk distance batch, and PoracleNG now decides a PATCH's conflicts per rule and says which. **Do not rebuild a client-side diff for creates or edits**: it will disagree with the server, which is what #521, #531, #553 and #561 were.

**"No override" has four spellings and they are one value.** Every dialog sends the unused half of a scope as an explicit empty (`overrideAreas: []`, `overrideLocationLabel: ""`), because null means "keep what is stored" on the write path; PoracleNG stores NULL and reads it back as `override_areas: null`. Area names compare lowercased but in order.

**Lure, invasion and Pokestop events take no refusal of their own.** Up to 5.1.0 their tables carried unique keys on `lure_id` and `(grunt_type, gender)`, which PoracleWeb.NET guarded; migration 8 dropped the keys and that code went with the old minimum. **Do not reintroduce a refusal on `lure_id` or on `(grunt_type, gender)` alone**: PoracleNG stores rules that differ only by area scope or place side by side, and a refusal would leave one uneditable (the #553 shape). `LureInvasionMergeGuardTests` holds the legitimate cases.

See #462, #463, #502, #531, #553, #561.

## `/api/v2` Writes: Creates, Edits, Deletes, And The uid Is Read Back Every Time

**Every tracking create, edit, read and delete for all eleven types goes through `/api/v2`, with no v1 fallback.** No v1 tracking call remains: bulk distance and the cleaning toggle use `PATCH /api/v2/humans/{id}/tracking/{type}` (see "Bulk Operations"), and `NoV1TrackingCallsTests` fails the build if a v1 tracking route reappears in a string literal. A route that answers gin's plaintext `404 page not found` raises `PoracleUnsupportedException("saving alarms", ...)`. A row the translator can't express is refused with `AlarmValidationException("PoracleNG can't store this alarm as it is: {reason}. Nothing was changed.")`, never sent to v1 and never trimmed.

**Reads go through v2 (sub-project 3a, PoracleNG 5.4.0), translated back to the v1 row.** v2 answers `null`
for a field at its wildcard where v1 answers the sentinel, and both the C# and TypeScript models are built on
the sentinels, so `TrackingV2Translator.ToV1Row` rebuilds the v1 row and `DefaultsFor` is the null-to-default
table (one entry per nullable property of each `V2*Rule` schema; a test checks it against the 5.4.0 schema
fixture in both directions). The `*Create` initialisers are ours, but where PoracleNG's own read translation
disagrees the table follows PoracleNG: fort `include_empty` null is 1 (v2's default, not v1's), raid and
max battle `level` null is 9000 with a `pokemon_id` and 90 without (90 is "every tier"), `gym_id` and
`station_id` null stay null, fort `fort_type` is `everything`, pokemon `min_iv` is -1. Enums go back to
ints, booleans to 0/1, `clean`/`edit`/`summary` to the `CleanFlags` bitmask, `change_types` to a JSON
string. **A property the type's table doesn't know is carried through verbatim**, so a future upstream field
or a field PoracleNG's own `/openapi.json` declares that PoracleWeb.NET doesn't model (a personal-shiny flag, say) survives a read; an unknown enum value throws, and the proxy logs and skips
the one rule rather than blanking the page. The write side matches: `TryTranslate` sends such a property
back on an edit when the server's own `/openapi.json` declares it, and refuses otherwise.

`GetByUserAsync` reads `/tracking/{type}` per type (validating the type first). `GetAllTrackingAsync`
reads the v2 snapshot, `GET /tracking`. `GetAllTrackingAllProfilesAsync` uses `all_profiles=true` with
`include_descriptions=true`, and **PoracleNG only enumerates profiles that have a `profiles` row**: a human
whose active profile has none (or who has none at all) gets nothing back for it. The proxy then appends the
active profile's rules from the plain snapshot, stamped with `current_profile_no`. Reads stay
active-profile scoped, as on v1. Legacy zeros in production (`pvp_ranking_best 0` on about 12,700 rules,
`pvp_ranking_worst 0`, `size 0`) come back from v2 as null and read as the defaults 1, 4096 and -1;
matching doesn't change. v2 also drops the gender on an invasion "everything"/boss rule and the `edit` bit
on gym, quest, nest, max battle and invasion rules.

**Pokéstop events (`incident`) are folded into the invasion read, as v1 did.** v2's invasion list excludes
them, so the proxy reads the incident list too and restates each rule in v1's form (`grunt_type` from
`display_type`, gender 0). Cleaning, the dashboard counts, `InvasionService` and `HumanService`'s delete-all
all see the same rows they did on v1 (`PokestopEventConsumerTests`). Writes go the other way: an event grunt
(`kecleon`, `gold-stop`, `showcase`) saved from the invasion page, a quick-pick row or a profile import goes
to the incident routes, because v2's invasion route refuses it, and an event with a gender is refused
rather than saved without it. Pokéstop events keep a role or user mention through an edit, get the same
dry-run takeover protection (`IncidentCreateProtocol`), and their bulk delete sends the uids in the body.

**Deletes are v2 only.** Single is `DELETE /tracking/{type}/{uid}`, bulk takes a `{uids}` body, and a
422 naming `query.uid` raises `PoracleUnsupportedException` ("Deleting alarms", PoracleNG 5.4.0). v2's ownership check reads the active profile, so it 404s a uid on another
profile and its bulk form skips such a uid and still answers 200 (#889). v1 ignored the profile. Three
things keep a delete from stranding a rule: an invasion delete retries leftovers as `incident`; whatever is
still left is resolved through one all-profiles read and re-issued with `?profile=N`, grouped by profile
for the bulk form (so quick-pick Remove still works after the scheduler has switched profile); and a
rule-not-found for a uid that really is gone counts as already gone and is logged, matching v1's
idempotence. Only uids that stay unresolved are logged as lost. The single-alarm case never reaches that
from the UI: each controller's `Delete` looks the uid up on the active profile first and answers 404, so a
click on a rule the scheduler just moved out of view is refused rather than half-done.

**Invasion has no grunt-name gate any more.** `IInvasionGruntNameService` and the `PoracleV2Capabilities` check it paired with are deleted, because v2 answers 422 naming an unknown grunt and 5.4.0 carries `grunt_type`. The steel grunt was being saved as `metal`, which matched nothing on v1 and which v2 refuses; `InvasionGruntTypes` and the add dialog send `steel`, `InvasionService` saves a stored `metal` rule as `steel` the next time it's written, and a profile import rewrites it before the first write. Two things the schema does not say, both established by POSTing to a running build: `V2InvasionRule.gender` says it is "ONLY valid together with `type_id`" while `grunt_type` says it "may be combined with gender", and the latter is correct; and v2 normalises a spaced name on write (`player team leader` is stored as `player_team_leader`). `genderless` has no v2 representation (the enum is `any|male|female`), so a stored gender 3 is refused with the translator's message.

**The v2 PUT is delete-then-insert, so on 5.2.x every type rotates its uid on edit; since
jfberry/PoracleNG #303 (merged into `develop`, unreleased) it keeps the rule's uid for all eleven types.**
The engine still deletes and inserts, then runs `UPDATE ... SET uid = old`. If that reassignment fails the
PUT still answers 200, with the new uid, so the response is read every time rather than the uid being
assumed either way. A returned uid equal to the addressed one means no remap: `TrackingV2Replacement`,
`MonsterService` and `TrackedUidRemapper` all treat it that way, and tests
pin it. Quick-pick applied
state is the thing that actually breaks when a uid does move and nobody follows it (#403), so every service
hands `ITrackedUidRemapper` to `TrackingV2Replacement.ApplyAsync`, the shared v2 branch each
`UpdateAsync` takes (it never returns null). Callers pass PoracleNG's type name and applied state
stores the quick pick's, and those differ for exactly one type: `pokemon` against `monster`. The remapper
translates it. Before it did, no Pokemon pick was ever remapped, and removing one after an edit left the
alarm behind (present in v2.18.0, found by driving the API against 5.2.1).

**Max battle edits are one PUT.** v1 could only delete and recreate, which left a window where the alarm existed nowhere; and bulk distance no longer deletes either, since a PATCH changes the rule in place. The v1 repair work (`ReconcileAsync`'s clean-up of the duplicate a v1 create leaves behind) went with the reconciler.

**One field table per type in `TrackingV2Translator`, never a shared one.** `V2PokemonRule` declares 28
integer filters and `V2FortRule` declares one; every rule sets `additionalProperties: false`, so a leaked
field is a 422.
`TrackingV2TypeTranslationTests` asserts each table against the schema's property list in both directions,
so a field the table leaks and a field it misses both fail the build.

Shape differences, all handled once at the wire:

| v1 | v2 | Types |
|---|---|---|
| `clean` 3-bit mask | `clean` / `edit` / `summary` booleans | all but fort |
| ^ with jfberry/PoracleNG#246 (unreleased): `edit: true` 422s | n/a | gym, invasion, nest, maxbattle, quest -- see "Clean Field Bitmask" above |
| `gender` 0-3 | `any` / `male` / `female` / `genderless` | pokemon |
| `team` 0-4 | `harmony` / `mystic` / `valor` / `instinct` / `any` | raid, egg, gym |
| `rsvp_changes` 0-2 | `none` / `rsvp` / `rsvp_only` | raid, egg |
| 0/1 columns | real booleans | `exclusive`, `slot_changes`, `battle_changes`, `gmax`, `shiny`, `include_empty` |
| `change_types` JSON string | array | fort |
| `pvp_ranking_league` any int | enum of `{0, 500, 1500, 2500}` | pokemon |

Plus `uid`, `id`, `profile_no` and `description`, which v2 has no place for and refuses outright; a target profile goes in `?profile=N`, and only profile import uses it (`CreateOnProfileAsync`), so it writes to the target profile without switching the active one. `ping` is a real field on all eleven types since jfberry/PoracleNG#249 (5.4.0), so `TrackingV2Translator` always carries it and a role or user mention survives an edit. The refusal that used to stop an edit falling back to v1 (which blanks the mention) is deleted with the fallback.

**The frontend exposes `ping` as a "Mention" field, edit dialogs only.** Each edit dialog computes
`showMention = isImpersonating() && mentionCapable(user().type)`, so the field appears while managing a
webhook, a Discord channel or a Discord thread and nowhere else: not on Telegram destinations, which have no
mention, and not on people. `GET /api/config` no longer reports `PingSupported`. A personal DM alarm has
nobody else to notify, so the field would be presentation-only noise there. It is
deliberately absent from every add dialog: a single Add has no stored mention to protect, and quick-pick apply and profile import create from a template. `alarm-dialog-parity.spec.ts` -- the test that catches a control shipped in
one dialog of a pair and not the other -- carries an explicit `EDIT_ONLY` exception for `ping` on every
type it was added to, with this reasoning, rather than being silently exempted.

The v1 shape stays the single internal currency (`TrackingFieldPreserver` and `QuickPickService` build and compare it), and the translator is the only exit onto v2, which is what stops a v1-shaped row reaching a v2 body.

**Defaults are sent as absent.** A v2 rule stores nothing for a field at its wildcard, so the translator omits a property whose value equals the type's read default unless the schema's `Required` list names it. (This replaces "sentinels are sent verbatim", which was right while v1 diffed on what was stored.) A boss-specific raid or max battle (stored `level` 9000) is sent without `level`, which is why boss edits no longer depend on the older API: v2's `level` maximum is 90.

Three per-type traps, all verified live and each a 422 if ignored:

- **egg `level` is required with minimum 1**, and `Egg.Level` is a plain int defaulting to 0. A
  quick-pick definition or import file that omits the level is refused by the translator rather than
  sent to v1; cleaning (v1) is unaffected.
- **fort has no `clean`, `edit` or `summary`** — no v2 field and no column. A shared clean helper applied
  blindly is a 422.
- **fort `include_empty` defaults to TRUE on v2 and FALSE on v1.** A PUT that omitted it flipped a stored
  0 to 1 and the alert text gained "including empty changes". The translator states it explicitly and
  refuses a fort row that carries none.

**The translator never changes what PoracleNG will accept, and never trims quietly.** A property it does not know, an enum outside
its range, an egg without a level: it refuses the row with the message above. Dropping the field would be #730 again.

**A v2 PUT is a full replace** — omitting `min_iv` wipes a stored 90, verified live — so
`TrackingFieldPreserver` matters more here than it did on v1, not less. When a rule names `override_areas`, the trusted write goes to the uid PoracleNG just reported, not the one addressed.

Errors are RFC 9457 problem+json at **422**, not 400, in two shapes: a schema failure carries `errors[]`
whose `location` is `body.x` on a PUT and `body[0].x` on a POST, and a semantic refusal carries only
`detail`. `PoracleProblemDetails` reads both, plus v1's `{"message":...}`.

Gating: **Version** — `PoracleServerProfile.IsSupported` (reachable and not known to be older than 5.4.0) — is informational now, since nothing falls back. **What the server publishes about itself** answers the little that is left, through `IPoracleV2SchemaService` reading the target instance's own `/openapi.json`: the `trusted` flag on `setAreas`, and which properties a rule declares (so an unmodelled one can be sent back). It is the schema-document sibling of `PoracleServerProfile`: one cached probe, typed questions, everything false when the document cannot be read. Neither is the `/health` capability map, which says nothing about v2. There is no setting to pin the surface: `Poracle:TrackingApiVersion` (`PORACLE_TRACKING_API_VERSION`) is removed and a leftover value is ignored, and the per-type "v2 route absent" flag is gone with the fallback it served. `PoracleServerProfileService.GetAsync` is single-flighted behind a static gate, because it is
registered transient, a cold cache costs a `/health` GET, and a dashboard load sends several
version-gated writes at once.

## Validation Attributes Live On The `*Create` DTOs, Not The Domain Models

`Monster`, `Raid`, `Gym` and friends carry **no** `[Range]` or `[StringLength]` attributes; `MonsterCreate` and its siblings do. Anything that validates an alarm outside the normal model-binding path must bind or deserialize into the `*Create` DTO, or it will validate nothing and pass silently.

This has cost three separate fixes: profile import (#548), quick-pick apply (#565), and the quick-pick id length check that bounded the name but not the id generated from it (#555). All three passed their unit tests while doing nothing, because the tests asserted the code ran rather than that it rejected anything.
