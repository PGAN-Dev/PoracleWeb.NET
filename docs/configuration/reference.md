# Configuration Reference

All configuration can be provided via environment variables or `appsettings.json`. The same `.env` file works for both Docker and standalone mode.

!!! tip "Short env var names"
    The `.env` file uses friendly short names (`DB_HOST`, `JWT_SECRET`, `DISCORD_CLIENT_ID`, etc.). The app automatically translates these to .NET's configuration format at startup. You can use either the short names or the `__`-delimited names shown in the table below.

## Server

| Setting | Env Variable | Default | Description |
|---|---|---|---|
| Port | `PORT` | `8082` | HTTP port the app listens on. In Docker, this is the host port. Standalone, this is the port the app binds to. |
| `Server:Port` | `Server__Port` | `8082` | Alternative to `PORT` for the .NET config convention. `PORT` takes precedence. Ignored when `ASPNETCORE_URLS` is set. |

## Required settings

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| Database | `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` | `ConnectionStrings__PoracleDb` | MySQL connection to Poracle database. Short vars are auto-composed into a connection string. |
| JWT Secret | `JWT_SECRET` | `Jwt__Secret` | JWT signing key (minimum 32 characters) |
| Discord Client ID | `DISCORD_CLIENT_ID` | `Discord__ClientId` | Discord OAuth2 application client ID |
| Discord Client Secret | `DISCORD_CLIENT_SECRET` | `Discord__ClientSecret` | Discord OAuth2 application client secret |
| Poracle API Address | `PORACLE_API_ADDRESS` | `Poracle__ApiAddress` | PoracleNG API base URL. **Critical** -- all alarm tracking, human/profile management, location, and area operations are proxied through this endpoint. |
| Poracle API Secret | `PORACLE_API_SECRET` | `Poracle__ApiSecret` | PoracleNG API shared secret. Sent as the `X-Poracle-Secret` header on every request. |
| Admin IDs | `PORACLE_ADMIN_IDS` | `Poracle__AdminIds` | Comma-separated Discord admin user IDs |

## Optional settings

### Authentication

| Setting | `.env` name | `.NET` env variable | Default | Description |
|---|---|---|---|---|
| JWT Expiration | — | `Jwt__ExpirationMinutes` | `1440` | Token expiry (24 hours) |
| Discord Bot Token | `DISCORD_BOT_TOKEN` | `Discord__BotToken` | — | Enables Discord avatar display |
| Discord Guild ID | `DISCORD_GUILD_ID` | `Discord__GuildId` | — | Discord server ID |
| Discord Geofence Forum | `DISCORD_GEOFENCE_FORUM_CHANNEL_ID` | `Discord__GeofenceForumChannelId` | — | Forum channel for geofence submission threads |
| Telegram Enabled | `TELEGRAM_ENABLED` | `Telegram__Enabled` | `false` | Enable Telegram authentication |
| Telegram Bot Token | `TELEGRAM_BOT_TOKEN` | `Telegram__BotToken` | — | Telegram bot token |
| Telegram Bot Username | `TELEGRAM_BOT_USERNAME` | `Telegram__BotUsername` | — | Telegram bot username |

### Databases

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| PoracleWeb DB | `WEB_DB_HOST`, `WEB_DB_PORT`, `WEB_DB_NAME`, `WEB_DB_USER`, `WEB_DB_PASSWORD` | `ConnectionStrings__PoracleWebDb` | MySQL connection to PoracleWeb database (site settings, webhook delegates, quick picks, user geofences). Short vars are auto-composed into a connection string. |
| Scanner DB | `SCANNER_DB_CONNECTION` | `ConnectionStrings__ScannerDb` | Scanner database connection (RDM). Optional. Used by the gym picker search in raid/gym/egg add dialogs. Provide a full connection string. |

### Golbat API

Optional. Enables "currently spawning" Pokemon availability indicators in the Pokemon selector.

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| Golbat API Address | `GOLBAT_API_ADDRESS` | `Golbat__ApiAddress` | Golbat scanner API URL (e.g., `http://localhost:9001`). When set, the Pokemon selector shows which species are actively spawning. |
| Golbat API Secret | `GOLBAT_API_SECRET` | `Golbat__ApiSecret` | Golbat API secret. Sent as the `X-Golbat-Secret` header on every request. Must match Golbat's `api_secret` config value. |

### Koji geofence API

Required for the custom geofences feature.

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| Koji API Address | `KOJI_API_ADDRESS` | `Koji__ApiAddress` | Koji geofence server URL (e.g., `http://localhost:8080`) |
| Koji Bearer Token | `KOJI_BEARER_TOKEN` | `Koji__BearerToken` | Koji API bearer token for authentication |
| Koji Project ID | `KOJI_PROJECT_ID` | `Koji__ProjectId` | Koji project ID for admin-promoted geofences (default: `0`) |
| Koji Project Name | `KOJI_PROJECT_NAME` | `Koji__ProjectName` | Koji project name for `/geofence/poracle/{name}` endpoint |

### Poracle servers

For remote PoracleJS server management. See [Server Management](../features/server-management.md) for full setup.

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| Server Config | `PORACLE_SERVER_1_HOST`, `_API`, `_SSH_USER`, `_RESTART_CMD`, `_GROUP_MAP` | `Poracle__Servers__0__Host`, etc. | PoracleJS server configs. Use 1-based numbering in `.env` (auto-converted to 0-based). Up to 10 servers. |
| SSH Key Path | `PORACLE_SSH_KEY_PATH` | `Poracle__SshKeyPath` | Path to SSH private key inside container (default `/app/ssh_key`) |

### CORS

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| CORS Origin | `CORS_ORIGIN` | `Cors__AllowedOrigins__0` | Allowed CORS origin. Required in production (empty = crash). Not required in development mode. |

## Configuration sources

| Source | Use case |
|---|---|
| `.env` file (project root) | Both Docker and standalone — the primary config file. Docker Compose reads it natively; standalone mode loads it on startup. Short env var names are auto-translated to .NET format. |
| `appsettings.json` | Default values. You rarely need to edit this. |
| `appsettings.Development.json` | Local development overrides (gitignored) |
| `docker-compose.yml` | Docker-specific config. Also translates `.env` variables — but the app now does this itself too, so standalone mode works identically. |
| `poracle_web.site_settings` table | Runtime admin-configurable settings (migrated from deprecated `pweb_settings`) |

!!! warning "PoracleNG must be reachable"
    `Poracle:ApiAddress` must point to a running PoracleNG instance that is reachable from the PoracleWeb container. All alarm tracking, human/profile management, location, and area operations are proxied through this API. If PoracleNG is unreachable, alarm operations fail entirely and user management (registration, login, location, areas, profile switch) also fails. The `Poracle:ApiSecret` must match the `server.apiSecret` value in PoracleNG's config.

!!! note "Secrets"
    `appsettings.Development.json` is gitignored and holds all connection strings, JWT secret, Discord/Telegram credentials, and Poracle API address/secret. Never commit secrets to the repository.

!!! tip "Runtime settings"
    Looking for settings you can change without restarting? See [Site Settings](site-settings.md) for admin-configurable runtime settings like branding, feature toggles, alarm types, and more.
