# Linked Accounts

Someone who uses PoracleWeb.NET from both Discord and Telegram has two accounts, and Poracle treats them as
two people. Linking pairs them: one Discord account with one Telegram account. The page shows each side's
state, lets the person unlink, and sets where new alerts go.

Nothing is copied or moved by linking, and unlinking deletes nothing. Both accounts keep their own alerts.

## Linking

Open **Linked accounts** in the user menu. What the page offers depends on how you signed in:

- **Signed in with Telegram.** Press **Link Discord**. You go to Discord to approve the sign-in and come back
  to the page with the link made.
- **Signed in with Discord.** Use the Telegram button on the page and approve it in Telegram.

A platform is offered only when its sign-in is set up on the site and switched on. An account can be in one
link at a time. Linking and unlinking are refused while an admin is viewing the site as that person.

### If a link is refused

The page says why, in your language. The reasons:

| Message | What happened |
|---|---|
| Only a personal account can be linked | The account isn't a personal Discord or Telegram account, or both sides are the same platform |
| The account's id isn't valid | The id Discord or Telegram sent can't be an account id |
| The account already exists as something else | That id is registered here as a channel, group or similar, not a person |
| Already linked to another account | Unlink it from the other account first |
| The sign-in took too long | A Telegram login is good for ten minutes after you approve it, and a Discord link for ten minutes after you press Link Discord. Start again |
| Not allowed to sign in | The account fails this site's own sign-in rules, such as a required Discord role |
| You cancelled the link | You backed out of the Discord approval. Nothing was changed |
| Couldn't link the accounts | Something went wrong that none of the above covers, or the link was lost part way. Nothing was changed; try again |
| Couldn't set up the other account | The site tried to create the other account and couldn't. Nothing was linked |

## Each side's state

| State | Meaning |
|---|---|
| Active | The account exists and is receiving alerts |
| Waiting to register | The account isn't registered with Poracle yet. Alerts on that side start once it is |
| Alerts stopped | The person stopped alerts on that account. PoracleWeb.NET never turns them back on |
| Paused by an admin | An admin blocked the account |

For a Telegram side that is waiting, press Start in the bot (the page names it). For a Discord side, register
the way the site's admins ask; if they've set a support link, the page shows it.

## Where new alerts go

When both sides exist, **New alerts go to** picks the default for alerts you add. It is a default for new
alerts only; existing alerts stay where they are.

## Unlinking

Press **Unlink** on the page. Both accounts and all their alerts stay. Admins can do the same from
**Admin > Users**: a linked account shows a "Linked to Telegram" or "Linked to Discord" chip, and its row menu
has **Unlink accounts**.

## Admin settings

Both settings are under **Admin > Settings > Features**, and described in
[Site settings](../configuration/site-settings.md).

- **Linked accounts** (`disable_account_linking`) switches the whole feature on or off. Switched off, the page
  and every link route are gone and each person acts as their single account. Existing links are kept for when
  it is switched back on.
- **Register the other account when linking** (`link_register_missing`) is off by default. Off, linking an
  account that isn't registered on the other platform saves the link and that side waits for the person to
  register. On, the site creates the account and copies the first account's location, areas, profiles, saved
  places and current profile, so alerts can be delivered at once. If creating it fails, nothing is linked and
  nothing is left behind.

Registering the account here does not exempt it from that platform's own registration rules. If the site
requires, say, a Discord role to be registered, an account made by linking still has to meet it, or Poracle
turns it off at its next sync.

## Example

Sam signs in with Telegram and has two alerts for Old Town. Sam presses **Link Discord**, approves the
sign-in, and returns to the page. With `link_register_missing` off, the Discord side reads "Waiting to
register" until Sam registers there. Once it reads "Active", Sam picks Discord under **New alerts go to**, and
the next alert added for Riverside is delivered on Discord. The two Old Town alerts keep arriving on Telegram.
