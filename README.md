# PoGO Alerts Network - DM Alerts Configuration

Modern web UI for configuring Pokemon GO notification alarms via the [Poracle](https://github.com/KartulUdworworkerpackRuntimeKernel/PoracleJS) bot system. Manage Pokemon, Raid, Quest, Invasion, Lure, Nest, and Gym alerts through an intuitive dashboard with interactive maps, area selection, and live template previews.

![Angular](https://img.shields.io/badge/Angular-21-dd0031?logo=angular)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet)
![Material Design](https://img.shields.io/badge/Material%20Design-3-757575?logo=materialdesign)
![MySQL](https://img.shields.io/badge/MySQL-8-4479a1?logo=mysql&logoColor=white)
![Leaflet](https://img.shields.io/badge/Leaflet-1.9-199900?logo=leaflet)
![Docker](https://img.shields.io/badge/Docker-ready-2496ed?logo=docker&logoColor=white)

---

## Features

### Authentication
- **Discord OAuth2** login with avatar display
- **Telegram Bot Login** widget with hash validation
- **JWT sessions** with configurable expiration

### Alarm Management
- **Pokemon** -- IV, CP, Level, PVP rank filters; form/gender selection
- **Raids** -- Pokemon, level, exclusive/mega filtering
- **Eggs** -- Tier-based filtering
- **Quests** -- Filter by reward type (stardust, items, Pokemon encounters)
- **Invasions** -- Grunt type and gender filtering
- **Lures** -- Lure type selection
- **Nests** -- Pokemon species filtering
- **Gyms** -- Team, slot count, EX eligibility

### Areas & Location
- Interactive **Leaflet map** with geofence polygon overlays
- **Grouped area selection** with map previews
- **Address search** with Nominatim geocoding
- **Static map previews** for configured locations
- **Distance vs area** toggle for alarm range

### Dashboard
- Alarm counts per category
- Location overview with static map
- Active areas display
- Quick-action navigation to alarm types

### Template System
- Template selector with **live Discord embed preview**
- DTS (Dynamic Token Strings) parsing with Handlebars rendering
- Scenario toggles for preview customization

### Profiles
- Multiple alarm profiles per user
- Switch active profile from any page
- Per-profile area and alarm configuration

### Admin Panel
- User management with Discord avatars
- Server settings configuration
- Background avatar caching service

### UI/UX
- Material Design 3 theming
- Dark and light theme support
- Responsive mobile layout
- Tabbed alarm dialogs with organized filters
- Cleaning tool for bulk alarm removal

---

## Architecture

```
PGAN.Poracle.Web.slnx
|
+-- Core/
|   +-- Core.Abstractions     Interfaces for repositories, services, units of work
|   +-- Core.Models            DTOs and domain models
|   +-- Core.Mappings          AutoMapper profiles (Entity <-> Model)
|   +-- Core.Repositories      Repository implementations (BaseRepository<TEntity, TModel>)
|   +-- Core.Services          Business logic services
|   +-- Core.UnitsOfWork       Unit of Work wrapping PoracleContext
|
+-- Data/
|   +-- Data                   EF Core DbContext for Poracle MySQL database + Entities
|   +-- Data.Scanner           EF Core DbContext for Scanner (RDM) database (optional)
|
+-- Applications/
|   +-- Web.Api                ASP.NET Core API (Controllers, Auth, Configuration)
|   +-- Web.App                Angular 21 SPA (standalone components, Material Design 3)
|
+-- Tests/                     Test projects
```

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0+ |
| Node.js | 22+ |
| npm | 11+ |
| MySQL | 8.0+ (Poracle's existing database) |
| Poracle bot | Running instance with REST API enabled |

---

## Configuration

Create `appsettings.Development.json` in `Applications/PGAN.Poracle.Web.Api/` with the following sections:

```json
{
  "ConnectionStrings": {
    "PoracleDb": "Server=localhost;Port=3306;Database=poracle;User=root;Password=yourpassword",
    "ScannerDb": "Server=localhost;Port=3306;Database=scanner;User=root;Password=yourpassword"
  },
  "Jwt": {
    "Secret": "your-jwt-secret-minimum-32-characters-long",
    "Issuer": "PGAN.Poracle.Web",
    "Audience": "PGAN.Poracle.Web.App",
    "ExpirationMinutes": 1440
  },
  "Discord": {
    "ClientId": "your_discord_application_client_id",
    "ClientSecret": "your_discord_application_client_secret",
    "RedirectUri": "http://localhost:4200/auth/discord/callback"
  },
  "Telegram": {
    "Enabled": true,
    "BotToken": "your_telegram_bot_token",
    "BotUsername": "your_telegram_bot_username"
  },
  "Poracle": {
    "ApiAddress": "http://localhost:4Pokemon",
    "ApiSecret": "your_poracle_api_secret",
    "AdminIds": "discord_user_id_1,discord_user_id_2"
  }
}
```

### Where to find these values

| Setting | Source |
|---|---|
| `PoracleDb` | Your Poracle MySQL database connection |
| `ScannerDb` | (Optional) RDM scanner database connection |
| `Discord.ClientId` / `ClientSecret` | [Discord Developer Portal](https://discord.com/developers/applications) -- OAuth2 section |
| `Discord.RedirectUri` | Must match the redirect URI in your Discord app (use `http://localhost:4200/auth/discord/callback` for local dev) |
| `Telegram.BotToken` / `BotUsername` | [@BotFather](https://t.me/BotFather) on Telegram |
| `Poracle.ApiAddress` | Poracle's REST API address -- stored in `pweb_settings` table (`api_address` key) |
| `Poracle.ApiSecret` | Poracle's API secret -- stored in `pweb_settings` table (`api_secret` key) |
| `Poracle.AdminIds` | Comma-separated Discord user IDs granted admin access |

---

## Running Locally

### 1. Clone the repository

```bash
git clone <repo-url>
cd PGAN.Poracle.Web
```

### 2. Configure the API

```bash
cp Applications/PGAN.Poracle.Web.Api/appsettings.json Applications/PGAN.Poracle.Web.Api/appsettings.Development.json
```

Edit `appsettings.Development.json` with your actual values (see Configuration above).

### 3. Start the API (Terminal 1)

```bash
cd Applications/PGAN.Poracle.Web.Api
dotnet run
```

The API starts on `http://localhost:5145` by default.

### 4. Start the Angular dev server (Terminal 2)

```bash
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm install
npx ng serve --host 0.0.0.0
```

The app is available at `http://localhost:4200`.

### 5. Configure Discord OAuth2

In the [Discord Developer Portal](https://discord.com/developers/applications), add `http://localhost:4200/auth/discord/callback` as an OAuth2 redirect URI for your application.

---

## Docker Deployment

### 1. Create environment file

```bash
cp .env.example .env
```

Edit `.env` with your production values:

```env
DB_PASSWORD=your_db_password
JWT_SECRET=your-jwt-secret-minimum-32-characters-long
DISCORD_CLIENT_ID=your_discord_client_id
DISCORD_CLIENT_SECRET=your_discord_client_secret
DISCORD_REDIRECT_URI=https://yourdomain.com/auth/callback
PORACLE_API_ADDRESS=http://host.docker.internal:4Pokemon
PORACLE_API_SECRET=your_poracle_api_secret
PORACLE_ADMIN_IDS=discord_user_id_1,discord_user_id_2
```

### 2. Build and run

```bash
docker compose up -d --build
```

The app is available at `http://localhost:8082`. The multi-stage Dockerfile builds the Angular SPA and .NET API separately, then combines them into a single runtime image serving on port 8080 internally.

---

## API Endpoints

All API routes are prefixed with `/api/`.

| Controller | Route | Description |
|---|---|---|
| `AuthController` | `/api/auth` | Discord/Telegram OAuth2 login, token exchange |
| `DashboardController` | `/api/dashboard` | Alarm counts, location, active areas summary |
| `MonsterController` | `/api/monster` | Pokemon alarm CRUD |
| `RaidController` | `/api/raid` | Raid alarm CRUD |
| `EggController` | `/api/egg` | Egg alarm CRUD |
| `QuestController` | `/api/quest` | Quest alarm CRUD |
| `InvasionController` | `/api/invasion` | Invasion alarm CRUD |
| `LureController` | `/api/lure` | Lure alarm CRUD |
| `NestController` | `/api/nest` | Nest alarm CRUD |
| `GymController` | `/api/gym` | Gym alarm CRUD |
| `ProfileController` | `/api/profile` | Profile management and switching |
| `AreaController` | `/api/area` | Area assignment for users |
| `LocationController` | `/api/location` | Geocoding and location updates |
| `ConfigController` | `/api/config` | Poracle config proxy (areas, geofences) |
| `MasterDataController` | `/api/masterdata` | Pokemon names, types, forms from masterfile |
| `CleaningController` | `/api/cleaning` | Bulk alarm cleanup |
| `SettingsController` | `/api/settings` | Pweb settings (API address, secret) |
| `AdminController` | `/api/admin` | User management (admin-only) |
| `ScannerController` | `/api/scanner` | Scanner DB queries (optional) |

---

## Database

This application uses **Poracle's existing MySQL database** directly. No migrations are needed -- the app reads and writes to the tables Poracle already manages.

### Tables Used

| Table | Purpose |
|---|---|
| `humans` | User records (Discord/Telegram ID, location, areas, active profile) |
| `monsters` | Pokemon alarm configurations |
| `raid` | Raid alarm configurations |
| `egg` | Egg alarm configurations |
| `quest` | Quest alarm configurations |
| `invasion` | Invasion alarm configurations |
| `lures` | Lure alarm configurations |
| `nests` | Nest alarm configurations |
| `gym` | Gym alarm configurations |
| `profiles` | User profile definitions |
| `pweb_settings` | Web UI settings (API address, secret, etc.) |

### Optional: Scanner Database

If `ScannerDb` connection string is configured, the app can query the RDM scanner database for additional Pokemon/gym data.

---

## External Dependencies

| Service | Purpose | URL |
|---|---|---|
| Poracle REST API | Config, areas, templates, geofence data | Configured via `Poracle.ApiAddress` |
| WatWowMap Masterfile-Generator | Pokemon names, forms, types | `github.com/WatWowMap/Masterfile-Generator` |
| Discord API | OAuth2 authentication, user avatars | `discordapp.com/api` |
| Nominatim | Geocoding (address search to coordinates) | `nominatim.openstreetmap.org` |
| PogoAssets | Pokemon sprite images | `github.com/PokeMiners/pogo_assets` |
| CARTO | Map tile provider for Leaflet | `basemaps.cartocdn.com` |

---

## License

This project is private and intended for use within the PoGO Alerts Network community.
