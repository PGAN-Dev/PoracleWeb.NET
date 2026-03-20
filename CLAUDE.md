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
|   +-- Core.Services/           Business logic (MonsterService, DashboardService, etc.)
|   +-- Core.UnitsOfWork/        PoracleUnitOfWork wrapping DbContext.SaveChangesAsync()
|
+-- Data/
|   +-- Data/                    PoracleContext (EF Core), Entities/, Configurations/
|   +-- Data.Scanner/            RdmScannerContext for optional scanner DB
|
+-- Applications/
|   +-- Web.Api/                 ASP.NET Core host
|   |   +-- Controllers/         20+ API controllers (REST, all under /api/)
|   |   +-- Configuration/       DI registration, JwtSettings, DiscordSettings, etc.
|   |   +-- Services/            Background services: AvatarCacheService, DtsCacheService
|   +-- Web.App/
|       +-- ClientApp/           Angular 21 SPA
|           +-- src/app/
|               +-- core/        Guards (auth, admin), services, interceptors, models
|               +-- modules/     Feature modules: auth, dashboard, pokemon, raids,
|               |                quests, invasions, lures, nests, gyms, areas,
|               |                profiles, cleaning, quick-picks, admin
|               +-- shared/      Reusable components: area-map, pokemon-selector,
|                                template-selector, delivery-preview, location-dialog,
|                                discord-avatar, alarm-info, confirm-dialog,
|                                language-selector, distance-dialog, onboarding
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

### Profiles
- `humans.current_profile_no` (not `profile_no`) tracks the active profile.
- All alarm tables reference `profile_no` to filter by active profile.

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
| EF Core DbContext | `Data/PGAN.Poracle.Web.Data/PoracleContext.cs` |
| API Controllers | `Applications/PGAN.Poracle.Web.Api/Controllers/` |
| DI Registration | `Applications/PGAN.Poracle.Web.Api/Configuration/ServiceCollectionExtensions.cs` |
| Settings Classes | `Applications/PGAN.Poracle.Web.Api/Configuration/` (JwtSettings, DiscordSettings, etc.) |
| Angular App Root | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/` |
| Angular Routes | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/app.routes.ts` |
| Angular Services | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/core/services/` |
| Angular Guards | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/core/guards/` |
| Shared Components | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/shared/components/` |
| Feature Modules | `Applications/PGAN.Poracle.Web.App/ClientApp/src/app/modules/` |
| AutoMapper Profile | `Core/PGAN.Poracle.Web.Core.Mappings/PoracleMappingProfile.cs` |
| Repository Base | `Core/PGAN.Poracle.Web.Core.Repositories/` |
| Service Layer | `Core/PGAN.Poracle.Web.Core.Services/` |
| Abstractions | `Core/PGAN.Poracle.Web.Core.Abstractions/` |
| Backend Tests | `Tests/PGAN.Poracle.Web.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Docker Config | `Dockerfile`, `docker-compose.yml`, `.env.example` |

## Testing

- **Frontend**: Jest with jest-preset-angular. Run with `npm test` from `ClientApp/`. Tests cover services, pipes, components, and dialogs.
- **Backend**: xUnit with Moq. Run with `dotnet test` from solution root. Tests cover controllers, services, and AutoMapper mappings.
- **CI**: Both test suites run automatically on push/PR to main via GitHub Actions.
