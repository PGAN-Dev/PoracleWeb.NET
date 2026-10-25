# Linked Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A person links a `discord:user` and a `telegram:user` human, sees one merged alert list with a per-alert "Deliver to", and keeps location, areas, profiles and places in step across both, against stock PoracleNG 5.4.0.

**Architecture:** A `poracle_web.account_links` row pairs two human ids. `ILinkedAccount` resolves the pair live per request (cached one minute); the JWT is unchanged. `LinkedAlarmRouter` fans alarm reads out and routes uid calls to the owning human, `AlarmMover` moves an alert create-then-delete, `PairedHumanWriter` writes person-level settings to both humans, and `PairDriftService` reports and resolves differences.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (`MySql.EntityFrameworkCore`, SQLite in tests), xUnit + Moq; Angular 21 standalone components with signals, Angular Material 21, Jest 30.

**Spec:** `docs/superpowers/specs/2026-10-23-linked-accounts-design.md`. Read it before any task; this plan cites its sections as "spec §N".

## Global Constraints

- Public repo, branch from `origin/develop`, PRs into `develop`. Nothing names a particular operator, its domains, its sign-in provider or how it admits members. Examples use Old Town, Northside, Riverside and made-up ids (Discord `100000000000000001`, Telegram `200000001`).
- Write "PoracleWeb.NET" in full in every commit, PR, doc and comment. No `Claude-Session` trailers or claude.ai links in commits or PRs.
- Links pair `discord:user` with `telegram:user` exactly. Never a destination type, never another `:user` string.
- No PoracleNG change and no Poracle database access (`Architecture/NoPoracleDatabaseTests` stays green).
- No `ExecuteDeleteAsync` (`Repositories/NoAliasedDeleteTests`).
- A person with no link: identical PoracleNG calls, byte-identical alarm JSON (`destination` is `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`).
- A move creates before it deletes. Drift is never resolved without the person choosing a side. A paused side is never re-enabled by PoracleWeb.NET.
- Site setting keys: `disable_account_linking` and `link_register_missing`, both boolean, category `features`, absent means false.
- Link `auth_date` window 10 minutes; sign-in keeps 24 hours. `oauth_link` cookie: HttpOnly, SameSite=Lax, 10 minutes, DataProtection purpose `"AccountLink.v1"`.
- Drift equality: lat/lon to 5 decimal places; areas compared with PoracleNG's `NormalizeAreaKey` (lowercase, trim, `_` equals space, order ignored).
- Every user-visible string in all 11 locales (`ClientApp/src/assets/i18n/*.json`), gated by `locale-parity.spec.ts`.
- Windows: `git config core.autocrlf false` before frontend work; after every `.cs` edit check `git diff --numstat` for whole-file line-ending flips and revert them. Run `npm run build` before pushing any template change.
- Each guard gets a legitimate-case-still-passes test beside its refusal test, and every new test is watched failing first.

## Review Focus

1. **The signed-in side's human is deleted or retyped after linking.** Expect: `ILinkedAccount` treats the missing or mismatched side as absent, the other side keeps working, nothing is written to it. Test owned by Task 3 (`Resolve_TreatsRetypedSideAsAbsent`, `Resolve_SignedInSideMissing_StillAnswersOtherSide`).
2. **A person moves an alert whose rule sits on a non-active profile (the scheduler switched profile mid-session).** Expect: preflight 409 naming the profile difference, nothing written. Test owned by Task 9 (`Move_RefusesWhenTargetCurrentProfileDiffers`).
3. **The same Telegram account is linked from two Discord sessions at once.** Expect: one wins, the other gets `link_taken` (409), never a 500. Test owned by Task 2 (`Add_SecondLinkForSameTelegramId_Throws`) and Task 5 (`Link_UniqueViolationRace_MapsToLinkTaken`).
4. **A bot `!location` changes the kept side between rendering the banner and pressing Keep.** Expect: 409 "That changed again. Check the banner.", nothing written. Test owned by Task 13 (`Resolve_StaleExpected_Returns409`).
5. **`disable_account_linking` is switched on while people are linked.** Expect: everyone acts as their single human, `destination` disappears from responses, rows survive and return when switched off. Test owned by Task 3 (`Resolve_LinkingDisabled_AnswersCallerAlone`) and Task 16 (route guard spec).

---

## File Structure

Backend, new:

| File | Responsibility |
|---|---|
| `Data/Pgan.PoracleWebNet.Data/Entities/AccountLinkEntity.cs` | table row |
| `Data/Pgan.PoracleWebNet.Data/Configurations/AccountLinkConfiguration.cs` | lengths, unique indexes |
| `Data/.../Migrations/PoracleWeb/<ts>_AddAccountLinks.cs` | migration (generated) |
| `Core/Pgan.PoracleWebNet.Core.Models/AccountLink.cs` | model, `LinkSide` enum, `LinkedPair`, `PairSide`, `PauseReason` |
| `Core/Pgan.PoracleWebNet.Core.Models/AccountLinkKeys.cs` | setting key constants, type constants |
| `Core/Pgan.PoracleWebNet.Core.Models/IHasDestination.cs` | `string? Destination` on alarm models |
| `Core/.../Abstractions/Repositories/IAccountLinkRepository.cs`, `Core/.../Repositories/AccountLinkRepository.cs` | persistence |
| `Core/.../Abstractions/Services/ILinkedAccount.cs`, `Core/.../Services/LinkedAccount.cs` | pair resolution and cache |
| `Core/.../Abstractions/Services/IAccountLinkService.cs`, `Core/.../Services/AccountLinkService.cs` | link checks, write, unlink, default |
| `Core/.../Services/LinkedHumanProvisioner.cs` | register-missing creation and rollback |
| `Core/.../Services/TelegramWidgetVerifier.cs` | HMAC + age check shared by sign-in and link |
| `Core/.../Abstractions/Services/IAlarmService.cs` | generic shape the eleven services already share |
| `Core/.../Services/LinkedAlarmRouter.cs` | merged reads, owner lookup, bulk grouping |
| `Core/.../Services/AlarmMover.cs`, `Core/.../Services/QuickPickUidTransfer.cs` | move protocol, applied-state transfer |
| `Core/.../Services/PairedHumanWriter.cs` | fan-out of person-level writes |
| `Core/.../Services/PairDriftService.cs` | compare and resolve |
| `Applications/.../Controllers/AccountLinkController.cs` | link, unlink, default, status, resolve |
| `Applications/.../Controllers/AlarmMoveController.cs` | `POST /api/alarm-move` |

Frontend, new: `core/services/linked-account.service.ts`, `modules/linked-accounts/` (page), `shared/components/deliver-to/`, `shared/components/pair-drift-banner/`, `shared/components/telegram-login-button/` (extracted from `modules/auth/login.component.ts`).

---

## Phase A: Links (PR 1)

### Task 1: Models, keys and the `AccountLink` entity with migration

**Files:**
- Create: `AccountLinkEntity.cs`, `AccountLinkConfiguration.cs`, `AccountLink.cs`, `AccountLinkKeys.cs`, migration
- Modify: `Data/Pgan.PoracleWebNet.Data/PoracleWebContext.cs` (add `DbSet<AccountLinkEntity> AccountLinks`), `Core/Pgan.PoracleWebNet.Core.Mappings/EntityMappingExtensions.cs`, `Core/Pgan.PoracleWebNet.Core.Models/DisableFeatureKeys.cs`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Mappings/AccountLinkMappingTests.cs`

**Interfaces:**
- Produces:
  - `AccountLinkEntity` columns exactly as spec §1, `[Table("account_links")]`; configuration: `discord_id`/`telegram_id` max 64, each `IsUnique()`; `default_destination` max 16.
  - `public enum LinkSide { Discord, Telegram }`; `AccountLinkKeys.DiscordUserType = "discord:user"`, `TelegramUserType = "telegram:user"`, `RegisterMissing = "link_register_missing"`, `static string Wire(LinkSide)` (`"discord"`/`"telegram"`), `static bool TryParse(string?, out LinkSide)`.
  - `DisableFeatureKeys.AccountLinking = "disable_account_linking"` with a doc comment in the file's style (not added to `ByTrackingType`).
  - `public sealed record AccountLink(int Id, string DiscordId, string TelegramId, LinkSide DefaultDestination, DateTime? DiscordVerifiedAt, DateTime? TelegramVerifiedAt, DateTime CreatedAt)`.
  - `public enum PauseReason { None, NotRegistered, AdminDisabled, Stopped }`; `public sealed record PairSide(LinkSide Side, string HumanId, bool Exists, PauseReason Pause)`; `public sealed record LinkedPair(string CallerId, AccountLink? Link, PairSide? Discord, PairSide? Telegram)` with `bool IsLinked`, `IReadOnlyList<string> ExistingHumanIds`, `string? HumanIdFor(LinkSide)`, `LinkSide? SideOf(string humanId)`, `static LinkedPair Single(string callerId)`.
  - `EntityMappingExtensions.ToModel(this AccountLinkEntity)` / `ToEntity(this AccountLink)` (entity id not copied on `ToEntity`).

- [ ] **Step 1:** Write `AccountLinkMappingTests`: round-trip keeps both ids, default and timestamps; `ToEntity` leaves `Id` 0; `AccountLinkKeys.TryParse("Telegram", out var s)` is true with `LinkSide.Telegram`; `TryParse("webhook", out _)` is false.
- [ ] **Step 2:** Run `dotnet test --filter AccountLinkMappingTests`; expect compile failure.
- [ ] **Step 3:** Add the types above and the `DbSet`.
- [ ] **Step 4:** Generate the migration: `dotnet ef migrations add AddAccountLinks --context PoracleWebContext --project Data/Pgan.PoracleWebNet.Data --startup-project Applications/Pgan.PoracleWebNet.Api --output-dir Migrations/PoracleWeb`. Check the generated SQL creates two unique indexes and nothing else changes in the snapshot.
- [ ] **Step 5:** Run the mapping tests; expect PASS. Run `dotnet build`.
- [ ] **Step 6:** Commit `feat: add the account_links table for linked Discord and Telegram accounts`.

### Task 2: `AccountLinkRepository`

**Files:**
- Create: `IAccountLinkRepository.cs`, `AccountLinkRepository.cs`
- Modify: `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs` (scoped registration)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Repositories/AccountLinkRepositoryTests.cs` (SQLite in-memory, same harness as `OidcSessionRepositoryTests`)

**Interfaces:**
- Produces:
  - `Task<AccountLink?> GetByHumanIdAsync(string humanId)` (matches either column)
  - `Task<IReadOnlyList<AccountLink>> GetAllAsync()`
  - `Task<AccountLink> AddAsync(AccountLink link)`; throws `AccountLinkConflictException` (new, in Core.Models) when either unique index is violated
  - `Task<bool> SetDefaultAsync(int id, LinkSide side)`
  - `Task<bool> TouchVerifiedAsync(int id, LinkSide side, DateTime at)`
  - `Task<int> RemoveByHumanIdAsync(string humanId)` (load and `Remove`)

- [ ] **Step 1:** Tests: `Add_ThenGetByEitherId_FindsRow`; `Add_SecondLinkForSameTelegramId_Throws` (`AccountLinkConflictException`); `Add_SecondLinkForSameDiscordId_Throws`; `Add_DistinctPairs_BothStored` (legitimate case); `RemoveByHumanId_ByTelegramId_RemovesRow`; `RemoveByHumanId_Unknown_ReturnsZero`; `SetDefault_ChangesOnlyThatRow`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement. Map `DbUpdateException` whose inner is a unique violation (SQLite `SQLITE_CONSTRAINT`, MySQL error 1062) to `AccountLinkConflictException`; any other `DbUpdateException` propagates.
- [ ] **Step 4:** Run the class and `NoAliasedDeleteTests`; expect PASS.
- [ ] **Step 5:** Commit `feat: store and look up account links`.

### Task 3: `ILinkedAccount`, the per-request pair

**Files:**
- Create: `ILinkedAccount.cs`, `LinkedAccount.cs`
- Modify: `ServiceCollectionExtensions.cs` (scoped)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/LinkedAccountTests.cs`

**Interfaces:**
- Consumes: `IAccountLinkRepository`, `IPoracleHumanProxy.GetHumanAsync`, `IFeatureGate.IsEnabledAsync(DisableFeatureKeys.AccountLinking)` (true means linking is on, matching the gate's existing semantics), `IMemoryCache`.
- Produces:
  - `Task<LinkedPair> ResolveAsync(string userId)`
  - `void Invalidate(string humanId)` (drops the cache entries for both ids of that link)
- Rules: link lookup cached one minute under `account-link:{humanId}`, including "no link". Human reads per side cached for the same minute under `account-link-human:{id}`. A side whose `type` isn't its column's type is `Exists: false` and logged once at Warning. `PauseReason`: missing human `NotRegistered`, `admin_disable` 1 `AdminDisabled`, `enabled` 0 `Stopped`. Linking disabled (`IsEnabledAsync` false): answer `LinkedPair.Single(userId)` without touching the repository. A `GetHumanAsync` throw propagates (it already means "unreachable", #994).

- [ ] **Step 1:** Tests: `Resolve_NoLink_AnswersCallerAloneWithoutHumanReads` (verify `GetHumanAsync` never called); `Resolve_Linked_BothSidesExist`; `Resolve_OtherSideNotRegistered_IsPausedNotRegistered`; `Resolve_TreatsRetypedSideAsAbsent` (Telegram id row returns `type: "telegram:group"`); `Resolve_SignedInSideMissing_StillAnswersOtherSide`; `Resolve_AdminDisabledSide_IsPaused`; `Resolve_LinkingDisabled_AnswersCallerAlone`; `Resolve_CachedForOneMinute` (second call no repository hit); `Invalidate_DropsBothIds`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `feat: resolve a linked pair per request`.

### Task 4: `TelegramWidgetVerifier`, shared by sign-in and link

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Services/TelegramWidgetVerifier.cs`
- Modify: `Applications/.../Controllers/AuthController.cs` (`TelegramVerify` calls the verifier; behaviour unchanged)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/TelegramWidgetVerifierTests.cs`; existing `AuthController` Telegram tests stay green

**Interfaces:**
- Produces: `public static class TelegramWidgetVerifier { public static TelegramWidgetResult Verify(IReadOnlyDictionary<string,string> data, string botToken, TimeSpan maxAge, DateTimeOffset now); }` and `public sealed record TelegramWidgetResult(bool Valid, string? Failure, long UserId, string Name)`; `Failure` is `"bad_hash"`, `"expired"` or `"missing_fields"`. `Name` is first + last name, falling back to username.
- Constants: `SignInMaxAge = TimeSpan.FromHours(24)`, `LinkMaxAge = TimeSpan.FromMinutes(10)`.

- [ ] **Step 1:** Tests, with a payload signed in the test by the same HMAC rule (`SHA256(botToken)` key over sorted `key=value` lines): valid payload passes with the id; tampered field fails `bad_hash`; `auth_date` 11 minutes old fails `expired` under `LinkMaxAge` and passes under `SignInMaxAge`; missing `hash` fails `missing_fields`.
- [ ] **Step 2:** Run; expect failures.
- [ ] **Step 3:** Move the HMAC code out of `TelegramVerify` into the verifier and call it with `SignInMaxAge`.
- [ ] **Step 4:** Run the new tests and every existing `TelegramVerify` test; expect PASS.
- [ ] **Step 5:** Commit `refactor: share the Telegram widget check between sign-in and linking`.

### Task 5: `AccountLinkService` and `LinkedHumanProvisioner`

**Files:**
- Create: `IAccountLinkService.cs`, `AccountLinkService.cs`, `LinkedHumanProvisioner.cs`, `Core/Pgan.PoracleWebNet.Core.Models/LinkRefusal.cs`
- Modify: `ServiceCollectionExtensions.cs`, `Core/.../Services/UserPurgeService.cs` (call `RemoveByHumanIdAsync` and `ILinkedAccount.Invalidate` for the purged id)
- Test: `Tests/.../Services/AccountLinkServiceTests.cs`, `LinkedHumanProvisionerTests.cs`, extend `UserPurgeServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3, `IPoracleHumanProxy` (`GetHumanAsync`, `CreateHumanAsync`, `GetProfilesAsync`, `AddProfileAsync`, `SetProfileGeographyAsync`, `UpdateProfileAsync`, `SwitchProfileAsync`, `GetPlacesAsync`, `AddPlaceAsync`, `DeleteHumanAsync`), `ISiteSettingService.GetByKeyAsync(AccountLinkKeys.RegisterMissing)`.
- Produces:
  - `public enum LinkRefusalCode { WrongType, BadId, TypeMismatch, Taken, Expired, NotPermitted, ProvisionFailed }` and `LinkRefusalException(LinkRefusalCode code, string message)`; wire codes `link_wrong_type`, `link_bad_id`, `link_type_mismatch`, `link_taken`, `link_expired`, `link_not_permitted`, `link_provision_failed`.
  - `IAccountLinkService.LinkAsync(string callerHumanId, LinkSide newSide, string newId, string newName) -> Task<AccountLink>`
  - `UnlinkAsync(string humanId) -> Task<bool>`, `SetDefaultAsync(string humanId, LinkSide side) -> Task`
  - `LinkedHumanProvisioner.CreateMirrorAsync(string sourceHumanId, LinkSide newSide, string newId, string newName) -> Task`; throws `LinkRefusalException(ProvisionFailed, "Nothing was linked.")` after rollback.
- Rules (spec §2): caller must be `discord:user`/`telegram:user` and the other side; Discord id ASCII digits 1-20; Telegram id positive `long`; existing other human must be the matching `:user`; unique conflict maps to `Taken`. Default starts as the caller's side. Provisioning copies `type, id, name, language, latitude, longitude, area, community` (from the source's `community_membership`), then profiles beyond the default (name, `active_hours`, per-profile areas and location), saved places, current profile. Rollback deletes the created human only if its `type` and `name` still match (the #1028 rule). Alerts are never copied.

- [ ] **Step 1:** `AccountLinkServiceTests`: `Link_DiscordCallerAddsTelegram_StoresDefaultDiscord`; `Link_TelegramCallerAddsDiscord_StoresDefaultTelegram`; `Link_SameTypeTwice_WrongType`; `Link_DestinationCaller_WrongType` (`discord:channel`); `Link_NegativeTelegramId_BadId`; `Link_DiscordIdWithLetters_BadId`; `Link_ExistingOtherIsGroup_TypeMismatch`; `Link_UniqueViolationRace_MapsToLinkTaken`; `Link_OtherMissing_SettingOff_SavesLinkWithoutCreate`; `Link_OtherMissing_SettingOn_ProvisionsThenSaves`; `Unlink_InvalidatesBothIds`; `SetDefault_SideMissing_Refused`.
- [ ] **Step 2:** `LinkedHumanProvisionerTests`: copies exactly the declared create fields (assert the body's property names); copies profiles 1 and 2 with names and schedules; copies places; profile copy failure deletes the created human and throws `ProvisionFailed`; rollback skips delete when the row's name no longer matches.
- [ ] **Step 3:** `UserPurgeServiceTests`: purging either id removes its link; purging an unlinked user makes no repository change beyond the call.
- [ ] **Step 4:** Run; expect failures. Implement. Run; expect PASS.
- [ ] **Step 5:** Commit `feat: link a Discord and a Telegram account, creating the missing side when allowed`.

### Task 6: `AccountLinkController` and the Discord callback branch

**Files:**
- Create: `Applications/.../Controllers/AccountLinkController.cs`
- Modify: `AuthController.cs` (`DiscordCallback` link branch; `DiscordLogin` unchanged), `AdminController.cs` (or a new `AdminAccountLinkController.cs`) for admin list and unlink
- Test: `Tests/.../Controllers/AccountLinkControllerTests.cs`, extend `Tests/.../Controllers/AuthControllerTests` (Discord callback), `Tests/.../Filters/FeatureGateCoverageTests.cs` covers the new key automatically once applied

**Interfaces:**
- Consumes: Task 5, `TelegramWidgetVerifier.LinkMaxAge`, `IDataProtectionProvider`, `TelegramSettings`, `DiscordSettings`.
- Produces, all `[RequireFeatureEnabled(DisableFeatureKeys.AccountLinking)]`, all refusing `IsImpersonating` with 403 except `GET status`:
  - `POST api/account-link/discord/start` -> `{ url }`; sets `oauth_state` (as `DiscordLogin` does) and `oauth_link`.
  - `POST api/account-link/telegram` body widget fields -> 200 `{ linked: AccountLinkView }` or 4xx `{ error, code }`.
  - `DELETE api/account-link` -> 204.
  - `PUT api/account-link/default` `{ destination: "discord"|"telegram" }` -> 204.
  - `GET api/account-link/status` -> filled in Task 13; this task answers `{ linked, sides, defaultDestination }` without `drift`.
  - Admin: `GET api/admin/account-links` -> `AccountLink[]`; `DELETE api/admin/account-links/{humanId}` -> 204. Admin only.
  - `LinkRefusalException` maps to 409 for `Taken`, 502 for `ProvisionFailed`, 403 for `NotPermitted`, 400 otherwise, body `{ error, code }` (an exception filter registered like `AreaNotPermittedExceptionFilter`).
  - `DiscordCallback`: with a valid unprotected `oauth_link` (not expired, `humanId` present) it runs `enable_discord` and `CheckRoleAccessAsync` for the Discord id, then `LinkAsync(humanId, Discord, discordId, username)`, deletes the cookie and redirects to `{frontendUrl}/linked-accounts#linked=discord` or `#link_error={code}`. It never issues a token on this branch. A cookie that fails to unprotect or has expired redirects with `link_error=link_expired`.

- [ ] **Step 1:** Controller tests: start sets both cookies and returns a URL carrying the same `state`; Telegram link with a 5-minute-old payload links; 11-minute-old refused `link_expired`; impersonating caller 403 on every write; status unlinked answers `{ linked: false }`; admin unlink by non-admin 403.
- [ ] **Step 2:** Callback tests: `DiscordCallback_WithLinkCookie_LinksAndRedirectsWithoutToken`; `DiscordCallback_LinkCookieTampered_RedirectsLinkExpired`; `DiscordCallback_LinkRoleCheckFails_RedirectsNotPermitted`; `DiscordCallback_WithoutLinkCookie_IsTodaysSignIn` (legitimate case: the existing sign-in assertions unchanged).
- [ ] **Step 3:** Run; expect failures. Implement. Run the controller, callback, `FeatureGateCoverageTests` and `ImpersonationPipelineTests`; expect PASS.
- [ ] **Step 4:** Commit `feat: account link endpoints and the Discord link callback`.

### Task 7: Linked accounts page, settings and docs (frontend)

**Files:**
- Create: `ClientApp/src/app/core/services/linked-account.service.ts` (+ spec), `modules/linked-accounts/linked-accounts.component.{ts,html,scss,spec.ts}`, `shared/components/telegram-login-button/` (extracted from `modules/auth/login.component.ts`, which now uses it)
- Modify: `app.routes.ts` (`linked-accounts` with `authGuard` and `disabledFeatureGuard('disable_account_linking')`), `app.ts`/`app.html` (user-menu item beside Alert Defaults, `disableKey: 'disable_account_linking'`), `modules/admin/admin-settings.component.ts` (two boolean entries under features), `modules/admin/admin-users` (chip "Linked to Telegram/Discord" and Unlink action from `GET api/admin/account-links`), all 11 locale files, `docs/features/linked-accounts.md` (new), `docs/configuration/site-settings.md`, Help page section, `CHANGELOG.md` `[Unreleased]` Added
- Test: the new specs, `login.component.spec.ts` stays green, `locale-parity.spec.ts`

**Interfaces:**
- Produces: `LinkedAccountService` with `status: Signal<LinkStatus | null>`, `refresh(): Promise<void>`, `startDiscordLink(): Promise<void>` (navigates to the returned URL), `linkTelegram(data: Record<string,string>): Promise<void>`, `unlink()`, `setDefault(side: 'discord'|'telegram')`; `LinkStatus` mirrors the status endpoint (`linked`, `sides[] {side, exists, pause}`, `defaultDestination`, `drift[]` added in Task 13). `refresh()` runs after `AuthService.loadCurrentUser` and on window focus at most once per 60 s.
- Page: one card per side (name, state: active, paused with reason, waiting to register), Link/Unlink, "New alerts go to" radio, the link-time hint lines from spec §2 (Telegram: press Start with the bot; Discord: generic copy plus `support_url` when set), reads `#linked=` and `#link_error=` from the fragment once and clears it.
- Link offered only when the other platform is configured: Discord from `GET api/auth/providers`, Telegram from `GET api/auth/telegram/config`.

- [ ] **Step 1:** Specs: service refresh throttle (two focus events within 60 s make one request); page renders Link for a configured, unlinked side and hides it when unconfigured; `#link_error=link_taken` shows the translated message and clears the fragment; default radio calls `setDefault`; settings page lists both keys; Users row shows the chip only for linked ids.
- [ ] **Step 2:** Run `npm test -- linked-account linked-accounts admin-users admin-settings login`; expect failures. Implement. Run; expect PASS. Run `npm run lint`, `npm run prettier-check`, `npm run build`.
- [ ] **Step 3:** Live check on the dev-01 PoracleNG 5.4.0 bed: link from a Discord session and from a Telegram session, with `link_register_missing` off then on; unlink. Record what happened in the PR.
- [ ] **Step 4:** Commit `feat: linked accounts page, settings and docs`. Open PR 1 into `develop`.

---

## Phase B: Alerts (PR 2)

### Task 8: `IAlarmService<T>`, `IHasDestination` and `LinkedAlarmRouter` reads

**Files:**
- Create: `IAlarmService.cs`, `IHasDestination.cs`, `LinkedAlarmRouter.cs`
- Modify: the eleven `I*Service` interfaces (`Monster, Raid, Egg, Quest, Invasion, Lure, Nest, Gym, MaxBattle, FortChange, PokestopEvent`) to extend `IAlarmService<TModel>` (members they already declare move to the base; no signature changes); the eleven alarm models gain `[JsonIgnore(Condition = WhenWritingNull)] public string? Destination { get; set; }` and implement `IHasDestination`; the eleven `*Update` DTOs gain `string? Destination`; the eleven `*Create` DTOs gain `string? Destination`; the eleven alarm controllers
- Test: `Tests/.../Services/LinkedAlarmRouterTests.cs`, `Tests/.../Architecture/AlarmControllersUseRouterTests.cs`, existing controller tests stay green

**Interfaces:**
- Produces:
  - `IAlarmService<TModel>`: `GetByUserAsync(string userId, int profileNo)`, `GetByUidAsync(string userId, int uid)`, `CreateAsync(string userId, TModel model)`, `UpdateAsync(string userId, TModel model)`, `DeleteAsync(string userId, int uid)`, `DeleteAllByUserAsync(string userId, int profileNo)`, `UpdateDistanceByUserAsync(string userId, int profileNo, int distance)`, `UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)`, `CountByUserAsync(string userId, int profileNo)`, `BulkCreateAsync(string userId, IEnumerable<TModel> models)`.
  - `LinkedAlarmRouter` (scoped):
    - `Task<IReadOnlyList<TModel>> ListAsync<TModel>(IAlarmService<TModel> svc, string userId, int profileNo) where TModel : IHasDestination`
    - `Task<(TModel Model, string OwnerId)?> FindAsync<TModel>(IAlarmService<TModel> svc, string userId, int uid)`
    - `Task<int> CountAsync<TModel>(...)`, `Task<int> DeleteAllAsync<TModel>(...)` (both humans)
    - `Task<DistanceUpdateResult> UpdateDistanceAllAsync<TModel>(...)`, `Task<DistanceUpdateResult> UpdateDistanceByUidsAsync<TModel>(svc, string userId, List<int> uids, int distance)` (group by owner, merge results)
    - `Task<string> CreateTargetAsync(string userId, string? destination)` -> human id for a create; throws `AlarmDestinationException(409, "Telegram isn't set up yet.")` for a side that doesn't exist; returns the default when `destination` is null; ignores `destination` when unlinked.
    - `bool IsPaused(string humanId)` from the resolved pair.
  - Stamping: `Destination` is set from the owner's side only when `pair.IsLinked`; unlinked leaves it null.
- Controllers: every action that today passes `this.UserId` to its service goes through the router. `POST` returns `pausedDestination: true` beside the model when the target is paused (wrap only in that case, so the unlinked body is unchanged).

- [ ] **Step 1:** Router tests: `List_Unlinked_SingleCallNoDestination`; `List_Linked_ConcatenatesAndStamps`; `Find_UidOnOtherSide_ReturnsOther`; `Find_UidOnNeither_ReturnsNull`; `UpdateDistanceByUids_GroupsByOwner`; `DeleteAll_Linked_HitsBoth`; `CreateTarget_Null_UsesDefault`; `CreateTarget_UnregisteredSide_Throws409`; `CreateTarget_Unlinked_IgnoresDestination`.
- [ ] **Step 2:** Architecture test `AlarmControllersUseRouterTests`: source-scan the eleven controller files; fail if any line matches `_\w+Service\.\w+Async\(this\.UserId` or `_\w+Service\.\w+Async\([^)]*this\.UserId` outside a router call. Watch it fail before the controllers change.
- [ ] **Step 3:** A JSON test `UnlinkedMonster_SerialisesWithoutDestination` comparing serialised output with and without the new property set to null.
- [ ] **Step 4:** Run; expect failures. Implement interfaces, models, router, controllers. Run all backend tests; expect PASS.
- [ ] **Step 5:** Commit `feat: route alarm calls across a linked pair`.

### Task 9: `AlarmMover`, `QuickPickUidTransfer` and `POST /api/alarm-move`

**Files:**
- Create: `AlarmMover.cs`, `QuickPickUidTransfer.cs`, `AlarmMoveController.cs`, `Core/Pgan.PoracleWebNet.Core.Models/AlarmMove.cs`
- Modify: the eleven alarm controllers' `PUT /{uid}` (a `Destination` naming the other side moves the updated model through the mover)
- Test: `Tests/.../Services/AlarmMoverTests.cs`, `QuickPickUidTransferTests.cs`, `Tests/.../Controllers/AlarmMoveControllerTests.cs`

**Interfaces:**
- Consumes: Task 8 router, `IPoracleHumanProxy.GetPlacesAsync`, `IUserAreaPolicy`, `IQuickPickAppliedStateRepository`, the type-name translation `TrackedUidRemapper` uses (`pokemon` <-> `monster`), `IFeatureGate`.
- Produces:
  - `public enum MoveStatus { Moved, Conflict, SourceRemains, Refused }`; `public sealed record MoveOutcome(int Uid, MoveStatus Status, int? NewUid, int? ExistingUid, string? Reason)`.
  - `AlarmMover.MoveAsync<TModel>(IAlarmService<TModel> svc, string trackingType, string userId, TModel model, string sourceOwnerId, LinkSide target) -> Task<MoveOutcome>`.
  - `QuickPickUidTransfer.TransferAsync(string fromUserId, string toUserId, int profileNo, string trackingType, int oldUid, int newUid) -> Task`.
  - `POST api/alarm-move` `{ type, uids: int[], destination }` -> `{ results: MoveOutcome[] }`; checks `IFeatureGate.EnsureEnabledAsync(DisableFeatureKeys.ByTrackingType[type])` and `AccountLinking`; refused while the pair is unlinked (400).
- Protocol (spec §3), with this plan's tightening: preflight requires the target human's `current_profile_no` to equal the rule's `profile_no` (refused "Your Discord and Telegram sides are on different profiles. Resolve the banner first."), the place label to exist on the target, `override_areas` to pass `IUserAreaPolicy` for the target. Create with the type's `svc.CreateAsync(targetId, model)` so the service's validation, feature gate and dry-run protocol run. `TrackingConflictException` -> `Conflict` with its `ExistingUid`. An answer with uid 0 (identical rule present) counts as created. Delete with `svc.DeleteAsync(sourceOwnerId, uid)`, retried once; still failing -> `SourceRemains`. Then `TransferAsync`.

- [ ] **Step 1:** Mover tests: `Move_HappyPath_CreatesThenDeletes` (verify order with `MockSequence`); `Move_RefusesWhenTargetCurrentProfileDiffers`; `Move_PlaceMissingOnTarget_RefusedNothingWritten`; `Move_AreaNotPermittedOnTarget_Refused`; `Move_CreateConflict_SourceUntouched`; `Move_IdenticalOnTarget_StillDeletesSource`; `Move_DeleteFailsTwice_SourceRemains`; `Move_DeleteFailsOnce_RetriesAndMoves`.
- [ ] **Step 2:** Transfer tests: uid leaves the source row and lands in the target row for the same pick and profile; creates the target row when absent; `pokemon` maps to applied-state `monster`; a uid in no pick is a no-op.
- [ ] **Step 3:** Controller tests: disabled type 403; unlinked 400; mixed results per uid; `PUT /{uid}` with the other destination moves, with the same destination edits in place (legitimate case).
- [ ] **Step 4:** Run; expect failures. Implement. Run; expect PASS.
- [ ] **Step 5:** Commit `feat: move an alert between Discord and Telegram without losing it`.

### Task 10: The rest of the alarm surfaces across the pair

**Files:**
- Modify: `CleaningController.cs`, `DashboardController.cs` (via `DashboardService`), `ProfileOverviewController.cs` (read side), `LocationController.cs` (`pin-usage`, `place-usage`), `TestAlertController.cs`, `QuickPickController.cs`
- Test: extend each controller's existing tests

**Interfaces:**
- Consumes: Task 8 router, `ILinkedAccount`.
- Rules: cleaning status sums `counts` per type across both humans and a type is clean only when both are; cleaning toggle patches both humans; dashboard counts sum; profile overview concatenates with `destination` stamped; pin and place usage add both humans' counts and rules; test alert finds the owner with `FindAsync` and sends as the owner; quick-pick `Apply` targets `CreateTargetAsync(userId, null)`, `GetAll` marks a pick applied when either human holds a row (counts summed), `Remove` runs for each human holding a row.

- [ ] **Step 1:** Per surface, one linked test and one unlinked test asserting today's single call: `Cleaning_Linked_SumsCounts`, `Cleaning_Unlinked_Unchanged`, `Dashboard_Linked_SumsCounts`, `TestAlert_RuleOnTelegram_SendsAsTelegram`, `PlaceUsage_Linked_UnionsRules`, `QuickPickApply_Linked_UsesDefault`, `QuickPickRemove_RowsOnBothSides_RemovesBoth`.
- [ ] **Step 2:** Run; expect failures. Implement. Run all backend tests; expect PASS.
- [ ] **Step 3:** Commit `feat: dashboard, cleaning, test alerts and quick picks see the whole pair`.

### Task 11: Deliver to, card glyph, filter and bulk move (frontend)

**Files:**
- Create: `shared/components/deliver-to/deliver-to-field.component.{ts,html,spec.ts}`
- Modify: the twenty add and edit dialogs under `modules/{pokemon,raids,quests,invasions,lures,nests,gyms,fort-changes,pokestop-events,max-battles}/` (eggs share raids), their alarm models in `core/models`, `shared/components/alarm-card`, `alarm-list-toolbar`, `bulk-action-bar`, the ten list components, `modules/alarm-dialog-parity.spec.ts`, locale files
- Test: the new spec, `alarm-dialog-parity.spec.ts`, `alarm-card-parity.spec.ts`, `bulk-visible-selection.spec.ts`

**Interfaces:**
- Consumes: `LinkedAccountService.status`, `POST api/alarm-move`, the `pausedDestination` and `destination` fields.
- Produces: `<app-deliver-to-field [control]="form.controls.destination" />`, rendered only when both sides exist; a paused side stays selectable with one warning line. Add dialogs seed the control from `defaultDestination`; edit dialogs from the alarm's `destination`. The card shows an uncoloured Discord or Telegram glyph beside the scope chip when `destination` is set. The toolbar gains an All/Discord/Telegram filter and the bulk bar a "Deliver to…" menu, both only when linked. Bulk move reports per uid with the copy for `SourceRemains` from spec §3.
- Parity: add `'deliver-to'` to the controls `alarm-dialog-parity.spec.ts` requires in both dialogs of every pair; nothing in `EDIT_ONLY`.

- [ ] **Step 1:** Specs: field hidden when unlinked or one side missing; add preselects the default; edit preselects the owner; paused side shows the warning; card glyph absent when `destination` is undefined; filter hides rows of the other side; bulk move renders `SourceRemains` text; parity spec fails until every dialog carries the field.
- [ ] **Step 2:** Run the specs; expect failures. Implement across all twenty dialogs. Run `npm test`, `npm run lint`, `npm run prettier-check`, `npm run build`; expect PASS.
- [ ] **Step 3:** Live check on dev-01: add one alert to each side and confirm delivery in a Discord DM and a Telegram DM; move one each way; make the source delete fail (stop the source human's route with a proxy fault in a debug build or by deleting the rule out of band) and see the "copy is still there" message. Deleting alerts DMs the owning account; say so before running.
- [ ] **Step 4:** Commit `feat: choose where each alert is delivered`. Open PR 2 into `develop`.

---

## Phase C: Keeping the pair in step (PR 3)

### Task 12: `PairedHumanWriter` and its callers

**Files:**
- Create: `PairedHumanWriter.cs`, `Core/Pgan.PoracleWebNet.Core.Models/PairWrite.cs`
- Modify: `LocationController.cs` (location, places), `AreaController.cs`, `UserGeofenceController.cs` / `UserGeofenceService.cs` (activate, deactivate, create's activation; ownership and the 10-fence cap across the pair), `UserAreaPolicy.cs` (own drawn fences of either id), `ProfileController.cs`, `ProfileOverviewController.cs` (duplicate, import), `NotificationLanguageController.cs`, `MuteController.cs`, `AuthController.ToggleAlerts`, `SummaryScheduleController.cs`
- Test: `Tests/.../Services/PairedHumanWriterTests.cs`, `Tests/.../Architecture/PersonWritesUseWriterTests.cs`, extensions to the touched controllers' tests

**Interfaces:**
- Consumes: `ILinkedAccount`.
- Produces:
  - `public sealed record PairWarning(LinkSide Side, string Reason)`; `public sealed record PairWriteResult<T>(T Primary, T? Other, PairWarning? Warning)`.
  - `Task<PairWriteResult<T>> WriteAsync<T>(string userId, Func<string, Task<T>> write)` and a non-generic `Task<PairWriteResult<bool>> WriteAsync(string userId, Func<string, Task> write)`. Writes the caller's human, then the other existing human; the first throwing propagates; the second throwing is caught (except `OperationCanceledException`), logged at Warning, and returned as `Warning`.
  - Controllers add `pairWarning: { side, reason }` to their response only when `Warning` is set; with no warning the body is unchanged.
  - Profile create: if `Primary` and `Other` profile numbers differ, delete the other side's new profile and return a `PairWarning` with reason "profiles out of step".
  - Profile import: profile through the writer, alerts through `CreateTargetAsync(userId, null)`.
- Architecture test: in each listed controller file (and `UserGeofenceService.cs`), every method whose body matches `_\w+\.(SetLocationAsync|AddPlaceAsync|UpdatePlaceAsync|DeletePlaceAsync|SetProfileGeographyAsync|AddProfileAsync|UpdateProfileAsync|DeleteProfileAsync|SwitchProfileAsync|CopyProfileAsync|SetLanguageAsync|StartAsync|StopAsync|SaveSelectionAsync|AddToActiveProfileAsync|RemoveFromActiveProfileAsync|SetScheduleAsync|DeleteScheduleAsync)\(` or `_muteProxy\.(CreateAsync|DeleteAsync|DeleteAllAsync)\(` must also contain `_pairWriter.WriteAsync`. These are the write members of `IPoracleHumanProxy`, `IHumanService`, `IUserAreaWriter`, `IPoracleSummaryProxy` and `IPoracleMuteProxy` on `develop`; a method that delegates to a service which itself writes (for example `ProfileService`) wraps that service call instead, and the test lists those service methods once found.

- [ ] **Step 1:** Writer tests: `Unlinked_OneCall`; `Linked_WritesCallerThenOther` (sequence); `FirstFails_SecondNotTried`; `SecondFails_ReturnsWarning`; `OtherSideNotRegistered_OneCall`.
- [ ] **Step 2:** Controller tests: location save on a linked pair writes both; a failing second side returns `pairWarning`; unlinked body unchanged (assert serialised JSON); profile create numbering mismatch deletes the stray profile; geofence ownership accepts the other id; the cap counts both ids' fences (11th refused).
- [ ] **Step 3:** Run the architecture test against today's controllers; watch it fail. Implement. Run all backend tests; expect PASS.
- [ ] **Step 4:** Commit `feat: write person-level settings to both linked accounts`.

### Task 13: `PairDriftService`, status drift and resolve

**Files:**
- Create: `PairDriftService.cs`, `Core/Pgan.PoracleWebNet.Core.Models/PairDrift.cs`
- Modify: `AccountLinkController.cs` (`GET status` adds `drift`; `POST resolve`)
- Test: `Tests/.../Services/PairDriftServiceTests.cs`, `AccountLinkControllerTests.cs`

**Interfaces:**
- Consumes: `ILinkedAccount`, `IPoracleHumanProxy` (`GetHumanAsync`, `GetAreaListAsync`, `GetProfilesAsync`, `GetPlacesAsync`, writes as in Task 12), `PairedHumanWriter`, the router's per-profile counts (for the delete refusal).
- Produces:
  - `public enum DriftField { Location, Areas, CurrentProfile, Profiles, Places }`; `public sealed record DriftItem(DriftField Field, JsonElement Discord, JsonElement Telegram, string Fingerprint)` where each side's value is the comparable form and `Fingerprint` is a SHA-256 of both sides' comparable JSON.
  - `Task<IReadOnlyList<DriftItem>> GetAsync(LinkedPair pair)`; only when both sides exist; empty for an unlinked pair without any PoracleNG call.
  - `Task ResolveAsync(LinkedPair pair, DriftField field, LinkSide keep, string expected)`; throws `DriftChangedException` (409 "That changed again. Check the banner.") when the re-read `Fingerprint` differs from `expected`; throws `DriftProfileHoldsAlarmsException(profileNo, name, count)` (409) instead of deleting a non-empty profile.
  - `GET api/account-link/status` gains `drift: [{ field, discord, telegram, fingerprint }]`; `POST api/account-link/resolve` `{ field, keep, expected }` -> 204.
- Comparable forms: location `{lat, lon}` rounded to 5 dp; areas sorted normalised keys; current profile number; profiles sorted by number with name, `active_hours` normalised to int `{day,hours,mins}` sorted, per-profile normalised areas and rounded location; places sorted by label with rounded coordinates.

- [ ] **Step 1:** Tests: `MatchingPair_NoDrift`; `LocationWithin1e-6_NoDrift`; `AreasDifferOnlyByCaseAndUnderscore_NoDrift`; `ActiveHoursStringVsInt_NoDrift`; `LocationDiffers_OneItem`; `PausedSide_NotDrift`; `Unlinked_NoProxyCalls`; `ResolveLocation_KeepTelegram_WritesDiscordOnly`; `ResolveAreas_KeepDiscord_WritesTelegram`; `ResolveCurrentProfile_SwitchesOther`; `ResolveProfiles_CreatesMissingAndCopiesSchedules`; `ResolveProfiles_EmptyExtraDeleted`; `ResolveProfiles_ExtraWithAlarms_Refused`; `Resolve_StaleExpected_Returns409`.
- [ ] **Step 2:** Run; expect failures. Implement. Run; expect PASS.
- [ ] **Step 3:** Commit `feat: find and resolve drift between linked accounts`.

### Task 14: Drift banner and pair warnings (frontend)

**Files:**
- Create: `shared/components/pair-drift-banner/pair-drift-banner.component.{ts,html,scss,spec.ts}`
- Modify: `app.html` (banner under the toolbar), `LinkedAccountService` (drift, `resolve(field, keep, expected)`), `core/interceptors` or the touched services so a `pairWarning` in any response shows "Saved for Discord. Telegram didn't take it: …" once, the location dialog/area page/profile page snackbars, locale files, `docs/features/linked-accounts.md`, `CLAUDE.md` (a "Linked accounts" section: router, writer, create-then-delete, drift never resolved silently, links are `discord:user` with `telegram:user` only), `CHANGELOG.md`
- Test: banner spec, a `pairWarning` spec

**Interfaces:**
- Consumes: Task 13 status and resolve, Task 12 `pairWarning`.
- Produces: one line per drift item, location named through the existing reverse geocode with coordinates as fallback, areas showing only the difference ("Telegram also has Riverside"); Keep buttons send the item's `fingerprint` as `expected`; a 409 refreshes status and shows the server's message; dismiss hides the banner until `sessionStorage` clears (wrapped in try/catch).

- [ ] **Step 1:** Specs: no drift renders nothing; a location item renders both names and two buttons; Keep sends `{ field, keep, expected: fingerprint }`; 409 refreshes; dismiss persists in the session only; `pairWarning` in a response shows one snackbar with the side's name.
- [ ] **Step 2:** Run; expect failures. Implement. Run `npm test`, lint, prettier, `npm run build`; expect PASS.
- [ ] **Step 3:** Live check on dev-01: `!location` in the Telegram bot, see the banner, keep Telegram, confirm both humans match; `!profile` add on one side, resolve; unlink.
- [ ] **Step 4:** Final public-path grep over the whole branch diff: `git diff origin/develop... | grep -n -i -E "$PRIVATE_TERMS" | grep -v "Pgan\.PoracleWebNet"`, with `PRIVATE_TERMS` supplied by the repo owner at run time (the list itself never goes into this repo). Expect no output. Commit `feat: show and resolve differences between linked accounts`. Open PR 3 into `develop`.

---

## Phase order and PRs

PR 1 (Tasks 1-7) is usable alone: people can link, see both identities and set a default, with nothing else changing. PR 2 (Tasks 8-11) adds the merged list and moves. PR 3 (Tasks 12-14) adds dual writes and drift. Merge in order, updating each branch from `develop` after the previous squash merge, and check the CHANGELOG entry lands under `[Unreleased]` after each merge.

Until PR 3 merges, a linked person's settings reach one side only and nothing reports the difference. Don't cut a release from `develop` between PR 1 and PR 3; if one has to go out, set `disable_account_linking` on for it in the release notes.
