# PoracleWeb.NET

A web application for managing [Poracle](https://github.com/KartulUdworworkin/PoracleJS) Pokemon GO notification alarms. Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids, Quests, Invasions, Lures, Nests, Gyms) through a browser-based UI.

**[Documentation](https://pgan-dev.github.io/PoracleWeb.NET/)** | **[Changelog](CHANGELOG.md)**

## Tech Stack

- **Backend**: .NET 10 / ASP.NET Core Web API, EF Core with MySQL (Oracle provider)
- **Frontend**: Angular 21, Angular Material 21 (Material Design 3), Leaflet maps
- **Auth**: Discord OAuth2, Telegram Bot Login, JWT bearer tokens
- **Testing**: Jest (frontend), xUnit (backend)
- **CI/CD**: GitHub Actions, Docker (ghcr.io)

## Quick Start (Docker)

```bash
# 1. Configure
cp .env.example .env
# Edit .env with your database, Discord, and Poracle settings

# 2. Pull and run
docker pull ghcr.io/pgan-dev/poracleweb.net:latest
docker compose up -d
```

The app will be available at **http://localhost:8082**.

See the [Quick Start guide](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/quick-start/) for detailed setup instructions.

## Features

- **Alarm Management** — Pokemon, Raids, Quests, Invasions, Lures, Nests, Gyms
- **Bulk Operations** — Multi-select with bulk delete and distance update
- **Custom Geofences** — Draw polygons, auto-served to PoracleJS via unified feed
- **Geofence Admin Review** — Approve/reject with Discord forum integration
- **Quick Picks** — One-click alarm templates
- **Profile Switching** — Multiple alarm profiles per user
- **DTS Preview** — Live Discord notification template preview
- **Dark/Light Mode** — Theme toggle with accent color customization
- **Server Management** — Monitor and restart PoracleJS instances remotely
- **18 Languages** — Pokemon name localization
- **Admin Panel** — User management, webhooks, settings, geofence review

## Documentation

Full documentation is available at **[pgan-dev.github.io/PoracleWeb.NET](https://pgan-dev.github.io/PoracleWeb.NET/)**:

- [Quick Start (Docker)](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/quick-start/)
- [Development Setup](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/development-setup/)
- [Configuration Reference](https://pgan-dev.github.io/PoracleWeb.NET/configuration/reference/)
- [Architecture Overview](https://pgan-dev.github.io/PoracleWeb.NET/architecture/overview/)
- [Custom Geofences](https://pgan-dev.github.io/PoracleWeb.NET/features/custom-geofences/)
- [Server Management](https://pgan-dev.github.io/PoracleWeb.NET/features/server-management/)
- [Troubleshooting](https://pgan-dev.github.io/PoracleWeb.NET/troubleshooting/)

## Development

```bash
# Clone
git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
cd PoracleWeb.NET

# Backend (http://localhost:5048)
cd Applications/Pgan.PoracleWebNet.Api
dotnet run

# Frontend (http://localhost:4200)
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm install && npm start

# Tests
dotnet test                  # Backend
cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm test  # Frontend
```

See the [Development Setup guide](https://pgan-dev.github.io/PoracleWeb.NET/getting-started/development-setup/) for full instructions.

## CI/CD

- **ci.yml** — Builds backend, runs tests, builds frontend, runs lint/prettier/jest
- **docker-publish.yml** — Builds and publishes Docker image to [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net)
