---
paths:
  - ".env.example"
  - "docker-compose.yml.example"
  - "Dockerfile"
  - "**/appsettings*.json"
  - "**/Program.cs"
  - "**/Configuration/*Settings.cs"
  - "**/ServiceCollectionExtensions*"
  - "**/*Settings.cs"
  - "**/DataDirectoryCheck*"
  - "**/PublicOrigin*"
---

## Configuration

- **Unified `.env` file**: A single `.env` file (copied from `.env.example`) configures PoracleWeb for both Docker and standalone mode. `Program.cs` loads `.env` at startup, bridges short env var names (e.g., `JWT_SECRET`, `DISCORD_CLIENT_ID`) to .NET's `__` convention (e.g., `Jwt__Secret`, `Discord__ClientId`), and auto-composes the PoracleWeb DB connection string from `WEB_DB_*`. The old `DB_*` variables are not mapped; an install that still sets them gets one Information line at startup and runs normally. Docker Compose also reads `.env` natively; the Program.cs bridge covers the standalone (`dotnet run`) case.
- **Env var bridge** (in `Program.cs`): Maps ~18 short env var names to .NET config paths. Two static helpers at the end of Program.cs: `MapEnvVar()` and `ComposeConnectionString()`.
- **`appsettings.Development.json`** (gitignored) can still be used for overrides, but `.env` is the primary configuration mechanism.
- **Poracle API** (critical for all writes): `Poracle:ApiAddress` and `Poracle:ApiSecret` are required for the application to function, against PoracleNG **5.4.0 or newer** (see "Minimum PoracleNG Is 5.4.0"). All alarm CRUD, human/profile management, area updates, and profile switches are proxied through the PoracleNG REST API. If the API is unreachable, all alarm, human, and profile operations will fail (no DB fallback).
- **Site Settings**: Admin-configurable settings (custom title, feature flags, etc.) are stored in `poracle_web.site_settings`. At startup `SettingsMigrationStartupService` seeds missing defaults; nothing reads Poracle's `pweb_settings` table any more, and the app never alters or drops it.
- **Discord Bot Token**: Sourced from PoracleJS server's `config/local-discord.json`.
- **Admin IDs**: Comma-separated Discord user IDs in `Poracle:AdminIds`.
- **PoracleWeb DB**: `ConnectionStrings:PoracleWebDb` -- connection string for the `poracle_web` database (user geofences, site settings, webhook delegates, quick picks). Auto-composed by `Program.cs` from `WEB_DB_HOST`, `WEB_DB_PORT`, `WEB_DB_NAME`, `WEB_DB_USER`, `WEB_DB_PASSWORD` env vars when the full connection string is not set.
- **Golbat API** (optional): `Golbat:ApiAddress` and `Golbat:ApiSecret` enable Pokemon availability indicators in the Pokemon selector. When configured, `IGolbatApiProxy` and `IPokemonAvailabilityService` are registered in DI. When not configured, the feature is hidden. Settings class: `GolbatSettings`. Env vars: `GOLBAT_API_ADDRESS`, `GOLBAT_API_SECRET`.
- **Koji Geofence API**:
  - `Koji:ApiAddress` -- Koji geofence server URL (e.g., `http://localhost:8080`).
  - `Koji:BearerToken` -- Koji API authentication token.
  - `Koji:ProjectId` -- Koji project ID for admin-promoted geofences.
  - `Koji:ProjectName` -- Koji project name used for the `/geofence/poracle/{name}` endpoint to fetch admin geofences. Settings class: `KojiSettings`.
- **Discord Geofence Forum**: `Discord:GeofenceForumChannelId` -- Discord forum channel ID where geofence submission threads are created.
- **DataProtection**: Keys are persisted to `DATA_DIR/dataprotection-keys` (Docker: `/app/data/dataprotection-keys`, standalone: `./data/dataprotection-keys`). Configured in `ServiceCollectionExtensions.cs` via `AddDataProtection().PersistKeysToFileSystem().SetApplicationName("Pgan.PoracleWebNet.Api")`. Uses the existing `DATA_DIR` env var (set in Dockerfile, read via `configuration["DATA_DIR"]`) with a fallback to `Path.Combine(Directory.GetCurrentDirectory(), "data")` for local dev. No additional env vars or NuGet packages needed.
- **PoracleJS config**: `geofence.path` in PoracleJS config is a single URL pointing to the PoracleWeb unified feed endpoint (e.g., `"http://poracleweb:8082/api/geofence-feed"`). PoracleWeb fetches admin geofences from Koji internally and merges them with user geofences.
