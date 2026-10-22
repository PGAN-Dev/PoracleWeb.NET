# PoracleNG 5.4.0 Minimum (Sub-project 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make PoracleNG 5.4.0 the minimum and delete every human, profile and version-gated fallback that existed for older servers, leaving area writes, tracking and `pweb_settings` for sub-projects 2–4.

**Architecture:** One feature branch off `develop`, one PR. Each task deletes one family of fallbacks: the capability flag, its false branch, its direct-DB or v1 call, and the tests that pinned the old behaviour, replacing them with tests that the v2 call is the only call made. A v2 route answering gin's plaintext `404 page not found` becomes `PoracleUnsupportedException`, never a silent fallback.

**Tech Stack:** .NET 10 / xUnit + Moq, Angular 21 / Jest, PoracleNG 5.4.0 (jfberry/PoracleNG#328, `fa2836719`).

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md` (sections "Before any sub-project merges", "Sub-project 1", "Docs", "Testing").

## Global Constraints

- Minimum PoracleNG is **5.4.0**: `PoracleServerProfile.MinimumSupported = new(5, 4, 0)`.
- Out of scope here, do not touch: `IUserAreaDualWriter`, `TrustedSetAreas`, `IAreaSecurityPolicyService` (sub-project 2); every tracking read/create/bulk path and `InvasionGruntType` (sub-project 3); `pweb_settings`, `PoracleContext`, `DB_*` (sub-project 4); tolerance for `area` as a JSON string.
- A plaintext 404 (`!PoracleProblemDetails.IsProblemJson`) from a v2 route throws `new PoracleUnsupportedException(feature, "PoracleNG 5.4.0")`. A problem+json 404 keeps its current meaning (not found).
- Webhook ids in v2 paths go through `Uri.EscapeDataString`.
- Write "PoracleWeb.NET" in full. No Claude-Session trailers, claude.ai links or Co-authored-by anywhere. Squash-merge into `develop`.
- After every `.cs` edit run `git diff --numstat` and fix CRLF/LF flips (memory: edit tool flips C# EOLs).
- Backend tests: `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS= dotnet test Tests/Pgan.PoracleWebNet.Tests --no-restore` (restore once first). Frontend: from `ClientApp`, `npm test`, `npm run lint`, `npm run prettier-check`, `npm run build` (template errors only show in a build). Set `core.autocrlf=false` before frontend work.

## Review Focus

1. **PoracleNG unreachable.** Controls that were hidden behind a capability answer (mutes, place move, pokecoins, costume, ping) now always show; their requests must fail with a readable 502/503, never a 500 or a blank page. Pinned by a unit test in Task 7 and checked live across all of them in Task 12, Step 3.
2. **A server below 5.4.0** (prod's fork reports 5.3.0 and lacks #328). Every v2-only call answers `PoracleUnsupportedException` naming the feature, and the startup log names 5.4.0. Pinned in Tasks 1 and 3.
3. **Profile create answering without `profile_no`.** Must throw, never guess a number (the #407 orphan shape). Pinned in Task 5.
4. **`POST /api/v2/humans` with an undeclared field** is a 422 under `additionalProperties: false` (the profile-body lesson in CLAUDE.md). The body sent must be built from the schema's property list. Pinned in Task 3.
5. **Webhook ids (URLs) in the new v2-only human paths** (`DELETE /v2/humans/{id}`, places, roles) must be escaped. Pinned in Task 2.

---

### Task 0: Dev environment on 5.4.0 (ops, no code)

**Files:** none in the repo. dev-01 (`ssh root@10.100.3.136`): `/source/poracleweb-dev/` (compose + env), `/source/poracle-ng-540/config/config.toml`.

- [ ] **Step 1:** `systemctl enable poracle-ng-540` so the bed survives a reboot.
- [ ] **Step 2:** In `/source/poracleweb-dev` (back up first), set `Poracle__ApiAddress=http://10.0.3.136:3046`, the API secret to the value in `/root/pweb-smoke/secret.poracle-ng-next`, and `ConnectionStrings__PoracleDb`'s database to `poracle_dev_540`. It currently points at :3040 (5.1.0) and `poracle_dev`.
- [ ] **Step 3:** Point `poracle-ng-540`'s `[geofence] paths` at `http://10.0.3.136:8083/api/geofence-feed` (the dev instance), back up the file, restart `poracle-ng-540`, then `docker compose up -d` in `/source/poracleweb-dev`.
- [ ] **Step 4:** Verify: `curl -s 10.0.3.136:3046/health` reports `5.4.0`; `curl -s 127.0.0.1:8083/api/version` answers; the dev instance's admin server-profile panel shows 5.4.0 and not "below minimum"; `journalctl -u poracle-ng-540` shows the fence load from :8083.
- [ ] **Step 5:** Update memory `project_poracleng_next_testbed.md`: the dev instance now runs on the 5.4.0 bed; branch instances (`instance.sh up <name> <port> 540 <image>`) see dev's fences, not their own.

### Task 1: Minimum 5.4.0 and the 5.4.0 schema fixture

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Models/PoracleServerProfile.cs:37`
- Create: `Tests/Pgan.PoracleWebNet.Tests/Fixtures/poracleng-openapi-5.4.0.json` (full `/openapi.json` from `:3046`, pretty-printed; add `"x-fixture-source": "jfberry/PoracleNG#328 fa2836719, dev-01 bed, 2026-10-21"` beside `info`)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/PoracleServerProfileTests.cs`, `Tests/Pgan.PoracleWebNet.Tests/Services/PoracleCompatibilityStartupServiceTests.cs` (or wherever `LogTooOld` is tested)

**Interfaces:** Produces the fixture path `Fixtures/poracleng-openapi-5.4.0.json` that Tasks 2–5 load the way existing tests load `poracleng-openapi-5.2.1.json`.

- [ ] **Step 1:** Write tests: `IsBelowMinimum` is true for `5.3.0` and `5.2.1`, false for `5.4.0` and `5.4.1`; `IsSupported` is false for a reachable `5.3.0`; the startup "too old" log message contains `5.4.0`.
- [ ] **Step 2:** Run; the `5.3.0` cases fail.
- [ ] **Step 3:** Set `MinimumSupported = new(5, 4, 0)`. Update the doc comments on `MinimumSupported`, `IsSupported` and `SupportsV2Tracking` that name 5.2.1. Add the fixture.
- [ ] **Step 4:** Run the whole backend suite. Fix only tests that asserted 5.2.1 is supported by changing their version to 5.4.0; leave fallback-behaviour tests for the task that deletes that fallback.
- [ ] **Step 5:** Commit `feat!: require PoracleNG 5.4.0`.

### Task 2: Admin human routes, no `HumanRepository`

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/HumanService.cs:30-44,100-101`, `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs:246,257`, `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs:1205-1290`, `Core/Pgan.PoracleWebNet.Core.Models/PoracleV2Capabilities.cs` (remove `AdminHumanRoutes`), `Core/Pgan.PoracleWebNet.Core.Services/PoracleV2SchemaService.cs`, `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs`
- Delete: `Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IHumanRepository.cs`, `Core/Pgan.PoracleWebNet.Core.Repositories/HumanRepository.cs` and their tests
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/HumanServiceTests.cs`, `PoracleHumanProxyV2Tests.cs`, `UserPurgeServiceTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<Human>> ListHumansAsync(string? type = null, IReadOnlyCollection<string>? ids = null)` (never null); `Task<bool> DeleteHumanAsync(string userId)` (true deleted, false PoracleNG had no such human; anything else throws).

- [ ] **Step 1:** Tests: `HumanService.GetAllAsync`, `GetWebhooksAsync`, `GetByIdsAsync`, `DeleteUserAsync` call only the proxy (construct `HumanService` without a repository); a proxy exception propagates; `DeleteHumanAsync` for a webhook id `https://discord.com/api/webhooks/1/x` requests `/api/v2/humans/https%3A%2F%2Fdiscord.com%2Fapi%2Fwebhooks%2F1%2Fx`; a plaintext 404 from `GET /api/v2/humans` throws `PoracleUnsupportedException`.
- [ ] **Step 2:** Run; they fail (constructor and nullability).
- [ ] **Step 3:** Implement: drop the repository from `HumanService` and DI, make the two proxy methods non-nullable as above, remove `AdminHumanRoutes` and its schema probe.
- [ ] **Step 4:** Run backend tests; pass.
- [ ] **Step 5:** Commit `refactor: admin human list and delete go through PoracleNG only`.

### Task 3: Single-human calls are v2 only

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs` (`TryV2Async` :81-108 and its callers `GetHumanAsync`, `StartAsync`, `StopAsync`, `SetLanguageAsync`, `AdminDisabledAsync`, `SetLocationAsync`, `GetAdminRolesAsync`, `CreateHumanAsync`; remove `V2AbsentFor` and the `poracle:v2-humans-absent:*` cache; `ServerCarriesV2Async`), `PoracleV2Capabilities.cs` + `PoracleV2SchemaService.cs` (remove `V2LocationLatitudeLongitude`)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/PoracleHumanProxyV2Tests.cs` and any `PoracleHumanProxy*Tests` asserting v1 URLs

**Interfaces:**
- Produces (private, used by Tasks 4–5 in the same file): `Task<HttpResponseMessage> SendV2Async(HttpMethod method, string path, string feature, object? body = null)`. It sends to `/api/v2/...`, throws `PoracleUnsupportedException(feature, "PoracleNG 5.4.0")` on a plaintext 404, and returns every other response for the caller to interpret.
- `CreateHumanAsync(JsonElement body)` posts to `POST /api/v2/humans` a body containing only the properties `V2CreateHumanBody` declares: `admin_disable, area, community, enabled, id, language, latitude, longitude, name, notes, profile_name, type`.

- [ ] **Step 1:** Tests:
  - each listed method requests only its `/api/v2/humans/...` URL;
  - a plaintext 404 throws `PoracleUnsupportedException` whose `Feature` names the operation;
  - `SetLocationAsync` sends `latitude`/`longitude`;
  - `CreateHumanAsync` with the body its real caller builds (find it with `grep -rn "CreateHumanAsync(" Core Applications`) sends only `V2CreateHumanBody`'s properties, checked against the 5.4.0 fixture's property list in both directions.
- [ ] **Step 2:** Run; fail.
- [ ] **Step 3:** Implement `SendV2Async`, move the methods onto it, delete the v1 branches and the absent-cache.
- [ ] **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: single-human PoracleNG calls are v2 only`.

### Task 4: Areas listing, set-areas and places are v2 only

**Files:**
- Modify: `PoracleHumanProxy.cs` (`SetAreasAsync` :344, `GetPlacesAsync` :1039, `AddPlaceAsync` :1093, `UpdatePlaceAsync` :1144, `DeletePlaceAsync` :1187), `Core/Pgan.PoracleWebNet.Core.Services/PoracleApiProxy.cs:346-363` (`GetAreasWithGroupsAsync`), `IPoracleHumanProxy.cs:226` (`UpdatePlaceAsync` becomes `Task`), `Applications/Pgan.PoracleWebNet.Api/Controllers/LocationController.cs:296-316`, `PoracleV2Capabilities.cs` + schema service (remove `SetAreasReportsRejected`, `AvailableAreas`)
- Test: `PoracleHumanProxyV2Tests.cs`, `PoracleApiProxy*Tests`, `LocationControllerTests.cs`

**Interfaces:** Consumes `SendV2Async` (Task 3). Produces `Task UpdatePlaceAsync(string userId, string label, double latitude, double longitude)`.

- [ ] **Step 1:** Tests: each method requests only its v2 URL; `SetAreasAsync` still returns/logs `rejected` as today; the place PUT no longer answers 501 for any server (remove that test, add one that a plaintext 404 maps to `PoracleUnsupportedException`); `GetAreasWithGroupsAsync` never requests `GET /api/humans/{id}`.
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: areas listing, set-areas and places are v2 only`.

### Task 5: Profiles are v2 only; no `ProfileRepository`, no numbering diff

**Files:**
- Modify: `PoracleHumanProxy.cs` (`SwitchProfileAsync` :649, `GetProfilesAsync` :664, `AddProfileAsync` :745, `UpdateProfileAsync` :768, `DeleteProfileAsync` :1006, `CopyProfileAsync` :1022, `SetProfileGeographyAsync` :569), `IPoracleHumanProxy.cs:155,166,185`, `Applications/Pgan.PoracleWebNet.Api/Controllers/ProfileController.cs` (:167, :181, :229-243, :321, :337), `ProfileOverviewController.cs` (:74, :171, :252), `Applications/Pgan.PoracleWebNet.Api/Services/NewProfileGeography.cs`, `PoracleV2Capabilities.cs` + schema service (remove `ProfileRename`, `ProfileCreateReturnsNumber`, `ProfileList`, `ProfileDelete`, `ProfileCopy`, `ProfileScopedGeography`), DI
- Delete: `Core/Pgan.PoracleWebNet.Core.Models/Helpers/ProfileNumbering.cs`, `IProfileRepository.cs`, `ProfileRepository.cs`, their tests
- Test: `ProfileControllerTests.cs`, `ProfileOverviewControllerTests.cs`, `NewProfileGeographyTests.cs`, `PoracleHumanProxyV2Tests.cs`

**Interfaces:**
- Produces: `Task<int> AddProfileAsync(string userId, JsonElement body)` returns the created `profile_no` and throws `PoracleRequestRefusedException` when the response has none; `Task UpdateProfileAsync(string userId, JsonElement body)` (rename always via PATCH); `Task SetProfileGeographyAsync(...)` (same parameters as today, throws instead of returning false).
- Keeps `V2ProfileBody` translation and `V1ProfilesFromV2` as they are.

- [ ] **Step 1:** Tests:
  - create, duplicate, overview duplicate and import use the returned number and make **one** profile-list read at most (no before/after diff);
  - `AddProfileAsync` against a response `{"status":"ok"}` throws;
  - rename never calls a repository and sends the trimmed name via PATCH (keep the blank-name rule: blank sends the stored name);
  - `NewProfileGeography` propagates a v2 failure rather than writing the DB;
  - each proxy method requests only its v2 URL.
- [ ] **Step 2:** Run; fail. **Step 3:** Implement and delete. **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: profiles are v2 only; drop the numbering diff and ProfileRepository`.

### Task 6: Ping is always supported

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs:164,186`, `TrackingV2Translator.cs` (ping always carried), `Applications/Pgan.PoracleWebNet.Api/Controllers/ConfigController.cs:72` (drop `PingSupported`), `PoracleV2Capabilities.cs` + schema service (remove `TrackingPing`)
- Modify (SPA): `core/services/poracle-config.service.ts`, `core/models/index.ts:409`, and the ten edit dialogs' `pingSupported` (`pokemon`, `raid`, `quest`, `invasion`, `lure`, `nest`, `gym`, `max-battle`, `fort-change`, plus the egg path inside raids if separate) `.ts`/`.html`
- Test: `PoracleTrackingProxyV2Tests.cs`, `ConfigControllerTests.cs`, each dialog's spec, `alarm-dialog-parity.spec.ts`

- [ ] **Step 1:** Tests: an edit carrying a ping is sent to v2 with `ping` (no `AlarmValidationException` path left); `/api/config` has no `pingSupported`; each edit dialog shows the Mention field when `isWebhook` and hides it otherwise, with no server-config dependency.
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Backend tests, `npm test`, `npm run build`; pass.
- [ ] **Step 5:** Commit `refactor: ping is always supported on PoracleNG 5.4.0`.

### Task 7: Mutes need no capability check

**Files:**
- Delete: `IMuteCapabilityService.cs`, `MuteCapabilityService.cs`, `MuteCapabilityServiceTests.cs`
- Modify: `Applications/Pgan.PoracleWebNet.Api/Controllers/MuteController.cs:30-56,232-237` (GET returns the list without `capable`; no 501 branch), DI, SPA `core/services/mute.service.ts`, `shared/components/quiet-chip/quiet-chip.component.html`, `modules/dashboard/dashboard.component.html`
- Test: `MuteControllerTests.cs`, `mute.service.spec.ts`, `quiet-surface-parity.spec.ts`

- [ ] **Step 1:** Tests: GET with a reachable PoracleNG returns the list and no `capable`; GET when `IPoracleMuteProxy` throws `HttpRequestException` answers 503 with a message (Review Focus 1); the SPA shows the quiet chip without a capability flag and shows an error toast, not a blank chip, on 503.
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Run backend + frontend; pass.
- [ ] **Step 5:** Commit `refactor: mutes need no capability check on PoracleNG 5.4.0`.

### Task 8: Place move and quest pokecoins need no capability check

**Files:**
- Delete: `IPlaceUpdateCapabilityService.cs`, `PlaceUpdateCapabilityService.cs`, `IQuestPokecoinCapabilityService.cs`, `QuestPokecoinCapabilityService.cs` and their tests (`PlaceUpdateCapabilityServiceTests.cs`, `QuestPokecoinCapabilityServiceTests.cs`, `QuestPokecoinCapabilityTests.cs`, `TestDoubles/PokecoinCapabilityStub.cs`)
- Modify: `LocationController.cs:265-286` (places response without `canEdit`), `QuestController.cs:11-28` (delete `GET /api/quests/capability`), `QuestService.cs:237`, DI; SPA `core/services/places.service.ts`, `shared/components/places-section/places-section.component.html`, `core/services/quest.service.ts`, `modules/quests/quest-add-dialog.component.ts`, `quest-edit-dialog.component.*`
- Test: `LocationControllerTests.cs`, quest service/controller tests, `places-section` and quest dialog specs

- [ ] **Step 1:** Tests: the places response has no `canEdit` and the edit control always shows; a pokecoin quest Add is sent without a capability check; the quest dialogs offer pokecoins with no capability request (assert no `GET /api/quests/capability`).
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: place move and quest pokecoins need no capability check`.

### Task 9: Costume needs no capability check

**Files:**
- Delete: `ICostumeCapabilityService.cs`, `CostumeCapabilityService.cs`, `Core/Pgan.PoracleWebNet.Core.Models/CostumeCapability.cs` (keep `AnyCostume = 9000` wherever it is still referenced, moved to the model that uses it), `CostumeCapabilityServiceTests.cs`, `CostumeCapabilityDoubles.cs`, `CostumeGateTests.cs`
- Modify: `MonsterService.cs:44,65,179`, `RaidService.cs:41,68,207`, `SettingsController.cs:176-180` (delete `GET /api/settings/costume-capability`), DI; SPA `core/services/settings.service.ts` and the pokemon/raid dialogs that read it
- Test: monster and raid service tests, `SettingsControllerTests.cs`, dialog specs

- [ ] **Step 1:** Tests: a pokemon and a raid Add/edit with a costume are sent with no capability check; the costume control shows in both dialogs with no capability request.
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: costume needs no capability check`.

### Task 10: Remaining older-server branches

**Files:**
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/UpstreamFeatureFlagService.cs` (delete `ProbePokestopEventsAsync` :101 and its comment at :76; Pokestop Events follow `disabledHooks` / `disable_showcase` like every other type), `PoracleApiProxy.cs:488-535` (delete `GetLegacyGeocodeAsync`; a plaintext 404 from `/api/geocode/*` throws `PoracleUnsupportedException("Address search", "PoracleNG 5.4.0")`), `PoracleTrackingProxy.cs:77,384-408` and `Applications/Pgan.PoracleWebNet.Api/Program.cs:94` (delete `Poracle:TrackingApiVersion` / `PORACLE_TRACKING_API_VERSION`), `.env.example:141`, `PoracleServerProfile.cs:117` (delete `SupportsV2Tracking`, callers use `IsSupported`)
- Test: `UpstreamFeatureFlagServiceTests.cs`, geocode proxy and `LocationController` tests, `PoracleTrackingProxyV2Tests.cs`

- [ ] **Step 1:** Tests: Pokestop Events are enabled when `disabledHooks` doesn't list them and `disable_showcase` is absent or false, with no route probe; a plaintext 404 from geocode throws and no third-party geocoder URL is ever requested; tracking edits try v2 with no config pin (a configured `Poracle:TrackingApiVersion=v1` is ignored).
- [ ] **Step 2:** Run; fail. **Step 3:** Implement. **Step 4:** Run; pass.
- [ ] **Step 5:** Commit `refactor: drop the last older-PoracleNG branches outside tracking and areas`.

### Task 11: Docs, CLAUDE.md, CHANGELOG, fixtures; fold in #992

**Files:**
- Modify: `CHANGELOG.md` (`[Unreleased]`, one entry under `### Changed` that opens "**Requires PoracleNG 5.4.0.**" and lists what users see: no more hidden controls on older servers, place move and pokecoins always available, `PORACLE_TRACKING_API_VERSION` removed), `CLAUDE.md` (prune 5.1.0/5.2.1 passages for everything deleted in Tasks 1–10: "PoracleNG Geocode Proxy" fallback paragraph, profile numbering and rename workaround in "Profile Numbering and Rename", "Capability is the reported version" in "Quiet Periods", `TrackingApiVersion` and the schema-gated list in the v2 section; keep everything about areas, tracking and the DB), `docs/index.md`, `docs/getting-started/quick-start.md`, `standalone-setup.md`, `development-setup.md` (5.4.0 requirement), `docs/architecture/poracleng-compatibility.md` (version matrix becomes "requires 5.4.0, and why"), `docs/features/quiet-periods.md`, `docs/configuration/reference.md:31` (delete the `PORACLE_TRACKING_API_VERSION` row), `docs/poracleng-enhancement-requests.md` (bring in #992's three commits, then refresh: everything requested landed in 5.4.0), `mkdocs.yml` (move `poracleng-enhancement-requests.md` and `poracleng-v2-review.md` under a new "Upstream history" section)
- Delete: `Tests/Pgan.PoracleWebNet.Tests/Fixtures/poracleng-openapi-5.2.1.json` and `poracleng-openapi-next.json` once `grep -rn "openapi-5.2.1\|openapi-next" Tests` is empty

- [ ] **Step 1:** `git cherry-pick` #992's commits (`11458d79`, `825f0473`, `baf34782`) and resolve against Tasks 1–10, then refresh the page.
- [ ] **Step 2:** Make the edits above. Verify `grep -rn -E "5\.2\.1|5\.1\.0|TrackingApiVersion" docs/getting-started docs/configuration docs/index.md` returns nothing, and `pip install mkdocs-material && mkdocs build --strict` passes (use the versions in `.github/workflows/docs.yml`).
- [ ] **Step 3:** Commit `docs: PoracleNG 5.4.0 is the minimum`. Close #992 with a comment linking this PR.

### Task 12: Verify on the 5.4.0 bed and open the PR

- [ ] **Step 1:** Full backend suite, `npm test`, `npm run lint`, `npm run prettier-check`, `npm run build`: all green.
- [ ] **Step 2:** Build the branch image on dev-01 (`git archive HEAD | ssh root@10.100.3.136 'mkdir -p /root/pweb-smoke/build-sp1 && tar -x -C /root/pweb-smoke/build-sp1'`, `docker build -t poracleweb.net:sp1 /root/pweb-smoke/build-sp1`), run `instance.sh up sp1 5192 540 poracleweb.net:sp1`, then `smoke_e2e540.py` (pointed at `poracle_web_sp1`) and the e2eC profile, places, mutes and admin scripts. Expected: 260/260 harness; no new failures in e2eC.
- [ ] **Step 3:** Stop PoracleNG for 30 seconds and load the dashboard, quiet list, places and a quest dialog on the sp1 instance: every failure is a readable message, no 500 (Review Focus 1). Restart it.
- [ ] **Step 4:** `instance.sh down sp1`. Push, open the PR against `develop` titled `feat!: require PoracleNG 5.4.0 and remove older-server fallbacks`, body listing Tasks 1–11 and the e2e results, `Refs #996`.
