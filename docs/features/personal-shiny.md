# Personal Shiny Alerts (Experimental)

A **personal shiny alert** fires when a Pokemon is shiny for one of *your* game accounts. A **personal
background alert** fires when it carries the event background for one of them. Both are decided per
player: two trainers standing at the same spawn can get different answers, because the game rolls each
spawn separately for every player ID. This is the same roll as Golbat's personal shiny filtering, applied
to Poracle's DMs.

You link your game accounts, then switch on "Only when it's shiny for me" or "Only when it has the event
background for me" on a Pokemon rule. The rule matches only when the roll for one of your linked
accounts comes up shiny (or with the background). With both switched on, both must hold.

## This is an experimental line

!!! warning "Unofficial, unreleased, unsupported by PoracleWeb.NET releases"
    The `experimental/shiny` branch is built against PoracleNG's `feat/personal-shiny` branch
    ([PoracleNG PR #255](https://github.com/jfberry/PoracleNG/pull/255)), which is not merged into a
    release. Until it is, you run two pre-release builds together. Either can change or break without
    notice. On a PoracleNG without #255 this build behaves like the official release it is based
    on, with the personal controls hidden.

It exists so people who want the feature can try it before upstream ships it. If you would rather wait
for a release, stay on the official image. Report problems on GitHub with the
[`experimental-shiny`](https://github.com/PGAN-Dev/PoracleWeb.NET/labels/experimental-shiny) label.

## What you run

Two images, and both have to be the experimental ones.

| Component | Image |
|---|---|
| PoracleWeb.NET | `ghcr.io/pgan-dev/poracleweb.net-experimental:shiny`, or a fixed version such as `:2.20.2-shiny.1` |
| PoracleNG | `ghcr.io/jfberry/poracleng:<short sha>`, such as `:37cc7a3` |

`:shiny` follows the newest `-shiny.N` prerelease. There is no `latest` tag on the experimental
repository, so pulling the default can't land you here by accident. Every push to the branch also
publishes `edge-<sha>`; use those only if you are testing the branch itself.

For PoracleNG, use the seven-character commit listed in the release notes of the PoracleWeb.NET
prerelease you are installing. That is the PoracleNG build the prerelease was tested against.
PoracleNG also publishes `:feat-personal-shiny`, which follows the branch as it moves; nobody has
tested a newer commit with your PoracleWeb.NET version, so pin the short SHA unless you want to.

On this branch `docker-compose.yml.example` already points at the experimental image. On an existing
install, change the one `image:` line in your `docker-compose.yml`:

```yaml
image: ghcr.io/pgan-dev/poracleweb.net-experimental:shiny
```

Then `docker compose pull && docker compose up -d`. Once it is running, the user menu shows an
**Experimental** chip under the profile name, and Admin > Settings > Versions says which build this is.
If you don't see the chip, you are on the official image.

## Configure PoracleNG

Two blocks in PoracleNG's `config.toml`.

```toml
[shiny_rates]
url = "https://shinx.sylvie.fyi/"   # base URL only: PoracleNG adds /v1/rates?compact=1
token = ""                          # optional bearer token
#max_bytes = 16777216               # largest feed response accepted (default 16 MiB)

[player_accounts]
self_service = false
```

Restart PoracleNG after changing either.

### The rate feed

Personal shiny needs each species' current shiny rate, so PoracleNG loads a feed in the format Golbat's
personal shiny support defines ([Golbat PR #423](https://github.com/UnownHash/Golbat/pull/423)): it
requests `<url>/v1/rates?compact=1` at startup and every five minutes.

Golbat **reads** that feed too, it doesn't serve one, so pointing `url` at your Golbat does nothing. Most
servers use the community feed at `https://shinx.sylvie.fyi/`. It is a third-party service, run by
neither PoracleWeb.NET nor PoracleNG, so it can change or go away; if your Golbat has a `[shiny_rates]`
section, use the same URL there. As of October 2026 it lists about 680 species and forms, the species
with no released shiny, and timezone overrides for local events such as Community Day.

If you would rather not depend on it, any web server can serve a static file at `<url>/v1/rates`. The
smallest useful one gives every species the standard rate and names the ones that can't be shiny:

```json
{
  "rates": [],
  "unsupportedPokemon": [132, 931],
  "fallback": { "probability": 0.001953125, "supported": true }
}
```

That gets ordinary spawns right and event rates wrong, and a species missing from `unsupportedPokemon`
alerts for shinies that can't exist. The full schema is in Golbat's `docs/personal-shinies.md` on the
PR branch.

Until the feed has loaded, personal rules match nothing. With `url` empty the feature is off, and every
personal rule stays silent. **Admin > Settings** warns when PoracleNG supports personal alerts but has no
feed configured. PoracleWeb.NET can see that a URL is set, not whether it answers, so after setting one,
check in Discord: `!poracle-admin status` reports the feed, how many variants it loaded, and its last
error.

The roll itself is a copy of Golbat's implementation, so for the same spawn, rate and player ID,
PoracleNG and a Golbat-backed map agree.

### Who may link accounts

`self_service` decides whether trainers can link their own game accounts. It defaults to `false`,
and you should think before changing it.

!!! warning "Turning on self_service means anyone can link any player ID"
    A player ID can't be verified. Nothing proves the person typing `SomeTrainer123` plays that
    account. With `self_service = true`, any trainer can claim any ID, including one that belongs to
    somebody else, and then receive alerts based on that player's rolls. For a private community that
    may not matter. For a public one, leave it off and have admins link accounts after checking
    ownership some other way.

With it off, only administrators (and a trusted API client) can link accounts. Trainers see their
linked accounts but can't change them, and the dialog tells them to send an admin their player ID.

## Link game accounts

### What a player ID is

The roll uses the game's internal **player ID** for your account: a string of letters and digits. It is
**not** your 12-digit friend code (trainer code). The Game accounts dialog warns when what you typed
looks like a friend code.

Players don't usually know their player ID. If your community runs ReactMap with personal shiny support
([WatWowMap/ReactMap#1268](https://github.com/WatWowMap/ReactMap/pull/1268)) and its operator has
configured a player ID lookup service, ReactMap turns a friend code into the player ID and shows the
trainer's name to confirm it. Otherwise ask your community's admins how they hand IDs out. Looking an ID
up from a friend code here is planned ([#974](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/974)).

Player IDs are case-sensitive and stored exactly as typed, so `AbC` and `abc` are different accounts.
Each account can carry an optional label, unique per person and compared without case. A person can
have up to 50 linked accounts.

### Three ways to link

**The trainer dialog.** Open the user menu and choose **Game accounts**. The entry appears only when
PoracleNG supports trainer game accounts. With `self_service` on, you can link, relabel and unlink. The
player ID is checked as you type (letters and digits only, nothing already linked), and an unlink offers
Undo for a few seconds, because removing your last account silently stops every personal rule. With
`self_service` off, the dialog is read-only and says to send an admin your player ID, with a link to your
support page if one is set. Admins can always edit, their own accounts included, and the dialog is
read-only while viewing another account.

**Admin > Users.** Each row has a **Game accounts** button that opens the same dialog for that user.
Admins can edit whatever `self_service` says. This is the route to use when self-service is off.

**The bot.** `!account` (or `/account`) lists, adds, removes and labels linked accounts, subject to
the same `self_service` rule.

### If you also run ReactMap with personal shiny

ReactMap #1268, with a Poracle webhook selected, links each user's active players to their Poracle
account itself and unlinks paused or removed ones, re-syncing every hour while the map is in use.
Those accounts show up in the Game accounts dialog like any other. ReactMap's profile is the source of
truth for them: an account you unlink here that is still active in ReactMap comes back at its next
sync, so pause or remove the player in ReactMap instead.

## Create a rule

1. Link at least one game account.
2. Add or edit a Pokemon rule and open the **Filters** tab.
3. In the **For my game accounts** block at the top, switch on **Only when it's shiny for me**,
   **Only when it has the event background for me**, or both.

The block appears only when PoracleNG's schema declares the fields, so it is absent on a server
without #255. Under the toggles, one line says whether the rule can fire:

| You see | Meaning |
|---|---|
| "Checks your account Main." (or two names, or a name and a count) | The accounts the roll will use |
| "This server hasn't set up shiny rates yet…" | No `[shiny_rates] url`: the rule can't fire anywhere |
| "This rule won't alert until you link a game account." with a **Link a game account** button | You may link one yourself |
| "…until an admin links your game account. Send them your player ID." | Self-service is off |

Cards for these rules show the shiny sprite and a "Shiny for me" or "Background for me" badge under the
name. An icon pack without shiny art falls back to the ordinary sprite.

From the bot, the equivalent is `!track <pokemon> personalshiny` or `personalbackground`.

A personal rule matches nothing while the rate feed hasn't loaded, or while the person has no linked
accounts. That's by design: a "shiny only" rule that matched everything would be worse than one that
stays quiet.

PoracleWeb.NET refuses to save a personal rule against a PoracleNG that can't store it. A server
without personal-shiny support answers 200 and silently drops the setting, which would leave a "shiny
only" rule that fires on every spawn, so you get an error instead.

## Going back to the official image

You can switch back to `ghcr.io/pgan-dev/poracleweb.net` at any time. What happens to your rules is
worth knowing first.

- Rules with a personal setting keep it. PoracleNG stores it in the rule's `personal` column, and
  changing the web image doesn't touch the database. While you stay on the experimental PoracleNG,
  those rules still fire as personal rules.
- The official PoracleWeb.NET doesn't show it. A personal rule looks like an ordinary Pokemon rule
  with no sign of the setting.
- Editing or deleting such a rule on the official build keeps the setting (checked against v2.20.2:
  the edited rule kept `personal` 1, 2 and 3). You just can't see or change the setting there, so a rule
  can look ordinary while it only fires for your accounts.

This page doesn't cover what a PoracleNG build without #255 does with the `personal` column, because
that depends on the build. Check its release notes before pointing the database at one.

## Related

- [Alarm Management](alarms.md) for the Pokemon rule dialogs this adds to
- [PoracleNG Version Compatibility](../architecture/poracleng-compatibility.md) for how version support is decided
