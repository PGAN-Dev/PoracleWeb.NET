# PoracleNG 5.4.0 sub-project 3d: rule repair and the upgrade guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An admin can find and repair, through PoracleNG's `/api/v2` and never the database, the stored rules that 5.4.0's v2 surface reads differently or refuses to edit, and operators have an "Upgrading to PoracleNG 5.4.0" guide.

**Architecture:** A pure classifier decides, per v2 rule, whether it is fine, repairable (and to what) or only reportable. A singleton background job scans every human (`GET /api/v2/humans`, then one all-profiles snapshot each, raw), keeps the report in memory and, on request, applies the repairs with uid-addressed v2 PUTs on the rule's own profile. Admin endpoints start the scan, show progress and the report, and apply. An Admin > Settings card drives it.

**Tech Stack:** .NET 10, xUnit + Moq, Angular 21 + Material, Jest, mkdocs.

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md`, "Sub-project 3", paragraph "Rows v2 can't represent" ("repair by v2 PUT where the meaning is clear, ask jfberry where it isn't, or show the rule read-only with the reason. Never a direct DB write."), plus the "Docs" section's new page "Upgrading to PoracleNG 5.4.0". User decision (2026-10-21): "Repair to likely intent": `metal` → `steel`, a raid with Pokémon 0 → any boss at its level, a raid stored at level 9000 with Pokémon 9000 → every tier. Production inventory: `scratchpad/sp3-v2-inventory.md` (7 `metal`, 1 raid Pokémon 0, 3 raid 9000/9000, 70 rules on profiles with no `profiles` row).

## Global Constraints

- PoracleNG 5.4.0 minimum. Every read and write goes through `/api/v2`; no direct database access, no `/api/tracking` v1 call (`NoV1TrackingCallsTests` must stay green).
- `?silent=true` on every write. Repairs restore what PoracleNG stored, so they're sent `?trusted=true` and skip `IUserAreaPolicy`, like the takeover restore.
- A repair is a uid-addressed `PUT /api/v2/humans/{id}/tracking/{type}/{uid}?silent=true&trusted=true&profile=N`, preceded by the same request with `&dry_run=true`. Apply only when the dry run answers `updated`; `unchanged` means nothing to do; a 409 means it would collide with another rule — skip and report it.
- Admin-only: every endpoint checks `IsAdmin` (as `AdminController.GetServerProfile` does) and refuses while impersonating.
- One scan or apply at a time per process; a second start answers 409 with the current state.
- The scan never writes. Apply re-checks each item with its own dry run, so a rule changed since the scan is judged on its current state.
- "PoracleWeb.NET" in full in docs and the CHANGELOG; prose direct, contractions, at most one em dash per paragraph, no "**Bold term:** explanation" lists in new prose; no Claude session links or `Co-authored-by` in commits; no plan task numbers in commit messages.
- Keep C# line endings as the file has them (`git diff --numstat` after each edit). dotnet with `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS=` and `--no-restore`.
- Every refusal or skip gets a legitimate-case test beside it; watch each new test fail first.

## Review Focus

1. **A rule on a profile other than the active one** (scheduler switched since): the repair must address `?profile=N`, not the active profile. Owned by Task 1 and Task 2.
2. **A rule limited to the user's own drawn fence:** the repair PUT must be trusted or it's refused `area not permitted`. Owned by Task 1.
3. **A raid that merely reads as every tier** (stored 90/9000, the normal case): the dry run says `unchanged` and nothing is written. Owned by Task 2.
4. **A human deleted between scan and apply, or PoracleNG down mid-scan:** that human is reported as skipped with the reason; the job finishes. Owned by Task 3.
5. **Running apply twice:** the second run finds nothing to do. Owned by Task 3.

---

### Task 1: Raw v2 rules everywhere, and a stored-rule replace

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs`
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.Reads.cs`, `PoracleTrackingProxy.Writes.cs`
- Create: `Core/Pgan.PoracleWebNet.Core.Models/StoredRule.cs`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/PoracleTrackingProxyRepairTests.cs`

**Interfaces:**
- Produces: `public sealed record StoredRule(string Type, int ProfileNo, int Uid, JsonElement Rule)` — `Rule` is the v2 rule exactly as PoracleNG returned it (no translation). `Type` uses v2 names (`incident` stays `incident`, not folded into invasion).
- Produces on `IPoracleTrackingProxy`: `Task<IReadOnlyList<StoredRule>> GetStoredRulesAsync(string userId)` — the all-profiles snapshot, plus the active profile's snapshot when the active profile isn't listed (the same fallback `GetAllTrackingAllProfilesAsync` uses, stamped with `current_profile_no`).
- Produces: `Task<StoredRuleReplaceOutcome> ReplaceStoredRuleAsync(string type, string userId, int uid, int profileNo, JsonElement v1Row, bool dryRun)` with `public enum StoredRuleReplaceOutcome { Updated, Unchanged, Conflict, NotFound }`. Translates `v1Row` with `TrackingV2Translator.TryTranslate` (an untranslatable row throws `AlarmValidationException` as other writes do), sends `PUT …/{uid}?silent=true&trusted=true&profile={profileNo}[&dry_run=true]`, maps 200 `updated`→Updated, 200 `unchanged`→Unchanged, 409→Conflict, problem+json 404 rule-not-found→NotFound, "human not found"→`AccountGoneException`, plaintext 404→`PoracleUnsupportedException`. Never calls `IUserAreaPolicy`.

- [ ] **Step 1: Write the failing tests**: `StoredRulesIncludeEveryProfileRaw` (rule JSON byte-equal to the response's), `StoredRulesKeepIncidentSeparate`, `StoredRulesAppendActiveProfileWithoutRow`, `ReplaceStoredRuleTargetsItsProfileAndIsTrusted` (URL exactly `…/raid/12?silent=true&trusted=true&profile=3&dry_run=true`), `ReplaceStoredRuleMapsOutcomes` (updated, unchanged, 409, rule 404, human 404), `ReplaceStoredRuleNeverAsksThePolicy`.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement**, reusing `GetV2Async`, `ProfilesOf`, `CurrentProfileNo`, `CreateRequest`, `ThrowForFailedWrite`.
- [ ] **Step 4: Run** the class plus `PoracleTrackingProxyV2Tests` → PASS.
- [ ] **Step 5: Commit** `feat: read stored v2 rules on every profile and replace one on its own profile`.

### Task 2: Rule repair classifier

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Services/RuleRepairClassifier.cs`, `Core/Pgan.PoracleWebNet.Core.Models/RuleRepair.cs`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/RuleRepairClassifierTests.cs`

**Interfaces:**
- Produces: `public enum RuleRepairKind { MetalGrunt, RaidPokemonZero, RaidAnyBossLevel9000, MaxBattleAnyBossLevel9000, CannotEdit }` and `public sealed record RuleRepairItem(string HumanId, string Type, int ProfileNo, int Uid, RuleRepairKind Kind, string Before, string? After, string? Reason)` (Before/After are short human-readable summaries such as `grunt_type metal` → `grunt_type steel`).
- Produces: `internal static class RuleRepairClassifier` with `static RuleRepairCandidate? Classify(StoredRule rule, string humanId)` returning null for a rule that needs nothing, or a candidate carrying the `RuleRepairItem` and, for repairable kinds, the v1 row to PUT (`JsonElement? RepairedV1Row`). `internal sealed record RuleRepairCandidate(RuleRepairItem Item, JsonElement? RepairedV1Row, bool NeedsDryRunToConfirm)`.

**Rules (decided; values verbatim):**
- invasion with `grunt_type` `metal` (case-insensitive) → `MetalGrunt`, repaired row = `ToV1Row` with `grunt_type` `steel`.
- raid with `pokemon_id` 0 → `RaidPokemonZero`, repaired row = `ToV1Row` with `pokemon_id` 9000 (by level) and its `level` kept.
- raid whose v2 read has `pokemon_id` and `level` both null → `RaidAnyBossLevel9000` candidate with `NeedsDryRunToConfirm = true` and repaired row = `ToV1Row` unchanged (level 90). The v2 read can't tell stored 9000/9000 from 90/9000; the job's dry run decides (`updated` = it was 9000/9000). Same for maxbattle → `MaxBattleAnyBossLevel9000`.
- any rule where `TrackingV2Translator.ToV1Row` throws, or `TryTranslate(ToV1Row(rule))` returns false → `CannotEdit` with `Reason` = the translator's reason and no repaired row (egg level 0, a PvP league outside 0/500/1500/2500, an enum PoracleNG added later).
- everything else → null.

- [ ] **Step 1: Write the failing tests**, one per rule plus legitimate cases: `MetalBecomesSteel`, `MetalMatchIsCaseInsensitive`, `SteelIsLeftAlone`, `RaidPokemonZeroBecomesByLevelKeepingItsLevel`, `RaidNamedBossIsLeftAlone`, `RaidByLevel5IsLeftAlone`, `RaidAnyTierNeedsDryRun`, `MaxBattleAnyTierNeedsDryRun`, `EggLevelZeroCannotBeEdited` (reason mentions level), `PvpLeagueOutsideTheFourCannotBeEdited`, `OrdinaryPokemonRuleNeedsNothing`, `IncidentRuleNeedsNothing`.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement** using `TrackingV2Translator.ToV1Row` / `TryTranslate` (pass `serverBounds: null, serverDeclared: null` for the round-trip check).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: classify stored rules PoracleNG 5.4.0 reads differently or can't edit`.

### Task 3: Repair job and admin endpoints

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IRuleRepairJob.cs`, `Core/Pgan.PoracleWebNet.Core.Services/RuleRepairJob.cs`
- Create: `Applications/Pgan.PoracleWebNet.Api/Controllers/AdminRuleRepairController.cs`
- Modify: `ServiceCollectionExtensions.cs` (singleton job)
- Test: `RuleRepairJobTests.cs`, `AdminRuleRepairControllerTests.cs`

**Interfaces:**
- Produces: `public enum RuleRepairPhase { Idle, Scanning, Scanned, Applying, Applied, Failed }`; `public sealed record RuleRepairStatus(RuleRepairPhase Phase, int HumansTotal, int HumansDone, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<RuleRepairItem> Repairable, IReadOnlyList<RuleRepairItem> CannotEdit, IReadOnlyList<RuleRepairSkip> Skipped, RuleRepairApplyResult? Applied, string? Error)`; `public sealed record RuleRepairSkip(string HumanId, string Reason)`; `public sealed record RuleRepairApplyResult(int Repaired, int AlreadyFine, int Conflicts, int Gone, IReadOnlyList<RuleRepairItem> Failed)`.
- Produces on `IRuleRepairJob` (singleton): `RuleRepairStatus GetStatus()`, `bool TryStartScan()`, `bool TryStartApply()` (false when busy, or for apply when the phase isn't `Scanned`/`Applied`).
- The job runs on a background `Task` with its own DI scope (`IServiceScopeFactory`), never the request's.
- Endpoints (`[Route("api/admin/rule-repair")]`, admin-only, refused while impersonating): `GET` → status; `POST scan` → 202 with status, 409 when busy; `POST apply` → 202, 409 when busy or nothing scanned.

**Behaviour:**
- Scan: `IPoracleHumanProxy.ListHumansAsync()` (all types), then per human `GetStoredRulesAsync` and `RuleRepairClassifier.Classify`. For `NeedsDryRunToConfirm` candidates, call `ReplaceStoredRuleAsync(..., dryRun: true)`; keep only `Updated`. A human whose read fails (`AccountGoneException`, `HttpRequestException`, a 4xx) is added to `Skipped` with the reason and the scan continues. Up to 4 humans in flight at once.
- Apply: for each repairable item, `ReplaceStoredRuleAsync(dryRun: true)` then, if `Updated`, the real PUT. `Unchanged` → AlreadyFine, `Conflict` → Conflicts (left as is), `NotFound` → Gone, an exception → Failed with the message. A rule whose uid comes back different is fine (PoracleNG reports it; nothing tracks these).
- Log one Information line per scan and apply with the counts, and a Warning per failure.

- [ ] **Step 1: Write the failing tests**: `ScanFindsRepairableAndCannotEdit`, `ScanConfirmsAnyTierRaidsWithADryRun` (an `Unchanged` dry run drops the candidate), `ScanSkipsAHumanWhoseReadFails`, `SecondScanWhileBusyIsRefused`, `ApplyRepairsOnlyWhatTheDryRunConfirms`, `ApplyCountsConflictsAndGoneRulesWithoutWriting`, `ApplyTwiceFindsNothingToDo`, controller `NonAdminIsForbidden`, `ImpersonatingIsForbidden`, `ApplyBeforeScanIs409`.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the full backend suite → green.
- [ ] **Step 5: Commit** `feat: admins can scan and repair rules PoracleNG 5.4.0 reads differently`.

### Task 4: Admin card

**Files:**
- Create: `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/rule-repair.service.ts` (+ spec), `src/app/modules/admin/rule-repair-card/rule-repair-card.component.{ts,html,scss,spec.ts}`
- Modify: `admin-settings.component.html` (place the card next to the PoracleNG versions card), the eleven locale files (`ADMIN.RULE_REPAIR.*`)

**Interfaces:**
- Consumes: Task 3's endpoints. Status polled every 2 s while `Scanning`/`Applying`, stopped otherwise and on destroy.
- UI: a title, one sentence saying what it does ("Finds alarms saved in a shape PoracleNG 5.4.0 reads differently, and fixes the ones whose meaning is clear. Nothing is changed until you press Repair."), a Scan button, progress (`HumansDone / HumansTotal`), a table of repairable items grouped by kind with Before → After, a Repair button behind the shared confirm dialog naming the count, the apply result, and the cannot-edit list with each reason. Skipped humans shown as a count with an expandable list.

- [ ] **Step 1: Write the failing specs**: service calls the three endpoints; card polls only while busy and stops on destroy; Repair asks for confirmation and doesn't call apply when cancelled; disabled buttons while busy.
- [ ] **Step 2: Run** `npx jest rule-repair` → FAIL.
- [ ] **Step 3: Implement**, standalone component with `inject()` and signals, Material 21, matching the versions card's styling.
- [ ] **Step 4: Run** `npx jest`, `npm run lint`, `npm run prettier-check`, `npm run build` → pass.
- [ ] **Step 5: Commit** `feat: rule repair card under Admin settings`.

### Task 5: Upgrade guide, docs, CHANGELOG

**Files:**
- Create: `docs/getting-started/upgrading-to-poracleng-5-4.md`
- Modify: `mkdocs.yml` (nav under Getting Started), `docs/index.md` (link), `docs/architecture/poracleng-compatibility.md` (link), `docs/configuration/site-settings.md` or the admin page doc if one describes Admin > Settings cards, `CLAUDE.md` (a short "Rule repair" section and File Locations rows), `CHANGELOG.md` (`[Unreleased]`: the repair card; the upgrade guide)

**Guide content (decided):** upgrade PoracleNG to 5.4.0 before PoracleWeb.NET; what users notice (Adds that would take over another alarm are refused as before, now by PoracleNG; adding the same max battle boss at a second radius is refused; a radius change reports rules it skipped because they'd clash; invasions saved as `metal` become `steel`); run Admin > Settings > Rule repair after upgrading (what each kind means, that nothing changes before Repair, that it can be re-run); rules on a profile number with no profile row are invisible to PoracleNG's v2 API until jfberry/PoracleNG#330 is fixed — they never fire, and the guide says so without offering a database command; `DB_*` is still required by this release for the settings migration and admin lists (sub-project 4 removes it — word this as "still required" without naming internal sub-projects).

- [ ] **Step 1: Write the page and the edits.**
- [ ] **Step 2: Run** `mkdocs build --strict` (scratchpad venv) → exit 0.
- [ ] **Step 3: Commit** `docs: upgrading to PoracleNG 5.4.0 and the rule repair card`.

### Task 6: Bed verification (controller)

- [ ] Build the branch image on dev-01, `instance.sh up sp3d 5199 540 poracleweb.net:sp3d`.
- [ ] Seed a probe human (`pweb-sp3d-probe-*`) on `poracle-ng-540` with: an invasion `metal` and one `steel`; a raid Pokémon 0 level 5; a raid 9000/9000 and a raid 90/9000; a max battle 9000/9000; an egg level 0; one of the repairable rules limited to a non-selectable fence and one on a second profile. v1's create (still served by PoracleNG) stores whatever it is given, which is the only way to recreate legacy rows on the bed; this is the dev bed only.
- [ ] As an admin token: scan, check the report lists exactly the expected items, apply, read the rules back (SQL on `poracle_dev_540`), run apply again (nothing to do). Clean up the probe human.
- [ ] Run `smoke_sp3b.py`-style smoke and e2eC against the instance; no new failures against 3b's baseline. Record results in the PR body.
