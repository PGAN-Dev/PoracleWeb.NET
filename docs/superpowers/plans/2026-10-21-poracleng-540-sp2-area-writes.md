# PoracleNG 5.4.0 Area Writes on v2 (Sub-project 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every user-fence area write goes through PoracleNG's v2 area routes, so `IUserAreaDualWriter` no longer touches `humans.area` / `profiles.area`.

**Architecture:** The proxy gains the v2 area operations PoracleNG 5.4.0 added: trusted set-areas, remove-everywhere and rename-everywhere, each with `dry_run`, plus a per-profile bulk tracking delete. A new `IUserAreaWriter` in Core.Services owns "which names this user may write" and every fence lifecycle write. `UserGeofenceService` and `AreaController` call it instead of the dual writer. Fence delete gains a dry-run impact preview, so the user chooses what happens to alarms that would widen. Only `SetAlarmOverrideAreasAsync` survives, moved to `IAlarmOverrideAreaWriter` for sub-project 3.

**Tech Stack:** .NET 10 / xUnit + Moq, Angular 21 / Jest, PoracleNG 5.4.0 (develop `715c0d7ea`).

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md`, section "Sub-project 2", plus "Docs" and "Testing".

## Global Constraints

- PoracleNG minimum is 5.4.0 (already enforced). Use v2 only; no DB fallback for any write this plan moves.
- Allowed names for a trusted write are the union of three sets. A name in none of them is refused, with the name in the error:
  1. names `GET /v2/humans/{id}/areas` lists with `userSelectable: true` for this human;
  2. the user's own drawn fences (`user_geofences.koji_name` for this human, lowercased);
  3. names the user already holds on the target profile.
- PoracleNG still drops names it has no fence for, even when trusted. Production holds 69 such names across 30 users (2026-10-21 count: renamed fences, `&`-escaped names, underscore spellings). That behaviour is unchanged and must not error the save.
- Verified on the 5.4.0 bed (2026-10-21):
  - Bulk tracking DELETE accepts `?profile=N` and refuses `?all_profiles` (422). It silently skips uids that aren't on that profile.
  - `DELETE …/areas?name=&all_profiles=true&dry_run=true` returns `{name, removed_from:[profiles], broadened:{type:[uids]}, dry_run}`. Types are PoracleNG names; incident rules report as `invasion`.
- Rename and approval must reload PoracleNG's geofences BEFORE the PATCH (`to` must be loaded). Create must reload before its POST (#937).
- Activate and deactivate target the **active** profile (today's behaviour; the JWT `ProfileNo` is not sent).
- Copy, translated in all 11 locales (`da, de, en, es, fr, it, nl, pl, pt-BR, pt, sv`):
  - `GEOFENCES.SNACK_SAVED_NOT_ACTIVE` = "Saved, but it couldn't be switched on yet. Use the toggle to retry."
  - The delete choice: `GEOFENCES.DELETE_WIDEN_TITLE`, `GEOFENCES.DELETE_WIDEN_BODY` ("{{count}} alarms are limited to this fence."), `GEOFENCES.DELETE_WIDEN_DELETE` ("Delete those alarms too", the default), `GEOFENCES.DELETE_WIDEN_KEEP` ("Keep them, using my profile's areas").
- Repo rules from CLAUDE.md:
  - Every refusal gets a legitimate-case test.
  - Watch new tests fail first.
  - Preserve each file's line endings (check `git diff --numstat`).
  - Keep the CHANGELOG under `[Unreleased]`.
  - Write "PoracleWeb.NET" in full.
  - No Claude trailers or links.
- Backend: `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS= dotnet test Tests/Pgan.PoracleWebNet.Tests --no-restore`.
- Frontend (`ClientApp`): `npm test`, `npm run lint`, `npm run prettier-check`, `npm run build`. Its `node_modules` is a junction to the main checkout: never install into it or delete it.

## Review Focus

1. **A user saves Areas holding a name PoracleNG doesn't know.** The 69 production cases. Expected: the save succeeds; the unknown name drops exactly as it does today; no error. Pinned in Task 2.
2. **A user tries to select someone else's private fence**, or a hidden admin fence, via a crafted `PUT /api/areas`. Expected: 400 naming the fence; nothing written. Pinned in Task 2.
3. **Deleting a fence whose widened alarms sit on a non-active profile.** Expected: with "delete", those alarms are deleted too, not left widened. Pinned in Task 4.
4. **PoracleNG unreachable during a fence delete** after the impact preview. Expected: a readable 503 (the controllers carry `PoracleUnreachableExceptionFilter`), and the fence row is NOT deleted, so the fence stays consistent with its subscriptions. Pinned in Task 4.
5. **Approval where the promoted name isn't loaded yet** (reload failed). Expected: the approval still succeeds (Koji has the fence); the owner's subscription rename is skipped with a warning log and reported in the response. Pinned in Task 5.

---

### Task 1: Proxy — v2 area operations and per-profile bulk delete

**Files:**
- Modify:
  - `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs`
  - `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs`
  - `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs`
  - `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs`
  - The `UserOwnedOverrideAreaProxy` decorator: pass the new parameter through.
- Create: `Core/Pgan.PoracleWebNet.Core.Models/AreaRemovalResult.cs` and `Core/Pgan.PoracleWebNet.Core.Models/AreaRenameResult.cs`.
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/PoracleHumanProxyV2Tests.cs`, `PoracleTrackingProxyV2Tests.cs`.

**Interfaces (Produces):**
- `Task<IReadOnlyList<string>> SetAreasTrustedAsync(string userId, IReadOnlyCollection<string> areas)`
  - POSTs `/api/v2/humans/{id}/areas {areas, trusted:true}`, a whole-list replace of the active profile.
  - Returns PoracleNG's `rejected` names (empty when none).
  - Throws on a refusal, via the existing `EnsureAcceptedAsync`.
- `Task<IReadOnlyList<string>> GetAreaListAsync(string userId)`
  - Returns the active profile's current area list, lowercased, from the v2 human read.
  - Throws if it can't be read. No null, no fallback.
- `Task<IReadOnlyList<V2AreaInfo>> GetAvailableAreasAsync(string userId)`
  - Returns `{Name, UserSelectable, ExemptFromAreaSecurity}`, taken from `GET /api/v2/humans/{id}/areas`.
  - Reuse the existing model if `PoracleApiProxy.GetAreasWithGroupsAsync` already parses this route; otherwise create `V2AreaInfo` in Core.Models.
- `Task<AreaRemovalResult> RemoveAreaEverywhereAsync(string userId, string name, bool dryRun)`
  - Calls `DELETE /api/v2/humans/{id}/areas?name=&all_profiles=true[&dry_run=true]`.
  - `AreaRemovalResult(IReadOnlyList<int> RemovedFrom, IReadOnlyDictionary<string, IReadOnlyList<int>> Broadened, bool DryRun)`.
- `Task<AreaRenameResult> RenameAreaEverywhereAsync(string userId, string from, string to, bool dryRun)`
  - Calls `PATCH /api/v2/humans/{id}/areas?from=&to=&all_profiles=true&trusted=true[&dry_run=true]`.
  - `AreaRenameResult(IReadOnlyList<int> RenamedIn, bool DryRun)`.
- `IPoracleTrackingProxy.BulkDeleteByUidsAsync` gains `int? profileNo = null`.
  - When set, it sends `?profile=N` on the v2 bulk DELETE.
  - It returns the uids PoracleNG reports deleted: `Task<IReadOnlyList<int>>`. If the existing signature returns something else, add an overload `BulkDeleteByUidsOnProfileAsync(string type, string userId, IReadOnlyList<int> uids, int profileNo)` instead and leave the old method alone.
- All new calls go through `SendV2Async` (plaintext 404 → `PoracleUnsupportedException`). Escape webhook ids with `Uri.EscapeDataString`, and URL-encode the area names.

- [ ] **Step 1: Write the failing tests**
  - **URLs and bodies:** each new method requests exactly its route and query string. Test with a name containing a space and an `&`.
  - **Results:** `RemoveAreaEverywhereAsync` parses `removed_from` and `broadened`, including `broadened: {}` and `null`. `RenameAreaEverywhereAsync` parses `renamed_in`.
  - **Rejected list:** `SetAreasTrustedAsync` returns `rejected`.
  - **Per-profile delete:** a bulk delete with profile 2 sends `?profile=2` and returns the deleted uids.
  - **Failure cases:** a plaintext 404 on each new route throws `PoracleUnsupportedException`; a 422 throws `PoracleRequestRefusedException` carrying PoracleNG's detail.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat: v2 area operations and per-profile bulk delete in the PoracleNG proxies`

### Task 2: `IUserAreaWriter` — authorisation and the Areas page save

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaWriter.cs` and `Core/Pgan.PoracleWebNet.Core.Services/UserAreaWriter.cs`.
- Modify:
  - `Applications/Pgan.PoracleWebNet.Api/Controllers/AreaController.cs` (`UpdateAreas`)
  - `ServiceCollectionExtensions.cs` (scoped registration)
  - `UserGeofenceService.cs` (delete `PreserveOwnedAreasInHumanAsync` and its interface member)
- Test: create `Tests/Pgan.PoracleWebNet.Tests/Services/UserAreaWriterTests.cs`; modify `Controllers/AreaControllerTests.cs`.

**Interfaces (Produces):**
- `Task<AreaSaveResult> SaveSelectionAsync(string humanId, IReadOnlyCollection<string> requested)`
  - `AreaSaveResult(IReadOnlyList<string> Stored, IReadOnlyList<string> Dropped)`. `Dropped` is PoracleNG's `rejected`.
  - Lowercases and trims the names, then authorises them by the three Global Constraints rules against the current list.
  - Throws `AreaNotPermittedException(IReadOnlyList<string> names)` if any name is outside all three. Create it in Core.Models, and map it to 400 `{ error, areas }` in a filter or in the controller, following existing filter style.
  - Otherwise calls `SetAreasTrustedAsync`.
- `Task<bool> AddToActiveProfileAsync(string humanId, string name)` and `Task<bool> RemoveFromActiveProfileAsync(string humanId, string name)`.
  - Each reads the list, changes it, and posts it trusted.
  - Returns false for a no-op, or when PoracleNG rejected the added name (the caller decides what that means).
- The ownership check reads `IUserGeofenceRepository`. Don't make `UserAreaWriter` depend on `UserGeofenceService`, which would create a cycle.

- [ ] **Step 1: Write the failing tests.** Refusal cases:
  - another user's fence → `AreaNotPermittedException(["other fence"])`, nothing posted;
  - a non-selectable admin fence not held → refused.

  Legitimate cases still pass:
  - a selectable admin fence, the user's own fence, and an already-held non-selectable fence → posted with `trusted:true`;
  - an unknown name PoracleNG rejects → save succeeds, `Dropped` lists it (Review Focus 1).

  Controller:
  - `PUT /api/areas` with an own fence and an admin area makes ONE trusted POST and no other write;
  - a refused name → 400 naming it.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** In `UpdateAreas`, replace `SetAreasAsync` plus `PreserveOwnedAreasInHumanAsync` with `SaveSelectionAsync`, and drop that site's HACK tag. Keep the read-back response shape as it is today. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat: Areas save goes through one trusted v2 write with PoracleWeb.NET's own authorisation`

### Task 3: Create, activate and deactivate on `IUserAreaWriter`

**Files:**
- Modify:
  - `UserGeofenceService.cs` (`CreateAsync` :62, `AddToProfileAsync` :836, `RemoveFromProfileAsync` :865; delete the private `AddAreaToActiveProfileAsync`, `AddAreasToActiveProfileAsync` and `RemoveAreaFromActiveProfileAsync` helpers)
  - `Core/Pgan.PoracleWebNet.Core.Models/UserGeofence.cs`: add `public bool ActivationFailed { get; set; }`, serialised as `activationFailed`
  - SPA: `core/models` (the `UserGeofence` type), `modules/geofences/geofence-list.component.ts` (create and toggle flows), and the 11 locale files
- Test: `UserGeofenceServiceTests.cs`, the geofence-list spec, `user-geofence.service.spec.ts`.

**Interfaces:** Consumes `IUserAreaWriter.AddToActiveProfileAsync` / `RemoveFromActiveProfileAsync` (Task 2).

- [ ] **Step 1: Write the failing tests:**
  - create → `ReloadGeofencesSafeAsync` runs before `AddToActiveProfileAsync`;
  - the add returns false, or the reload failed → the fence row is kept and `ActivationFailed == true` (no dual writer);
  - activate and deactivate call the writer, then reload;
  - SPA: a create response with `activationFailed: true` shows `GEOFENCES.SNACK_SAVED_NOT_ACTIVE` and leaves the toggle off;
  - legitimate case: a normal create leaves the toggle on and shows no snackbar.
- [ ] **Step 2: Run; fail.** **Step 3: Implement**, removing the HACK tags at these sites. **Step 4: Run backend and frontend; pass.**
- [ ] **Step 5: Commit** `feat: fence create and the profile toggle write areas through v2`

### Task 4: Fence delete with an impact preview and the user's choice

**Files:**
- Modify:
  - `IUserAreaWriter`/`UserAreaWriter`
  - `UserGeofenceService.cs` (`DeleteAsync` :321, `AdminDeleteAsync` :434)
  - `UserGeofenceController.cs` (delete :106, new impact action)
  - `AdminGeofenceController.cs` (delete :59, new impact action)
  - `UserPurgeService.cs:56` (passes delete)
- Create: `Core/Pgan.PoracleWebNet.Core.Models/AreaDeleteImpact.cs`; SPA `shared/components/geofence-delete-dialog/` (component, template, spec).
- Modify (SPA):
  - `core/services/user-geofence.service.ts` and `admin-geofence.service.ts` (impact GET; delete with `?alarms=`)
  - `modules/geofences/geofence-list.component.ts` (`deleteGeofence` :133)
  - `modules/admin/geofence-submissions/geofence-submissions.component.ts` (`adminDelete` :177)
  - the locale files
- Test: `UserAreaWriterTests.cs`, `UserGeofenceServiceTests.cs`, both controller test classes, the service specs, and a `geofence-delete-dialog` spec.

**Interfaces (Produces):**
- `Task<AreaDeleteImpact> PreviewRemovalAsync(string humanId, string name)`
  - `AreaDeleteImpact(IReadOnlyDictionary<string,int> WidenedByType, int Total)`.
  - Wraps `RemoveAreaEverywhereAsync(dryRun: true)`.
- `Task RemoveEverywhereAsync(string humanId, string name, bool deleteWidenedAlarms)`. When `deleteWidenedAlarms` is set:
  1. dry-run first;
  2. for every type in `broadened`, and for every profile the human has (`GetProfilesAsync`), call the per-profile bulk delete with that type's uids (PoracleNG skips uids not on that profile);
  3. assert that the union of deleted uids covers the broadened set, and log a warning for any it doesn't;
  4. run the real `RemoveAreaEverywhereAsync`.
- API:
  - `GET /api/geofences/custom/{id}/delete-impact` → `{ widened: {type: count}, total }`.
  - `DELETE /api/geofences/custom/{id}?alarms=delete|keep`, default `delete`; any other value → 400.
  - The same pair under `/api/admin/geofences/{id}`.
  - Both controllers get `[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]` with a reflection pin. Check first that every outbound HTTP call in them is PoracleNG. Koji and Discord calls there must keep catching their own errors; if any doesn't, report it rather than mislabel it.
- Order in `DeleteAsync`/`AdminDeleteAsync`: Koji cleanup (as today), then `RemoveEverywhereAsync`, then the row delete, then reload. If `RemoveEverywhereAsync` throws, the row is NOT deleted. That overrides `AdminDeleteAsync`'s current try/catch-and-continue around the area removal.

- [ ] **Step 1: Write the failing tests.**
  - **Impact preview:** maps `broadened` to counts.
  - **Delete across profiles:** "delete" with widened uids on profiles 1 and 2 → a bulk delete per profile with `?profile=`, then the real remove (Review Focus 3).
  - **Keep:** "keep" → no tracking delete, only the remove.
  - **Remove failure:** a remove that throws → the row is not deleted and the controller answers 503 through the filter (Review Focus 4).
  - **Bad parameter:** `alarms=bogus` → 400.
  - **SPA, nothing widens:** total 0 → the existing confirm dialog, then the delete without `alarms` or with `alarms=delete`.
  - **SPA, alarms widen:** total > 0 → the new dialog. "Delete those alarms too" is preselected. Each choice sends the matching `alarms=` value.
  - **SPA, admin:** the admin flow behaves the same.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** **Step 4: Run backend and frontend; pass.**
- [ ] **Step 5: Commit** `feat: fence delete removes the area through v2 and asks what to do with alarms that would widen`

### Task 5: Rename and approval through the v2 rename

**Files:** Modify `UserGeofenceService.cs` (`RenameAsync` :224, `ApproveSubmissionAsync` :641), `IUserAreaWriter`/`UserAreaWriter`, and `AdminGeofenceController.cs` (approve response). Test: `UserGeofenceServiceTests.cs`, `AdminGeofenceControllerTests.cs`.

**Interfaces (Produces):**
- `Task<bool> RenameEverywhereAsync(string humanId, string from, string to)`.
  - Reloads geofences first. Returns false if the reload failed or PoracleNG 422s because `to` isn't loaded. In both cases it logs a warning.
  - Otherwise it calls `RenameAreaEverywhereAsync(dryRun: false)` and returns true.
- Approval response: add `ownerAreasRenamed: bool` to the existing approve response DTO.

- [ ] **Step 1: Write the failing tests:**
  - user rename calls `RenameEverywhereAsync(old kojiName, new kojiName)` after the row update;
  - approval calls it with the owner's kojiName and the promoted name, after the Koji save and cache invalidation;
  - a rename returning false still lets the approval succeed with `ownerAreasRenamed:false` (Review Focus 5);
  - no rename call when the name is unchanged.
- [ ] **Step 2: Run; fail.** **Step 3: Implement** and remove the HACK tags. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat: fence rename and approval rename the area through v2`

### Task 6: Remove the dual writer and the trusted gate

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IAlarmOverrideAreaWriter.cs` and `Core/Pgan.PoracleWebNet.Core.Repositories/AlarmOverrideAreaWriter.cs`. Move `SetAlarmOverrideAreasAsync` here verbatim with its HACK note; it stays until sub-project 3.
- Delete:
  - `IUserAreaDualWriter.cs`, `UserAreaDualWriter.cs`
  - `UserAreaDualWriterTests.cs`; move the tests for `SetAlarmOverrideAreasAsync` into an `AlarmOverrideAreaWriterTests.cs`
  - `IAreaSecurityPolicyService.cs`, `AreaSecurityPolicyService.cs`, `AreaSecurityPolicyServiceTests.cs`
- Modify:
  - `UserOwnedOverrideAreaProxy.cs` and DI (`ServiceCollectionExtensions.cs:59,93,157-163`)
  - `PoracleHumanProxy.cs`: delete `CanUseTrustedSetAreasAsync`, `AddAreaToActiveProfileTrustedAsync`, `AddAreasToActiveProfileTrustedAsync`, `RemoveAreaFromActiveProfileTrustedAsync`, the old private `PostTrustedAreasAsync` if `SetAreasTrustedAsync` replaced it, and the `_areaSecurityPolicy` dependency
  - `SetProfileGeographyAsync`: always `trusted:true` when the list is non-empty
  - `PoracleV2Capabilities.cs` and `PoracleV2SchemaService.cs`: remove `TrustedSetAreas`
  - every test helper that builds these
- Test: whatever references the removed types. `NewProfileGeographyTests`: a new profile's own drawn fences are now carried over even with `area_security` on.

- [ ] **Step 1:** Update or replace the tests:
  - new-profile geography always posts trusted, and a duplicate keeps the user's drawn fences;
  - `UserOwnedOverrideAreaProxy` still writes back through `IAlarmOverrideAreaWriter`.
- [ ] **Step 2: Run; fail.** **Step 3: Implement.** `grep -rn "IUserAreaDualWriter\|UserAreaDualWriter\|AreaSecurityPolicy\|TrustedSetAreas" Core Applications Tests` returns nothing. `grep -rn "HACK: trusted-set-areas"` lists only `AlarmOverrideAreaWriter`, `IAlarmOverrideAreaWriter`, `UserOwnedOverrideAreaProxy` and its DI line. **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `refactor: delete the area dual writer; only alarm override areas still write the database`

### Task 7: Docs, CLAUDE.md, CHANGELOG

**Files:**
- `CHANGELOG.md` (`[Unreleased]`):
  - Under Changed: areas and fences write through PoracleNG.
  - Under Added: the delete choice.
  - Remove the sub-project 1 line that says a duplicated profile loses drawn fences under `area_security`. Task 6 fixes that.
  - Narrow the "still touches the database" sentence to alarm override areas and `pweb_settings`.
- `CLAUDE.md`:
  - Rewrite "Areas and Profile-Scoped Storage", "Custom Geofences" (creation, deletion, toggle) and "User Geofence Persistence (PoracleNG setAreas Filter)".
  - Remove the dual-writer and area-security-gate passages.
  - Keep the override-areas HACK description.
- Docs: `docs/features/custom-geofences/*` (the delete dialog with an anonymised screenshot, per memory `feedback_screenshot_anonymisation`, and the "saved, not switched on yet" message); `docs/features/custom-geofences/koji-and-regions.md` (rename and approval); `docs/architecture/database.md`, `poracleng-compatibility.md`, `poracleng-proxy.md`; `docs/troubleshooting.md` (a "fence saved but not switched on" entry).

- [ ] **Step 1:** Make the edits. `grep -rn -i "dual.writ\|IUserAreaDualWriter\|AreaSecurityPolicy\|TrustedSetAreas" CLAUDE.md docs README.md` finds only history.
- [ ] **Step 2:** `mkdocs build --strict` passes (venv in the scratchpad, versions from `.github/workflows/docs.yml`).
- [ ] **Step 3: Commit** `docs: area writes go through PoracleNG v2`

### Task 8: Verify on the 5.4.0 bed

- [ ] **Step 1:** Full backend suite, `npm test`, lint, prettier-check and build: all green.
- [ ] **Step 2: Bring up the branch instance.**
  - Build the branch image on dev-01 and run `instance.sh up sp2 5195 540 poracleweb.net:sp2`.
  - Temporarily point `poracle-ng-540`'s `[geofence] paths` at `http://127.0.0.1:5195/api/geofence-feed`, back up the config first, then restart the unit. The dev instance's fences disappear from the bed meanwhile; that's acceptable.
- [ ] **Step 3: Run the checks** on the sp2 instance with probe users `pweb-sp2-probe*`:
  - the harness (`smoke_e2e540.py` pointed at `poracle_web_sp2`): expect 258 of 260;
  - the e2eC fence flows: create, toggle on profiles 1 and 2, Areas save holding an own fence plus an admin area, rename;
  - delete with `keep` and with `delete`, where widened alarms sit on both profiles: verify by SQL that "delete" removed them on both profiles;
  - approval through the admin API;
  - repeat the fence flows with `area_security` on (the e2eD procedure), and restore it after.
  - Assert by SQL that no `humans.area` / `profiles.area` change happens outside a PoracleNG request (the PoracleNG journal shows the matching API call).
- [ ] **Step 4: Restore and clean up.** Restore the bed config and restart it, so its feed is the dev instance again. Clean up the probes, then `instance.sh down sp2`.
- [ ] **Step 5:** Stop here. The push and PR happen at the finishing step.
