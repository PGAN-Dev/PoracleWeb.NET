---
paths:
  - "**/*Webhook*"
  - "**/*webhook*"
  - "**/Destination*"
  - "**/Impersonation*"
  - "**/HumanService*"
  - "**/PoracleHumanProxy*"
  - "**/IPoracleHumanProxy*"
  - "**/human-kind*"
  - "**/*Delegate*"
  - "**/*delegate*"
  - "**/AdminController*"
  - "**/admin-users*"
  - "**/server-profile-card/**"
---

## People vs destinations

`humans` holds people and destinations in one table, told apart by `type`. **A person is a type that is null,
empty, whitespace or ends `:user` (case-insensitive); everything else is a destination.** That is upstream's
`!broadcast` split, so an operator's typo or a type added later lands on Destinations rather than on neither page.
`HumanKind` (`Core.Models/HumanKind.cs`) is the one classifier: `IsPerson`, `KindOf` (`"person"` or `"destination"`),
and three sets.

`HumanKind.Destinations` is exactly `webhook`, `discord:channel`, `discord:thread`, `telegram:channel`,
`telegram:group`, `telegram:topic`, and it is the only list that grants power: destination create
(`AdminController`), grant targets (the delegate grant) and delegate impersonation all check
`HumanKind.IsKnownDestination`, and `DestinationDirectory` lists exactly those six types. An unknown non-user type is
listed as a destination and has none of those powers: it can't be created, granted or opened by a delegate (admins can
still open any account). `MentionCapable` (webhook, Discord channel, Discord thread) says where a `ping` makes sense;
`NameAddressable` says which types PoracleNG resolves by name.

`GET /api/admin/users` stamps `kind` on every row. Admin > Users shows the people, Admin > Destinations the rest, split
client-side on that field (`shared/utils/human-kind.ts`); the two pages share their list state in `admin-list-state.ts`.
The delegate grant refuses a grantee that isn't a person (400) and a target that isn't one of the six. The
`webhook_delegates.webhook_id` column holds any destination's id (a webhook URL, a channel or group id, a
`chat:topic` pair); the column name predates #1025.

## Destination Delegation Resolves Live, From Three Sources

A destination is a Poracle human alerts are posted into: any of the six types in `HumanKind.Destinations`
(see "People vs destinations"). A **delegate** is a non-admin allowed to manage one, which they do by
impersonating it, so "who may manage what" is an authorisation question asked on three surfaces: the
My Destinations list (`GET /api/admin/my-destinations`; `my-webhooks` stays as a route alias, and the SPA
redirects `/my-webhooks` to `/my-destinations`), `POST /api/admin/impersonate`, and `/api/auth/me`, which
decides whether the nav item renders at all.

`IUserRoleResolver.ResolveAsync` is the single answer, unioning:

1. PoracleNG's `getAdministrationRoles` → `admin.discord.webhooks` (covers `discord.webhook_admins`
   **and** guild-role delegation), plus `admin.discord.channels` and `admin.telegram.channels` **only when
   the `delegation_poracle_channels` site setting is on** (absent = off). Those lists come from
   `[[discord.delegated_admins]]`/`[[telegram.delegated_admins]]`, which say who may administer a channel in
   the bot; an operator may not mean that as "may manage its alarms here", so it is opt-in. A guild-wide entry
   reaches only channels registered as destinations, because every grant is canonicalised against the
   directory below.
2. `poracle_web.webhook_delegates` (what the admin dialog writes, now for any of the six types),
3. admins from `Poracle:AdminIds` or Poracle's config, who short-circuit before any network call.

Cached one minute per user; a degraded answer is never cached. Writes do not wait out the minute:
granting or revoking a delegate drops the cached answer for both the delegate and the webhook
(`Invalidate`), and deleting a human drops every cached answer (`InvalidateAll`), because a delegate
PoracleJS names by the webhook's *name* sits in no table that could say who else just lost access. The
cache keys carry a generation number held in the shared `IMemoryCache`, since the resolver is scoped.

This is also what re-authorises impersonation sessions on every request (see "JWT Generation"), so a
stale entry here is a minute of access an admin has just taken away. Any new writer of a grant must
invalidate too, and so must a write of `delegation_poracle_channels`, which is why saving that setting takes
effect at once rather than after the minute.

**All three surfaces must resolve live, and the same union.** This has now broken four separate ways,
each time because one surface disagreed with another:

| Fix | What it broke |
|---|---|
| #564 | `/my-webhooks` (now `/my-destinations`) loaded from the admin user list, which 403s non-admins — the only people who could see the page |
| #601 | reading the claim let a revoked delegate keep access for 24 hours |
| #626 | resolving from the local table alone refused a PoracleJS-configured delegate the nav item had just offered |
| #786 | `/api/auth/me` still read the claim, so a *new* delegate could not find a page that would have let them in |
| #797 | all three agreed on the union and disagreed on what the strings in it *identify* — see below |
| v2humans | `GetAdminRolesAsync` returned null on **any** non-2xx, so a 503 read as "administers nothing" with `Resolved:true` and got cached — #656/#667 on the one source they missed |

**A degraded source must throw, not answer.** `Resolved` only protects what actually reports failure.
The admin-roles read now lives on `IPoracleHumanProxy`: a 404 means PoracleNG has no such human and
answers null; anything else non-2xx throws so the resolver records the answer as unresolved.

**Admin status never comes from the roles body.** Two `isAdmin` branches read one and neither could
ever fire — both API versions build it from the same `adminRolesResult` (`channels`, `webhooks`,
`users`), and v2's schema is `additionalProperties:false`. They are deleted; do not re-add a promotion
path from a field upstream does not send.

**A grant names a destination; it is not necessarily its id.** PoracleNG returns whatever key the
operator wrote in `[[discord.webhook_admins]] target`, and upstream that key is the webhook's **name**: the
bot resolves it with `LookupWebhookByName` and authorises with `CanAdminWebhook(cfg, userID, nameOverride)`.
The local delegate table stores ids. `UserRoleResolver` therefore canonicalises every grant to a
`humans.id` against **`IDestinationDirectory`** (every destination of the six types, one PoracleNG list call
per type, cached a minute, never cached on a failed read) and drops what names nothing: a grant that
matches no row could never match one downstream, and carrying it drew a nav item onto an empty page. Ids
match any of the six; names match only `HumanKind.NameAddressable` types, a webhook winning a name it
shares with other types, and a name shared by two or more webhooks, or by two or more non-webhooks with no
webhook, dropped as ambiguous (#1038); channel ids from Poracle's channel lists are
matched by id only. Consumers may assume `ManagedDestinations` holds ids. See #797, #1025.

An impersonation session resolves the managed list live, like every other surface, because `this.UserId`
is the account the whole session is acting as, the same account `my-destinations` and `POST impersonate`
already answer for. `IsAdmin` is the carve-out, not this. Chaining a second impersonation is refused
server-side: the SPA holds one `poracle_admin_token`, so a second hop overwrites the way back out. A
delegate's impersonation also checks the target's live type with `HumanKind.IsKnownDestination`, so a grant
whose id belongs to a person or an unknown type opens nothing.

**The names differ by layer, deliberately.** C# says `ManagedDestinations` (`UserInfo`, `UserRoles`,
`BaseApiController`), the `/api/auth/me` body says `managedDestinations`, and the SPA says
`managedDestinations`/`hasManagedDestinations`. The **JWT claim is still named `managedWebhooks`**
(`JwtService`, `BaseApiController`): renaming it would make every token minted before the rename read as
"manages nothing" until it expired. Leave the claim name alone.

That claim is now a **cold fallback only**, used when a resolve is degraded so an outage does not strip a
delegate mid-session. Nothing authorises off it. Do not reintroduce it as a source of truth.

Two carve-outs worth keeping: an impersonation session resolves nothing (`this.UserId` is the
impersonated account, so its own delegations answer a different question — the shape of #663), and
`IsAdmin` deliberately still comes from the claim, because every admin endpoint authorises off the
claim too and resolving one live without the other puts the UI and the API into disagreement.

Deleting a human purges grants in both directions (`RemoveAllForIdAsync` matches `WebhookId` **or**
`UserId`), so a recreated webhook URL does not adopt the old delegates. See #510-#512.

**Never log a human id raw.** A webhook's id carries its token. Route every logged user, admin, target, grant
or delegate id through `LogSafe.Id` (`Core.Models/LogSafe.cs`); `LoggedHumanIdsAreMaskedTests` fails the build
otherwise, and `appsettings.json` keeps `System.Net.Http.HttpClient` at Warning because its request lines
print the encoded webhook path (#1038).

User-facing documentation: `docs/features/webhooks.md`.

## Webhook ID URL Encoding
Webhook IDs in Poracle are URLs (e.g., `http://host:port/path`). When constructing proxy API paths that include a webhook ID as a path segment, the ID must be encoded with `Uri.EscapeDataString()` to escape slashes and other special characters. Both `PoracleTrackingProxy` and `PoracleHumanProxy` apply this encoding.
