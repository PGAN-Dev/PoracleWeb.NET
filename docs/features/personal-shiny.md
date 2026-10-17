# Personal Shiny Alerts (Experimental)

A **personal shiny alert** fires when a Pokemon is shiny for one of *your* game accounts. A **personal
background alert** fires when it carries the event background for one of them. Both are decided per
player: two trainers standing at the same spawn can get different answers, because the roll uses each
player ID. This is the same idea as Golbat's personal shiny filtering, applied to Poracle's DMs.

You link your game accounts, then switch on "Shiny for my accounts" or "Event background for my
accounts" on a Pokemon rule. The rule matches only when the roll for one of your linked accounts comes
up shiny (or with the background). With both switched on, both must hold.

## This is an experimental line

!!! warning "Unofficial, unreleased, unsupported by PoracleWeb.NET releases"
    The `experimental/shiny` branch is built against PoracleNG's `feat/personal-shiny` branch
    ([PoracleNG PR #255](https://github.com/jfberry/PoracleNG/pull/255)), which is not merged into a
    release. Until it is, you run two pre-release builds together. Either can change or break without
    notice, and an official PoracleNG does not have the endpoints this build calls.

It exists so people who want the feature can try it before upstream ships it. If you would rather wait
for a release, stay on the official image. Report problems on GitHub with the `experimental-shiny`
label.

## What you run

Two images, and both have to be the experimental ones.

| Component | Image |
|---|---|
| PoracleWeb.NET | `ghcr.io/pgan-dev/poracleweb.net-experimental:shiny`, or a fixed version such as `:2.20.0-shiny.1` |
| PoracleNG | `ghcr.io/jfberry/poracleng:<sha>` |

`:shiny` follows the newest `-shiny.N` prerelease. There is no `latest` tag on the experimental
repository, so pulling the default can't land you here by accident. Every push to the branch also
publishes `edge-<sha>`; use those only if you are testing the branch itself.

For PoracleNG, use the `<sha>` listed in the release notes of the PoracleWeb.NET prerelease you are
installing. That is the PoracleNG commit the prerelease was built and checked against. A newer
`feat/personal-shiny` build may work, but nobody has tested the pair.

Once it is running, the user menu shows an **Experimental** chip under the profile name. If you don't
see it, you are on the official image.

## Configure PoracleNG

Two blocks in PoracleNG's `config.toml`.

```toml
[shiny_rates]
url = "http://golbat:9001"     # base URL of a Golbat-format shiny-rate feed
token = ""                     # optional bearer token
#max_bytes = 16777216          # largest feed response accepted (default 16 MiB)

[player_accounts]
self_service = false
```

### The rate feed

Personal shiny needs to know each species' shiny rate, so PoracleNG loads a feed in the format Golbat
uses for its own `[shiny_rates]`: PoracleNG requests `<url>/v1/rates?compact=1`. Point `url` at
whatever serves that, usually the Golbat you already run for the same data.

Until the feed has loaded, personal rules match nothing. With `url` empty, the feature is off, and
every personal rule stays silent. **Admin → Settings** shows a warning when PoracleNG supports
personal alerts but has no feed configured, so you hear about it before your users do.

The roll itself is a copy of Golbat's implementation (Golbat PR #423, pinned at `bec2c233`), so for the
same spawn, rate and player ID, PoracleNG and a Golbat-backed map agree.

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
linked accounts but can't change them.

## Link game accounts

A human can have up to 50 linked accounts. Player IDs are case-sensitive and are stored exactly as
typed, so `AbC` and `abc` are different accounts. Each account can carry an optional label to help you
tell them apart.

There are three ways to do it.

**The trainer dialog.** Open the user menu and choose **Game accounts**. The entry appears only when
PoracleNG supports trainer game accounts. With `self_service` on, you can link, relabel and unlink.
With it off, the dialog is read-only and says why, with a link to your support page if one is set.
Admins can always edit, and the dialog is read-only while an admin is viewing another account.

**Admin → Users.** Each row has a **Game accounts** button that opens the same dialog for that user.
Admins can edit whatever `self_service` says. This is the route to use when self-service is off.

**The bot.** `!account` (or `/account`) lists, adds, removes and labels linked accounts, subject to
the same `self_service` rule.

## Create a rule

1. Link at least one game account.
2. Add or edit a Pokemon rule and open the **Filters** tab.
3. In the **Personal** section, switch on **Shiny for my accounts**, **Event background for my
   accounts**, or both.

The section appears only when PoracleNG's schema declares the fields, so it is absent on a server
without #255. If a toggle is on and you have no linked account, the dialog says the rule matches
nothing yet and links to the Game accounts dialog. Cards for these rules carry a "Shiny for me" or
"Background for me" badge.

From the bot, the equivalent is `!track <pokemon> personalshiny` or `personalbackground`.

A personal rule matches nothing while the rate feed hasn't loaded, or while the human has no linked
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
- Editing such a rule on the official build can drop the setting, and until a fix for that is public
  you should expect it to. The rule then saves as an ordinary rule and starts matching every spawn
  that fits its other filters. Don't edit personal rules from the official build.

This page doesn't cover what a PoracleNG build without #255 does with the `personal` column, because
that depends on the build. Check its release notes before pointing the database at one.

## Related

- [Alarm Management](alarms.md) for the Pokemon rule dialogs this adds to
- [PoracleNG Version Compatibility](../architecture/poracleng-compatibility.md) for how version support is decided
