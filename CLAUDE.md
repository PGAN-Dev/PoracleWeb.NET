# CLAUDE.md

Guidelines for Claude Code when working on the PGAN.Poracle.Web project.

## Project Overview

This is a full-stack web application for configuring Pokemon GO DM notification alarms through the Poracle bot system. Users authenticate via Discord OAuth2 or Telegram and manage alert filters (Pokemon, Raids, Quests, etc.) that Poracle uses to send personalized notifications.

## Tech Stack

- **Backend**: .NET 10, ASP.NET Core Web API, EF Core with MySQL (Oracle provider -- `MySql.EntityFrameworkCore`, NOT Pomelo)
- **Frontend**: Angular 21, standalone components with `inject()` and signals, Angular Material 21 (Material Design 3)
- **Mapping**: AutoMapper for Entity-to-Model conversions
- **Maps**: Leaflet 1.9 for interactive geofence display
- **Auth**: JWT bearer tokens, Discord OAuth2, Telegram Bot Login
- **Testing**: Vitest (frontend)

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
|   |   +-- Controllers/         20 API controllers (REST, all under /api/)
|   |   +-- Configuration/       DI registration (ServiceCollectionExtensions)
|   |   +-- Services/            AvatarCacheService (IHostedService)
|   +-- Web.App/
|       +-- ClientApp/           Angular 21 SPA
|           +-- src/app/
|               +-- core/        Guards (auth, admin), services, interceptors, models
|               +-- modules/     Feature modules: auth, dashboard, pokemon, raids,
|               |                quests, invasions, lures, nests, gyms, areas,
|               |                profiles, cleaning, admin
|               +-- shared/      Reusable components: area-map, pokemon-selector,
|                                template-selector, delivery-preview, location-dialog,
|                                discord-avatar, alarm-info, confirm-dialog,
|                                language-selector
```

## Key Patterns

### Repository Layer
- `BaseRepository<TEntity, TModel>` uses expression-based filters and AutoMapper projections.
- `EnsureNotNullDefaults()` method handles MySQL `NOT NULL` text columns that EF Core maps as nullable strings. Call this before saving to avoid constraint violations.

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

### Dashboard
- `DashboardService` runs sequential DB queries (not `Task.WhenAll`) because it uses a single scoped `DbContext` instance which is not thread-safe.

### Angular Patterns
- All components are **standalone** (no NgModules).
- Uses `inject()` function instead of constructor injection.
- Uses Angular signals for reactive state where applicable.
- Lazy-loaded routes in `app.routes.ts`.
- Services in `core/services/` use `HttpClient` to call the .NET API.

## Configuration

- **Secrets**: `appsettings.Development.json` (gitignored) holds all connection strings, JWT secret, Discord/Telegram credentials, Poracle API address/secret.
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

## Build & Run

```bash
# Build entire solution
dotnet build    # from solution root (E:\PGAN\pogogit\PGAN.Poracle.Web\)

# Run API (starts on http://localhost:5145)
cd Applications/PGAN.Poracle.Web.Api
dotnet run

# Run Angular dev server (starts on http://localhost:4200)
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm install
npx ng serve --host 0.0.0.0

# Docker build
docker compose up -d --build
```

## File Locations

| What | Path |
|---|---|
| EF Core Entities | `Data/PGAN.Poracle.Web.Data/Entities/` |
| EF Core DbContext | `Data/PGAN.Poracle.Web.Data/PoracleContext.cs` |
| API Controllers | `Applications/PGAN.Poracle.Web.Api/Controllers/` |
| DI Registration | `Applications/PGAN.Poracle.Web.Api/Configuration/ServiceCollectionExtensions.cs` |
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

## Testing

- Frontend tests use **Vitest** (not Karma/Jasmine). Run with `npx vitest` from the `ClientApp/` directory.
- No backend test projects are currently set up.
