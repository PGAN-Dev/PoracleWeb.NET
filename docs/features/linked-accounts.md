# Linked Accounts

Someone who gets alerts on both Discord and Telegram has two PoracleNG accounts, and PoracleNG treats them as
two people. Linking pairs one Discord account with one Telegram account so that PoracleWeb.NET treats them as
one person: one list of alarms, the app chosen per alarm, and personal settings saved to both.

Linking copies nothing and moves nothing on its own, and unlinking deletes nothing. For how the two apps
differ and how people register on each, see [Discord and Telegram](discord-and-telegram.md). For how the pair
is stored and resolved, see the [technical page](../architecture/linked-accounts.md).

## Linking

Open **Linked accounts** from the user menu or under **Settings** in the sidebar. There's a card for each app,
and what it offers depends on how you signed in:

- **Signed in with Telegram.** Press **Link Discord**, approve the sign-in on Discord, and you come back to
  the page with the link made.
- **Signed in with Discord.** Use the Telegram button on the Telegram card and approve it in Telegram.

An app is offered only when its sign-in is set up on the site and switched on. An account can be in one link
at a time, and only personal accounts link: a channel, group, thread or webhook never does. While an admin is
viewing the site as you, the page says the link is yours to manage and offers nothing.

### If a link is refused

The page says why, and what to try next. Dismiss the message with its close button.

| Message starts | What happened |
|---|---|
| You can only link a Discord account to a Telegram account | The account isn't a personal Discord or Telegram account, or both sides are the same app |
| That account couldn't be read | The id Discord or Telegram sent can't be an account id |
| That account is already used here as a channel or group | That id is registered here as a destination, not a person |
| That account is already linked to someone else | Unlink it from the other account first |
| That sign-in took too long | A Telegram login is good for ten minutes after you approve it, and a Discord link for ten minutes after you press **Link Discord**. Start again |
| That account can't sign in to this site | The account fails this site's own sign-in rules, such as a required Discord role |
| You cancelled the link | You backed out of the Discord approval. Nothing was changed |
| The accounts couldn't be linked | Something none of the above covers, or the link was lost part way. Nothing was changed; try again |
| Your other account couldn't be set up | The site tried to create the other account and couldn't. Nothing was linked |

## Each side's state

Each card says where that account stands.

| State | Meaning |
|---|---|
| You're signed in with this account | The account you're using now, before anything is linked |
| Not linked | No account on this app is linked yet |
| Receiving alerts | The account exists and Poracle is sending it alerts |
| Waiting to register | The account is linked but Poracle doesn't know it yet. Alerts on that app start once you register there |
| Alerts stopped | You stopped alerts on that account. Start them in that app |
| Paused by an admin | An admin blocked the account. The card offers **Get help** when the site has a support link |

PoracleWeb.NET never turns a stopped or paused account back on. That's for the person or the admins on that app.

**Waiting to register on Telegram.** Join the server's Telegram registration group and send `/poracle` there.
If the admins set a registration group link, the card shows a **Join the Telegram group** button. Press
**Start** in a chat with the bot as well, or it can't DM you.

**Waiting to register on Discord.** Register the way the site's admins ask, usually by getting a role on the
community's Discord server. The card shows **Get help** when the site has a support link.

## Where new alarms go

Once both accounts are registered, **New alarms go to** picks the app for alarms you add. It's a default for
new alarms only; existing alarms stay where they are, and every dialog lets you choose differently. An app
that isn't receiving alerts is marked "(not receiving alerts)" and can't be picked. If it's already the
default, the page says nothing sent there will arrive and suggests the other app. While one side is still
waiting to register, the page says the choice comes once both are registered.

Quick picks you saved yourself belong to the person, not to one account, and applying one creates its alarms
on the default app.

## Alarms on both apps

Every alarm list shows the alarms of both accounts together. Each card names the app it goes to, with
"alerts off" when that app isn't receiving alerts, and **Delivered to: All / Discord / Telegram** above the
list filters by app.

Every add and edit dialog asks **Which app gets this alarm?** above its tabs. A new alarm starts on the
default app. Picking the other app in an edit moves the alarm there when you save.

To move several, tick them in select mode and choose **Deliver to…** (on a phone it's under **More**). An app
every ticked alarm is already on is greyed out. The result counts what moved, what was already there, and
what wasn't moved and why.

### How a move works

A move creates the alarm on the other account first and deletes the original only once that worked, so an
alarm is never lost on the way. If the delete fails, both copies stay and the message says so: "Moved to
Telegram, but the Discord copy is still there. Delete it from the list."

A move is refused, with nothing changed, when:

| Reason shown | What to do |
|---|---|
| Discord and Telegram are on different profiles | Switch one app's active profile to match, then move again. The Linked accounts page offers this as a difference to fix |
| The place "Home" isn't saved on Telegram | Save that place on the other app first, or match saved places on Linked accounts |
| It's limited to an area Telegram can't use | Change the alarm's areas, then move it |
| Telegram already has an alarm like it | Nothing to move; delete the duplicate if you don't need it |
| Telegram isn't set up yet | The other side is still waiting to register |
| Alerts can't be moved while inspecting another account | An admin viewing as you can't move your alarms |

## Settings saved to both apps

Saving any of these on the site saves it to both accounts: location, saved places, areas, profiles (names,
schedules, areas and location), language, quiet periods, quest summary schedules, and alerts on or off.

The account you signed in with is saved first. If the other app refuses or doesn't answer, what you saved
stands on the first app and you see one message naming both, such as "Saved for Discord, but not for
Telegram: the alert service didn't answer. Try again in a minute. Review the difference on Linked accounts."
Its **Review** button opens Linked accounts, where the difference is waiting. Nothing is rolled back.

## When the two accounts differ

Poracle's bot commands change one account only. The site can't stop the two from drifting, so it shows the
drift and lets you choose.

**A worked example.** You're linked, and both apps are set to Old Town. On the bus you send `/location` with a
pin in Riverside to the Telegram bot. Telegram's account now measures distances from Riverside; Discord's
still uses Old Town. Next time you open a page on the site, a one-line banner under the toolbar says
"Discord and Telegram have different settings." with **Review**. On Linked accounts the Location line reads
"Discord's is in Old Town, Telegram's is in Riverside, 4 km apart." Under **Use Discord's** it says "Telegram
moves to Discord's location."; under **Use Telegram's**, "Discord moves to Telegram's location." You press
**Use Telegram's**, a message confirms "Discord now has Telegram's location.", and the line goes.

### What's compared

| Line | What it names |
|---|---|
| Location | Both places at the same level of detail and the distance between them. Never raw coordinates |
| Areas | The areas only one app has, in Poracle's spelling |
| Profiles | Profiles only one app has, and for shared ones whether the name, schedule or areas differ |
| Active profile | The profile each app is using, by name |
| Saved places | Places only one app has, and places in different spots |

Language, quiet periods and quest summary schedules are saved to both but not compared. Quiet periods, for
one, live only in Poracle's memory and vanish when it restarts, so they'd show as different for no reason.

### Choosing a side

Each line has **Use Discord's** and **Use Telegram's**, and under each button what it changes on the other app.
Nothing is ever resolved for you.

- A choice that deletes a profile or saved place from the other app asks first, naming what goes ("Delete
  Weekend from Telegram").
- If that deletes the profile the other app is using, the line says that app switches profile, so its areas
  and location change too.
- A profile that still holds alarms on the other app isn't deleted, and neither is a saved place that alarms
  there still use. The message names the app and what to change first.
- The active profile can only be matched once the other app has that profile, so Profiles comes first in the
  list.
- If either app changed since the list was drawn, you're told "That changed in the meantime", nothing is
  overwritten, and the list shows what differs now.
- If a choice stops part way, the page says so and the list shows what still differs.

**Dismiss** on the banner hides it for the rest of the browser session. A new or changed difference shows it
again, and the list on Linked accounts always shows everything. The banner appears when a page opens, never
in the middle of one, and isn't shown on Linked accounts itself or while an admin is viewing as you.

### Profile 0

Some older accounts have a profile 0. Poracle never gives a new profile that number, so the other account
can't have one. The pair leaves profile 0 out: it isn't compared, copied, renamed, switched to or deleted, and
its alarms stay on the account that has it. The page says which account has one, for example "Your Discord
account has an older profile 0 that Telegram can't have. Its alarms stay on Discord."

## Unlinking

Press **Unlink** on the page and confirm. Both accounts and all their alarms stay; they just stop being
treated as one person. Admins can do the same from **Admin > Users**, where a linked account has a chip naming
the other app and its row menu has **Unlink accounts**.

## Settings

The settings are under **Admin > Server settings > Linked accounts** and in
[Site settings](../configuration/site-settings.md#linked-accounts).

| Setting | Key | Default | What it does |
|---|---|---|---|
| Linked accounts | `disable_account_linking` | On | Off hides the page and refuses every link route. Each person acts as their single account again. Links are kept for when it's switched back on, and Admin > Users can still unlink them |
| Register the other account when linking | `link_register_missing` | Off | On, linking creates the missing account straight away, copying language, location, areas, profiles (on the same numbers) and saved places. Alarms are never copied. If creating it fails, nothing is linked and nothing is left behind |
| Telegram registration group link | `telegram_registration_url` | Empty | The invite link people use to register on Telegram, such as `https://t.me/+abc123`. Must be `https://` on `t.me` or `telegram.me`. Shown as **Join the Telegram group** while a Telegram side is waiting |

A created account skips that app's registration rules but not its later checks. Poracle removes an account
it doesn't think should exist only when the server runs its role check with `role_check_mode` set to
`disable-user` or `delete`; with Poracle's defaults the account stays. See
[Telegram](../configuration/telegram.md#removing-people-who-leave).

**Let Poracle's channel admins manage here** (`delegation_poracle_channels`) sits near these settings but is
about destinations, not linking. See [Destinations & delegates](webhooks.md#letting-poracles-channel-admins-manage-here).

## Example

Sam signs in with Telegram and has two alarms for Old Town. Sam presses **Link Discord**, approves the sign-in
and returns to the page. With `link_register_missing` off, the Discord card reads "Waiting to register" until
Sam gets the community's role. Once it reads "Receiving alerts", Sam picks Discord under **New alarms go to**,
and the next alarm Sam adds, for Riverside, is delivered on Discord. The two Old Town alarms keep arriving on
Telegram until Sam moves them with **Deliver to…**.
