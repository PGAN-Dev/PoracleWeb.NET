# Configuration Reference

All configuration can be provided via environment variables (Docker) or `appsettings.json` (development).

## Required settings

| Setting | Env Variable | Description |
|---|---|---|
| `ConnectionStrings:PoracleDb` | `ConnectionStrings__PoracleDb` | MySQL connection to Poracle database |
| `Jwt:Secret` | `Jwt__Secret` | JWT signing key (minimum 32 characters) |
| `Discord:ClientId` | `Discord__ClientId` | Discord OAuth2 application client ID |
| `Discord:ClientSecret` | `Discord__ClientSecret` | Discord OAuth2 application client secret |
| `Poracle:ApiAddress` | `Poracle__ApiAddress` | PoracleNG API base URL. **Critical** -- all alarm tracking, human/profile management, location, and area operations are proxied through this endpoint. |
| `Poracle:ApiSecret` | `Poracle__ApiSecret` | PoracleNG API shared secret. Sent as the `X-Poracle-Secret` header on every request. |
| `Poracle:AdminIds` | `Poracle__AdminIds` | Comma-separated Discord admin user IDs |

## Optional settings

### Authentication

| Setting | Env Variable | Default | Description |
|---|---|---|---|
| `Jwt:ExpirationMinutes` | `Jwt__ExpirationMinutes` | `1440` | Token expiry (24 hours) |
| `Discord:BotToken` | `Discord__BotToken` | — | Enables Discord avatar display |
| `Discord:GuildId` | `Discord__GuildId` | — | Discord server ID |
| `Discord:GeofenceForumChannelId` | `Discord__GeofenceForumChannelId` | — | Forum channel for geofence submission threads |
| `Telegram:Enabled` | `Telegram__Enabled` | `false` | Enable Telegram authentication |
| `Telegram:BotToken` | `Telegram__BotToken` | — | Telegram bot token |
| `Telegram:BotUsername` | `Telegram__BotUsername` | — | Telegram bot username |

### Databases

| Setting | Env Variable | Description |
|---|---|---|
| `ConnectionStrings:PoracleWebDb` | `ConnectionStrings__PoracleWebDb` | MySQL connection to PoracleWeb database (user geofences). Required for custom geofences feature. |
| `ConnectionStrings:ScannerDb` | `ConnectionStrings__ScannerDb` | Scanner database connection (RDM). Optional. Used by quest/raid scanner features and the gym picker search in raid/gym add dialogs. |

### Koji geofence API

Required for the custom geofences feature.

| Setting | Env Variable | Description |
|---|---|---|
| `Koji:ApiAddress` | `Koji__ApiAddress` | Koji geofence server URL (e.g., `http://localhost:8080`) |
| `Koji:BearerToken` | `Koji__BearerToken` | Koji API bearer token for authentication |
| `Koji:ProjectId` | `Koji__ProjectId` | Koji project ID for admin-promoted geofences (default: `0`) |
| `Koji:ProjectName` | `Koji__ProjectName` | Koji project name for `/geofence/poracle/{name}` endpoint |

### Poracle servers

For remote PoracleJS server management. See [Server Management](../features/server-management.md) for full setup.

| Setting | Env Variable | Description |
|---|---|---|
| `Poracle:Servers` | `Poracle__Servers__0__Name`, etc. | Array of PoracleJS server configs (name, host, API address, SSH user) |
| `Poracle:SshKeyPath` | `Poracle__SshKeyPath` | Path to SSH private key inside container (default `/app/ssh_key`) |

### CORS

| Setting | Env Variable | Description |
|---|---|---|
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | Allowed CORS origin (empty = allow all) |

## Configuration sources

| Source | Use case |
|---|---|
| `appsettings.Development.json` | Local development (gitignored) |
| `.env` file | Docker deployment |
| `docker-compose.yml` | Environment variable mapping |
| `poracle_web.site_settings` table | Runtime admin-configurable settings (migrated from deprecated `pweb_settings`) |

!!! warning "PoracleNG must be reachable"
    `Poracle:ApiAddress` must point to a running PoracleNG instance that is reachable from the PoracleWeb container. All alarm tracking, human/profile management, location, and area operations are proxied through this API. If PoracleNG is unreachable, alarm operations fail entirely and user management (registration, login, location, areas, profile switch) also fails. The `Poracle:ApiSecret` must match the `server.apiSecret` value in PoracleNG's config.

!!! note "Secrets"
    `appsettings.Development.json` is gitignored and holds all connection strings, JWT secret, Discord/Telegram credentials, and Poracle API address/secret. Never commit secrets to the repository.

!!! tip "Runtime settings"
    Looking for settings you can change without restarting? See [Site Settings](site-settings.md) for admin-configurable runtime settings like branding, feature toggles, alarm types, and more.
