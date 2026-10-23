# Changelog (experimental line)

This file lists only what the experimental line adds to the official PoracleWeb.NET release it is based on. It is not a full changelog: everything else is in [CHANGELOG.md](CHANGELOG.md), which this branch does not touch.

Versions are the official release number plus a `-shiny.N` suffix (for example `2.20.2-shiny.1`). The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This line is unofficial and unsupported by PoracleWeb.NET releases. Report problems with the `experimental-shiny` label.

## [Unreleased]

## [3.1.0-shiny.1] - 2026-10-23

Based on PoracleWeb.NET 3.1.0. Needs the same PoracleNG as 3.0.0-shiny.1: `feat/personal-shiny` ([jfberry/PoracleNG#255](https://github.com/jfberry/PoracleNG/pull/255)) rebased on 5.4.0, tested against the `ghcr.io/jfberry/poracleng:feat-personal-shiny` image built on 22 October 2026 from `3104d18d` (`sha256:ae21dba6d697b820399a143510cb801a6ab42f1251143cc42101d0299d187d5d`). Earlier `feat/personal-shiny` builds, and PoracleNG `main`, are not supported.

### Changed

- **Rebased onto PoracleWeb.NET 3.1.0.** It separates people from destinations: Admin > Webhooks becomes Admin > Destinations and also lists and creates Discord channels and threads and Telegram channels, groups and topics, Admin > Users lists people only, a channel or webhook can no longer sign in, delegates can be granted any destination and manage them from My Destinations, and Mention shows only on webhooks and Discord destinations (#1025). The PoracleNG this line needs is unchanged. See [CHANGELOG.md](CHANGELOG.md) for details.

## [3.0.1-shiny.1] - 2026-10-23

Based on PoracleWeb.NET 3.0.1. Needs the same PoracleNG as 3.0.0-shiny.1: `feat/personal-shiny` ([jfberry/PoracleNG#255](https://github.com/jfberry/PoracleNG/pull/255)) rebased on 5.4.0, tested against the `ghcr.io/jfberry/poracleng:feat-personal-shiny` image built on 22 October 2026 from `3104d18d` (`sha256:ae21dba6d697b820399a143510cb801a6ab42f1251143cc42101d0299d187d5d`). Earlier `feat/personal-shiny` builds, and PoracleNG `main`, are not supported.

### Changed

- **Rebased onto PoracleWeb.NET 3.0.1.** It brings that release's security fix: an impersonation session is re-checked on every request again, so a revoked delegate loses access promptly and a newly granted one is no longer refused until a restart (#1039). It also brings the region and group picker headings fix (#1030), and the rule repair scan now judges rules against the fields the server declares (#1021). The PoracleNG this line needs is unchanged. See [CHANGELOG.md](CHANGELOG.md) for details.

## [3.0.0-shiny.1] - 2026-10-22

Based on PoracleWeb.NET 3.0.0. Needs PoracleNG `feat/personal-shiny` ([jfberry/PoracleNG#255](https://github.com/jfberry/PoracleNG/pull/255)) rebased on 5.4.0, tested against the `ghcr.io/jfberry/poracleng:feat-personal-shiny` image built on 22 October 2026. Earlier `feat/personal-shiny` builds, and PoracleNG `main`, are not supported.

### Changed

- **Rebased onto PoracleWeb.NET 3.0.0.** That release needs PoracleNG 5.4.0 and talks to it only through `/api/v2`, so this line now needs `feat/personal-shiny` rebased on 5.4.0: PoracleNG `develop` at `34421b82` or later, image `ghcr.io/jfberry/poracleng:feat-personal-shiny`. It also brings 3.0.0's redesigned alarm cards and add and edit dialogs; the "Shiny for me" and "Background for me" badges are now status chips on the shared card, and the "For my game accounts" block still opens the Filters tab. See [CHANGELOG.md](CHANGELOG.md) for the rest.
- **Personal shiny reads and writes go through v2.** A v2 read's `personal_shiny` and `personal_background` fold into the rule's `personal` bitmask, and an edit or create sends them back as the two booleans when the server's `/openapi.json` declares both. There is no v1 fallback any more: a personal bit on a server that doesn't declare the fields, or a reserved bit (4 or 8), is refused with "PoracleNG can't store this alarm as it is" instead of being written through v1.

## [2.21.0-shiny.1] - 2026-10-20

Based on PoracleWeb.NET 2.21.0. The experimental additions are unchanged from 2.20.2-shiny.1, so it needs the same PoracleNG: `feat/personal-shiny` ([jfberry/PoracleNG#255](https://github.com/jfberry/PoracleNG/pull/255)), last tested at `37cc7a3`.

### Changed

- **Rebased onto PoracleWeb.NET 2.21.0.** It brings the Minior note in the Pokemon picker and edit dialog, the Docker `./data` ownership fix (run `mkdir -p data && sudo chown 999:999 data` before updating) and that release's dependency bumps. See [CHANGELOG.md](CHANGELOG.md) for details.

## [2.20.2-shiny.1] - 2026-10-18

The first prerelease of the experimental line. Based on PoracleWeb.NET 2.20.2, tested against PoracleNG `feat/personal-shiny` at `37cc7a3` (`ghcr.io/jfberry/poracleng:37cc7a3`).

### Added

- **An experimental release line with its own Docker image.** `ghcr.io/pgan-dev/poracleweb.net-experimental` is built from the `experimental/shiny` branch: every push publishes `edge-<sha>`, and a `-shiny.N` prerelease publishes that version plus `shiny`. There is no `latest` tag, so nobody lands on this line by pulling the default. The official `ghcr.io/pgan-dev/poracleweb.net` image and its workflow are untouched.
- **`GET /api/version` reports a `channel`.** It is `experimental` in images built by the experimental workflow (`RELEASE_CHANNEL` build argument) and `official` everywhere else, including a local build with nothing set.
- **Pokemon rules carry PoracleNG's `personal` bitmask** (1 = shiny for one of your linked game accounts, 2 = event background). PoracleWeb.NET reads it on every pokemon rule and sends the reserved bits (4 and 8) back unchanged on every edit. PoracleNG doesn't store them yet: it masks `personal` to the bits it knows on every write, so a reserved bit only survives once PoracleNG defines it.
- **An "Experimental" chip in the user menu**, under the profile name, shown only when the server reports the `experimental` channel. Admin > Settings > Versions says what the build is (unofficial, adds personal shiny alerts, not supported by official releases), where the people who run the server will read it. Translated in all eleven languages.
- **Pokemon edits write `personal` through PoracleNG's v2 API** when the server declares `personal_shiny` and `personal_background`: a set shiny or background bit goes out as the matching boolean, and a clear bit writes nothing. A rule carrying a reserved bit (4 or 8) on a server that declares the fields is edited through v1, since v2 has no field for it. A personal flag on a server that does not declare the fields is refused rather than written (next entry).
- **A pokemon rule with a personal shiny or background bit is refused on a PoracleNG that cannot store it.** Without personal-shiny support the server answers 200 and drops the bit, which would save a "shiny only" rule that matches every spawn. Create, edit, bulk create and profile import answer 400 with "Personal shiny and background alerts need a PoracleNG with personal-shiny support." Rules that carry no personal bit are unaffected. A profile duplicate is not checked because it copies rules the same server already holds, and quick picks cannot carry the bit at all.
- **Linked game accounts are usable over the API, and the player ID never leaves the server.** `GET/POST api/accounts`, `PUT/DELETE api/accounts/{id}` and `POST api/accounts/restore` list, link, relabel, unlink and restore the signed-in trainer's accounts, and `api/admin/accounts?id=` does the same for any human. Every account is answered as `{ id, label, createdAt }`, where `id` is an opaque handle (HMAC-SHA256 over the human and the player ID, keyed from the JWT secret), so a player ID, the input to someone's shiny rolls, appears in no response. An unlink answers a sealed undo token, good for five minutes and only for the human it came from, that `restore` takes in place of the ID. The one way an ID arrives is a link request. PoracleNG's account API ignores its own `[player_accounts] self_service` setting, so PoracleWeb.NET enforces it: a trainer can write only when self-service is on (otherwise 403 with `code: self_service_disabled`), nobody can while impersonating, and an administrator always can, their own accounts included. A PoracleNG without the route answers 501, reads included. Player IDs are case-sensitive and pass through untouched.
- **A Game accounts dialog for trainers and administrators, where linking points to the map.** Linking belongs to the map that pushes accounts into PoracleNG (ReactMap's personal shiny support, Diadem), which looks the player ID up from a friend code. So the user menu's "Game accounts" entry (only when PoracleNG has trainer game accounts) lists your accounts by label, or "Account 1", "Account 2", never by player ID, and says where to link one: the new **Game accounts link URL** site setting with an **Open the map** button, or the bot's `!account` when self-service is on, or an admin, with the support link when one is set. With self-service on a trainer can relabel and unlink, and an unlink offers Undo, because removing the last account quietly stops every personal rule. While viewing another account it is read-only and says why. In Admin > Users each row gets a Game accounts button that opens the same list for that user with a masked player ID field (and a show/hide toggle), checked as you type against PoracleNG's own rule, for support cases and servers whose map doesn't link accounts. Admin > Settings warns when PoracleNG supports personal alerts but has no `[shiny_rates] url`, names the feed most servers use, and says to confirm with `!poracle-admin status` that it loaded. Translated in all eleven languages.
- **"Shiny for me" on pokemon rules.** The add and edit dialogs open their Filters tab with a "For my game accounts" block, shown only when PoracleNG supports personal rules: "Only when it's shiny for me" and "Only when it has the event background for me". Under the toggles one line says whether the rule can fire: which of your accounts it checks, that the server has no shiny rate feed yet, or that it won't alert until an account is linked and where that happens: an **Open the map** button when the link URL is set, the bot's `!account` when self-service is on, or an admin (plus the support link). Accounts are named by label or "Account N", never by player ID. If the accounts could not be read it says nothing rather than claiming you have none. Cards for these rules show the shiny sprite (falling back to the ordinary one when the icon pack has no shiny art) and "Shiny for me" or "Background for me" badges under the name. Editing keeps the reserved bits (4 and 8), so a rule holding one saves it back unchanged. Translated in all eleven languages.
- **A setup page for the experimental line.** [Personal Shiny Alerts (Experimental)](docs/features/personal-shiny.md) covers the two images and their tags, PoracleNG's `[shiny_rates]` feed (the community feed most servers use, or a static file of your own) and `[player_accounts] self_service` (and what turning it on means), what a player ID is and how to find it, how this coexists with ReactMap's personal shiny linking, creating a rule, and what happens to personal rules if you go back to the official image. On this branch `docker-compose.yml.example` points at the experimental image, so copying it lands you on the line you cloned.

[Unreleased]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.21.0-shiny.1...experimental/shiny
[2.21.0-shiny.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.20.2-shiny.1...v2.21.0-shiny.1
[2.20.2-shiny.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v2.20.2...v2.20.2-shiny.1
