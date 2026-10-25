# PoracleWeb.NET

A web application for managing Pokemon GO notification alarms through the [PoracleNG](https://github.com/jfberry/PoracleNG) bot. Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids and Eggs, Max Battles, Quests, Invasions, Pokéstop Events, Lures, Nests, Gyms, Fort Changes) through a browser-based UI.

> **PoracleNG 5.4.0 or newer is required.** All alarm management, profile handling, and user operations are proxied through PoracleNG's REST API. PoracleWeb.NET still starts against an older PoracleNG, but it logs an error at startup and shows the mismatch on the Versions card under Admin > Server settings, and the calls that need a 5.4.0 route fail. Upgrade PoracleNG first. Why 5.4.0, and what it replaced, is in [PoracleNG Version Compatibility](https://pgan-dev.github.io/PoracleWeb.NET/architecture/poracleng-compatibility/).
>
> [PoracleJS](https://github.com/KartulUdus/PoracleJS) is not a tested or supported configuration — some operations that rely on PoracleNG-specific endpoints will not work.

**[Documentation](https://pgan-dev.github.io/PoracleWeb.NET/)** | **[Changelog](CHANGELOG.md)**

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 / ASP.NET Core Web API, EF Core with MySQL (Oracle provider) |
| Frontend | Angular 21, Angular Material 21 (Material Design 3), Leaflet maps |
| Auth | Discord OAuth2, Telegram Bot Login, OIDC, JWT bearer tokens |
| Testing | Jest (frontend), xUnit (backend) |
| CI/CD | GitHub Actions, Docker (ghcr.io) |

## Community & Support

Questions and chat happen in the PoracleWeb.NET channels on the [Poracle Discord server](https://discord.gg/MHVPAXDFr6). Report bugs and request features in [Issues](https://github.com/PGAN-Dev/PoracleWeb.NET/issues), so they don't get lost in chat, and send contributions as [Pull Requests](https://github.com/PGAN-Dev/PoracleWeb.NET/pulls). [Discussions](https://github.com/PGAN-Dev/PoracleWeb.NET/discussions) are open too, for anything that suits a thread better than a chat.

## Quick Start (Docker)

```bash
# 1. Copy the templates
cp .env.example .env
cp docker-compose.yml.example docker-compose.yml
# Edit .env with your database, Poracle and sign-in settings.
# docker-compose.yml rarely needs changes — all config flows through .env.

# 2. Create the data directory, owned by the user the container runs as (uid 999)
mkdir -p data && sudo chown 999:999 data

# 3. Pull and run
docker compose pull
docker compose up -d
```

The app will be available at **http://localhost:8082**.

See the [Quick Start guide](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/quick-start/) for detailed setup instructions.

## Features

| Feature | What it does |
|---|---|
| Alarm management | Pokemon, Raids and Eggs, Max Battles, Quests, Invasions, Pokéstop Events, Lures, Nests, Gyms, Fort Changes |
| Gym picker | Search for a gym by name and aim a gym, raid or egg alarm at it (needs the scanner database) |
| Pokemon availability | See which species are spawning now while you pick Pokemon for an alarm (needs Golbat) |
| Bulk operations | Select several alarms to delete them or change their radius in one go; on a phone the bar stays pinned to the bottom of the screen |
| Per-alarm delivery scope | Aim each alert anywhere in your areas, at only specific areas, or within a radius of your pin or a saved place |
| Saved places | Name the points your alerts measure from, so an alarm doesn't have to follow your profile pin |
| Test alerts | Send yourself a sample notification for an alarm to check its filters and template (every type except Fort Changes, Max Battles and Pokéstop Events) |
| Quiet periods | Silence one gym, area, species or Power Spot for a set time, without touching the alarms themselves |
| Alert defaults | Choose where new alerts default to reaching you: your areas, or a radius from your pin or a saved place |
| Custom geofences | Draw polygons, served to the Poracle bot through a unified feed |
| Hidden areas | Take a staging or test area off the area lists and the bot's area list without deleting it |
| Geofence review | Admins approve or reject submitted geofences, with a Discord forum thread for each |
| Quick picks | One-click alarm templates |
| Profiles | Several alarm profiles per user, with active hours that switch between them by day and time |
| DTS preview | Live preview of the Discord notification template |
| Dark and light mode | Theme toggle with accent colours; maps on the site default switch to a dark basemap in dark mode |
| 11 UI languages | The interface, plus Pokemon names, types and forms, which follow the display language. What Poracle writes in your DMs is a separate choice, **Alert language**, beside **Display language** in the user menu |
| Single sign-on | Discord and Telegram login, plus any OIDC provider, with optional silent refresh and single logout |
| Destinations and delegates | Webhooks, Discord channels and threads, and Telegram channels, groups and topics, managed as their own accounts, with named people allowed to manage one without being made an admin |
| Weather | Current in-game weather at your pin and across your areas, on the dashboard (needs the scanner database) |
| Linked accounts | One person on both Discord and Telegram: one list of alarms, the app chosen per alarm, personal settings saved to both, and the differences bot commands leave shown for the person to settle |
| Admin panel | Users, destinations, site settings, geofence review, hidden areas, a status line on the settings page that opens a Versions card showing the running PoracleWeb.NET and PoracleNG builds, and a Rule repair card for rules PoracleNG 5.4.0 reads differently. The Versions card runs an anonymous GitHub check, cached six hours, which the `disable_update_check` site setting switches off |

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

| Workflow | What it does |
|---|---|
| `ci.yml` | Builds the backend, runs its tests, builds the frontend, runs lint, Prettier and Jest |
| `docker-publish.yml` | Builds and publishes the Docker image to [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net) (`:latest` on a full release, `:beta` on `develop`; prereleases are skipped) |
| `docker-preview.yml` | Builds `:pr-<number>` images on PRs labelled `preview` (not for PRs into `experimental/*`) |
| `docker-prune.yml` | Nightly cleanup of stale `pr-*` and `develop-<sha>` tags |
| `pr-labeler.yml` | Labels PRs from the branch prefix or PR title for release-note grouping |
| `changelog.yml` | Checks that a PR adds an entry under `## [Unreleased]` in CHANGELOG.md |
| `release-changelog.yml` | On a published full release, opens a PR promoting `[Unreleased]` to the new version section |
| `sync-experimental.yml` | After a full release, opens a merge PR from `main` into each `experimental/*` branch that is behind |
| `docs.yml` | Builds the MkDocs site and deploys it to GitHub Pages |
| `auto-merge-deps.yml` | Enables auto-merge on low-risk Dependabot bumps (patches, curated groups, Actions minors); majors wait for review |

`.github/release.yml` is a config file, not a workflow: it groups PRs by label when GitHub generates
release notes.

## Credits

PoracleWeb.NET stands on the shoulders of these projects and their authors:

| Project | Author | Role |
|---|---|---|
| [PoracleJS](https://github.com/KartulUdus/PoracleJS) | KartulUdus | The original Poracle bot |
| [PoracleNG](https://github.com/jfberry/PoracleNG) | jfberry | Next-generation Poracle whose REST API powers all alarm tracking |
| [Kōji](https://github.com/TurtIeSocks/Koji) | TurtIeSocks | Geofence management platform used for admin areas, region detection and public geofence promotion |
| [Golbat](https://github.com/UnownHash/Golbat) | Unown# team | Scanner whose API shows which Pokemon are spawning in the species picker; its database backs the gym picker and the weather card |
| [Masterfile Generator](https://github.com/WatWowMap/Masterfile-Generator) | WatWowMap | Game data: raid levels, and the English names used when Poracle can't supply translated ones |
| UICONS icon packs | [nileplumb](https://github.com/nileplumb/PkmnHomeIcons), [jms412](https://github.com/jms412/PkmnHomeIcons) | Pokemon, item and raid icons; jms412's Home icons are the default |
| [Leaflet](https://leafletjs.com/) | Leaflet contributors | Every map on the site |
| Map tiles | [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, CARTO, Esri | The basemaps an admin or viewer can choose |
