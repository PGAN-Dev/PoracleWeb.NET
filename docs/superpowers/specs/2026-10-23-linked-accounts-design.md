# Linked accounts: one person, a Discord and a Telegram identity, and a destination per alert

**Date:** 2026-10-23
**Base:** `develop` at `646defad` (v3.1.0: PoracleNG 5.4.0 minimum, no Poracle database, #1025 people vs destinations merged).
**Scope:** public PoracleWeb.NET. Works against any operator's stock PoracleNG 5.4.0 with no PoracleNG changes.

## Goal

A person can link a Discord identity and a Telegram identity to themselves. They see one list of alerts, and each alert says where it is delivered: Discord or Telegram. They choose which app new alerts default to. Location, areas, profiles and saved places stay the same on both sides; when a bot command makes them differ, the person is shown the difference and picks which side to keep.

Success means:

- A person signed in through either platform sees the same merged list, can add an alert to either side, and can move an alert between sides without ever losing it.
- Every person-level setting written through PoracleWeb.NET reaches both humans, and a difference made outside PoracleWeb.NET is shown, never silently resolved.
- A person with no link sees and sends exactly what they do today. Their API responses are byte-identical and their requests make the same PoracleNG calls.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Where per-alert destination lives | PoracleWeb.NET only. Each person has two Poracle humans, a `discord:user` and a `telegram:user`; an alert is stored under the human it is delivered by, and moving it is create-then-delete | A per-rule destination column in PoracleNG needs matching and delivery changes upstream is unlikely to take |
| Default destination | A per-person preference that preselects "Deliver to" in add dialogs | |
| Verifying each side | That platform's own sign-in: Discord OAuth, and the Telegram login widget on the operator's PoracleWeb.NET domain | A Telegram bot's widget works on exactly one domain, which is what each operator's site is |
| Registering the missing side | Operator setting `link_register_missing`, default off | Off, the link waits until the person registers with that bot under the operator's normal rules |
| Keeping the pair in step | PoracleWeb.NET writes every person-level change to both humans; drift found later is shown in a banner and the person picks the side to keep | No silent "default wins" rule |
| Session shape | The JWT is unchanged. The pair is resolved live per request from a link table (cached one minute, dropped on link and unlink) | A token carrying both ids would outlive an unlink by up to 24 hours; delegation already learnt that lesson (#601, #786) |
| Fan-out | Explicit: a `LinkedAlarmRouter` for alarms and a `PairedHumanWriter` for person-level writes | A decorator on `IPoracleHumanProxy` would also fan out admin paths (block, purge, rule repair) and could not report a half-applied write |
| Which types link | `discord:user` with `telegram:user`, exactly | They are the only person types PoracleNG delivers to (its rate limiter's `isUserType` is exactly these two) |

## Facts this rests on

PoracleNG (upstream `develop`, read 2026-10-23):

- Delivery routes by `humans.type`; a Telegram human's id is the chat id. A DM only arrives after the person has pressed Start or signed in through the widget with write access, which the widget requests.
- Each platform reconciles only its own humans. Discord role reconciliation reads `discord:user` rows. Telegram reconciliation reads `telegram:user` rows, derives membership from the operator's registration groups, disables a non-member when `removeInvalidUsers` is on, and, with area security on, overwrites `community_membership` from Telegram group membership. It reactivates only a row with `admin_disable=1` and `disabled_date` set.
- The rate limiter keys on the human and bans (disables) one that keeps breaching. `!stop` is per human.
- `POST /api/v2/humans` declares `admin_disable, area, community, enabled, id, language, latitude, longitude, name, notes, profile_name, type` and nothing else. A human's id and type cannot change after creation.
- A new profile takes the lowest free number. Profiles copy only within one human; there is no cross-human copy.

PoracleWeb.NET (`develop`):

- `BaseApiController.UserId` comes from the JWT; the acting human changes only by minting a new token. `ImpersonationAuthority` re-checks impersonation grants on every request.
- `HumanKind.IsPerson` (`:user` suffix) and `HumanKind.Destinations` (#1025). All four sign-in paths refuse destination rows.
- The eleven alarm controllers call per-type services with `this.UserId`; the services call `IPoracleTrackingProxy`. `CreateOnProfileAsync` and `BulkDeleteByUidsOnProfileAsync` take an explicit profile.
- Tracking uids are table-wide auto-increments, so `(type, uid)` belongs to at most one human.
- Saved places are per human (`/locations`), and an alert can be scoped to a place label.
- `user_geofences.human_id` owns a drawn fence; `UserAreaPolicy` authorises "your own drawn fence" by that id.
- `quick_pick_applied_states` is keyed on `user_id` and profile, and stores bare uids.
- Discord sign-in uses an `oauth_state` cookie and the fixed `/api/auth/discord/callback`. `TelegramVerify` checks the widget HMAC with a 24-hour `auth_date` window.

## 1. Data model

New table `poracle_web.account_links`, one row per linked person:

| column | type | notes |
|---|---|---|
| `id` | int PK | |
| `discord_id` | varchar(64), unique, not null | a `discord:user` human id |
| `telegram_id` | varchar(64), unique, not null | a `telegram:user` human id |
| `default_destination` | varchar(16), not null | `discord` or `telegram`; starts as the side the person was signed in on when they linked |
| `discord_verified_at`, `telegram_verified_at` | datetime | when each side last proved itself through its own sign-in; audit, never authorisation |
| `created_at` | datetime | |

- The two unique constraints are the main rule: a human is in at most one link, and a link holds one Discord and one Telegram id.
- There is no stored pending status. Whether each side's human exists is checked when the pair is resolved. With `link_register_missing` off, a link is saved once both identities are verified; the pair covers only the humans that exist, and starts working on its own when the person registers with the other bot.
- `default_destination` is ignored while only one side exists.
- Unlinking deletes the row. Alerts stay on the human that holds them; both humans stay registered.
- Deleting uses raw SQL or load-and-remove, never `ExecuteDeleteAsync` (see "`ExecuteDeleteAsync` Is Unusable On MariaDB").

Code: `AccountLinkEntity` and its configuration, migration `AddAccountLinks`, `IAccountLinkRepository` / `AccountLinkRepository`, an `AccountLink` model with mappings in `EntityMappingExtensions`.

**`ILinkedAccount`** (scoped) answers, for the current `UserId`: the link if any, each side's human id, whether each side's human exists, whether each is paused and why, and the default destination. Rules:

- The link lookup is cached in the shared `IMemoryCache` for one minute, including a "no link" answer, and dropped on link, unlink, default change and purge.
- A side whose live row's `type` doesn't match its column (`discord:user` for `discord_id`, `telegram:user` for `telegram_id`) is treated as absent and logged. It is never written to.
- With `disable_account_linking` on, it answers the caller's own human alone, as if unlinked. Rows are kept.
- A paused side is one with `admin_disable` set, `enabled` off, or not registered. Paused is reported, never corrected: PoracleWeb.NET does not re-enable a human the operator's rules turned off.

**Quick-pick applied state** keeps its schema. Under a link a pick applies to the default destination's human, and applied state is read across both ids, so a pick's status and its Remove cover alerts on either side. A move takes the uid out of the source human's row and puts the new uid in the target human's row for the same pick and profile, creating that row if needed.

**Drawn geofences** keep their schema. Ownership checks in `UserGeofenceService` and `UserAreaPolicy` accept either id in the pair, and the ten-fence cap counts across both.

## 2. Linking

The "Linked accounts" card on the account page is shown only when the other platform's sign-in is configured (Discord: `Discord:ClientId` and secret; Telegram: `Telegram:Enabled` and a bot), its `enable_discord` / `enable_telegram` setting is on, and `disable_account_linking` is off. Linking and unlinking are refused while impersonating.

**Discord, from a Telegram session.**

1. `POST /api/account-link/discord/start` (JWT) sets `oauth_state` and an `oauth_link` cookie (HttpOnly, SameSite=Lax, ten minutes, DataProtection-protected `{humanId, expires}`) and returns Discord's authorize URL.
2. Discord returns to the existing `/api/auth/discord/callback`. Operators register no new redirect URI. The callback cannot see the JWT, so the cookie is what says this is a link and for whom.
3. With a valid `oauth_link`, the callback exchanges the code, runs the same `enable_discord` and `CheckRoleAccessAsync` checks a Discord sign-in runs, links, and redirects to `/account#linked=discord` or `/account#link_error=<code>`. It never issues a session token on this path.
4. Without the cookie the callback is exactly today's sign-in.

**Telegram, from a Discord session.** The card renders the widget for the bot `telegram/config` names. Its payload goes to `POST /api/account-link/telegram` (JWT). The HMAC check moves out of `TelegramVerify` into a shared `TelegramWidgetVerifier` used by both endpoints. Linking accepts an `auth_date` no older than ten minutes; sign-in keeps 24 hours.

Both endpoints, and the Discord callback's link branch, hand a verified identity to one `IAccountLinkService.LinkAsync(callerHumanId, type, id, name)`, which runs the checks below, creates the missing human when the setting allows, and writes the row. It is designed for these two flows only.

**Checks before a link is written**, each a coded refusal with copy in all 11 locales:

| Code | Refused when |
|---|---|
| `link_wrong_type` | the caller isn't `discord:user` or `telegram:user`, or the new identity is the same type |
| `link_bad_id` | the Discord id isn't ASCII digits (at most 20), or the Telegram id isn't a positive integer |
| `link_type_mismatch` | the other identity's human exists with a type other than the matching `:user` |
| `link_taken` | either id is already in a link ("That Telegram account is already linked to another Discord account. Unlink it there first.") |
| `link_expired` | the `oauth_link` cookie or widget `auth_date` is out of date, or the state doesn't match |
| `link_not_permitted` | the Discord side fails `enable_discord` or the role check, or the Telegram side fails `enable_telegram` |

A unique-constraint violation from a race maps to `link_taken` (409), never a 500.

**The other side's human doesn't exist.**

- `link_register_missing` off: the link is saved. The card says alerts on that side start once the person registers with that bot: for Telegram, pressing Start with the bot; for Discord, generic copy plus `support_url` when set.
- On: create the human with `type`, `id`, the name from Discord or Telegram, `language`, `latitude`, `longitude`, `area` and `community` copied from the first side; then copy the profiles (name, `active_hours`, per-profile areas and location), saved places, and the current profile. Alerts are not copied: they stay where they are and appear in the merged list. Any failure rolls back: the created human is deleted only if its type and name still match what was sent (the #1028 rule), no link is saved, and the answer is 502 "Nothing was linked."
- The setting's description says the created account still has to pass that platform's own registration check, or Poracle turns it off at the next sync.

**Unlink.** `DELETE /api/account-link` for the person; `DELETE /api/admin/account-links/{humanId}` for admins, from a "Linked to Telegram/Discord" chip on the Users row. No alerts move.

**Default.** `PUT /api/account-link/default { destination }`, refused for a side that doesn't exist.

## 3. Alerts: the merged list and moving

Per-type services stay single-human. `LinkedAlarmRouter` sits between the eleven alarm controllers and their services and picks the human for each call from `ILinkedAccount`. Unlinked, every call goes to `this.UserId`.

**Reading.** `GET /api/{type}` reads each human in the pair and concatenates. Each alarm model gains a nullable `destination` (`discord` / `telegram`), `[JsonIgnore(WhenWritingNull)]`, set only under a link. Dashboard counts, cleaning status, profile overview, pin usage and place usage sum or union across both humans.

**By uid.** `GET`, `PUT` and `DELETE /{uid}` try the signed-in human, then the other; a uid neither owns is today's 404. Bulk delete, bulk distance and cleaning group uids by owner and send one request per human. "Delete all" and "distance for all" go to both. A test alert goes to the human that owns the rule.

**Creating.** The `POST` body takes an optional `destination`; absent, the default is used; unlinked, it is ignored. A side that isn't registered answers 409 "Telegram isn't set up yet." A paused side saves and returns `pausedDestination: true` so the dialog can say nothing will arrive yet. Quick-pick apply and profile import create on the default destination.

**Moving.** `PUT /{uid}` with a different `destination`, the card action, and the bulk "Deliver to" all use one `AlarmMover`:

1. Preflight, nothing written: the target human exists; the rule's `profile_no` exists on the target; a place-scoped rule's label exists there; `override_areas` pass `IUserAreaPolicy` for the target. Each failure is a 409 naming what is missing ("Your Telegram side has no place called Home").
2. Create on the target with `CreateOnProfileAsync(source profile_no)`, through the existing dry-run protocol. An identical rule already on the target counts as created (step 3 still runs, merging the duplicate). A would-be takeover of another target rule answers 409 with `existingUid` and leaves the source alone.
3. Delete the source by uid on its profile.
4. Remap quick-pick applied state.

Create always comes before delete, so a move never loses an alert. Step 2 failing changes nothing. Step 3 failing is retried once; if it still fails the answer is 200 with `sourceRemains: true`, the SPA says "Moved to Telegram, but the Discord copy is still there. Delete it from the list.", and the list shows both, tagged. Two concurrent moves of one alert end the same way, since a not-found delete already counts as gone. A bulk move reports per uid: `moved`, `conflict`, `sourceRemains`, `refused` with reason.

## 4. Person-level writes and drift

**`PairedHumanWriter`** carries every person-level write: location and saved places; areas (Save and the geofence toggle); profile create, update, delete, switch, duplicate and import; notification language; quiet periods; alerts on/off; summary schedules. It writes the signed-in human, then the other.

- The first failing is today's error, and the second isn't tried.
- The second failing still answers success, with `pairWarning: { side, reason }`; the SPA says "Saved for Discord. Telegram didn't take it: …". Nothing is rolled back; the drift banner is the repair.
- Profile create: if the two humans assign different numbers (they had already drifted), the second side's new profile is deleted and the difference is reported as drift.
- Profile import: the profile goes to both, its alerts to the default destination.
- Unlinked: one call, today's.

**`GET /api/account-link/status`** answers the link, each side's state (exists, paused and why), the default, and a `drift` list. The SPA calls it after `/me` and on window focus at most once a minute. Unlinked, it answers `{ linked: false }` without calling PoracleNG.

| Field | Equal when |
|---|---|
| location | lat/lon agree to 5 decimal places |
| areas (active profile) | same set under PoracleNG's `NormalizeAreaKey` (case, `_` vs space; order ignored) |
| current profile | same number |
| profiles | same numbers, names, `active_hours` (string and int hours normalised), per-profile areas and location |
| saved places | same labels and coordinates |

Language, summary schedules and quiet periods are written to both but not compared; quiet periods live in PoracleNG's memory and vanish on restart. A paused side is reported as paused, not as drift.

**Banner.** `PairDriftBannerComponent`, under the toolbar on every page, one line per differing field: "Location differs. Telegram: Old Town · Discord: Northside [Keep Telegram] [Keep Discord]". Location is shown through the existing reverse geocode, falling back to coordinates; areas show only the difference ("Telegram also has Riverside"). Dismissing hides it for the browser session. Nothing resolves on its own.

**`POST /api/account-link/resolve { field, keep, expected }`** re-reads both sides, answers 409 "That changed again. Check the banner." when the kept side no longer matches `expected`, and otherwise applies the kept value to the other side through the writer. Profiles are the destructive case: keeping one side's profiles creates missing ones on the other, copies names and schedules, and switches the current profile; it deletes an extra profile on the other side only when it holds no alerts, and otherwise refuses, naming the profile and its alert count.

## 5. Settings, SPA, and existing users

**Settings** (both boolean, category `features`, on the admin settings page):

- `disable_account_linking`, default false. Gated in all four layers: `[RequireFeatureEnabled]` on the account-link controller, a route guard, the card's `disableKey`, and `FeatureGateCoverageTests`. Switched on, every person acts as their single human and link rows are kept for when it is switched off.
- `link_register_missing`, default false, described as in section 2.

**SPA.**

- `LinkedAccountService` with a `status` signal.
- Account page card: each side's name or avatar and state (active, paused, waiting to register), Link, Unlink, and a "New alerts go to" choice.
- `DeliverToFieldComponent` (`shared/components/deliver-to/`) in every add and edit dialog, shown only when both sides exist. Add preselects the default; edit shows the owner and changing it moves the alert. A paused side stays selectable with a one-line warning. `alarm-dialog-parity.spec.ts` requires it on every pair.
- `app-alarm-card`: a neutral, uncoloured Discord or Telegram glyph beside the scope chip. The list toolbar gains an All / Discord / Telegram filter; the bulk bar gains "Deliver to…". All only when linked.
- `PairDriftBannerComponent` as in section 4.
- Every string in all 11 locales.

**Existing single-platform users** see no change:

- No link row: `ILinkedAccount` answers their own human after one cached `poracle_web` lookup; the router and writer make today's calls; status makes no PoracleNG call.
- Alarm JSON is byte-identical, since `destination` is omitted when null.
- Discord, Telegram and OIDC sign-in are untouched; only a request carrying `oauth_link` takes the link branch.
- Admin > Users gains a chip on linked rows only. Purging either human deletes its link; the other human stays.
- Shape 1: the only new validation is on new endpoints, so nothing that works today is refused.

## Error handling summary

| Situation | Answer |
|---|---|
| Linking refused | coded 4xx (section 2), translated |
| Register-missing creation fails | 502, created human rolled back under the #1028 rule, no link |
| Alert to an unregistered side | 409 |
| Alert to a paused side | saved, `pausedDestination: true` |
| Move preflight fails | 409 naming what's missing, nothing written |
| Move create conflicts | 409 with `existingUid`, source untouched |
| Move delete fails twice | 200, `sourceRemains: true` |
| Second side of a person-level write fails | success with `pairWarning`; drift banner repairs |
| Drift resolve on stale data | 409 |
| Drift resolve would delete a profile with alerts | refused, naming it |
| PoracleNG unreachable | 503, as today (`PoracleUnreachableExceptionFilter`) |

## Testing

Each guard ships with a test that the legitimate case still passes, and each new test is watched failing first.

Backend (xUnit):

- Repository on SQLite: both unique constraints; lookup and delete by either id; `NoAliasedDeleteTests` still passes.
- `ILinkedAccount`: no link answers the caller alone; type mismatch treats a side as absent; paused reasons; `disable_account_linking` answers single; cache dropped on link, unlink, default change and purge.
- Linking: each refusal code; the legitimate link from each side; a tampered or expired `oauth_link`; a Discord callback with no `oauth_link` is today's sign-in; widget windows (10 minutes for link, 24 hours for sign-in); register-missing creation and its rollback that never deletes a row it didn't create; refused while impersonating.
- Router: unlinked controllers make exactly today's calls; merged read stamps `destination`; uid lookup finds the other side; bulk grouping per owner.
- Mover: each preflight refusal; create failure leaves the source; delete failure reports `sourceRemains`; identical-on-target merges; applied state follows the uid.
- Writer: partial failure warns; profile-number mismatch cleanup; unlinked single call.
- Drift: normalisation (area casing and underscores, float noise, string and int `active_hours`); a matching pair reports nothing; each field's resolution; the profile-with-alerts refusal; stale `expected` 409; unlinked status makes no PoracleNG call.
- Architecture: a source scan fails the build if any alarm controller passes `this.UserId` to its service instead of the router, or a user-facing controller calls a person-level `IPoracleHumanProxy` write instead of the writer. `FeatureGateCoverageTests` covers the new key.

Frontend (Jest):

- `DeliverToFieldComponent` visibility and preselection; `alarm-dialog-parity.spec.ts` on every pair.
- Card glyph, toolbar filter and bulk action appear only when linked.
- Banner lines, resolve calls and the stale 409.
- Account card states and both link flows' fragment handling.
- `locale-parity.spec.ts`; `npm run build` before any push.

Live, on the dev-01 PoracleNG 5.4.0 bed (stock build): link from each side with register-missing off and on; add an alert to each side and confirm delivery in a Discord DM and a Telegram DM; move one each way; break the delete step and see `sourceRemains`; change location with `!location` in one bot and resolve it from the banner; unlink. Deleting alerts DMs the owning account; say so before running.

## Public-path checklist

- Nothing in code, settings, copy, docs or fixtures names a particular operator, its domains, its sign-in provider or how it admits members. Examples use neutral places (Old Town, Northside, Riverside) and made-up ids.
- No decision relies on one operator's production data. The table is new, so no existing row is affected.
- No hooks or settings for operator-specific layers. `IAccountLinkService` is designed for the two public flows only.
- Before any PR: grep the diff for operator names, private fork names and real ids.

## Docs

`docs/features/linked-accounts.md`; both settings in `docs/configuration/site-settings.md`; a Help page section; CHANGELOG `[Unreleased]` (Added); a CLAUDE.md section on the pair rules (router, writer, create-then-delete, drift never resolved silently, links are `discord:user` with `telegram:user` only).

## Out of scope

- Any PoracleNG change, including a per-rule destination.
- Linking more than two humans, or any type other than `discord:user` and `telegram:user`.
- Re-enabling a side the operator's rules turned off.
- Comparing language, summary schedules or quiet periods for drift.
