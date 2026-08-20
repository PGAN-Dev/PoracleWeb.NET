---
template: home.html
---

# PoracleWeb.NET

A web application for managing Pokemon GO notification alarms through the [PoracleNG](https://github.com/jfberry/PoracleNG) bot. Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids, Quests, Invasions, Lures, Nests, Gyms) through a browser-based UI.

!!! warning "PoracleNG is required"
    All alarm management, profile handling, and user operations are proxied through PoracleNG's REST API. [PoracleJS](https://github.com/KartulUdus/PoracleJS) is not a tested or supported configuration — some operations that rely on PoracleNG-specific endpoints will not work.

    **PoracleNG 5.1.0 or newer is required.** Older servers have no column to store per-alarm delivery scope, the PVP mega evolution filter or the minimum time-left filter, so those three controls save without complaint and change nothing. PoracleWeb logs an error at startup when it finds an older server, and reports the version on Admin → Settings.

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider) |
| **Frontend** | Angular 21, Angular Material 21 (Material Design 3), Leaflet maps |
| **Auth** | Discord OAuth2, Telegram Bot Login, JWT bearer tokens |
| **Testing** | Jest (frontend), xUnit (backend) |
| **CI/CD** | GitHub Actions, Docker (ghcr.io) |

## Features

- **Alarm Management** — Create, edit, and delete filters for Pokemon, Raids, Max Battles, Quests, Invasions, Lures, Nests, Gyms, and Fort Changes
- **Gym Picker** — Search and target specific gyms for team, raid, and egg alarms with photo thumbnails and area names
- **Pokemon Availability** — See which species are currently spawning when creating alarms (requires Golbat scanner)
- **Bulk Operations** — Multi-select alarms with bulk delete and bulk distance update
- **Alert Defaults** — Set where new alerts reach you by default: your areas, or a radius from your pin or a saved place
- **Per-Alarm Delivery Scope** — Aim an individual alert anywhere in your areas, near a place or a point on the map, or only in specific areas — including geofences you drew yourself
- **Saved Places** — Name the points your alerts measure from, so one alert can watch your workplace while the rest follow your pin
- **Quick Picks** — Admin-defined alarm templates users can apply with one click
- **Areas & Places** — Interactive Leaflet map for choosing geofence areas, dropping your pin, and naming the places your alerts measure from
- **Custom Geofences** — Draw custom polygon geofences on a map, served to the Poracle bot via a built-in unified feed endpoint. Submit for admin review to promote to public areas.
- **Geofence Admin Review** — Approve or reject user-submitted geofences with Discord forum integration
- **Profile Switching** — Multiple alarm profiles per user
- **Profile Active Hours** — Schedule automatic profile switching by day and time
- **Discord Notification Preview** — Live preview of DTS templates with Handlebars evaluation
- **Dark/Light Mode** — Theme toggle with localStorage persistence
- **Accent Themes** — Customizable toolbar and UI accent colors (Pokemon, Raids, Mystic, Valor, Instinct)
- **Responsive Design** — Full mobile support with fullscreen dialogs and collapsible sidebar
- **Onboarding Wizard** — First-run setup guide for new users
- **Keyboard Shortcuts** — ++question++ for help, ++bracket-left++ / ++bracket-right++ for sidebar collapse
- **11 UI Languages** — Full interface translation (English, French, German, Spanish, Dutch, Italian, Portuguese, Brazilian Portuguese, Polish, Danish, Swedish). Alert text and Pokemon names are localized separately from the same 11, chosen as **Alert language** in the user menu beside **Display language**
- **Single Sign-On** — Discord and Telegram login, plus any OIDC provider ([setup](configuration/external-sso.md)), with optional [silent refresh and single logout](configuration/oidc-refresh-tokens.md)
- **Admin Panel** — User management, webhook configuration, site settings, geofence submission review
- **Test Alerts** — Send a sample notification from an alarm card to preview exactly what your alerts look like (all types except Fort Changes and Max Battles)
- **Weather Display** — View current in-game weather at your pin and across all tracked areas on the dashboard
- **Fort Change Tracking** — Get notified when pokestops or gyms are added, removed, renamed, relocated, re-described, or given a new image
- **Max Battle (Dynamax) Alarms** — Track Dynamax and Gigantamax battles at Power Spots by level or specific Pokemon
- **GeoJSON Import/Export** — Import and export custom geofences in standard GeoJSON format
- **Profile Backup & Restore** — Export profiles as JSON backups and import them, including full alarm filter restoration
- **Profile Duplication** — Clone any profile with all its alarms in one click

## Prerequisites

| Requirement | Version | Purpose |
|---|---|---|
| MySQL | 5.7+ or 8.0+ | Poracle database (existing Poracle installation) |
| Poracle | [PoracleNG](https://github.com/jfberry/PoracleNG) 5.1.0 or newer | Running instance with REST API enabled. All alarm, profile, and user operations are proxied through PoracleNG's REST API. PoracleJS is not a tested configuration. |
| Discord App | — | OAuth2 application for user authentication |
| Koji | — | Geofence management server (required for custom geofences feature) |
| .NET SDK | 10.0 | Backend development (not needed for Docker) |
| Node.js | 22+ | Frontend development (not needed for Docker) |
| Docker | 20+ | Production deployment |

## Quick Links

<div class="grid cards" markdown>

-   :material-rocket-launch:{ .lg .middle } **Getting Started**

    ---

    Get up and running with Docker, standalone, or a development environment

    [:octicons-arrow-right-24: Quick Start (Docker)](getting-started/quick-start.md)

    [:octicons-arrow-right-24: Standalone Setup (no Docker)](getting-started/standalone-setup.md)

-   :material-cog:{ .lg .middle } **Configuration**

    ---

    Full reference of all environment variables and settings

    [:octicons-arrow-right-24: Configuration Reference](configuration/reference.md)

-   :material-layers-outline:{ .lg .middle } **Architecture**

    ---

    Solution structure, backend and frontend patterns

    [:octicons-arrow-right-24: Architecture Overview](architecture/overview.md)

-   :material-map-marker-radius:{ .lg .middle } **Custom Geofences**

    ---

    How the unified geofence feed works

    [:octicons-arrow-right-24: Custom Geofences](features/custom-geofences/index.md)

</div>

## Community & Support

There is **no Discord server** for PoracleWeb.NET at this time. All support, bug reports, feature requests, and community discussion happen directly on GitHub:

- **[Issues](https://github.com/PGAN-Dev/PoracleWeb.NET/issues)** — bug reports and feature requests
- **[Discussions](https://github.com/PGAN-Dev/PoracleWeb.NET/discussions)** — questions, ideas, and general conversation
- **[Pull Requests](https://github.com/PGAN-Dev/PoracleWeb.NET/pulls)** — contributions welcome

## Credits

PoracleWeb.NET stands on the shoulders of these projects and their authors:

| Project | Author | Role |
|---|---|---|
| [PoracleJS](https://github.com/KartulUdus/PoracleJS) | KartulUdus | The original Poracle bot — the notification engine this UI manages |
| [PoracleNG](https://github.com/jfberry/PoracleNG) | jfberry | Next-generation fork whose REST API powers all alarm tracking |
| [PoracleWeb (PHP)](https://github.com/bbdoc/PoracleWeb) | bbdoc | The original PHP web interface that inspired this .NET rewrite |
| [Kōji](https://github.com/TurtIeSocks/Koji) | TurtIeSocks | Geofence management platform used for admin areas, region detection, and public geofence promotion |
