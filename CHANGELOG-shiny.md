# Changelog (experimental line)

This file lists only what the experimental line adds to the official PoracleWeb.NET release it is based on. It is not a full changelog: everything else is in [CHANGELOG.md](CHANGELOG.md), which this branch does not touch.

Versions are the official release number plus a `-shiny.N` suffix (for example `2.20.0-shiny.1`). The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This line is unofficial and unsupported by PoracleWeb.NET releases. Report problems with the `experimental-shiny` label.

## [Unreleased]

### Added

- **An experimental release line with its own Docker image.** `ghcr.io/pgan-dev/poracleweb.net-experimental` is built from the `experimental/shiny` branch: every push publishes `edge-<sha>`, and a `-shiny.N` prerelease publishes that version plus `shiny`. There is no `latest` tag, so nobody lands on this line by pulling the default. The official `ghcr.io/pgan-dev/poracleweb.net` image and its workflow are untouched.
- **`GET /api/version` reports a `channel`.** It is `experimental` in images built by the experimental workflow (`RELEASE_CHANNEL` build argument) and `official` everywhere else, including a local build with nothing set.
- **An "Experimental" chip in the user menu**, under the profile name, with a tooltip saying this is an unofficial build with personal shiny alerts that PoracleWeb.NET releases do not support. It appears only when the server reports the `experimental` channel, and it is translated in all eleven languages.
