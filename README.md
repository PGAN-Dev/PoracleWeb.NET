# PoracleWeb.NET

A web application for managing Pokemon GO notification alarms through the [PoracleNG](https://github.com/jfberry/PoracleNG) bot. Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids and Eggs, Max Battles, Quests, Invasions, Lures, Nests, Gyms, Fort Changes) through a browser-based UI.

> **PoracleNG 5.1.0 or newer is required.** All alarm management, profile handling, and user operations are proxied through PoracleNG's REST API. On an older server, per-alarm delivery scope, the PVP mega evolution filter and the minimum time-left filter write columns that don't exist: the controls accept input, save, and change nothing. PoracleWeb logs an error at startup and shows the mismatch on the Versions card under Admin > Settings.
>
> [PoracleJS](https://github.com/KartulUdus/PoracleJS) is not a tested or supported configuration — some operations that rely on PoracleNG-specific endpoints will not work.

**[Documentation](https://pgan-dev.github.io/PoracleWeb.NET/)** | **[Changelog](CHANGELOG.md)**

## Tech Stack

- **Backend**: .NET 10 / ASP.NET Core Web API, EF Core with MySQL (Oracle provider)
- **Frontend**: Angular 21, Angular Material 21 (Material Design 3), Leaflet maps
- **Auth**: Discord OAuth2, Telegram Bot Login, JWT bearer tokens
- **Testing**: Jest (frontend), xUnit (backend)
- **CI/CD**: GitHub Actions, Docker (ghcr.io)

## Community & Support

There is **no Discord server** for PoracleWeb.NET at this time. All support, bug reports, feature requests, and community discussion happen directly on GitHub:

- **[Issues](https://github.com/PGAN-Dev/PoracleWeb.NET/issues)** — bug reports and feature requests
- **[Discussions](https://github.com/PGAN-Dev/PoracleWeb.NET/discussions)** — questions, ideas, and general conversation
- **[Pull Requests](https://github.com/PGAN-Dev/PoracleWeb.NET/pulls)** — contributions welcome

## Quick Start (Docker)

```bash
# 1. Copy the templates
cp .env.example .env
cp docker-compose.yml.example docker-compose.yml
# Edit .env with your database, Discord, and Poracle settings.
# docker-compose.yml rarely needs changes — all config flows through .env.

# 2. Pull and run
docker compose pull
docker compose up -d
```

The app will be available at **http://localhost:8082**.

See the [Quick Start guide](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/quick-start/) for detailed setup instructions.

## Features

- **Alarm Management** — Pokemon, Raids and Eggs, Max Battles, Quests, Invasions, Lures, Nests, Gyms, Fort Changes
- **Gym Picker** — Search and target specific gyms for team change, raid, and egg alarms
- **Bulk Operations** — Multi-select with bulk delete and distance update
- **Per-Alarm Delivery Scope** — Aim each alert anywhere in your areas, at only specific areas, or within a radius of your pin or a saved place
- **Saved Places** — Name the points your alerts measure from, so an alarm doesn't have to follow your profile pin
- **Test Alerts** — Send yourself a sample notification for any alarm to check its filters and template
- **Alert Defaults** — Choose where new alerts default to reaching you: your areas, or a radius from your pin or a saved place
- **Custom Geofences** — Draw polygons, auto-served to the Poracle bot via unified feed
- **Geofence Admin Review** — Approve/reject with Discord forum integration
- **Quick Picks** — One-click alarm templates
- **Profile Switching** — Multiple alarm profiles per user
- **Profile Active Hours** — Schedule automatic profile switching by day and time
- **DTS Preview** — Live Discord notification template preview
- **Dark/Light Mode** — Theme toggle with accent color customization
- **11 UI Languages** — the interface, plus Pokemon names, types and forms, which follow the display language. What Poracle writes in your DMs is a separate choice, **Alert language**, sitting beside **Display language** in the user menu
- **Single Sign-On** — Discord and Telegram login, plus any OIDC provider, with optional silent refresh and single logout
- **Admin Panel** — User management, webhooks, settings, geofence review, and a Versions card showing the running PoracleWeb and PoracleNG builds. Opening that card runs an anonymous GitHub check, cached six hours, switched off with the **Do not check for updates** (`disable_update_check`) site setting

## Documentation

Full documentation is available at **[pgan-dev.github.io/PoracleWeb.NET](https://pgan-dev.github.io/PoracleWeb.NET/)**:

- [Quick Start (Docker)](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/quick-start/)
- [Development Setup](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/development-setup/)
- [Configuration Reference](https://pgan-dev.github.io/PoracleWeb.NET/configuration/reference/)
- [Architecture Overview](https://pgan-dev.github.io/PoracleWeb.NET/architecture/overview/)
- [Custom Geofences](https://pgan-dev.github.io/PoracleWeb.NET/features/custom-geofences/)
- [Troubleshooting](https://pgan-dev.github.io/PoracleWeb.NET/troubleshooting/)

## Development

```bash
# Clone
git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
cd PoracleWeb.NET

# First-time setup (interactive; writes .env) and frontend dependencies
./scripts/setup.sh
./scripts/dev.sh install

# Run both servers — API on http://localhost:5048, Angular on http://localhost:4200
./scripts/dev.sh start

# Or one at a time
./scripts/dev.sh api
./scripts/dev.sh app

# Tests
./scripts/dev.sh test
```

Run everything from the repo root. `Program.cs` reads `.env` from the working directory, so
`cd`-ing into the API project before `dotnet run` starts the app with no connection strings, no JWT
secret and no Poracle API address. `scripts/dev.sh` exports `.env` for you; without it, use
`dotnet run --project Applications/Pgan.PoracleWebNet.Api` from the root.

See the [Development Setup guide](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/development-setup/) for full instructions.

## Branch Naming

Use conventional prefixes so PRs are auto-labeled and release notes group correctly:

| Prefix | Example | Release-note section |
|---|---|---|
| `feat/` | `feat/test-alerts` | Features |
| `fix/` | `fix/jwt-desync` | Bug Fixes |
| `perf/` | `perf/dashboard-counts` | Performance |
| `docs/` | `docs/geofence-readme` | Documentation |
| `refactor/` | `refactor/remove-unitofwork` | Refactors |
| `test/` | `test/alarm-mappings` | Tests |
| `build/`, `ci/` | `ci/docker-prune` | Build & CI |
| `chore/` | `chore/bump-deps` | Chores |
| `breaking/` | `breaking/v3-api` | Breaking Changes |

Conventional Commit style in the PR title (`feat: ...`, `fix(scope)!: ...`) works too and is preferred for PRs where the branch name can't be controlled (e.g. Dependabot). The `!` marker promotes the PR into the Breaking Changes section.

## Release Channels

Three Docker channels are published to GHCR — see [TESTING.md](TESTING.md) for details.

| Channel | Tag | Trigger |
|---|---|---|
| Stable | `:latest`, `:X.Y.Z`, `:X.Y` | Release published |
| Beta | `:beta`, `:develop-<sha>` | Every push to `develop` |
| PR preview | `:pr-<number>` | PRs with the `preview` label |

The same split applies when you **build from source**:

| You check out | You get |
|---|---|
| `main`, or a release tag | Stable — the same code as `:latest` |
| `develop` | Beta — every merged PR, including work that has never been in a release |

`main` only moves when a release is published, so a plain `git clone` gives you released code. `develop` is where changes soak first; running it means running code that has not shipped yet.

## Branches

| Branch | Purpose |
|---|---|
| `main` | Released code. Only moves on a release. Publishes `:latest`. |
| `develop` | Integration. **Pull requests target this.** Publishes `:beta` on every merge. |

Cutting a release means merging `develop` into `main` and publishing a GitHub release; the changelog is promoted from `[Unreleased]` automatically.

## CI/CD

- **ci.yml** — Builds backend, runs tests, builds frontend, runs lint/prettier/jest
- **docker-publish.yml** — Builds and publishes Docker image to [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net) (`:latest` on release, `:beta` on `develop`)
- **docker-preview.yml** — Builds `:pr-<number>` images on PRs labeled `preview`
- **docker-prune.yml** — Nightly cleanup of stale `pr-*` and `develop-<sha>` tags
- **pr-labeler.yml** — Auto-labels PRs from branch prefix / PR title for release-note grouping
- **changelog.yml** — Checks that a PR adds an entry under `## [Unreleased]` in CHANGELOG.md
- **release-changelog.yml** — On a published release, opens a PR promoting `[Unreleased]` to the new version section
- **docs.yml** — Builds the MkDocs site and deploys it to GitHub Pages
- **auto-merge-deps.yml** — Enables auto-merge on low-risk Dependabot bumps (patches, curated groups, Actions minors); majors wait for review

`.github/release.yml` is a config file, not a workflow: it groups PRs by label when GitHub generates
release notes.

## Credits

PoracleWeb.NET stands on the shoulders of these projects and their authors:

- **[PoracleJS](https://github.com/KartulUdus/PoracleJS)** by KartulUdus — the original Poracle bot (alarm management in this app uses PoracleNG's REST API)
- **[PoracleNG](https://github.com/jfberry/PoracleNG)** by jfberry — next-generation fork whose REST API powers all alarm tracking
- **[PoracleWeb (PHP)](https://github.com/bbdoc/PoracleWeb)** by bbdoc — the original PHP web interface that inspired this .NET rewrite
- **[Kōji](https://github.com/TurtIeSocks/Koji)** by TurtIeSocks — geofence management platform used for admin areas, region detection, and public geofence promotion
