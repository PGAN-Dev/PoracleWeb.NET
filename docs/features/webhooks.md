# Webhooks and Delegates

A **webhook** here is a Poracle account whose id is a Discord webhook URL. It holds alarms exactly as
a person does — profiles, areas, a pin, filters — and Poracle posts its alerts to that URL instead of
into a DM. Communities use one per feed: a raids channel, a hundo channel, a nests channel.

A **delegate** is a person allowed to manage one of those accounts without being an administrator of
this site.

## Creating a webhook

**Admin → Webhooks → Add Webhook**, with a display name and the Discord webhook URL. The URL is the
account id, so it must be unique; an existing one is refused rather than merged. If Poracle rejects
the account, the half-written record is removed, so a failure means nothing was created and a retry
is safe.

The same page can pause, resume, block, delete the account's alarms, delete the account outright, and
open its alarms directly (see [Managing alarms](#managing-a-webhooks-alarms)).

Deleting a webhook takes everything it owned with it, delegate grants included. Recreating the same
URL later starts clean rather than adopting the old grants.

## Adding a delegate

**Admin → Webhooks →** the group-add icon on the webhook's row.

Both accounts have to exist first. The person must have registered with the Poracle bot and signed in
at least once, so there is an account to grant; a grant naming an account that does not exist is
refused rather than stored.

Search for them and select. The grant is written immediately, and they can reach the webhook on their
next page load — no sign-out, no restart.

The dialog shows three kinds of chip:

| Chip | Source | Removable here |
|---|---|---|
| Locked, "global admin" | Poracle's `admins` list, or `PORACLE_ADMIN_IDS` | No |
| Locked, "config delegate" | Poracle's own config | No |
| Removable | This site's `webhook_delegates` table | Yes |

The locked ones are shown so you can see who already has access, not so you can change it. They come
from somewhere this site does not write.

## What a delegate can do

**Settings → My Webhooks** lists the webhooks they manage. **Manage Alarms** switches the session to
that webhook: from there they see the site as it does, and can set up alarms, areas, profiles, its
pin, and send test alerts. A banner names the account being viewed, and the button on it returns them
to their own.

A delegate **cannot** create or delete webhooks, grant delegation to anyone else, or reach any admin
page. Their access is exactly the webhooks granted to them and nothing more, re-checked on every
attempt rather than trusted from their sign-in.

## Do you need to edit Poracle's config?

**For managing the webhook on this site, no.** Alarm writes reach Poracle over a server-to-server
secret, and the authorisation happens here. A row in this site's own table is enough, which is what
the admin dialog writes.

**For managing it through the Discord bot, yes.** That is `discord.webhook_admins` in Poracle's
config, and it needs a Poracle restart. This site cannot grant it.

The two are independent, and someone who should have both needs both. This site accepts either as
proof for its own access: a delegate configured in Poracle gets **My Webhooks** here without anyone
adding a row.

## Where delegation is resolved from

Three sources, unioned on every check:

1. **Poracle's `getAdministrationRoles`** — covers `discord.webhook_admins` and any Discord
   guild-role-based delegation Poracle performs.
2. **This site's `webhook_delegates` table** — what the admin dialog writes.
3. **Administrators**, from `PORACLE_ADMIN_IDS` or Poracle's `admins` list, who can manage any
   webhook and are never listed as delegates of a particular one.

The answer is cached for a minute per person, so granting or revoking takes effect within about that
long rather than at their next sign-in. A lookup that cannot reach one of its sources is never
cached, and falls back to what the session already knew rather than dropping access mid-session.

## Troubleshooting

**They cannot see My Webhooks.** The nav item appears only for people who manage at least one
webhook. Check the grant exists on the webhook's delegates dialog, and that they signed in at least
once before being granted — a grant to an account that never existed is refused, so an absent chip
means it was never written.

**They can see it in Discord but not here, or the reverse.** These are separate grants. The bot side
is Poracle's config; this side is the delegates dialog. Having one does not imply the other, although
a Poracle-side grant does also work here.

**A revoked delegate still has access.** Give it a minute — the resolution is cached that long.
Beyond that, check whether they are a global admin or hold a Poracle-side grant, both of which this
site can display but not remove.

## Related

- [Site Settings](../configuration/site-settings.md) — the admin settings the webhook pages sit
  alongside
- [Database](../architecture/database.md) — the `webhook_delegates` table
