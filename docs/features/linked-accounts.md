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

For a Telegram side that is waiting, join the server's Telegram registration group and send `/poracle` there.
If the admins set a registration group link (`telegram_registration_url`), the page shows a **Join the
Telegram group** button under the waiting text. For a Discord side, register
the way the site's admins ask; if they've set a support link, the page shows it.

## When the two accounts differ

Settings that belong to the person, such as location, areas, profiles and saved places, are saved to both
accounts. If the accounts have drifted apart (you changed your location with `!location` in the Telegram bot,
say, or added a profile on one side only), a banner under the toolbar says so with one line per difference:

- **Location:** "Location: Discord has Old Town, Telegram has Northside." The place name comes from the same
  address lookup the location dialog uses, and falls back to coordinates when that is switched off or finds
  nothing.
- **Areas:** only what one side has and the other lacks, such as "Telegram also has Riverside."
- **Profiles, saved places and the current profile:** the same way, naming what is on one side only.

Each line has **Keep Discord** and **Keep Telegram**. Pressing one makes the other account match the side you
kept. Nothing is ever resolved for you.

- If the accounts changed again since the banner was drawn, you're told "That changed again. Check the banner."
  and nothing is overwritten.
- A profile that holds alerts on the other account isn't deleted. The message names it and says "Nothing was
  changed." Move or delete those alerts first.
- A saved place that alerts on the other account are still aimed at, on any of its profiles, is not deleted
  either.
- If a resolve fails part way, the banner says so and shows what still differs.

Some older accounts have a profile 0. PoracleNG never gives a new profile that number, so the other account
can't have one, and the pair leaves profile 0 out: it isn't compared, copied, renamed or deleted, and its
alerts stay on the account that has it. The Linked accounts page says which account has one.

**Dismiss** hides the banner for the rest of the browser session. A difference that appears later shows again.

### "Saved for Discord. Telegram didn't take it"

When a save reaches one account and the other refuses it, you see this once, naming the account that saved and
the reason the other gave. Nothing is rolled back. The auto-delete switches on **Cleaning** work the same way:
the count you see is the alerts the first account changed. The banner then shows the difference, and you pick the side
to keep.

## Where new alerts go

When both sides exist, **New alerts go to** picks the default for alerts you add. It is a default for new
alerts only; existing alerts stay where they are.

Quick picks you saved yourself belong to the person, not to one account: a pick saved while signed in
with Discord shows, edits and deletes the same way when you sign in with Telegram. Applying one creates its
alerts on the default side.

## Unlinking

Press **Unlink** on the page. Both accounts and all their alerts stay. Admins can do the same from
**Admin > Users**: a linked account shows a "Linked to Telegram" or "Linked to Discord" chip, and its row menu
has **Unlink accounts**.

## Admin settings

The settings are under **Admin > Settings > Features**, and described in
[Site settings](../configuration/site-settings.md).

- **Linked accounts** (`disable_account_linking`) switches the whole feature on or off. Switched off, the page
  and every link route are gone and each person acts as their single account. Existing links are kept for when
  it is switched back on.
- **Register the other account when linking** (`link_register_missing`) is off by default. Off, linking an
  account that isn't registered on the other platform saves the link and that side waits for the person to
  register. On, the site creates the person's account on the other platform instead of waiting for them to
  register through the server's usual route (a Discord role, or a Telegram registration group), and copies
  the first account's location, areas, profiles, saved places and current profile, so alerts can be delivered
  at once. If creating it fails, nothing is linked and nothing is left behind.
- **Telegram registration group link** (`telegram_registration_url`) is empty by default. Set it to the
  invite link of the group people join to register (for example `https://t.me/+abc123`). It must be an
  `https://` link on `t.me` or `telegram.me`; anything else is refused. While someone's Telegram side is
  waiting to register, Linked accounts shows a **Join the Telegram group** button that opens it.

Registering the account here does not exempt it from that platform's own registration rules. Poracle removes
an account it doesn't think should exist only if the server runs Poracle's role check with a removal mode
set; with Poracle's defaults the account stays.

## Example

Sam signs in with Telegram and has two alerts for Old Town. Sam presses **Link Discord**, approves the
sign-in, and returns to the page. With `link_register_missing` off, the Discord side reads "Waiting to
register" until Sam registers there. Once it reads "Active", Sam picks Discord under **New alerts go to**, and
the next alert added for Riverside is delivered on Discord. The two Old Town alerts keep arriving on Telegram.
