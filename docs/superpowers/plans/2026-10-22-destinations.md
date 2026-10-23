# People and Destinations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin > Users lists people; a Destinations page lists, creates, manages and delegates every webhook, Discord channel/thread and Telegram channel/group/topic (#1025).

**Architecture:** One server-side classifier (`HumanKind`) decides person vs destination and which types may be created, granted or reached by a delegate. The admin list carries the server's `kind` to the SPA. Delegation keeps `UserRoleResolver` as the single answer for all three surfaces, widened to destinations through a cached destination directory and one opt-in source.

**Tech Stack:** .NET 10 / ASP.NET Core, xUnit + Moq; Angular 21 standalone + signals, Angular Material 3, Jest.

**Spec:** `docs/superpowers/specs/2026-10-22-destinations-design.md`

## Global Constraints

- Base every part on `origin/develop`; one branch and one PR per part, in the order below. Part 2 merges before Parts 4 and 6.
- Person = type null, empty, whitespace, or ending `:user` (case-insensitive). Everything else is a destination.
- `HumanKind.Destinations` = exactly `webhook`, `discord:channel`, `discord:thread`, `telegram:channel`, `telegram:group`, `telegram:topic`. It is the only list that grants power (create, grant target, delegate impersonation).
- `HumanKind.MentionCapable` = `webhook`, `discord:channel`, `discord:thread`.
- New setting key: `delegation_poracle_channels`, boolean, absent = off.
- Login refusal code: `destination_account`; i18n key `AUTH.ERR_DESTINATION_ACCOUNT`.
- The JWT claim stays named `managedWebhooks`.
- Every i18n change lands in all 11 locales (`da de en es fr it nl pl pt pt-BR sv` under `ClientApp/src/assets/i18n/`); `locale-parity.spec.ts` enforces it. No English string may start with "Failed to" (`failure-message-parity.spec.ts`).
- Writing style per the user's CLAUDE.md for every comment, string, doc, commit and PR. Always write "PoracleWeb.NET". No `Claude-Session` trailer or claude.ai link anywhere (memory: no session URLs).
- CHANGELOG `[Unreleased]` entry in every PR (Keep a Changelog).
- Windows hygiene: `git config core.autocrlf false` before frontend work; after every `.cs` edit run `git diff --numstat` and fix any whole-file EOL flip.
- Backend tests: `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS= dotnet test --no-restore --filter "FullyQualifiedName~<Class>"`. Frontend: `npx jest <path>` from `ClientApp/`. Before any push that touches a template: `npm run build`, `npm run lint`, `npm run prettier-check`.

## Review Focus

1. **An unknown type string** (`x:unknown`, an operator's typo) must appear on Destinations, never on both pages or neither, and must be refused as a create type, grant target and delegate impersonation target. Tests: Task 1.1 (classifier), Task 3.1 (`kind` stamped), Task 3.4 (partition), Task 6.2 (grant), Task 6.4 (impersonate).
2. **A legacy OIDC or Telegram person with a null or empty type** must still sign in. Tests: Task 2.1 (`MeAcceptsANullType`), Task 2.2 (OIDC null type).
3. **A delegate's session ending by a 401 burst** must land on My Destinations, not `/admin` then `/dashboard`. Test: Task 5.2 (`session-401-burst.spec.ts` updated assertion).
4. **A webhook grant written before Part 6** (all six prod grants) must keep resolving identically after the directory replaces `GetWebhooksAsync`. Test: Task 6.3 (`AnExistingWebhookGrantStillResolves`).
5. **A Telegram supergroup id (`-100…`) and a legacy basic-group id (`-` plus 10 digits)** must both be accepted by create. Test: Task 4.1.

---

## Part 1 — Create rollback race (#1028)

Branch `fix/1028-create-rollback-race`.

### Task 1.1: HumanKind classifier

**Files:**
- Create: `Core/Pgan.PoracleWebNet.Core.Models/HumanKind.cs`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Models/HumanKindTests.cs`

**Interfaces:**
- Produces (namespace `Pgan.PoracleWebNet.Core.Models`, `public static class HumanKind`, styled like `CleanFlags`):
  - `const string Webhook = "webhook"`, `DiscordChannel = "discord:channel"`, `DiscordThread = "discord:thread"`, `TelegramChannel = "telegram:channel"`, `TelegramGroup = "telegram:group"`, `TelegramTopic = "telegram:topic"`
  - `static IReadOnlySet<string> Destinations` (OrdinalIgnoreCase, the six above)
  - `static IReadOnlySet<string> NameAddressable` (`webhook`, `discord:channel`, `telegram:channel`, `telegram:group`)
  - `static IReadOnlySet<string> MentionCapable` (`webhook`, `discord:channel`, `discord:thread`)
  - `static bool IsPerson(string? type)`; `static bool IsKnownDestination(string? type)` (= `type` in `Destinations`)
  - `static string KindOf(string? type)` → `"person"` or `"destination"`

This lands in Part 1 because Task 1.2 matches the rollback row's type with it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("discord:user", true)] [InlineData("telegram:user", true)] [InlineData("oidc:user", true)]
[InlineData("DISCORD:USER", true)] [InlineData(null, true)] [InlineData("", true)] [InlineData("  ", true)]
[InlineData("webhook", false)] [InlineData("Webhook", false)] [InlineData("discord:channel", false)]
[InlineData("discord:thread", false)] [InlineData("telegram:channel", false)] [InlineData("telegram:group", false)]
[InlineData("telegram:topic", false)] [InlineData("x:unknown", false)]
public void IsPersonFollowsUpstreamsUserSuffix(string? type, bool expected) => Assert.Equal(expected, HumanKind.IsPerson(type));

[Theory] [InlineData("x:unknown")] [InlineData("discord:user")] [InlineData(null)]
public void OnlyTheSixKnownTypesGrantPower(string? type) => Assert.False(HumanKind.IsKnownDestination(type));

[Fact] public void AnUnknownTypeIsListedAsADestination() => Assert.Equal("destination", HumanKind.KindOf("x:unknown"));

[Fact] public void MentionIsDiscordAndWebhookOnly()
{
    Assert.True(HumanKind.MentionCapable.Contains("discord:thread"));
    Assert.False(HumanKind.MentionCapable.Contains("telegram:group"));
}
```

- [ ] **Step 2: Run, expect compile failure** (`HumanKind` missing).
- [ ] **Step 3: Implement `HumanKind`.** `IsPerson`: `string.IsNullOrWhiteSpace(type) || type.EndsWith(":user", StringComparison.OrdinalIgnoreCase)`. The `<summary>` cites upstream's `!broadcast` `type LIKE '%:user'` split and #1025.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: classify Poracle humans as people or destinations (#1025)`.

### Task 1.2: Roll back only the row this request wrote

**Files:**
- Modify: `Applications/Pgan.PoracleWebNet.Api/Controllers/AdminController.cs:326-396` (`CreateWebhook`)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Controllers/AdminControllerTests.cs` (CreateWebhook section, 282-363)

**Interfaces:**
- Consumes: `IHumanService.GetByIdAsync`, `IUserPurgeService.PurgeAsync(string)`, `HumanKind`.
- Produces: `private async Task<IActionResult> CreateHumanAsync(Human human, string conflictError)` holding pre-check, create, rollback and the two 502 messages. `CreateWebhook` builds the `Human` and calls it. Part 4 reuses it.

- [ ] **Step 1: Write the failing test** `CreateWebhookLeavesARowAnotherRequestWrote`:
  - The pre-check (`ExistsAsync`) answers false.
  - `CreateAsync` throws `HttpRequestException`.
  - `GetByIdAsync("http://test")` returns `Human { Id = "http://test", Name = "Someone else", Type = "webhook" }`.
  - Assert a 502, then `_userPurgeService.Verify(p => p.PurgeAsync(It.IsAny<string>()), Times.Never)` and `_humanService.Verify(s => s.DeleteUserAsync(It.IsAny<string>()), Times.Never)`.
- [ ] **Step 2: Update `CreateWebhookStillUndoesAHalfWriteItCanSee`.** The re-read now comes from `GetByIdAsync` returning `Name = "Test", Type = "webhook"`; verify `PurgeAsync("http://test")` once. Keep `CreateWebhookStillAnswers502WhenTheCleanupCannotReachPoracle`, but make `GetByIdAsync` the call that throws.
- [ ] **Step 3: Run, expect the new test to FAIL** (today's rollback deletes it).
- [ ] **Step 4: Implement `CreateHumanAsync`.** On `HttpRequestException`:
  - Re-read with `GetByIdAsync(human.Id)`.
  - Purge only when the row's `Type` equals `human.Type` (OrdinalIgnoreCase) and its `Name` equals `human.Name` (Ordinal), via `_userPurgeService.PurgeAsync(human.Id)` so grants go too.
  - A re-read that throws keeps today's "could not be asked" 502.
  - A mismatched row answers 502 "Poracle rejected it, and a record with this id already exists. Nothing was changed." and touches nothing.
- [ ] **Step 5: Run the CreateWebhook tests, expect PASS.**
- [ ] **Step 6: Commit** `fix: a failed webhook create never deletes a row another request wrote (#1028)` with a CHANGELOG Fixed entry. Open the PR to `develop` (closes #1028).

---

## Part 2 — Login refuses destination rows

Branch `feat/1025-login-people-only`. Merges before Parts 4 and 6.

### Task 2.1: Server refusals on every login path and `/me`

**Files:**
- Modify: `Applications/Pgan.PoracleWebNet.Api/Controllers/AuthController.cs`:
  - DiscordCallback after 217-221
  - TelegramVerify after 616-623
  - `BuildOidcUserInfoAsync` after 1217-1221
  - `Me` after 815-818
- Test: `Tests/Pgan.PoracleWebNet.Tests/Controllers/AuthControllerMeTests.cs`; new `Tests/Pgan.PoracleWebNet.Tests/Controllers/AuthControllerOidcTests.cs`

**Interfaces:**
- Consumes: `HumanKind.IsPerson`.
- Produces:
  - DiscordCallback and the OIDC callback redirect `/login#error=destination_account`.
  - `BuildOidcUserInfoAsync` returns `(null, "destination_account")`, so OIDC refresh aborts as `account_inactive` and answers its existing 401.
  - TelegramVerify answers `StatusCode(403, new { error = "This id is a channel or webhook in Poracle, not a person.", code = "destination_account" })`.
  - `Me` answers `Unauthorized(new { error = ..., code = "destination_account" })` when `!IsImpersonating && !HumanKind.IsPerson(human.Type)`.
  - A new `[LoggerMessage]` `LogDestinationLoginRefused(ILogger, string method)`, Information.

- [ ] **Step 1: Write the failing tests** in `AuthControllerMeTests`:
  - `MeRefusesASessionOnADestinationRow`: human `Type = "discord:channel"`, not impersonating. Expect `UnauthorizedObjectResult` with `GetProp(result.Value, "code") == "destination_account"`.
  - `MeAcceptsANullType`: `Type = null`, expect `Ok`.
  - `MeKeepsAnImpersonationOfAChannel`: `SetupImpersonatingUser`, `Type = "discord:channel"`, expect `Ok`.
- [ ] **Step 2: Write `AuthControllerOidcTests`.** Construct the controller as `AuthControllerMeTests` does, with `IOidcClient` and `IOidcSessionService` mocked so `OidcCallback` reaches `BuildOidcUserInfoAsync`.
  - `OidcCallbackRefusesADestinationRow`: expect a redirect ending `#error=destination_account`.
  - `OidcCallbackStillSignsInANullTypePerson`: expect a redirect to the token callback.
  - `OidcRefreshEndsADestinationSession`: expect a 401 and `AbortRotationAsync(ticket, "account_inactive")` called once.
- [ ] **Step 3: Run, expect FAIL.**
- [ ] **Step 4: Implement the four checks.** Each sits directly after the existing `human == null` branch and calls `LogDestinationLoginRefused`. Discord and Telegram have no unit seam (inline `HttpClient`, HMAC); the live walk in Task 5.4 covers them.
- [ ] **Step 5: Run both test classes, expect PASS.**
- [ ] **Step 6: Commit** `feat: refuse sign-in to a Poracle row that isn't a person (#1025)`.

### Task 2.2: SPA shows the refusal

**Files:**
- Modify: `ClientApp/src/app/modules/auth/login.component.ts:194-210` (`errorKeys`) and `:256-270` (`handleTelegramAuth`)
- Modify: `ClientApp/src/app/modules/auth/callback.component.ts:35-46` (`messageKeys`)
- Modify: `ClientApp/src/app/core/services/auth.service.ts:118-151` (`handleTokenFromCallback`): a `/me` 401 with `code: 'destination_account'` clears the session and navigates to `/login#error=destination_account`, exactly as `account_disabled` does at :138
- Modify: all 11 locale files, new `AUTH.ERR_DESTINATION_ACCOUNT` ("This sign-in belongs to a channel or webhook in Poracle, not a person. Sign in with your own account.")
- Test: `login.component.spec.ts:345-365` ("still names every code the API sends"), `auth.service.spec.ts`

- [ ] **Step 1: Write the failing tests.**
  - Add `destination_account: 'AUTH.ERR_DESTINATION_ACCOUNT'` to the spec's code map.
  - New `it('shows the destination refusal from a Telegram 403')`: `loginWithTelegram` errors with `{ status: 403, error: { code: 'destination_account' } }`, so `error()` is `'AUTH.ERR_DESTINATION_ACCOUNT'`.
  - New `it('keeps the generic Telegram message for any other failure')`.
  - In `auth.service.spec.ts`: `it('sends a destination row back to login with its reason')`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `handleTelegramAuth` reads `err?.error?.code === 'destination_account'`; anything else keeps `AUTH.ERR_TELEGRAM_FAILED`.
- [ ] **Step 4: Run the specs and `locale-parity.spec.ts`, expect PASS.**
- [ ] **Step 5: Commit** `feat: tell a destination sign-in why it was refused (#1025)`. Add a CHANGELOG Security entry and open the PR.

---

## Part 3 — Users and Destinations

Branch `feat/1025-destinations-page`.

### Task 3.1: Stamp `kind` and refuse a non-person grantee

**Files:**
- Modify: `AdminController.cs`:
  - `GetAllUsers` (44-71) and `GetManagedWebhooks` (106-119) projections: add `Kind = HumanKind.KindOf(h.Type)`
  - `AddWebhookDelegate` (636-640): the grantee check becomes `GetByIdAsync` + `IsPerson`
- Modify: `ClientApp/src/app/core/models/index.ts:289-302` (`AdminUser` gains `kind: 'person' | 'destination'`)
- Test: `AdminControllerTests.cs`

- [ ] **Step 1: Write the failing tests.**
  - `GetAllUsersStampsEachRowsKind`: rows `discord:user`, `webhook`, `telegram:group`, `x:unknown`, null. Read `Kind` off each anonymous row via the existing reflection helper; expect `person, destination, destination, destination, person`.
  - `AddWebhookDelegateRefusesAChannelAsGrantee`: grantee `Type = "discord:channel"`, expect `BadRequest` "userId does not name a person."
  - `AddWebhookDelegateStillGrantsADiscordUser`: grantee `Type = "discord:user"`, expect `Ok`.
- [ ] **Step 2: Update `GivenWebhookAndUserExist` (50-55)** to set up `GetByIdAsync(userId)` returning `Type = "discord:user"`. Run, expect the new tests to FAIL.
- [ ] **Step 3: Implement.** The target check stays `webhook`-only here; Part 6 widens it.
- [ ] **Step 4: Run AdminControllerTests, expect PASS.**
- [ ] **Step 5: Commit** `feat: the admin list says whether each row is a person or a destination (#1025)`.

### Task 3.2: Shared list state

**Files:**
- Create: `ClientApp/src/app/modules/admin/admin-list-state.ts`, `admin-list-state.spec.ts`
- Modify: `admin-users.component.ts` to use it (search, status filter, sort, paging)

**Interfaces:**
- Produces: `class AdminListState<T extends AdminUser>`.
  - Constructor `(rows: Signal<T[]>, storagePrefix: string, sortValue: (row: T, column: string) => string | number)`.
  - Writable signals: `searchTerm`, `statusFilter: Signal<StatusFilter>`, `typeFilter: Signal<string | 'all'>`, `sortActive`, `sortDirection`, `pageIndex`, `pageSize`.
  - Computed: `filtered`, `page`, `typesPresent: Signal<string[]>`.
  - Search covers `id`, `name` and `notesLabel(notes)`.
  - `pageIndex`, `pageSize` and `typeFilter` persist to sessionStorage under `${storagePrefix}pageIndex` / `pageSize` / `type`, each read and write in try/catch.

This replaces the spec's "one list component": the two pages' columns and actions differ too much for one template. Shared state still means a filter, sort or paging fix reaches both pages.

- [ ] **Step 1: Write the failing spec.**
  - `filters by type and keeps the choice per prefix`
  - `searches notes as well as name and id`
  - `survives sessionStorage throwing`
  - `typesPresent lists only types in the rows`
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement it, then move AdminUsersComponent onto it with prefix `admin_`** (keeps today's `admin_pageIndex` and `admin_pageSize`). Users' rows are `allUsers().filter(u => u.kind === 'person')`, replacing `:68`.
- [ ] **Step 4: Run `admin-users.component.spec.ts` and the new spec, expect PASS.** Fix any fixture missing `kind`: the `row()` factory gets `kind: 'person'`.
- [ ] **Step 5: Commit** `refactor: share admin list filtering, sorting and paging (#1025)`.

### Task 3.3: Rename Webhooks to Destinations (route, nav, files, keys)

**Files:**
- `git mv` in `modules/admin/`:
  - `admin-webhooks.component.*` → `admin-destinations.component.*` (class `AdminDestinationsComponent`, selector `app-admin-destinations`)
  - `add-webhook-dialog.component.*` → `add-destination-dialog.component.*`
  - `webhook-delegates-dialog.component.*` → `destination-delegates-dialog.component.*`
  - Same for their specs.
- Modify: `app.routes.ts:149-155`:
  - `admin/destinations` loads `AdminDestinationsComponent`, title `NAV.DESTINATIONS`.
  - Add `{ path: 'admin/webhooks', pathMatch: 'full', redirectTo: 'admin/destinations' }`.
- Modify: `app.ts:213`: `icon: 'campaign'`, `label: 'NAV.DESTINATIONS'`, `route: '/admin/destinations'`.
- Modify: 11 locale files, `ClientApp/translate.js` (244-245, 614-615, 984-985, 1354-1355), `failure-message-parity.spec.ts:72-74` (paths), `sentence-case-labels.spec.ts:26`.

Key renames (old → new; values carry over, then reword per Step 3):

| Old | New |
|---|---|
| `NAV.WEBHOOKS` | `NAV.DESTINATIONS` |
| `ADMIN.WEBHOOKS_TITLE` / `_DESC` / `_ADMINS_NOTE` | `ADMIN.DESTINATIONS_TITLE` / `_DESC` / `_ADMINS_NOTE` |
| `ADMIN.ADD_WEBHOOK` | `ADMIN.ADD_DESTINATION` |
| `ADMIN.SEARCH_WEBHOOKS`, `ADMIN.FILTER_WEBHOOK_PLACEHOLDER` | `ADMIN.SEARCH_DESTINATIONS`, `ADMIN.FILTER_DESTINATION_PLACEHOLDER` |
| `ADMIN.BLOCK_WEBHOOK` / `UNBLOCK_WEBHOOK` / `DELETE_WEBHOOK` | `ADMIN.BLOCK_DESTINATION` / `UNBLOCK_DESTINATION` / `DELETE_DESTINATION` |
| `ADMIN.NO_WEBHOOKS` / `NO_WEBHOOKS_YET` / `CONFIRM_DELETE_WEBHOOK` | `ADMIN.NO_DESTINATIONS` / `NO_DESTINATIONS_YET` / `CONFIRM_DELETE_DESTINATION` |
| `ADMIN.SNACK_FAILED_DELETE_WEBHOOK` / `SNACK_WEBHOOK_DELETED` / `SNACK_FAILED_ADD_WEBHOOK` / `SNACK_WEBHOOK_ADDED` | `ADMIN.SNACK_FAILED_DELETE_DESTINATION` / `SNACK_DESTINATION_DELETED` / `SNACK_FAILED_ADD_DESTINATION` / `SNACK_DESTINATION_ADDED` |
| `ADMIN.SNACK_URL_COPIED`, `ADMIN.COLUMN_URL` | `ADMIN.SNACK_ID_COPIED`, `ADMIN.COLUMN_ID` (My Webhooks uses `COLUMN_ID` too) |
| `ADMIN.WEBHOOK_NAME` / `_NAME_PLACEHOLDER` / `_NAME_REQUIRED` | `ADMIN.DESTINATION_NAME` / `_NAME_PLACEHOLDER` / `_NAME_REQUIRED` |

Leave alone: `ADMIN.WEBHOOK_URL*` (they stay the webhook URL field's labels in Part 4), `NAV.MY_WEBHOOKS`, `NAV.GROUP_WEBHOOKS`, `ADMIN.MY_WEBHOOKS_*`, `SNACK_FAILED_LOAD_WEBHOOKS`, `SNACK_FAILED_SWITCH_WEBHOOK` (all Part 6), and every shared key the admin explorer listed (`COLUMN_NAME`, `STATUS_*`, `MANAGE_ALARMS`, …).

- [ ] **Step 1: Write the failing spec** in `app.routes.spec.ts` (or the existing routes spec): `it('sends the old webhooks address to destinations')`, so `/admin/webhooks` resolves to `/admin/destinations`.
- [ ] **Step 2: Move the files and rename the keys** in all 11 locales and every referencing `.ts`/`.html`/spec with one scripted pass (python `-I`, UTF-8 read/write; never PowerShell `Get-Content`). Then `grep -rn` each old key and expect no hits outside CHANGELOG.
- [ ] **Step 3: Reword the English values.** "Destinations"; "Add destination"; `DESTINATIONS_DESC` = "Webhooks, channels and groups that Poracle posts alerts into. Nobody signs in as these; you manage their alarms by opening them."; `COLUMN_ID` = "ID". Translate the reworded values into the other 10 locales by hand.
- [ ] **Step 4: Run** `npx jest src/app/core/i18n src/app/modules/admin src/app/app` and `npm run build`. Expect PASS.
- [ ] **Step 5: Commit** `feat: Admin > Webhooks becomes Destinations (#1025)`.

### Task 3.4: Destinations page content

**Files:**
- Modify: `admin-destinations.component.ts/.html/.scss`, `destination-delegates-dialog.component.html`
- Create: `ClientApp/src/app/shared/utils/human-kind.ts`, `human-kind.spec.ts`
- Locales: new keys for `ADMIN.TYPE_WEBHOOK`, `TYPE_DISCORD_CHANNEL`, `TYPE_DISCORD_THREAD`, `TYPE_TELEGRAM_CHANNEL`, `TYPE_TELEGRAM_GROUP`, `TYPE_TELEGRAM_TOPIC`, `TYPE_OTHER`, `TYPE_ALL`, `COLUMN_TYPE`, `FILTER_BY_TYPE`, `COPY_ID`, `BLOCKED_CHANNEL_TOOLTIP`, `DELETE_DESTINATION_NOTE`, `USERS_DESTINATIONS_LINK`, `DISABLED_SINCE` (if Users' key isn't reusable)

**Interfaces:**
- Produces (`human-kind.ts`):
  - `MENTION_CAPABLE: ReadonlySet<string>` (lowercase, three types)
  - `mentionCapable(type: string | null | undefined): boolean`
  - `kindLabelKey(type: string | null): string` (known type → `ADMIN.TYPE_*`, else `ADMIN.TYPE_OTHER`)
  - `kindIcon(type: string | null): string` (`webhook`, `tag`, `send`, `help_outline`)

- [ ] **Step 1: Write the failing specs.**
  - `human-kind.spec.ts`:
    - `labels every known type and falls back to Other`
    - `mention is Discord and webhook only`
  - `admin-destinations.component.spec.ts`:
    - `lists every destination row and no person` (fixture rows of kinds person and destination, including type `x:unknown`)
    - `shows the raw type for an unknown kind`
    - `offers type chips only for types present`
    - `searches notes`
    - `shows managers only on webhook rows`
  - `admin-users.component.spec.ts`: `lists people only, including a null type`.
  - `destination-delegates-dialog.component.spec.ts`: `offers only people as delegates`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
  - Destinations rows: `kind === 'destination'`, through `AdminListState` with prefix `admin_destinations_`.
  - Columns: `['name', 'type', 'id', 'enabled', 'disabledSince', 'delegates', 'actions']`. Name shows the notes beneath; type shows icon and label.
  - ID: `truncateUrl` only for `webhook` rows, a copy button with `ADMIN.COPY_ID` on every row.
  - Type chips are `mat-chip-listbox` over `typesPresent()`.
  - Managers cell and delegates button: `@if (row.type === 'webhook')` until Part 6.
  - Block tooltip uses `BLOCKED_CHANNEL_TOOLTIP` on non-webhook rows; the delete confirm appends `DELETE_DESTINATION_NOTE` on non-webhook rows.
  - Delegate candidates are `allUsers.filter(u => u.kind === 'person')` (was :104).
  - Users' page description gains a `routerLink="/admin/destinations"` using `USERS_DESTINATIONS_LINK`.
- [ ] **Step 4: Run** the admin specs, `locale-parity`, `npm run build` and lint. Expect PASS.
- [ ] **Step 5: Commit** `feat: Destinations lists every channel, group and webhook with its type (#1025)`.

### Task 3.5: Docs for Part 3

**Files:**
- `docs/features/webhooks.md` (title "Destinations and delegates"; :12, :20, :27 Admin → Destinations; a "Kinds" table)
- `docs/troubleshooting.md:668`, `docs/architecture/frontend.md:40`, `docs/index.md:48`
- `mkdocs.yml:102` ("Destinations & Delegates")
- `CLAUDE.md` (new "### People vs destinations" section under Key Patterns: the rule, `HumanKind`, `kind` on the admin list, that `HumanKind.Destinations` alone grants power, and that the delegates table's `webhook_id` holds a destination id; also update the CLAUDE.md:490 row)
- Screenshots `docs/screenshots/sidenav.png`, `mobile-sidenav.png`, `admin-webhooks.png` → retake as `admin-destinations.png` under the anonymisation rule
- CHANGELOG

- [ ] **Step 1: Edit the docs; retake the screenshots** (neutral name, avatar and title per memory `feedback_screenshot_anonymisation`).
- [ ] **Step 2: `mkdocs build --strict`** if available locally, else check that the links resolve by grep.
- [ ] **Step 3: Commit** `docs: describe the Destinations page (#1025)` and open the PR.

---

## Part 4 — Creating a destination

Branch `feat/1025-destination-create`. Requires Parts 1 and 2 merged.

### Task 4.1: `POST /api/admin/destinations`

**Files:**
- Modify: `AdminController.cs` (new action beside `CreateWebhook`; new nested `public record CreateDestinationRequest(string Type, string Id, string Name, string? Notes);`)
- Modify: `Core/Pgan.PoracleWebNet.Core.Models/HumanKind.cs` (add `static string? ValidateNewId(string type, string id)`, returning an error sentence or null)
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/HumanService.cs:127-143` (`SerializeHumanForCreate` sends `notes` when non-null), **only if** Step 0 confirms the field
- Test: `AdminControllerTests.cs`, `HumanKindTests.cs`, `HumanServiceTests.cs`

- [ ] **Step 0: Gate on the schema.** Read `V2CreateHumanBody` from the dev-01 5.4.0 bed's `/openapi.json` (memory `project_poracleng_next_testbed`).
  - If it declares `notes`, keep `Notes` in the request and send it.
  - If not, drop `Notes` from `CreateDestinationRequest` and from this plan's dialog (Task 4.2). Sending an undeclared field is a 422 under `additionalProperties: false`.
- [ ] **Step 1: Write the failing tests.**
  - `HumanKindTests`:
    - `[Theory] AcceptsEachKindsRealId`: `("discord:channel","123456789012345678")`, `("discord:thread","123456789012345678")`, `("telegram:channel","-1001234567890")`, `("telegram:group","-1234567890")`, `("telegram:topic","-1001234567890:42")`. Each answers null.
    - `[Theory] RefusesTheImpossible`: `("discord:channel","12a")`, `("discord:channel", 21 digits)`, `("telegram:group","1234")`, `("telegram:topic","-100:0")`, `("telegram:topic","-100")`, `("telegram:topic","100:5")`, and `("discord:channel","123\u0007")`. Each answers non-null.
  - `AdminControllerTests`:
    - `CreateDestinationCreatesADiscordChannel`: Human sent with `Type = "discord:channel"`, `Id`, `Name`.
    - `CreateDestinationRefusesAPersonType` (`discord:user` → 400).
    - `CreateDestinationRefusesAnUnknownType` (`x:unknown` → 400).
    - `CreateDestinationRefusesWhileImpersonating` (Forbid).
    - `CreateDestinationNamesTheRowAlreadyThere` (409 body carries `type` and `name`).
    - `CreateDestinationRollsBackOnlyItsOwnRow` (the Task 1.2 shape through this endpoint).
  - `HumanServiceTests`: `CreateAsyncSendsNotes` (only if Step 0 kept it), using the `CreateAsyncSendsEnabledAndAdminDisableAsBooleans` callback pattern.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
  - `ValidateNewId`:
    - Discord: `^[0-9]{1,20}$`.
    - Telegram channel/group: `^-[0-9]{1,19}$`.
    - Topic: `^(-[0-9]{1,19}):([1-9][0-9]{0,9})$`.
    - Webhook: today's `CreateWebhook` rule (non-blank, `http(s)://`).
    - Any `char.IsControl` refuses for every kind.
  - The action:
    1. Admin check, then not impersonating.
    2. `HumanKind.IsKnownDestination(request.Type)`.
    3. Name and id trimmed and non-blank, no control characters.
    4. `ValidateNewId`.
    5. The 409 pre-check returns `{ error, type, name }` from `GetByIdAsync`.
    6. Then `CreateHumanAsync` (Task 1.2).
  - `CreateWebhook` keeps its route and delegates to the same path with type `webhook`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: create channels, threads, groups and topics as destinations (#1025)`.

### Task 4.2: Add dialog by kind

**Files:**
- Modify: `add-destination-dialog.component.html/.scss`, `AddDestinationDialogComponent` in `admin-destinations.component.ts`, `core/services/admin.service.ts` (new `createDestination(req: { type: string; id: string; name: string; notes?: string }): Observable<void>` → `POST /api/admin/destinations`), and `admin-destinations.component.ts` `openAddDialog`
- Locales: `ADMIN.WHAT_KIND`, per-kind id labels and placeholders (`ID_DISCORD`, `ID_TELEGRAM_CHAT`, `ID_TELEGRAM_TOPIC`), refusal texts (`ID_DIGITS_ONLY`, `ID_NEGATIVE_CHAT`, `ID_TOPIC_POSITIVE`), `ID_UNCHECKED_NOTE`, `DESTINATION_EXISTS` (with `{{type}}` and `{{name}}`), `DESTINATION_NOTE_LABEL` (only if Task 4.1 Step 0 kept notes)
- Test: `destination-delegates-dialog.component.spec.ts` (holds the add-dialog tests today, :126-151) and `admin.service.spec.ts`

- [ ] **Step 1: Write the failing specs.**
  - `asks for a URL for a webhook and an id for a Discord channel`
  - `joins a Telegram topic's chat and topic ids`: chat `-1001234567890`, topic `42` gives id `-1001234567890:42`
  - `refuses a positive Telegram chat id`
  - `keeps the name and clears the id when the kind changes`
  - `shows who already holds the id on a 409`
  - Admin service: `createDestination posts to /api/admin/destinations`
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
  - A `mat-radio-group` over the six kinds (labels from `kindLabelKey`) and a `name` control.
  - `id` validators swap on kind change, mirroring `ValidateNewId`; the topic uses `chatId` and `topicId` controls.
  - Closing yields the request.
  - The parent calls `createDestination`. Errors go through `serverReason(err, { allow5xx: [502] })`; a 409 uses `DESTINATION_EXISTS` with the body's `type` and `name`.
- [ ] **Step 4: Run the specs, `npm run build` and lint. Expect PASS.**
- [ ] **Step 5: Live gate.** On the dev-01 5.4.0 bed (`project_api_smoke_harness`), create one of each of the six kinds through the API. Add a pokemon alarm to each by impersonating, then confirm delivery for the Discord channel and thread. Telegram needs a bot on the bed; if there isn't one, record that delivery for those kinds wasn't verified. Delete the seeded rows; tell the user first that deletes DM the owner.
- [ ] **Step 6: Commit** `feat: the Add destination dialog asks what kind first (#1025)`. Update docs (`webhooks.md` "Adding a destination" with a per-kind id table) and the CHANGELOG, then open the PR with the live-gate results in its body.

---

## Part 5 — Managing a destination

Branch `feat/1025-destination-sessions`.

### Task 5.1: Mention by type

**Files:**
- Modify the nine edit dialogs' `.ts` and `.html`: pokemon (`.ts:121`, `.html:311`), raid (87/85), quest (102/73), invasion (76/70), lure (64/50), nest (66/50), gym (67/49), max-battle (94/75), fort-change (76/57, plus the heading at `.html:47`)
- Test: the six existing `describe('Mention field')` blocks (raid, fort-change, max-battle, lure, pokemon, quest); add the same block to the invasion, nest and gym edit specs

**Interfaces:**
- Consumes: `mentionCapable` from `shared/utils/human-kind.ts`.
- Produces: in each dialog, `readonly showMention = this.auth.isImpersonating() && mentionCapable(this.auth.user()?.type)` replacing `isWebhook`. Templates use `@if (showMention)`; fort-change's heading becomes `templatesEnabled() || showMention`. The dead `isWebhook` in the nine add dialogs is left alone.

- [ ] **Step 1: Write the failing tests.** In each of the nine specs:
  - `it('is offered while managing a Discord channel')` (impersonating, `user: () => ({ type: 'discord:channel' })`)
  - `it('is hidden while managing a Telegram group')`
  - `it('is hidden on a personal alarm')`

  Add `user` to the raid and max-battle mocks.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** across all nine. Confirm with `grep -rn "isWebhook" modules/*/*-edit-dialog*`, which should return no hits.
- [ ] **Step 4: Run** the nine specs and `alarm-dialog-parity.spec.ts` (unchanged; it reads control names, not conditions). Expect PASS.
- [ ] **Step 5: Commit** `feat: show Mention only where a mention can render (#1025)`.

### Task 5.2: Banner names the kind; exit returns to where it started

**Files:**
- Modify: `core/services/token-store.service.ts`:
  - `setImpersonationReturn(route: string): void`
  - `takeImpersonationReturn(): string` (returns the stored route or `'/admin'` and removes it)
  - key `poracle_impersonation_return`, also cleared by `clear()`
- Modify: `core/services/auth.service.ts`:
  - `impersonate(token: string, returnTo: string)`
  - `stopImpersonating` navigates to `tokenStore.takeImpersonationReturn()` (was `['/admin']` at :278), keeping `{ state: ADMIN_RETURN_STATE }`
- Modify: `core/interceptors/error.interceptor.ts:118-122`, same change
- Modify the callers:
  - `admin-users.component.ts:194-203` → `'/admin/users'`
  - `admin-destinations.component.ts` `manageAlarms` → `'/admin/destinations'`
  - `my-webhooks.component.ts:47-58` → `'/my-webhooks'` (Part 6 renames it)
- Modify: `app.html:5-13`: the banner uses `BANNER.MANAGING_KIND` ("Managing {{kind}} {{name}}. Changes you make are saved to its alerts.") when `user().type` is not a person type, else today's `BANNER.VIEWING_AS`
- Modify: `core/services/test-alert.service.ts:51`: while impersonating a non-person type, show `TEST_ALERT.SUCCESS_DESTINATION` ("Test alert sent. It posts in {{name}}.") instead of `TEST_ALERT.SUCCESS`
- Locales: `BANNER.MANAGING_KIND`, `TEST_ALERT.SUCCESS_DESTINATION`
- Test: `auth.service.spec.ts:400-417, 547-556`; `error.interceptor.spec.ts:68-80`; `session-401-burst.spec.ts:67-76`; `token-store.service.spec.ts:141-155`; `app.spec.ts:290-331`; `test-alert.service.spec.ts`

- [ ] **Step 1: Write and update the failing tests.**
  - `auth.service.spec`: `'returns a delegate to the page they started from'` (`impersonate(t, '/my-webhooks')`, then `stopImpersonating()` navigates `['/my-webhooks']`).
  - `'falls back to /admin when nothing was recorded'`.
  - `session-401-burst.spec.ts:76`: change the asserted route to the recorded delegate origin. This is Review Focus 3, and the old assertion encoded the defect.
  - `token-store.service.spec`: `'forgets the return route when the session is cleared'`.
  - `app.spec`: `'names a Discord channel in the banner'`.
  - `test-alert.service.spec`: `'says where a destination's test alert went'`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** `kind` in the banner is the translated `kindLabelKey(user().type)`.
- [ ] **Step 4: Run the listed specs, `locale-parity` and `npm run build`. Expect PASS.**
- [ ] **Step 5: Commit** `feat: managing a destination says what it is and exits back where it started (#1025)`. Add the CHANGELOG and open the PR.

---

## Part 6 — Delegation to destinations

Branch `feat/1025-destination-delegation`. Requires Part 2 merged.

### Task 6.1: Destination directory

**Files:**
- Create: `Applications/Pgan.PoracleWebNet.Api/Services/DestinationDirectory.cs`
- Modify: `Configuration/ServiceCollectionExtensions.cs` (register scoped beside `UserRoleResolver` at :210)
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/DestinationDirectoryTests.cs`

**Interfaces:**
- Produces:
  - `public interface IDestinationDirectory` with `Task<IReadOnlyList<Human>> GetAsync()` and `void Invalidate()`.
  - `public sealed class DestinationDirectory(IPoracleHumanProxy humanProxy, IMemoryCache cache) : IDestinationDirectory`.
  - Cache key `"destinations:directory"`, TTL 1 minute.
  - A `private static readonly SemaphoreSlim Gate = new(1, 1)` double-checked like `PoracleServerProfileService.GetAsync` (:58-84).
  - One `ListHumansAsync(type: t)` per `HumanKind.Destinations` entry via `Task.WhenAll`.
  - Any exception propagates and nothing is cached.

- [ ] **Step 1: Write the failing tests.**
  - `ReadsEachDestinationTypeOnce`: two calls, so `ListHumansAsync` runs six times total.
  - `NeverAsksForPeople`: verify no call with `discord:user`.
  - `ADegradedReadIsNotCached`: first call throws, second succeeds and makes six fresh calls.
  - `InvalidateDropsTheCachedList`.
- [ ] **Step 2: Run, expect FAIL. Step 3: Implement. Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: a cached directory of every registered destination (#1025)`.

### Task 6.2: Grants may target any destination

**Files:**
- Modify: `AdminController.cs` `AddWebhookDelegate` (631): `webhook is null || !HumanKind.IsKnownDestination(webhook.Type)` → BadRequest "webhookId does not name a destination."
- Modify: `AdminController.cs` `DeleteUser` (744-752), and the create success paths of `CreateHumanAsync`: call `IDestinationDirectory.Invalidate()`. Block and unblock (`EnableUser`/`DisableUser`) call it too.
- Modify: `admin-destinations.component.html`: drop the `@if (row.type === 'webhook')` from Task 3.4, so Managers and the delegates button show on every row.
- Test: `AdminControllerTests.cs` (rename `AddWebhookDelegateRejectsAnIdThatIsNotAWebhook`, 707-717), `admin-destinations.component.spec.ts`

- [ ] **Step 1: Write the failing tests.**
  - `AddWebhookDelegateGrantsADiscordChannel`
  - `AddWebhookDelegateRefusesAnUnknownType` (`x:unknown`)
  - `AddWebhookDelegateRefusesAPersonAsTarget`
  - `CreatingADestinationRefreshesTheDirectory`
  - SPA: `shows managers on every destination row`
- [ ] **Step 2: Run, expect FAIL. Step 3: Implement. Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: delegate any destination, not only webhooks (#1025)`.

### Task 6.3: Resolver widens to destinations and the opt-in source

**Files:**
- Modify: `Applications/Pgan.PoracleWebNet.Api/Services/UserRoleResolver.cs`:
  - constructor: drop `IHumanService` if unused afterwards; add `IDestinationDirectory`, `ISiteSettingService`
  - parse block 170-204
  - `CanonicaliseAsync` 251-289
  - `UserRoles` at L19: `ManagedWebhooks` → `ManagedDestinations`
- Test: `Tests/Pgan.PoracleWebNet.Tests/Services/UserRoleResolverTests.cs`. `ArrangeDelegate` (279-295) stubs `IDestinationDirectory.GetAsync` instead of `GetWebhooksAsync`, and gains an optional `channels` / `telegramChannels` argument.

**Interfaces:**
- Produces:
  - `public readonly record struct UserRoles(bool IsAdmin, string[]? ManagedDestinations, bool Resolved = true)`.
  - `public const string PoracleChannelsSettingKey = "delegation_poracle_channels"` on `UserRoleResolver`.
  - Canonicalisation: `byId` over every directory row. `byName` over rows whose type is in `HumanKind.NameAddressable` only (upstream resolves names over those four).
  - Channel ids from `admin.discord.channels` and `admin.telegram.channels` are added only when `GetBoolAsync(PoracleChannelsSettingKey)` is true, and are matched by id only.
  - `users: true` is ignored.

- [ ] **Step 1: Write the failing tests.**
  - `AnExistingWebhookGrantStillResolves` (Review Focus 4): the `TeamHarmonyRares` fixture by name and by id, identical to today.
  - `ALocalGrantOnAChannelResolves`
  - `AWebhookAdminsNameThatMatchesAChannelResolves`
  - `PoraclesChannelListsAreIgnoredWhileTheSettingIsOff`
  - `PoraclesChannelListsCountOnceTheSettingIsOn`: `discord.channels` holds a registered id, an unregistered id and a duplicate; expect only the registered id, once.
  - `TelegramChannelListsCountOnceTheSettingIsOn`
  - `ANameNeverResolvesToAThread`: a thread row whose name equals a grant; expect it dropped.
  - `ADegradedDirectoryIsNotCached` (as `GetWebhooksAsync` throwing today).
  - `TheSettingFailingToReadCountsAsOff`: `GetBoolAsync` throws; expect Resolved true, channel lists ignored.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Then rename `ManagedWebhooks` → `ManagedDestinations` at every C# site the explorer listed: `ImpersonationAuthority.cs:31,82`, `JwtService.cs:129-131` (claim string stays `"managedWebhooks"`), `BaseApiController.cs:31` (property renamed, still reading `"managedWebhooks"`), `AuthController.cs` (`ResolveManagedWebhooksAsync` → `ResolveManagedDestinationsAsync` and its callers), `AdminController.cs:77,83,96,686`, `UserInfo.cs:25`, and the tests. Grep `ManagedWebhooks` and expect no hits.
- [ ] **Step 4: Run** UserRoleResolverTests, ImpersonationAuthorityTests, ImpersonationRoleProbeTests, AuthControllerMeTests, AdminControllerTests and ImpersonationPipelineTests. Expect PASS.
- [ ] **Step 5: Commit** `feat: delegation resolves every destination, and Poracle's channel admins on request (#1025)`.

### Task 6.4: Enforcement, setting, parity

**Files:**
- Modify: `AdminController.cs` `ImpersonateById` (667-718): after loading `human`, when `!this.IsAdmin && !HumanKind.IsKnownDestination(human.Type)` → `Forbid()`.
- Modify: `Program.cs`:
  - new `static string TestAlertPartitionKey(HttpContext)`: `impersonatedBy` claim → `"user:" + value`, else `UserOrIpPartitionKey`
  - the `"test-alert"` policy (268-277) uses it
- Modify: `SettingsController.cs` Upsert (254-349): inject `IUserRoleResolver`; after writing `UserRoleResolver.PoracleChannelsSettingKey`, call `InvalidateAll()`.
- Modify: `admin-settings.component.ts` Administration group (400-418): add the `delegation_poracle_channels` entry shaped like `enable_roles`, with keys `ADMIN_SETTINGS.DELEGATION_PORACLE_CHANNELS_LABEL` ("Follow Poracle's channel admins") and `_DESC` ("Let anyone PoracleNG names as a channel admin manage that channel here too. Guild-wide entries cover only channels registered on this site.").
- Test:
  - `AdminControllerTests`
  - new `Tests/.../Configuration/TestAlertPartitionKeyTests.cs`
  - `SettingsControllerTests`
  - `admin-settings.groups.spec.ts`
  - `admin-settings.truth.spec.ts` (`'delegation_poracle_channels is off when unset'`)
  - new `Tests/.../Controllers/DelegationParityTests.cs`

- [ ] **Step 1: Write the failing tests.**
  - `ImpersonateByIdRefusesADelegateWhoseTargetIsAPerson`: the resolver lists the id, but the live row is `discord:user`.
  - `ImpersonateByIdLetsADelegateManageAChannel`.
  - Fix `ImpersonateByIdRefusesADelegateWhoseGrantWasRevoked` (550) to set the resolver mock up explicitly (it passes today only by `default(UserRoles)`).
  - `TestAlertsShareOneBucketPerImpersonator`: two contexts with different `userId` and the same `impersonatedBy` give the same key.
  - `SavingTheChannelSettingDropsCachedRoles`.
  - `DelegationParityTests.ADelegatedChannelIsTheSameOnEverySurface`: one `UserRoleResolver` over mocked sources granting `discord:channel` "c1". Expect `/my-destinations` to list c1, `ImpersonateById("c1")` to be Ok, and `Me().ManagedDestinations` to contain c1.
- [ ] **Step 2: Run, expect FAIL. Step 3: Implement. Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: delegates reach destinations only, and test alerts share one bucket per person (#1025)`.

### Task 6.5: My Destinations

**Files:**
- Modify: `AdminController.cs:82` (`GetManagedWebhooks`): add `[HttpGet("my-destinations")]` beside the existing `[HttpGet("my-webhooks")]` and rename the method `GetManagedDestinations`.
- `git mv` `my-webhooks.component.*` → `my-destinations.component.*` (`MyDestinationsComponent`).
- Routes:
  - `app.routes.ts:180-185` → `my-destinations`, plus a `my-webhooks` redirect
  - `app.ts:225` → `label: 'NAV.MY_DESTINATIONS'`, `icon: 'campaign'`, route `/my-destinations`
  - `app.ts:29,314` `delegateOnly` uses `hasManagedDestinations`
- Rename `admin.service.ts:52` `getManagedWebhooks` → `getManagedDestinations` (GET `/api/admin/my-destinations`).
- Rename in `auth.service.ts:54,58` and `core/models/index.ts:343`: `managedDestinations`, `hasManagedDestinations`. The JSON property follows `UserInfo.ManagedDestinations`.
- Rename these i18n keys, then reword:
  - `NAV.MY_WEBHOOKS` → `NAV.MY_DESTINATIONS`
  - `NAV.GROUP_WEBHOOKS` → `NAV.GROUP_DESTINATIONS`
  - `ADMIN.MY_WEBHOOKS_TITLE/_DESC` → `ADMIN.MY_DESTINATIONS_TITLE/_DESC`
  - `ADMIN.NO_WEBHOOKS_ASSIGNED/_DESC` → `ADMIN.NO_DESTINATIONS_ASSIGNED/_DESC`
  - `ADMIN.SNACK_FAILED_LOAD_WEBHOOKS` → `ADMIN.SNACK_FAILED_LOAD_DESTINATIONS`
  - `ADMIN.SNACK_FAILED_SWITCH_WEBHOOK` → `ADMIN.SNACK_FAILED_SWITCH_DESTINATION`
  - `HELP.SECTION_WEBHOOKS/CONTENT_WEBHOOKS` → reworded values only
- My Destinations table gains a Type column (`kindIcon`, `kindLabelKey`). The Task 5.2 return route becomes `'/my-destinations'`.
- Docs: `docs/features/webhooks.md` (My Destinations, delegation to any destination, the new setting); `docs/configuration/site-settings.md` (the setting); CLAUDE.md "Webhook Delegation Resolves Live" (destinations, the directory, the setting, the claim-name carve-out). Retake `my-webhooks.png` as `my-destinations.png`.
- Test: `my-destinations.component.spec.ts`, `auth.service.spec.ts:359-372`, `app.spec.ts`, guards specs, `AdminControllerTests` (`GetManagedWebhooks*` renamed)

- [ ] **Step 1: Write and update the failing tests.**
  - `'lists each delegated destination with its kind'`
  - `'redirects /my-webhooks'`
  - `'shows the Destinations group only to a delegate with grants'`
  - `GetManagedDestinationsAnswersOnBothRoutes` (attribute check via reflection)
- [ ] **Step 2: Run, expect FAIL. Step 3: Implement** with the same scripted key rename as Task 3.3. Then grep the old names and expect no hits.
- [ ] **Step 4: Run** the full frontend suite (`npm test`), `dotnet test`, `npm run build`, lint and prettier. Expect PASS.
- [ ] **Step 5: Live walk** on the dev-01 5.4.0 bed with one row per destination kind.
  - As an admin, then as a delegate granted each, walk: impersonate, add, edit with Mention, delete, test alert, cleaning, profile switch and exit.
  - Then turn `delegation_poracle_channels` on with a `[[discord.delegated_admins]]` entry on the bed and confirm a guild-wide grant reaches only the registered channel.
  - Anything broken becomes a follow-up issue per the spec, not a fix in this PR, unless it breaks something this plan built.
- [ ] **Step 6: Commit** `feat: My Webhooks becomes My Destinations (#1025)`. Add the CHANGELOG, open the PR with the walk results, then file the spec's follow-up issues.

---

## Spec corrections made with this plan

- Nine edit dialogs carry Mention, not ten (pokestop events has none).
- The test alert has no confirm; the success snackbar ("Check your DMs") is what changes.
- "One list component" became shared list state (`AdminListState`): the two pages' columns and actions differ too much for one template.
- The Managers column shows on webhook rows only until Part 6 widens grant targets.
- `notes` on create is gated on the 5.4.0 schema declaring it.
