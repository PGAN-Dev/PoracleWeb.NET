# PoracleNG 5.4.0 sub-project 3b: tracking creates and edits on v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every alarm create and edit goes through PoracleNG's `/api/v2`, with no v1 fallback. Creates are protected by `dry_run`, user-drawn fences in `override_areas` are sent `?trusted=true` after PoracleWeb.NET's own check, and the merge guard, the reconciler, the ping refusal and the override-area decorator with its direct DB write are gone.

**Architecture:** The proxy keeps owning the wire. `IPoracleTrackingProxy.CreateAsync` becomes the v2 create protocol (dry run, refuse or skip a takeover, commit, restore on a race), so the ten services lose their guards and keep their shape. `UpdateByUidAsync` becomes a v2-only PUT and `TryReplaceV2Async` is deleted. The v1 create survives only as `UpsertV1Async`, for bulk distance and cleaning, which 3c moves to PATCH. The v1 row stays the internal currency (approach A); `TrackingV2Translator` is still the only exit onto v2.

**Tech Stack:** .NET 10, xUnit + Moq, Angular 21 (one dialog constant), mkdocs.

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md`, "Sub-project 3", the **Writes**, **Creates** and **Edits** paragraphs. It is phased by layer (3a reads/deletes, merged as #1008; 3b creates/edits; 3c bulk distance and cleaning; 3d repair and docs), as agreed on 2026-10-21. Builds on 3a's `TrackingV2Translator.ToV1Row` and `DefaultsFor`.

## Global Constraints

- PoracleNG **5.4.0** is the hard minimum. No code path may fall back to `/api/tracking` (v1) for a create or an edit. The only v1 tracking call left after this plan is `UpsertV1Async`, called only from bulk distance (`UpdateDistanceByUserAsync`, `UpdateDistanceByUidsAsync`) and `CleaningService`.
- A route answering gin's plaintext `404 page not found` means the server is too old: throw `PoracleUnsupportedException("saving alarms", RequiredVersion)` (the constant 3a's deletes use).
- Never a direct write to a Poracle table. `IAlarmOverrideAreaWriter` and `AlarmOverrideAreaWriter` are deleted.
- `?silent=true` on every v2 write.
- The takeover refusal keeps today's wording exactly: `"Another alarm of this type already uses those settings. Edit or remove that one instead."` (thrown as `TrackingConflictException`, which the global filter maps to 409). Max battle keeps `"You already have an identical max battle alarm."` for an exact duplicate.
- A row the translator cannot express is refused with `AlarmValidationException("PoracleNG can't store this alarm as it is: {reason}. Nothing was changed.")`. No v1 fallback, no silent field drop.
- Writes never send `profile_no`, `uid`, `id` or `description` in a rule body (the translator drops them); a target profile goes in `?profile=N` and only profile import uses it.
- Always write "PoracleWeb.NET" in full in docs and the CHANGELOG. No Claude session links or `Co-authored-by` in commits.
- Every new refusal gets a legitimate-case-still-passes test beside it. Watch each new test fail before the code that makes it pass.
- Keep line endings as the file has them (`git diff --numstat` after every `.cs` edit; see the memory note on the Edit tool flipping EOLs).
- Run `dotnet test` with `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS=` and `--no-restore` after the first restore.

## Review Focus

1. **An Add next to a rule one updatable field away** (same Pokemon, different `min_iv`; same raid, different radius): must be refused with the takeover message and must not change the other rule. Owned by Task 3 (proxy) and Task 4 (each service's legit-case test: two updatable differences still insert).
2. **A quick-pick apply that fans out over rules the user partly has already** (`all-invasions` when two grunts are already tracked): the new ones are created and tracked, the existing ones are left alone and not tracked, nothing is taken over. Owned by Task 3 (`RowUids`) and Task 4 (QuickPick test).
3. **An edit of a rule limited to a user-drawn geofence, or to an area since made non-selectable**: must save, with `?trusted=true`, and keep its areas. Owned by Task 2 and Task 3.
4. **An edit of a boss-specific raid or max battle** (stored level 9000): must go through v2 and keep the boss (today it falls back to v1 because 9000 > v2's max 90). Owned by Task 1.
5. **The `all-invasions` quick pick and the invasion add dialog's steel button**: v2 refuses `metal`, so both must send `steel`. Owned by Task 6.

---

### Task 1: Translator write direction: defaults go out as absent, no fallback reasons left unexplained

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Translator.cs`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/TrackingV2WriteDefaultsTests.cs` (create)
- Modify: `Tests/Pgan.PoracleWebNet.Tests/Services/TrackingV2TypeTranslationTests.cs` (only tests whose expectations change)

**Interfaces:**
- Consumes: `TrackingV2Translator.DefaultsFor(string type)` and the `Specs` tables from 3a.
- Produces: `TryTranslate(type, row, serverBounds, serverDeclared, out translated, out unsupported)` unchanged in signature; its output now omits every property equal to its read default unless the type lists it in `Required`. `OmitWhenEquals` is deleted (every entry equals a read default). Ping is always written when present (5.4.0 carries jfberry/PoracleNG#249); delete the capability gate inside `TryWritePing` if one remains.

- [ ] **Step 1: Write the failing tests** in `TrackingV2WriteDefaultsTests`:
  - `RaidNamingABoss_OmitsLevel9000`: v1 raid `{pokemon_id:150, level:9000, team:4, ...}` translates with no `level` property and validates against `V2RaidRule` in `Fixtures/poracleng-openapi-5.4.0.json` (use the same schema-validation helper `TrackingV2TypeTranslationTests` uses).
  - `RaidByLevel_KeepsLevel5`, `RaidByLevelAnyTier_OmitsLevel90`, `RaidAnyBossLevel9000_OmitsLevel` (pokemon_id 9000, level 9000: omitted, which stores 90).
  - Same four for `maxbattle`.
  - `PokemonAtDefaults_SendsOnlyPokemonId`: a `MonsterCreate`-default row (bound through `MonsterCreate.ToMonster()` then `PoracleJsonHelper.SerializeToElement`) translates to `{pokemon_id, template?, ...}` containing no key whose value equals `DefaultsFor("pokemon")`; `pokemon_id` is present even at 0.
  - `GymIdEmptyString_IsOmitted` for raid, egg and gym.
  - `FortIncludeEmptyZero_IsSentFalse` and `FortWithoutIncludeEmpty_IsRefused` (Required stays).
  - `EggLevelZero_IsRefusedWithReason`: `TryTranslate` false, `unsupported` mentions `level`.
  - `NonDefaultValues_AreSent`: pokemon `min_iv:90`, `pvp_ranking_best:5`, raid `team:2` are present in the output.
  - `PingIsAlwaysWritten`: a row with `ping:"<@&1>"` translates with `ping` present for all ten types.
- [ ] **Step 2: Run them, expect failures**: `dotnet test --no-restore --filter FullyQualifiedName~TrackingV2WriteDefaultsTests` → raid/maxbattle 9000 cases and the pokemon-defaults case fail.
- [ ] **Step 3: Implement.** In the per-property write loop, skip a property when the type's `Required` does not list it and its value equals the type's read default (compare numbers as numbers, `""` and `null` as equal for strings). `LevelFollowsPokemonId` types: omit `level` when it is 9000, or when it is 90 and `pokemon_id` is 9000 or absent. Update the class remarks: the "Sentinels are sent verbatim" paragraph becomes "defaults go out as absent, because that is what v2 stores them as, and v2 bounds some sentinels (`level` max 90)". Delete the remarks that describe falling back to v1.
- [ ] **Step 4: Run** the new tests plus `TrackingV2TypeTranslationTests` and `TrackingV2ReadTranslationTests`; fix only expectations that asserted a verbatim sentinel. Expected: all pass.
- [ ] **Step 5: Commit** `feat: v2 writes send our defaults as absent and keep boss raid levels`.

### Task 2: One area policy for subscriptions and override areas

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaPolicy.cs`, `Core/Pgan.PoracleWebNet.Core.Services/UserAreaPolicy.cs`
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/UserAreaWriter.cs` (use the policy instead of its private `AllowedKeysAsync`/`Key`/`Clean`)
- Modify: `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs` (register scoped)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/UserAreaPolicyTests.cs` (create); existing `UserAreaWriter` tests keep passing unchanged.

**Interfaces:**
- Produces:
  - `Task<IReadOnlySet<string>> AllowedKeysAsync(string humanId)`: PoracleNG's `userSelectable` areas for the human, the human's own drawn fences, and what the active profile holds, as keys.
  - `static string Key(string name)` (trim, lowercase, `_` → space) and `static string Clean(string name)` (trim, lowercase), moved verbatim from `UserAreaWriter`.
  - `Task EnsureOverrideAreasAllowedAsync(string humanId, IReadOnlyCollection<string> requested, Func<Task<IReadOnlyCollection<string>>>? storedOnRule)`: throws `AlarmValidationException("These areas can't be used on an alarm: {names}.")` for names outside `AllowedKeysAsync`, after also allowing the names `storedOnRule` returns (called lazily, only when something would otherwise be refused).
- Depends on `IPoracleHumanProxy` and `IUserGeofenceRepository` only, never on `IPoracleTrackingProxy` (`UserAreaWriter` depends on the tracking proxy, and Task 3 makes the tracking proxy depend on this policy).

- [ ] **Step 1: Write the failing tests**: `SelectableArea_Allowed`, `OwnFence_Allowed` (fence served `userSelectable:false`), `AreaOnActiveProfile_Allowed`, `OtherUsersPrivateFence_Refused` (names it in the message), `NameStoredOnTheRuleBeingEdited_Allowed` and `StoredOnRule_NotReadWhenEverythingIsAllowed` (the delegate is never invoked), `CaseAndUnderscores_MatchAsPoracleNGKeysThem`.
- [ ] **Step 2: Run, expect compile failure / FAIL.**
- [ ] **Step 3: Implement** `UserAreaPolicy`, and make `UserAreaWriter` call `AllowedKeysAsync` and the static helpers from it.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~UserAreaPolicy|FullyQualifiedName~UserAreaWriter"` → PASS.
- [ ] **Step 5: Commit** `refactor: one area policy for subscriptions and alarm override areas`.

### Task 3: Proxy creates and edits through v2 only

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs`
- Create: `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.Writes.cs` (move `PutV2Async`, `UidFromV2Envelope`, `ServerRuleSchemaAsync` here; add the create protocol)
- Create: `Core/Pgan.PoracleWebNet.Core.Services/OverrideScope.cs` (the three coherence rules, moved from `UserOwnedOverrideAreaProxy.EnsureScopeIsCoherent`, same wording)
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs`
- Delete: `Core/Pgan.PoracleWebNet.Core.Services/UserOwnedOverrideAreaProxy.cs`, `Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IAlarmOverrideAreaWriter.cs`, `Core/Pgan.PoracleWebNet.Core.Repositories/AlarmOverrideAreaWriter.cs`, `Tests/.../Services/UserOwnedOverrideAreaProxyTests.cs`, `Tests/.../Repositories/AlarmOverrideAreaWriterTests.cs`
- Modify: `ServiceCollectionExtensions.cs` (register `AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>()` directly; drop the writer registration and the decorator comment)
- Test: `Tests/.../Services/PoracleTrackingProxyV2Tests.cs` (extend), `Tests/.../Services/TrackingCreateProtocolTests.cs` (create)

**Interfaces:**
- Produces on `IPoracleTrackingProxy`:
  - `Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body)`: `body` is one v1 row (object) or several (array). Behaviour below.
  - `Task<TrackingCreateResult> CreateOnProfileAsync(string type, string userId, JsonElement body, int profileNo)`: the same, with `&profile=N`. An overload rather than an optional parameter, because Moq setups are expression trees and cannot use optional arguments (CS0854), which would break every existing `Setup(x => x.CreateAsync(...))`.
  - `Task<TrackingCreateResult> UpsertV1Async(string type, string userId, JsonElement body)`: today's v1 `CreateAsync` body, renamed, doc comment "v1; bulk distance and cleaning only, until sub-project 3c".
  - `Task<TrackingUpdateResult> UpdateByUidAsync(string type, string userId, int uid, JsonElement body)`: v2 PUT only.
  - `TryReplaceV2Async` is deleted.
- Produces on `TrackingCreateResult`: a new trailing positional member `IReadOnlyList<int> RowUids` (one entry per submitted row, in submission order: the created uid, or 0 when nothing was created for that row) and `int TakeoversSkipped`. Existing members keep their meaning: `NewUids` = created uids, `AlreadyPresent` = rows already stored identically, `Updates` = 0 after this task, `Inserts` = created count. Give both new members defaults so existing `new TrackingCreateResult(...)` calls in tests compile, or update them.
- Consumes: `IUserAreaPolicy` (Task 2), `TrackingV2Translator.TryTranslate` and `ToV1Row` (Task 1, 3a).

**Write behaviour, both verbs:**
- Translate every row first; any failure → the `AlarmValidationException` from Global Constraints, naming the reason, before any request.
- `OverrideScope.EnsureCoherent(row)` for every row, before any request.
- Rows carrying a non-empty `override_areas`: `IUserAreaPolicy.EnsureOverrideAreasAllowedAsync` (for an edit, `storedOnRule` reads `GET /api/v2/humans/{id}/tracking/{type}/{uid}` and returns its `override_areas`). If any row carries one, the request gets `&trusted=true`.
- Status mapping as `PutV2Async` has it today, except: plaintext 404 → `PoracleUnsupportedException`; 409 → `TrackingConflictException(type, <the takeover message>)`. Delete `ShouldTryV2`, `ServerCarriesV2Async`, `V2AbsentCacheKey`, `LogV2RouteAbsent`, `InvasionUnsendableReasonAsync`, `RefuseIfV1WouldDropPing`, `LogV2Untranslatable`, and the `IInvasionGruntNameService` dependency (delete the interface, `InvasionGruntNameService` and its registration and tests if nothing else uses them; v2 itself answers 422 naming an unknown grunt).

**Create protocol** (`POST /api/v2/humans/{id}/tracking/{type}?silent=true[&profile=N][&trusted=true]`, body a JSON array):

```
dry = POST …&dry_run=true with all rows
if dry.updated is empty and dry.unchanged is empty:
    keep = all rows
else if body was a single object:
    if dry.updated non-empty: throw TrackingConflictException(takeover message)
    return { AlreadyPresent: 1, RowUids: [0] }          # identical rule exists; nothing written
else:
    classify each row alone with its own dry run; keep the ones it reports as created;
    unchanged rows → RowUids 0, AlreadyPresent++; updated rows → RowUids 0, TakeoversSkipped++ (log each)
    if keep is empty: return
commit = POST (no dry_run) with keep
for each commit.updated entry: PUT its `previous` (ToV1Row → TryTranslate) back to its uid with trusted=true,
    then single → throw TrackingConflictException(takeover message); batch → TakeoversSkipped++
RowUids for kept rows = commit.created uids in order when the counts match (PoracleNG keeps submission
    order for inserts); otherwise 0 for all of them, NewUids still lists what was created, and log a warning
```

PoracleNG classifies only against stored rules, never between rows of one batch (`store.RowDiffPolicy.Classify`), which is why one dry run per row on the mixed path is safe.

- [ ] **Step 1: Write the failing tests** in `TrackingCreateProtocolTests` (fake `HttpMessageHandler` recording requests, as `PoracleTrackingProxyV2Tests` does):
  - `SingleCreate_AllNew_DryRunThenCommit_ReturnsUid` (two requests, second without `dry_run`, both `silent=true`, body is an array of one translated rule).
  - `SingleCreate_WouldTakeOver_RefusesWithMessage_NoCommit`.
  - `SingleCreate_Identical_ReturnsAlreadyPresent_NoCommit`.
  - `SingleCreate_TwoUpdatableDifferences_DryRunSaysCreated_Commits` (legit case).
  - `CommitReportsUpdated_RestoresPreviousThenRefuses` (asserts the PUT body is the translated `previous` and carries `trusted=true`).
  - `Batch_AllNew_RowUidsInOrder`.
  - `Batch_Mixed_PerRowDryRuns_CommitsOnlyNew_RowUidsZeroForOthers_TakeoversSkippedCounted`.
  - `Batch_CommitCountMismatch_RowUidsAllZero_NewUidsKept`.
  - `ProfileNo_GoesInQuery_NotBody`.
  - `UntranslatableRow_RefusedBeforeAnyRequest` and `IncoherentScope_RefusedBeforeAnyRequest` (each of the three rules).
  - `OwnFenceInOverrideAreas_SendsTrusted`; `NoOverrideAreas_NoTrustedNoPolicyCall`; `ForeignPrivateFence_Refused_NoRequest`.
  - `PlaintextRoute404_ThrowsUnsupported`.
- [ ] **Step 2: Extend `PoracleTrackingProxyV2Tests`** for edits: `Update_IsSinglePut_NeverV1` (no request to `/api/tracking`), `Update_409_MapsToTakeoverMessage`, `Update_StoredLegacyArea_AllowedViaStoredRule_SendsTrusted`, `Update_UntranslatableRow_Refused_NoRequest` (replacing every test that expected a v1 fallback; delete tests that asserted the ping refusal or the grunt-name gate, saying why in the commit message).
- [ ] **Step 3: Run, expect failures.**
- [ ] **Step 4: Implement** per the behaviour above. Delete the decorator, the writer and their tests; register the proxy directly.
- [ ] **Step 5: Build the solution** (`dotnet build --no-restore`) and fix only compile errors here; behaviour changes come in Task 4.
- [ ] **Step 6: Run** `dotnet test --no-restore` → the proxy tests pass; record any service-test failures in the report for Task 4 (expected: tests asserting the v1 fallback or the reconciler).
- [ ] **Step 7: Commit** `feat: tracking creates and edits go through PoracleNG v2 only` (plus a second commit for the deletions if it keeps the diff readable).

### Task 4: Services drop the guards and the v1 tail

**Files:**
- Modify: `EggService.cs`, `FortChangeService.cs`, `GymService.cs`, `InvasionService.cs`, `LureService.cs`, `MaxBattleService.cs`, `MonsterService.cs`, `NestService.cs`, `QuestService.cs`, `RaidService.cs`, `TrackingV2Replacement.cs`, `TrackingUpdateReconciler.cs`, `TrackingFieldPreserver.cs` (comment only), all under `Core/Pgan.PoracleWebNet.Core.Services/`; the bulk-distance methods in the ten services and `CleaningService` switch `CreateAsync` → `UpsertV1Async` and nothing else (their tests' `Setup`/`Verify` move with them).
- Delete tests: `OverrideScopeMergeGuardTests.cs`, `LureInvasionMergeGuardTests.cs`, and the reconcile/no-op-edit cases in `CreateResultSemanticsTests.cs` and the per-service tests.
- Test: `AlarmServiceV2UpdatePathTests.cs` (rewrite to "every type's UpdateAsync makes exactly one PUT and no POST"), per-service tests.

**Interfaces:**
- Consumes: Task 3's `CreateAsync`, `UpdateByUidAsync`, `TrackingCreateResult.RowUids`.
- Produces: `TrackingV2Replacement.ApplyAsync(proxy, type, userId, oldUid, body, uidRemapper) -> Task<int>` (renamed from `TryApplyAsync`, never null). `TrackingUpdateReconciler` keeps only what bulk distance needs (`EnsureBatchDoesNotCollapse`, `EnsureBatchDoesNotTakeOverOthersAsync` and their helpers); `ReconcileAsync`, `EnsureNoMergeIntoAnotherAlarmAsync`, `IsNoOpEditAsync` and helpers only they use are deleted.

- [ ] **Step 1: Write the failing tests**, per service where the type has one:
  - `Create_TakeoverRefusal_PropagatesAsConflict` (proxy mock throws; service lets it through, no second write).
  - `Update_MakesOnePutNoPost` for all ten (in `AlarmServiceV2UpdatePathTests`).
  - `BulkCreate_UsesRowUids` for monster, invasion and max battle: three rows, `RowUids [11, 0, 12]` → models get 11, 0, 12.
  - `MaxBattle_Create_Identical_StillRefusedWithItsMessage` (proxy returns `AlreadyPresent:1`), `MaxBattle_Create_DiffersByRadius_Inserts` (legit), `MaxBattle_Update_NoDeleteThenCreate`.
  - `QuickPick_AllInvasions_PartlyTracked_TracksOnlyNew` in `QuickPickServiceSecurityTests` or a new `QuickPickApplyPartialTests`.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement.** Each `CreateAsync`: drop `EnsureNoMergeIntoAnotherAlarmAsync`, call `_proxy.CreateAsync`. Each `UpdateAsync`: keep the feature gate, the preserver and any type check that is not a merge guard (invasion's `RequireGruntType`); replace the v2-then-v1 block with `model.Uid = await TrackingV2Replacement.ApplyAsync(...)`. `MonsterService.UpdateAsync` keeps its own remap. `MaxBattleService`: delete the sibling scan on create and update and the delete-then-create; map `AlreadyPresent > 0 && Inserts == 0` on create to the identical-alarm refusal. `BulkCreateAsync`: assign `RowUids[i]`. Remove the `ILogger` parameter from services only where nothing else logs.
- [ ] **Step 4: Run** `dotnet test --no-restore` → all pass.
- [ ] **Step 5: Grep check**: `grep -rn "EnsureNoMergeIntoAnotherAlarmAsync\|ReconcileAsync\|TryReplaceV2Async\|TryApplyAsync" Core Applications Tests --include=*.cs` → no hits. `grep -rn "UpsertV1Async" Core --include=*.cs` → only the ten distance methods, `CleaningService` and the proxy.
- [ ] **Step 6: Commit** `feat: alarm services rely on PoracleNG v2 for collisions; merge guard and reconciler removed`.

### Task 5: Profile import and duplicate write straight to the target profile

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/ProfileOverviewService.cs`
- Test: `Tests/.../Services/ProfileOverviewServiceTests.cs`

**Interfaces:**
- Consumes: `CreateOnProfileAsync(type, userId, body, profileNo)`.
- Produces: `ImportAlarmsAsync` unchanged in signature; returns the number of rules created (sum of `Inserts`).

- [ ] **Step 1: Write the failing tests**: `Import_NeverSwitchesProfile` (no `SwitchProfileAsync` call), `Import_OneCreatePerType_WithTargetProfile`, `Import_IdenticalRulesOnTarget_NotCounted`, `Import_StillRefusesInvalidFileBeforeAnyWrite` (existing #548 behaviour kept).
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement**: per type, strip `uid`/`profile_no`/`id` from each row as today, then one `CreateOnProfileAsync(type, userId, array, targetProfileNo)`. Delete the switch-and-restore and its `finally`.
- [ ] **Step 4: Run** the class → PASS.
- [ ] **Step 5: Commit** `feat: profile import and duplicate write to the target profile without switching`.

### Task 6: Invasion grunt names and Pokéstop events on v2

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Models/InvasionGruntTypes.cs` (`metal` → `steel`)
- Modify: `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/invasions/invasion-add-dialog.component.ts` (`gruntType: 'steel'`; keep `key`/icon as they are, check the card label still resolves)
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/InvasionService.cs` (event grunts go to `IPoracleIncidentProxy`)
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/PoracleIncidentProxy.cs`, `IPoracleIncidentProxy.cs`, `PokestopEventService.cs`
- Test: `InvasionServiceTests.cs`, `InvasionEventGruntTests.cs`, `PokestopEventServiceTests.cs`, the dialog spec

**Interfaces:**
- Consumes: `IUserAreaPolicy` (Task 2), `PokestopEventTypes` name ↔ display type helpers (add `DisplayTypeFor(string name)` if only `NameFor` exists).
- Produces: `IPoracleIncidentProxy.CreateAsync(userId, rules, bool dryRun = false)`; `ReplaceAsync` and `CreateAsync` send `trusted=true` after `EnsureOverrideAreasAllowedAsync` when a rule carries override areas; `BulkDeleteByUidsAsync` sends `{"uids":[...]}` in the body like 3a's tracking deletes.

- [ ] **Step 1: Write the failing tests**:
  - `AllInvasionsFanOut_HasNoMetal_HasSteel` and a test that every name in `InvasionGruntTypes.All` is in the 5.4.0 fixture's grunt enum or the catch-alls (if the fixture has no enum, assert against a list copied from the bed's `/api/masterdata/grunts` in the test, with the date).
  - `InvasionCreate_EventGrunt_GoesToIncident` and `InvasionUpdate_EventGrunt_GoesToIncidentReplace` (no invasion POST/PUT).
  - `PokestopEventCreate_WouldTakeOver_Refused` via dry run (replacing `RefuseIfEventTaken`'s own diff), `PokestopEventCreate_AreaScopedTwin_Inserts` (legit).
  - `IncidentBulkDelete_SendsUidsInBody`.
  - `PokestopEvent_OwnFence_SendsTrusted`.
- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement.** `PokestopEventService` uses the dry-run classification (created/updated/unchanged, same rules as Task 3's single and batch paths) instead of `RefuseIfEventTaken`; keep its same-request duplicate check. Delete the DI comment saying incident is deliberately not wrapped.
- [ ] **Step 4: Run** backend tests and `npx jest invasion-add-dialog` → PASS.
- [ ] **Step 5: Commit** `feat: invasion steel grunt, event grunts and Pokestop events write through v2`.

### Task 7: Docs, CLAUDE.md and CHANGELOG

**Files:**
- Modify: `docs/features/alarms.md` (an Add that would take over another alarm is refused; editing keeps areas limited to your own geofences), `docs/architecture/poracleng-proxy.md`, `docs/architecture/backend.md`, `docs/architecture/poracleng-compatibility.md` if it still describes a v1 fallback
- Modify: `CLAUDE.md`: delete or rewrite "PoracleNG Decides Insert vs Update vs Duplicate" (now: v2 dry run decides, the mirror is gone), the `TryReplaceV2Async` / v1-fallback / ping-refusal passages in "`/api/v2` Writes", "User Geofence Persistence" remnants about `UserOwnedOverrideAreaProxy`, the "Sentinels are sent verbatim" paragraph, the `IUserAreaDualWriter`/`AlarmOverrideAreaWriter` rows in File Locations, and `TrackingUpdateReconciler` mentions that describe deleted methods
- Modify: `CHANGELOG.md` under `[Unreleased]`

- [ ] **Step 1: Grep for stale claims**: `grep -rn -i "falls back to v1\|v1 fallback\|TryReplaceV2\|UserOwnedOverrideAreaProxy\|AlarmOverrideAreaWriter\|EnsureNoMergeIntoAnotherAlarmAsync\|ReconcileAsync\|sent verbatim\|can't preserve it" docs CLAUDE.md` and fix every hit that describes create or edit behaviour (3c's bulk distance and cleaning still use v1; say so where relevant).
- [ ] **Step 2: CHANGELOG entries** (Keep a Changelog, full product name): creates and edits go through PoracleNG's v2 API; an Add that would take over another alarm is refused as before, now decided by PoracleNG; an alarm limited to your own geofence is saved by PoracleNG instead of a direct database write, so `DB_*` is used only by the settings migration and the admin user list (check what SP1/SP2 already left and say it accurately); boss-specific raid and max battle edits no longer depend on the older API; the steel grunt was being saved as `metal`, which matched nothing; profile import no longer switches your active profile while it copies.
- [ ] **Step 3: Verify**: `python -m mkdocs build --strict` (the scratchpad venv from 3a works) exits 0.
- [ ] **Step 4: Commit** `docs: v2 creates and edits`.

### Task 8: Bed verification

Run by the controller, not a subagent: it needs dev-01.

- [ ] **Step 1:** Build the branch image locally, run it as `pweb-e2e540` against `poracle-ng-540` (:3046), following the recipe in the memory note `project_api_smoke_harness`. Probe humans only (`pweb-*probe*`), cleaned up after.
- [ ] **Step 2:** Confirm the batch assumption live: a three-rule POST with `dry_run=true` where one rule is new, one identical and one a one-field update reports them in `created`, `unchanged`, `updated` with `previous`, and a commit of the new rule alone reports one `created` in order.
- [ ] **Step 3:** Run `smoke.py` and the e2e scripts (`e2eB`, `e2eC`, `e2eD`) against the instance. Expected: no new failures against 3a's baseline (254/260 smoke, 149/173 e2eC, with the known environmental and gender-3 cases). Exercise by hand: Add next to a one-field sibling (refused), Add next to a two-field sibling (created), edit a boss raid, edit a pokemon limited to a drawn fence, apply `all-invasions` with two grunts already tracked, import a profile.
- [ ] **Step 4:** Record the results in the PR body.
