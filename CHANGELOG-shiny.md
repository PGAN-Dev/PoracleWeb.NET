# Changelog (experimental line)

This file lists only what the experimental line adds to the official PoracleWeb.NET release it is based on. It is not a full changelog: everything else is in [CHANGELOG.md](CHANGELOG.md), which this branch does not touch.

Versions are the official release number plus a `-shiny.N` suffix (for example `2.20.0-shiny.1`). The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This line is unofficial and unsupported by PoracleWeb.NET releases. Report problems with the `experimental-shiny` label.

## [Unreleased]

### Added

- **An experimental release line with its own Docker image.** `ghcr.io/pgan-dev/poracleweb.net-experimental` is built from the `experimental/shiny` branch: every push publishes `edge-<sha>`, and a `-shiny.N` prerelease publishes that version plus `shiny`. There is no `latest` tag, so nobody lands on this line by pulling the default. The official `ghcr.io/pgan-dev/poracleweb.net` image and its workflow are untouched.
- **`GET /api/version` reports a `channel`.** It is `experimental` in images built by the experimental workflow (`RELEASE_CHANNEL` build argument) and `official` everywhere else, including a local build with nothing set.
- **Pokemon rules carry PoracleNG's `personal` bitmask** (1 = shiny for one of your linked game accounts, 2 = event background). PoracleWeb.NET reads it on every pokemon rule and keeps the reserved bits (4 and 8) intact through every edit. No screen shows it yet.
- **An "Experimental" chip in the user menu**, under the profile name, with a tooltip saying this is an unofficial build with personal shiny alerts that PoracleWeb.NET releases do not support. It appears only when the server reports the `experimental` channel, and it is translated in all eleven languages.
- **Pokemon edits write `personal` through PoracleNG's v2 API** when the server declares `personal_shiny` and `personal_background`: a set shiny or background bit goes out as the matching boolean, and a clear bit writes nothing. A rule carrying a reserved bit (4 or 8) on a server that declares the fields is edited through v1, so the reserved bit is kept. A personal flag on a server that does not declare the fields is refused rather than written (next entry).
- **A pokemon rule with a personal shiny or background bit is refused on a PoracleNG that cannot store it.** Without personal-shiny support the server answers 200 and drops the bit, which would save a "shiny only" rule that matches every spawn. Create, edit, quick-pick apply and profile import now answer 400 with "Personal shiny and background alerts need a PoracleNG with personal-shiny support." Rules that carry no personal bit are unaffected, and a profile duplicate is not checked because it copies rules the same server already holds.
