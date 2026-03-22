# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a full-stack web application for configuring Pokemon GO DM notification alarms through the Poracle bot system. Users authenticate via Discord OAuth2 or Telegram and manage alert filters (Pokemon, Raids, Quests, etc.) that Poracle uses to send personalized notifications.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider -- `MySql.EntityFrameworkCore`, NOT Pomelo)
- **Frontend**: Angular 21, standalone components with `inject()` and signals, Angular Material 21 (Material Design 3)
- **Mapping**: AutoMapper for Entity-to-Model conversions
- **Maps**: Leaflet 1.9 for interactive geofence display
- **Auth**: JWT bearer tokens, Discord OAuth2, Telegram Bot Login
- **Testing**: Jest (frontend), xUnit (backend)

## Solution Structure

```
PGAN.Poracle.Web.slnx
|
+-- Core/
|   +-- Core.Abstractions/       Interfaces: IRepository, IService, IUnitOfWork
|   +-- Core.Models/             DTOs passed between layers (not EF entities)
|   +-- Core.Mappings/           AutoMapper PoracleMappingProfile
|   +-- Core.Repositories/       BaseRepository<TEntity, TModel> implementations
|   +-- Core.Services/           Business logic (MonsterService, DashboardService,
|   |                            UserGeofenceService, KojiService,
|   |                            DiscordNotificationService, PoracleServerService, etc.)
|   +-- Core.UnitsOfWork/        PoracleUnitOfWork wrapping DbContext.SaveChangesAsync()
|
+-- Data/
|   +-- Data/                    PoracleContext (EF Core), PoracleWebContext (app-owned DB),
|   |                            Entities/ (incl. UserGeofenceEntity), Configurations/
|   +-- Data.Scanner/            RdmScannerContext for optional scanner DB
|
+-- Applications/
|   +-- Web.Api/                 ASP.NET Core host
|   |   +-- Controllers/         20+ API controllers (REST, all under /api/)
|   |   |                        incl. UserGeofenceController, AdminGeofenceController,
|   |   |                        GeofenceFeedController
|   |   +-- Configuration/       DI registration, JwtSettings, DiscordSettings, KojiSettings, etc.
|   |   +-- Services/            Background services: AvatarCacheService, DtsCacheService
|   +-- Web.App/
|       +-- ClientApp/           Angular 21 SPA
|           +-- src/app/
|               +-- core/        Guards (auth, admin), services, interceptors, models
|               +-- modules/     Feature modules: auth, dashboard, pokemon, raids,
|               |                quests, invasions, lures, nests, gyms, areas,
|               |                profiles, cleaning, quick-picks, admin, geofences
|               +-- shared/      Reusable components: area-map, pokemon-selector,
|                                template-selector, delivery-preview, location-dialog,
|                                discord-avatar, alarm-info, confirm-dialog,
|                                language-selector, distance-dialog, onboarding,
|                                region-selector, geofence-name-dialog,
|                                geofence-approval-dialog
|                                utils/: geo.utils (point-in-polygon, centroid)
|
+-- Tests/
    +-- PGAN.Poracle.Web.Tests/  xUnit backend tests (controllers, services, mappings)
```

## Key Patterns

### Repository Layer
- `BaseRepository<TEntity, TModel>` uses expression-based filters and AutoMapper projections.
- `EnsureNotNullDefaults()` method handles MySQL `NOT NULL` text columns that EF Core maps as nullable strings. Call this before saving to avoid constraint violations.
- `UpdateDistanceByUidsAsync()` does targeted distance-only SQL updates without touching other fields -- use this for bulk distance operations instead of the generic `UpdateAsync`.

### AutoMapper Update Models
- All `*Update` models (MonsterUpdate, RaidUpdate, etc.) use **nullable `int?`** properties so partial updates don't zero out unset fields.
- The mapping profile uses `.ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))` to skip null properties.
- **Important**: When calling the PUT `/{uid}` endpoint from the frontend, always spread the full alarm object (`{ ...alarm, distance }`) rather than sending a partial `{ distance }`. This ensures existing values like `clean`, `template`, and filter settings are preserved.

### Bulk Operations
- Each alarm controller has three distance endpoints:
  - `PUT /{uid}` -- Update a single alarm (full object)
  - `PUT /distance` -- Update ALL alarms' distance for the current user/profile
  - `PUT /distance/bulk` -- Update distance for specific UIDs: `{ uids: number[], distance: number }`. This does a targeted `SetDistance()` on matching entities, bypassing AutoMapper entirely -- safe for bulk operations.

### Poracle API Proxy
- `IPoracleApiProxy` / `PoracleApiProxy` wraps HttpClient calls to the external Poracle REST API.
- Used for: fetching config, areas/geofences, templates, sending commands.
- Registered via `AddHttpClient<IPoracleApiProxy, PoracleApiProxy>()`.

### Poracle Config Parsing
- `PoracleConfig` is parsed from Poracle's JSON configuration.
- Handles mixed types: `defaultTemplateName` can be a number or string in Poracle's config. Use `JsonElement` or careful deserialization.

### Areas
- User areas are stored as JSON arrays in the `humans.area` column: `["west end", "downtown"]`.
- Geofence polygons come from the Poracle API, not the database.

### Custom Geofences
- User geofences are stored in `poracle_web.user_geofences` (separate database from Poracle, see "PoracleWeb Database" below).
- **PoracleWeb is the single geofence source for PoracleJS.** The `GET /api/geofence-feed` endpoint (`[AllowAnonymous]`, intended for internal network access) serves a unified feed that merges admin geofences from Koji with user-drawn geofences from the PoracleWeb database. No custom code is needed in PoracleJS or Koji -- standard upstream versions work.
- PoracleJS `geofence.path` config is a single URL pointing to PoracleWeb (not an array, not dual Koji+PoracleWeb sources). PoracleJS does not connect to Koji directly for geofences.
- Admin geofences are fetched from Koji via the `/geofence/poracle/{projectName}` endpoint, with group names resolved from the Koji parent chain. They are served with `displayInMatches: true` and `group` populated. Results are cached for 5 minutes (`IMemoryCache` with `TimeSpan.FromMinutes(5)` TTL). The cache is invalidated when a geofence is approved/promoted to Koji.
- User geofences are served with `displayInMatches: false` and `userSelectable: false` -- names are hidden from all DMs and are not selectable on the Poracle bot's area list.
- Parent/region geofences from Koji are excluded from the feed (they are structural, not alerting areas).
- **Graceful degradation**: If Koji is unreachable, the feed endpoint logs the error and still serves user geofences from the local DB. PoracleJS's built-in `.cache/` directory provides additional failover -- if PoracleWeb itself is down, PoracleJS falls back to its last cached geofence data.
- `group_map.json` is not needed in PoracleJS -- group names are resolved automatically from the Koji parent chain by PoracleWeb.
- On admin approval, the geofence polygon is pushed to Koji with `isPublic: true` (`userSelectable: true`), making it a proper public area.
- Koji is used only for admin/approved public geofences; user-drawn private geofences remain in the PoracleWeb database.
- Geofence names (the `kojiName` field) are always **lowercase** because Poracle does case-sensitive area matching and `humans.area` stores names in lowercase.
- Geofence names are auto-generated from the user-provided display name (lowercased). Collisions are resolved by appending a numeric suffix.
- Maximum 10 custom geofences per user. Polygons limited to 500 points.
- Discord forum posts are created on submission via the bot API (`discordapp.com/api/v9`). Forum tags (Pending, Approved, Rejected) are auto-created.
- On approval/rejection, the Discord forum thread is updated with a status message, tagged, locked, and archived.
- Geofence statuses: `active` (private, user-only), `pending_review` (submitted for admin review), `approved` (promoted to Koji as public), `rejected` (remains private with review notes).
- `KojiService` fetches region (parent) geofences from Koji's reference API and serves them to the frontend `region-selector` component for auto-detection of which region a drawn polygon belongs to.

### Poracle Server Management
- Servers configured via `Poracle:Servers` array in appsettings (name, host, API address, SSH user).
- Health check pings each server's API endpoint to determine online/offline status.
- Restart executes `ssh user@host "pm2 restart all"` via `System.Diagnostics.Process`.
- SSH key mounted as read-only volume at `/app/ssh_key` (path configurable via `Poracle:SshKeyPath`).

### PoracleWeb Database
- Second `DbContext`: `PoracleWebContext` using `ConnectionStrings:PoracleWebDb`.
- Separate `poracle_web` MariaDB/MySQL database for application-owned data (not managed by PoracleJS).
- Does **not** modify the Poracle DB schema -- the Poracle database remains exclusively managed by PoracleJS.
- Currently stores: `user_geofences` table.
- Uses EF Core `EnsureCreated()` or migrations for schema management.

### Profiles
- `humans.current_profile_no` (not `profile_no`) tracks the active profile.
- All alarm tables reference `profile_no` to filter by active profile.

### Rate Limiting
- Auth endpoints use **per-IP** partitioned rate limiting (not global).
- `auth` policy: 30 requests per 60s per IP (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s per IP (current user, profile switch).
- Configured in `Program.cs` using `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

### Service Lifetimes
- Most services are **scoped** (per-request). `MasterDataService` is a **singleton** (cached game data).
- `DashboardService` runs sequential DB queries (not `Task.WhenAll`) because it uses a single scoped `DbContext` instance which is not thread-safe.
- Swagger/OpenAPI is available in the development environment.

### Angular Patterns
- All components are **standalone** (no NgModules).
- Uses `inject()` function instead of constructor injection.
- Uses Angular signals for reactive state where applicable.
- Lazy-loaded routes in `app.routes.ts`.
- Services in `core/services/` use `HttpClient` to call the .NET API.

### UI Patterns
- **Alarm lists**: Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance.
- **Bulk operations**: Select mode toggle (checklist icon) on each alarm list, bulk toolbar with Select All, Update Distance, Delete.
- **Skeleton loading**: Animated skeleton card placeholders on Pokemon, Raids, Quests pages.
- **Staggered animations**: Grid items fade in with 30ms stagger delay.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag.

## Configuration

- **Secrets**: `appsettings.Development.json` (gitignored) holds all connection strings, JWT secret, Discord/Telegram credentials, Poracle API address/secret.
- **Docker**: Environment variables configured in `.env` file, mapped in `docker-compose.yml`.
- **Poracle API**: Address comes from `pweb_settings` table or `appsettings.json` `Poracle:ApiAddress`.
- **Discord Bot Token**: Sourced from PoracleJS server's `config/local-discord.json`.
- **Admin IDs**: Comma-separated Discord user IDs in `Poracle:AdminIds`.
- **PoracleWeb DB**: `ConnectionStrings:PoracleWebDb` -- connection string for the `poracle_web` database (user geofences and app-owned data).
- **Koji Geofence API**:
  - `Koji:ApiAddress` -- Koji geofence server URL (e.g., `http://localhost:8080`).
  - `Koji:BearerToken` -- Koji API authentication token.
  - `Koji:ProjectId` -- Koji project ID for admin-promoted geofences.
  - `Koji:ProjectName` -- Koji project name used for the `/geofence/poracle/{name}` endpoint to fetch admin geofences. Settings class: `KojiSettings`.
- **Discord Geofence Forum**: `Discord:GeofenceForumChannelId` -- Discord forum channel ID where geofence submission threads are created.
- **Poracle Servers**: `Poracle:Servers` -- array of PoracleJS server configs (name, host, API address, SSH user) for multi-server management.
- **SSH Key Path**: `Poracle:SshKeyPath` -- path to SSH private key inside the container (default `/app/ssh_key`).
- **PoracleJS config**: `geofence.path` in PoracleJS config is a single URL pointing to the PoracleWeb unified feed endpoint (e.g., `"http://poracleweb:8082/api/geofence-feed"`). PoracleWeb fetches admin geofences from Koji internally and merges them with user geofences.

## Common Issues

### MySQL Provider
Pomelo MySQL provider (`Pomelo.EntityFrameworkCore.MySql`) is **incompatible** with EF Core 10. This project uses `MySql.EntityFrameworkCore` (Oracle's official provider). Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

### NULL String Columns
Many Poracle DB columns are `NOT NULL` with empty-string defaults but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method in `BaseRepository` sets null strings to `""` before saving.

### Discord API
- Use `discordapp.com` (not `discord.com`) for API calls -- `discord.com` is blocked by Cloudflare in some server environments.
- `AvatarCacheService` fetches avatars sequentially with delays to avoid rate limiting.

### Poracle Config Mixed Types
`defaultTemplateName` in Poracle's config can be a number (e.g., `1`) or a string (e.g., `"default"`). Deserialization must handle both.

### Scanner DB is Optional
The `ScannerDb` connection string is optional. If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses.

### Bulk Update Preserving Fields
When updating alarms in bulk, **never** send partial objects to `PUT /{uid}`. AutoMapper maps all fields from the Update model onto the existing entity -- `int` properties default to `0` when absent from JSON, which overwrites existing values like `clean` and filter settings. Use the dedicated `PUT /distance/bulk` endpoint for distance changes, or spread the full alarm: `{ ...alarm, distance }`.

### Discord API Version for Geofence Notifications
Use `discordapp.com/api/v9` (not v10) -- v10 is not supported on the `discordapp.com` domain. The `DiscordNotificationService` HttpClient is configured with base address `https://discordapp.com/api/v9/`.

### Poracle Area Case Sensitivity
Poracle does **case-sensitive** area matching. Geofence names stored in `humans.area` and the `kojiName` field in `user_geofences` must always be lowercase. The `UserGeofenceService.CreateAsync()` method enforces this with `ToLowerInvariant()`.

### Koji displayInMatches Limitation
Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. To ensure user geofence names are hidden from DMs, serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

## Build & Run

```bash
# Build entire solution (from solution root)
dotnet build

# Run API (starts on http://localhost:5048)
cd Applications/PGAN.Poracle.Web.Api
dotnet run

# Run Angular dev server (starts on http://localhost:4200)
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm install
npm start              # alias for ng serve
npm run watch          # watch mode for development
npm run build          # production build

# Run frontend tests (Jest)
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm test

# Run backend tests (xUnit)
dotnet test

# Lint and format
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm run lint           # ESLint check
npm run prettier-check # Prettier check
npx eslint --fix src/  # Auto-fix lint
npm run prettier-format # Auto-format

# Docker — build from source
docker build -t poracleweb-local:latest .
docker compose up -d

# Docker — force clean rebuild
docker build --no-cache -t poracleweb-local:latest .
docker compose up -d --force-recreate
```

## Code Style

- **Prettier**: 140-char print width, single quotes, 2-space indent, configured in `.prettierrc`
- **ESLint**: Configured with Angular, perfectionist (sorted class members), and Prettier plugins
- **EditorConfig**: 2-space indent, UTF-8, in `ClientApp/.editorconfig`

## File Locations

| What | Path |
|---|---|
| EF Core Entities | `Data/PGAN.Poracle.Web.Data/Entities/` |
| EF Core DbContext (Poracle) | `Data/PGAN.Poracle.Web.Data/PoracleContext.cs` |
| EF Core DbContext (PoracleWeb) | `Data/PGAN.Poracle.Web.Data/PoracleWebContext.cs` |
| User Geofence Entity | `Data/PGAN.Poracle.Web.Data/Entities/UserGeofenceEntity.cs` |
| API Controllers | `Applications/PGAN.Poracle.Web.Api/Controllers/` |
| Geofence Feed Controller | `Applications/PGAN.Poracle.Web.Api/Controllers/GeofenceFeedController.cs` |
| Admin Geofence Controller | `Applications/PGAN.Poracle.Web.Api/Controllers/AdminGeofenceController.cs` |
| User Geofence Controller | `Applications/PGAN.Poracle.Web.Api/Controllers/UserGeofenceController.cs` |
| DI Registration | `Applications/PGAN.Poracle.Web.Api/Configuration/ServiceCollectionExtensions.cs` |
| Settings Classes | `Applications/PGAN.Poracle.Web.Api/Configuration/` (JwtSettings, DiscordSettings, KojiSettings, PoracleServerSettings, etc.) |
| Angular App Root | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/` |
| Angular Routes | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/app.routes.ts` |
| Angular Services | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/core/services/` |
| Angular Guards | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/core/guards/` |
| Shared Components | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/components/` |
| Feature Modules | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/modules/` |
| Geofences Module | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/modules/geofences/` |
| Admin Geofence Submissions | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/modules/admin/geofence-submissions/` |
| Region Selector Component | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/components/region-selector/` |
| Geofence Name Dialog | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/components/geofence-name-dialog/` |
| Geofence Approval Dialog | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/components/geofence-approval-dialog/` |
| Geo Utilities | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/utils/geo.utils.ts` |
| AutoMapper Profile | `Core/PGAN.Poracle.Web.Core.Mappings/PoracleMappingProfile.cs` |
| Repository Base | `Core/PGAN.Poracle.Web.Core.Repositories/` |
| Service Layer | `Core/PGAN.Poracle.Web.Core.Services/` |
| UserGeofenceService | `Core/PGAN.Poracle.Web.Core.Services/UserGeofenceService.cs` |
| KojiService | `Core/PGAN.Poracle.Web.Core.Services/KojiService.cs` |
| DiscordNotificationService | `Core/PGAN.Poracle.Web.Core.Services/DiscordNotificationService.cs` |
| IPoracleServerService | `Core/PGAN.Poracle.Web.Core.Abstractions/Services/` |
| PoracleServerService | `Core/PGAN.Poracle.Web.Core.Services/PoracleServerService.cs` |
| Poracle Servers Page | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/modules/admin/poracle-servers/` |
| Abstractions | `Core/PGAN.Poracle.Web.Core.Abstractions/` |
| Backend Tests | `Tests/PGAN.Poracle.Web.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Docker Config | `Dockerfile`, `docker-compose.yml`, `.env.example` |

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, dialogs, and utilities (including `geo.utils.spec.ts`, `user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `region-selector.component.spec.ts`, `geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`).
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and AutoMapper mappings (including `UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `UserGeofenceServiceTests`).
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
