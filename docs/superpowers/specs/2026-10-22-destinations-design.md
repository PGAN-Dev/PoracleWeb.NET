# People and destinations: channels, groups and webhooks get their own page

**Date:** 2026-10-22
**Issue:** #1025. Related: #1027 (Telegram gaps), #1028 (create rollback race).
**Base:** `develop` at `378d292a` (v3.0.0, PoracleNG 5.4.0 minimum, no Poracle database).
**Research:** an 18-agent read-only design run (code map, prod data, upstream PoracleNG, UX, security, performance, test strategy, three critics). Its dossier was built from a pre-3.0.0 checkout; every fact below was re-checked against `develop`.

## Goal

Admin > Users lists people. Everything Poracle posts into that nobody signs in as (webhooks, Discord channels and threads, Telegram channels, groups and topics) lives on one Destinations page. Admins can create every kind there, manage each the same way, and delegate any of them to a person, the way webhooks can be delegated today.

Success means:

- No human row is listed on both pages or on neither, whatever its `type` string.
- A seeded `discord:channel`, `discord:thread`, `telegram:group` and `telegram:topic` can each be created, impersonated, given alarms, edited, test-alerted, cleaned and deleted on the dev-01 5.4.0 bed, first as an admin and then as a delegate.
- Every surface that answers "who may manage what" (My Destinations, `POST /api/admin/impersonate`, `/api/auth/me`) gives the same answer for destinations as it does for webhooks today.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Page structure | One Destinations page at `/admin/destinations`; `/admin/webhooks` redirects | One list of about 28 rows doesn't need two pages repeating every row action. |
| i18n keys | Renamed to match: `NAV.DESTINATIONS`, `ADMIN.DESTINATIONS_*` | Keys that match what they render. All 11 locales move in the same PR. |
| Kinds the Add dialog creates | All six destination types | Owner's call. Threads and topics have never been created this way anywhere, so a live check gates that PR. |
| Delegation | Extends to every destination type | Owner's call. Local grants and `webhook_admins` name grants resolve to any destination. |
| PoracleNG's `getAdministrationRoles` channel lists | Honoured behind `delegation_poracle_channels`, default off | `discord.channels` expands a guild to every channel in it, and the API and bot disagree in four places upstream, so an operator opts in. |
| Where the work lands | Public repo, branched from `develop` | |
| Classification on the wire | The server stamps `kind` on every admin list row; the SPA filters on it | One rule in one place, so the pages can't drift from the server's authorisation. See "Classification". |

## Facts this rests on

Production, read-only, 2026-10-22:

| type | humans | with alarms | profile rows | delegate grants |
|---|---|---|---|---|
| discord:user | 2689 | 541 | 620 | 6 as grantee |
| telegram:user | 4 | 4 | 0 | 0 |
| webhook | 25 | 25 | 0 | 6 as target |
| discord:channel | 2 | 0 | 0 | 0 |
| telegram:group | 1 | 0 | 0 | 0 |

No other types, no null types. The three channel rows sit on Users today and have no alarms, so moving them breaks nothing live, and the end-to-end walk has to use seeded rows on dev. All six prod grants are `webhook` → `discord:user`, from `poracle_web.webhook_delegates`. Prod's PoracleNG has no `webhook_admins` and no `delegated_admins`, so every delegation change here is additive in prod.

Discord channel ids are 18-digit snowflakes, the same shape as user ids. Kind can only come from `humans.type`, never from the id.

Upstream PoracleNG:

- Types in use: `discord:user`, `discord:channel`, `discord:thread`, `webhook`, `telegram:user`, `telegram:channel`, `telegram:group`, `telegram:topic` (id `"<chatId>:<topicId>"`). `type` is free text on create, so unknown strings are possible.
- Upstream's own person/destination split is `type LIKE '%:user'` (`!broadcast`). Its name-addressable set is `webhook`, `discord:channel`, `telegram:channel`, `telegram:group`; threads and topics are id-only.
- Create doesn't check that a channel exists. A wrong id fails silently at delivery.
- Reconciliation admin-disables a Discord channel that 404s, so "blocked" on a channel row often means "deleted in Discord".
- `getAdministrationRoles` answers `{admin:{discord:{channels[],webhooks[],users},telegram:{channels[],users}}}`. `discord.webhooks` holds names, resolved upstream over every name-addressable type. `discord.channels` holds ids, expanded from a guild to every text, news and forum channel in it, registered or not, possibly duplicated.
- Ping is appended to the message content for every target type. On Telegram `<@&id>` is literal text.
- `GET /api/v2/humans?type=` takes one exact value. A comma list returns nothing.

PoracleWeb.NET on `develop`:

- `admin-users.component.ts` filters `type !== 'webhook'`; `admin-webhooks.component.ts` filters `type === 'webhook'`, and its delegate picker offers every non-webhook row, channels included.
- `CreateWebhook` hard-codes `Type = "webhook"` and `Id = Url`. Its rollback deletes any row with the id (#1028).
- `POST webhook-delegates` refuses a target that isn't `webhook`, and checks only that the grantee exists.
- `UserRoleResolver` reads `admin.discord.webhooks` only and canonicalises grants against `HumanService.GetWebhooksAsync()` (`ListHumansAsync(type: "webhook")`), dropping everything else.
- `ImpersonationAuthority` re-authorises on `IsAdmin || ManagedWebhooks.Contains(target)`, type-blind.
- No login path (Discord callback, Telegram verify, OIDC callback, OIDC refresh) checks the row's type.
- The Mention field shows on `isWebhook`, which is `AuthService.isImpersonating()`, in ten edit dialogs. `pingSupported` no longer exists (5.4.0 sub-project 1).
- `GetHumanAsync` throws on anything but 404 (#994), so existence checks no longer read an outage as "absent".
- `webhook_delegates.webhook_id` is 500 characters. Channel and topic ids fit; no migration.

## Classification

`Core/Pgan.PoracleWebNet.Core.Models/HumanKind.cs`:

- `IsPerson(type)`: null, empty, whitespace, or ends with `:user` (case-insensitive). Mirrors upstream's split.
- `IsDestination(type)`: `!IsPerson(type)`. Display only. Every row lands on exactly one page, unknown types included.
- `Destinations`: the explicit allowlist `webhook`, `discord:channel`, `discord:thread`, `telegram:channel`, `telegram:group`, `telegram:topic`. Used for anything that grants power: create, grant targets, delegate impersonation. An unknown type is listed but can't be created, granted or reached by a delegate.
- `MentionCapable`: `webhook`, `discord:channel`, `discord:thread`.

`GET /api/admin/users` stamps `kind: "person" | "destination"` on every row from `IsPerson`. The SPA filters on `kind` and never re-derives it. It keeps only presentation helpers in `shared/utils/human-kind.ts`: `mentionCapable` (a three-entry list, read from the impersonation token's `type`) and `kindLabelKey`. The backend tests carry the case list: `discord:user`, `telegram:user`, `oidc:user`, `DISCORD:USER`, null, `""`, `"  "`, `webhook`, `Webhook`, the six destination types, `x:unknown`.

## Work, in merge order

### 1. Create rollback race (#1028)

`CreateWebhook` rolls back only a row whose type and name match what the request sent, deleted through the admin purge path so its grants go too. Otherwise 502, nothing touched. The new create endpoint (step 4) shares this code.

### 2. Login refuses destination rows

Discord callback, Telegram verify, OIDC callback and OIDC refresh (`BuildOidcUserInfoAsync`) refuse a row where `!HumanKind.IsPerson(row.Type)`. Null and empty types pass. Never compare against `Oidc:IdentityType`, which is a claim value and never reaches the row. Redirect errors use a new `destination_account` code, with copy in all 11 locales in `LoginComponent`'s `errorKeys`.

This merges before steps 4 and 6. Without it, an admin pasting a person's id as a channel id, or a delegate grant on a mistyped row, hands a person a destination's session.

Shape 1: every one of the 2,693 prod person rows is `discord:user` or `telegram:user`, so nothing live is refused. Re-query dev and prod before merging.

### 3. Users and Destinations

Frontend:

- `admin/destinations` replaces `admin/webhooks` (redirect kept). Nav label "Destinations", icon `campaign`.
- Users shows `kind === 'person'` rows. Its description links to Destinations.
- Destinations shows `kind === 'destination'` rows: name with notes beneath (upstream writes "Guild / Category" there), a Type column with glyph and label (`webhook`, `tag` for Discord, `send` for Telegram, `help_outline` plus the raw string for unknown), ID with copy, status, Disabled since, Managers, actions.
- Type filter chips appear only for kinds present. Search covers name, id and notes. Paging and the filter persist under `admin_destinations_*`, separate from Users.
- Row actions are identical for every kind: Manage alarms (`POST /api/admin/impersonate`), view alarms, block, delete, delegates. A channel's Block tooltip says Poracle also blocks channels it can no longer find. Delete says the Discord or Telegram channel itself isn't touched.
- The delegate picker offers `kind === 'person'` rows only.
- One shared list component backs both pages, parameterised by `kind`, so a fix to one reaches the other. Both keep one fetch of the full list; the delegates column needs people.

Backend: `POST webhook-delegates` refuses a grantee that isn't `IsPerson`. All six prod grantees are `discord:user`.

Copy: keys renamed to `NAV.DESTINATIONS` / `ADMIN.DESTINATIONS_*`, values reworded (`ADMIN.COLUMN_URL` becomes "ID"), new keys for type labels, the filter, the tooltip and the empty states. All 11 locales, gated by `locale-parity.spec.ts`. My Webhooks keys move in step 6.

Docs: `docs/features/webhooks.md` becomes "Destinations and delegates" (filename kept), plus the admin docs, troubleshooting, README and architecture pages that name the Webhooks page. CLAUDE.md gains a short "People vs destinations" section: the rule, where it lives, and that `HumanKind.Destinations` is the only list that grants power. Retake `sidenav.png` and `mobile-sidenav.png` under the anonymisation rule.

CHANGELOG `[Unreleased]`, added per PR as each lands: Fixed (channels and groups no longer listed as users; the create rollback race), Added (Destinations page, destination create, channel delegation, `delegation_poracle_channels`), Changed (Webhooks renamed Destinations, My Webhooks renamed My Destinations).

### 4. Creating a destination

`POST /api/admin/destinations`, admin only, refused while impersonating. Body `{type, id, name, notes?}`. `type` must be in `HumanKind.Destinations`. Refusals cover the impossible only:

| Kind | Id input | Refused |
|---|---|---|
| webhook | URL | today's `CreateWebhook` check |
| discord:channel, discord:thread | id | anything but ASCII digits, at most 20 |
| telegram:channel, telegram:group | chat id | anything but a negative integer |
| telegram:topic | chat id and topic id, two fields | chat not negative, or topic not positive; stored as `chatId:topicId` |

All kinds refuse a blank name or id and control characters. Pre-check 409 names the existing row's type and name. Rollback as in step 1. `HumanService` sends `notes` on create. `POST /api/admin/webhooks` stays and calls the same code.

The Add dialog asks "What kind?" first, then shows that kind's id field with its own label, placeholder and validation. Switching kind keeps the name and clears the id. A line under the id says Poracle doesn't check it exists and the bot needs permission to post there. 409, 502 and 503 answers render translated, never raw.

Gate: before merge, create each of the six kinds on the dev-01 5.4.0 bed through this endpoint, add an alarm, and confirm delivery. Thread and topic delivery have never been exercised by this app.

### 5. Managing a destination's alarms

- Mention shows when `isImpersonating() && mentionCapable(user().type)`, in all ten edit dialogs. Hidden on Telegram, where a mention is literal text. The form control stays, so a stored mention round-trips. `alarm-dialog-parity.spec.ts`'s `EDIT_ONLY` entry for `ping` stays.
- The impersonation banner names the kind ("Managing Discord channel #raids").
- Exit returns to where the session started: Destinations for an admin, My Destinations for a delegate. Today it always goes to `/admin`, which bounces a delegate to the dashboard. `auth.service.ts` and the sibling in `error.interceptor.ts` change together.
- The test-alert confirm says the alert posts in the channel or group.

The type comes from the impersonation token's `type` claim, which is the target row's type.

### 6. Delegation to destinations

Grants:

- `POST webhook-delegates` accepts any target in `HumanKind.Destinations`, grantee still `IsPerson`. The admin delegate dialog is on every Destinations row. No table or column rename; `webhook_id` now holds a destination id, recorded in CLAUDE.md.

Resolver (`UserRoleResolver`, the single answer for all three surfaces):

- Canonicalises grants against every destination row, not webhooks only. A `webhook_admins` name that matches a channel resolves, as it already does upstream.
- When `delegation_poracle_channels` is on, it also reads `admin.discord.channels` and `admin.telegram.channels`, intersects them with registered destination rows, and dedupes. `users: true` is ignored. Off by default.
- Destination rows come from a cached directory: one `ListHumansAsync(type:)` call per type in `HumanKind.Destinations` (v2 takes one type per call), single-flighted, cached in the shared `IMemoryCache` for one minute, dropped on destination create, delete or block. Not per-resolve: six list calls for every user's resolve would be the cost of this feature.
- Toggling the setting, creating or deleting a destination, and changing a grant call `Invalidate`/`InvalidateAll` as grant writes do today.
- A degraded source still throws rather than answering, and an unresolved answer is still never cached.
- `ManagedWebhooks` becomes `ManagedDestinations` in C# and TypeScript. The JWT claim keeps the name `managedWebhooks`: it's a cold fallback only, and live tokens carry it.

Enforcement:

- `POST /api/admin/impersonate` reads the target row live and refuses a delegate whose target isn't in `HumanKind.Destinations`, whatever the resolver said. Admins keep impersonating anything.
- `ImpersonationAuthority`'s per-request re-authorisation is unchanged.
- The test-alert rate limit partitions on `impersonatedBy` when present, so a delegate managing several destinations shares one bucket.
- A delegate can set a Mention on a channel they manage, as they can on a webhook today. Parity, not a new restriction.

Setting: `delegation_poracle_channels`, category `admin`, boolean, shown on the admin settings page with a description saying it follows PoracleNG's channel admin config and that guild-wide entries cover only channels registered here.

My Webhooks becomes My Destinations at `/my-destinations` (redirect from `/my-webhooks`), listing each grant with its kind. Its keys rename with the rest.

Shape 1: the six prod grants are webhook → person and keep resolving. With the setting off and no `webhook_admins` in prod, nothing changes for anyone until an admin grants a channel.

## Testing

Each guard ships with a test that the legitimate case still passes, and each new test is watched failing first.

Backend (xUnit):

- `HumanKind`: the shared case list, for `IsPerson`, `IsDestination`, `Destinations` membership and `MentionCapable`.
- Create race: two overlapping creates for one id leave the first row intact; a real half-write (#482) is still rolled back.
- Login: `destination_account` refusal on all four paths; `discord:user`, `telegram:user`, a null type and an OIDC refresh still sign in.
- Grants: a `discord:user` grantee still succeeds; a channel grantee is refused; a channel target succeeds; an `x:unknown` target is refused.
- Create: each kind's legitimate id passes, including a `-100…` supergroup and a legacy `-` basic group; each impossible one is refused; a person type is refused.
- Resolver: setting off ignores channel lists; setting on intersects with registered rows, dedupes and ignores unregistered ids; a name grant that matches a channel resolves; an unknown-type grant fails closed; a degraded directory read is not cached.
- Parity: My Destinations, `POST impersonate` and `/api/auth/me` give the same answer for a delegated channel.
- Impersonate: a delegate is refused for a target whose live type is a person even when the resolver lists it.
- Settings: `delegation_poracle_channels` bounds and invalidation.

Frontend (Jest):

- Partition specs for Users and Destinations (neither page has a spec today): rows of each `kind` land on exactly one page. A backend test pins that the case list's types are stamped as expected, so the two halves meet at `kind`.
- Add dialog branching per kind, including the two topic fields.
- Mention gate per type in all ten edit dialogs.
- Exit-to-origin for admin and delegate, in `auth.service` and the interceptor.
- `locale-parity.spec.ts` across the 11 locales, and the route redirects.

Live, on the dev-01 5.4.0 bed: seed one of each destination kind, then as an admin and as a delegate walk create, impersonate, add, edit with Mention, delete, test alert, cleaning, profile switch, and exit. Delete DMs whoever owns a seeded row; say so before running. `npm run build` before pushing any template change.

## Follow-ups (filed when this spec's plan lands)

- Blocked destinations under impersonation: `BlockedAccountFilter` checks the target, never the impersonator.
- Quiet-period write controls hidden under impersonation (writes are already refused).
- Webhook URLs in logs (`LogWebhookCreated`, `LogAdminImpersonating`, `LogUnresolvedWebhookGrant`).
- Admin list performance: a shared store and a server-side `?kind=`.
- Alarm counts per destination, and a Help section.
- Anything the live walk shows broken for profile switch, places, onboarding or the geofence forum mention, each its own issue. Telegram-specific gaps stay on #1027.

## Out of scope

- Changing the person/destination rule upstream, or asking PoracleNG to validate channel ids.
- Renaming `webhook_delegates` or its columns.
- Restricting what a delegate may put in a Mention.
