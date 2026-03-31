# PoracleWeb.NET

A web application for managing Pokemon GO notification alarms through the Poracle bot system. Compatible with both [PoracleJS](https://github.com/KartulUdus/PoracleJS) and [PoracleNG](https://github.com/jfberry/PoracleNG). Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids, Quests, Invasions, Lures, Nests, Gyms) through a browser-based UI.

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider) |
| **Frontend** | Angular 21, Angular Material 21 (Material Design 3), Leaflet maps |
| **Auth** | Discord OAuth2, Telegram Bot Login, JWT bearer tokens |
| **Testing** | Jest (frontend), xUnit (backend) |
| **CI/CD** | GitHub Actions, Docker (ghcr.io) |

## Features

- **Alarm Management** — Create, edit, and delete filters for Pokemon, Raids, Quests, Invasions, Lures, Nests, and Gyms
- **Gym Picker** — Search and target specific gyms for team, raid, and egg alarms with photo thumbnails and area names
- **Bulk Operations** — Multi-select alarms with bulk delete and bulk distance update
- **Quick Picks** — Admin-defined alarm templates users can apply with one click
- **Area Management** — Interactive Leaflet map for selecting geofence areas
- **Custom Geofences** — Draw custom polygon geofences on a map, served to PoracleJS via a built-in feed endpoint. Submit for admin review to promote to public areas.
- **Geofence Admin Review** — Approve or reject user-submitted geofences with Discord forum integration
- **Profile Switching** — Multiple alarm profiles per user
- **Discord Notification Preview** — Live preview of DTS templates with Handlebars evaluation
- **Dark/Light Mode** — Theme toggle with localStorage persistence
- **Accent Themes** — Customizable toolbar and UI accent colors (Pokemon, Raids, Mystic, Valor, Instinct)
- **Responsive Design** — Full mobile support with fullscreen dialogs and collapsible sidebar
- **Onboarding Wizard** — First-run setup guide for new users
- **Keyboard Shortcuts** — ++question++ for help, ++bracket-left++ / ++bracket-right++ for sidebar collapse
- **18 Languages** — Pokemon name localization
- **Poracle Server Management** — Monitor health and restart PoracleJS instances remotely
- **Admin Panel** — User management, webhook configuration, site settings, geofence submission review

## Prerequisites

| Requirement | Version | Purpose |
|---|---|---|
| MySQL | 5.7+ or 8.0+ | Poracle database (existing Poracle installation) |
| Poracle | PoracleJS | Running instance with REST API enabled |
| Discord App | — | OAuth2 application for user authentication |
| Koji | — | Geofence management server (required for custom geofences feature) |
| .NET SDK | 10.0 | Backend development (not needed for Docker) |
| Node.js | 22+ | Frontend development (not needed for Docker) |
| Docker | 20+ | Production deployment |

## Quick Links

<div class="grid cards" markdown>

-   :material-rocket-launch:{ .lg .middle } **Getting Started**

    ---

    Get up and running with Docker or a development environment

    [:octicons-arrow-right-24: Quick Start](getting-started/quick-start.md)

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

    [:octicons-arrow-right-24: Custom Geofences](features/custom-geofences.md)

</div>
