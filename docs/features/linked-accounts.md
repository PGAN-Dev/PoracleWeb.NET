# Linked Accounts

Someone who uses PoracleWeb.NET from both Discord and Telegram has two accounts, and Poracle treats them as
two people. Linking pairs them: one Discord account with one Telegram account. The page shows each side's
state, lets the person unlink, and sets where new alerts go.

Nothing is copied or moved by linking, and unlinking deletes nothing. Both accounts keep their own alerts.

## Linking

Open **Linked accounts** in the user menu or under **Settings** in the sidebar. Each card names the account
on that side. What the page offers depends on how you signed in:

- **Signed in with Telegram.** Press **Link Discord**. You go to Discord to approve the sign-in and come back
  to the page with the link made.
- **Signed in with Discord.** Use the Telegram button on the page and approve it in Telegram.

A platform is offered only when its sign-in is set up on the site and switched on. An account can be in one
link at a time. Linking and unlinking are refused while an admin is viewing the site as that person, and the
page says so instead of showing the person's link.

### If a link is refused

The page says why, in your language, and what to try next. Dismiss the message with its close button. The
reasons:

| Message starts | What happened |
|---|---|
| You can only link a Discord account to a Telegram account | The account isn't a personal Discord or Telegram account, or both sides are the same app |
| That account couldn't be read | The id Discord or Telegram sent can't be an account id |
| That account is already used here as a channel or group | That id is registered here as a channel, group or similar, not a person |
| That account is already linked to someone else | Unlink it from the other account first |
| That sign-in took too long | A Telegram login is good for ten minutes after you approve it, and a Discord link for ten minutes after you press Link Discord. Start again |
| That account can't sign in to this site | The account fails this site's own sign-in rules, such as a required Discord role |
| You cancelled the link | You backed out of the Discord approval. Nothing was changed |
| The accounts couldn't be linked | Something went wrong that none of the above covers, or the link was lost part way. Nothing was changed; try again |
| Your other account couldn't be set up | The site tried to create the other account and couldn't. Nothing was linked |

## Each side's state

| State | Meaning |
|---|---|
| Receiving alerts | The account exists and is receiving alerts |
| Waiting to register | The account isn't registered with Poracle yet. Alerts on that side start once it is |
| Alerts stopped | The person stopped alerts on that account. PoracleWeb.NET never turns them back on |
| Paused by an admin | An admin blocked the account |

For a Telegram side that is waiting, join the server's Telegram registration group and send `/poracle` there.
If the admins set a registration group link (`telegram_registration_url`), the page shows a **Join the
Telegram group** button under the waiting text. For a Discord side, register
the way the site's admins ask; if they've set a support link, the page shows it.

## When the two accounts differ

Settings that belong to the person, such as location, areas, profiles and saved places, are saved to both
accounts. They can still come apart (you changed your location with `!location` in the Telegram bot, say, or
added a profile on one side only). When they do, a one-line banner under the toolbar says "Discord and Telegram
have different settings." with a **Review** link to the Linked accounts page. The banner only appears when a page
opens, so nothing you're reading jumps down under you.

The **Linked accounts** page lists every difference, whether or not you dismissed the banner. Each has a label and
names what differs:

- **Location:** both places at the same level of detail (two towns, or two countries) and the distance between
  them, such as "Discord's is in Richmond, Telegram's is in Glasgow, 555 km apart." The names come from the address
  lookup the location dialog uses; without it you get the distance alone, never raw coordinates.
- **Areas:** the areas only one app has, spelled the way Poracle spells them.
- **Profiles:** the profiles only one app has, and for the ones both have, what differs: name, schedule, or areas
  and location, with each app's name for a renamed one ("Daily on Discord, Default on Telegram"). The profile either
  app is on isn't reported here for its areas or location; those are the Location and Areas lines.
- **Active profile:** the profiles each app is using, by name.
- **Saved places:** the places only one app has, and the ones in different spots, by name.

Each line has **Use Discord's** and **Use Telegram's**, and under each button what it does to the other app ("Telegram
gets Aberdeen.", "Discord loses Weekend."). Pressing one makes the other app match. Nothing is ever resolved for
you. A choice that deletes a profile or saved place on the other app asks first and names what goes. If it deletes
the profile the other app is on, the line says that app switches profile, and so its areas and location change
too. Afterwards a message at the top of the screen says what changed, and the page moves to the next difference.

- If the apps changed again since the list was drawn, you're told so, nothing is overwritten, and the list shows
  what differs now.
- A profile that still has alarms on the other app isn't deleted. The message names the app and the profile; move
  or delete those alarms in that app first, or use that app's profiles.
- A saved place that alarms on the other app still use, on any of its profiles, isn't deleted either.
- The active profile can only be matched once the other app has that profile. The line says so, and Profiles comes
  first in the list.
- If a choice fails part way, the page says so and the list shows what still differs.

Some older accounts have a profile 0. PoracleNG never gives a new profile that number, so the other account
can't have one, and the pair leaves profile 0 out: it isn't compared, copied, renamed or deleted, and its
alerts stay on the account that has it. The Linked accounts page says which account has one.

**Dismiss** hides the banner for the rest of this browser session (closing the tab or window brings it back). A new
or changed difference shows it again. The list on Linked accounts is always there.

### "Saved for Discord, but not for Telegram"

When a save reaches one app and the other refuses it, you see this once, naming the app that saved and why the
other didn't, with a **Review** button that opens Linked accounts. It's added to the page's own "Saved" message
rather than replaced by it. Nothing is rolled back. The auto-delete switches on **Cleaning** work the same way:
the count you see is the alarms the first app changed. The Linked accounts list then shows the difference, and you
pick the app to use. Every save reads the differences again, so the banner and the list are never out of date.

## Where new alarms go

When both sides exist, **New alarms go to** picks the app for alarms you add. It is a default for new
alarms only; existing alarms stay where they are. A side that isn't receiving alerts (stopped, or paused by an
admin) is marked and can't be picked; if it is already the default, the page says nothing will arrive there
and suggests the other app. While one side is still waiting to register, the page says the choice comes once
both are registered.

Quick picks you saved yourself belong to the person, not to one account: a pick saved while signed in
with Discord shows, edits and deletes the same way when you sign in with Telegram. Applying one creates its
alerts on the default side.

## Alarms on both apps

Every alarm list shows the alarms of both accounts together. Each card names the app it goes to under the
alarm's name, with a pause mark when that app isn't receiving alerts, and **Delivered to: All / Discord /
Telegram** above the list filters by app.

Every add and edit dialog asks **Which app gets this alarm?** above its tabs, so the answer is in view
whichever tab is open. A new alarm starts on the default from Linked accounts. Picking the other app on an
edit moves the alarm there.

To move several at once, tick them in select mode and choose **Deliver to...** (on a phone it's under
**More**). The app every ticked alarm is already on is greyed out. A move creates the alarm on the other
account before removing it from the first, so it is never lost. The result counts what moved, what was
already on that app, and what wasn't moved and why.

## Unlinking

Press **Unlink** on the page. Both accounts and all their alarms stay. Admins can do the same from
**Admin > Users**: a linked account shows a chip with a link icon and the other app's name, and its row menu
has **Unlink accounts**. The confirm names both accounts.

## Admin settings

The settings are under **Admin > Settings > Linked accounts**, and described in
[Site settings](../configuration/site-settings.md).

- **Linked accounts** (`disable_account_linking`) switches the whole feature on or off. Switched off, the page
  and every route a person links through are gone and each person acts as their single account. Existing links
  are kept for when it is switched back on, and **Admin > Users** still shows them and can unlink them.
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
register" until Sam registers there. Once it reads "Receiving alerts", Sam picks Discord under **New alarms go to**, and
the next alert added for Riverside is delivered on Discord. The two Old Town alerts keep arriving on Telegram.
