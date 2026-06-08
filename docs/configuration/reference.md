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
| JWT Issuer | `JWT_ISSUER` | `Jwt__Issuer` | `PoracleWeb` | Issuer claim (`iss`) baked into signed tokens. Override only if validating tokens externally. |
| JWT Audience | `JWT_AUDIENCE` | `Jwt__Audience` | `PoracleWeb.App` | Audience claim (`aud`) baked into signed tokens. Override only if validating tokens externally. |
| Discord Bot Token | `DISCORD_BOT_TOKEN` | `Discord__BotToken` | — | Enables Discord avatar display |
| Discord Guild ID | `DISCORD_GUILD_ID` | `Discord__GuildId` | — | Discord server ID |
| Discord Geofence Forum | `DISCORD_GEOFENCE_FORUM_CHANNEL_ID` | `Discord__GeofenceForumChannelId` | — | Forum channel for geofence submission threads |
| Telegram Enabled | `TELEGRAM_ENABLED` | `Telegram__Enabled` | `false` | Enable Telegram authentication |
| Telegram Bot Token | `TELEGRAM_BOT_TOKEN` | `Telegram__BotToken` | — | Telegram bot token |
| Telegram Bot Username | `TELEGRAM_BOT_USERNAME` | `Telegram__BotUsername` | — | Telegram bot username |

### External SSO / OIDC

Optional. Delegates PoracleWeb.NET login to your own OAuth2/OIDC provider (e.g. PogoAlerts) for single sign-on. See [External SSO setup](external-sso.md) for the full setup guide.

The provider is enabled automatically when `OIDC_CLIENT_ID` plus all three of `OIDC_AUTHORIZATION_URL`, `OIDC_TOKEN_URL`, and `OIDC_USERINFO_URL` are set. Set `OIDC_ENABLED` explicitly to override the inference.

| Setting | `.env` name | `.NET` env variable | Default | Description |
|---|---|---|---|---|
| Enabled | `OIDC_ENABLED` | `Oidc__Enabled` | — | Master switch. Auto-inferred `true` when `OIDC_CLIENT_ID` and the three endpoint URLs are all set. When `false`, the provider is hidden regardless of other values. |
| Provider Name | `OIDC_PROVIDER_NAME` | `Oidc__ProviderName` | `""` | Display name shown on the login button (e.g. `PogoAlerts`). |
| Authorization URL | `OIDC_AUTHORIZATION_URL` | `Oidc__AuthorizationUrl` | `""` | Browser-facing authorize endpoint. Any existing query string is preserved. |
| Token URL | `OIDC_TOKEN_URL` | `Oidc__TokenUrl` | `""` | Token endpoint that exchanges the authorization code for an access token. |
| UserInfo URL | `OIDC_USERINFO_URL` | `Oidc__UserInfoUrl` | `""` | UserInfo endpoint returning the user's claims. |
| End Session URL | `OIDC_END_SESSION_URL` | `Oidc__EndSessionUrl` | `""` | Optional RP-initiated logout (end-session) endpoint. When set, enables single logout (the provider's session is also ended). When empty, logout is local-only. |
| Client ID | `OIDC_CLIENT_ID` | `Oidc__ClientId` | `""` | OAuth2 client ID. |
| Client Secret | `OIDC_CLIENT_SECRET` | `Oidc__ClientSecret` | `""` | OAuth2 client secret. Read from config only — never stored in the database. |
| Scopes | `OIDC_SCOPES` | `Oidc__Scopes` | `openid profile email` | Space-delimited OAuth scopes requested at authorization time. |
| Identity Claim | `OIDC_IDENTITY_CLAIM` | `Oidc__IdentityClaim` | `discord_id` | UserInfo claim whose value maps to the Poracle human id (a Discord/Telegram id). Falls back to `sub` when the configured claim is absent. |
| Username Claim | `OIDC_USERNAME_CLAIM` | `Oidc__UsernameClaim` | `preferred_username` | UserInfo claim used as the display username. |
| Avatar Claim | `OIDC_AVATAR_CLAIM` | `Oidc__AvatarClaim` | `picture` | UserInfo claim used as the avatar URL. |
| Identity Type | `OIDC_IDENTITY_TYPE` | `Oidc__IdentityType` | `discord:user` | Value written to the JWT `type` claim for users logging in via this provider. |
| Use PKCE | `OIDC_USE_PKCE` | `Oidc__UsePkce` | `true` | Use PKCE (Proof Key for Code Exchange) for the authorization-code flow. |
| Force Local Login | `AUTH_FORCE_LOCAL` | `Auth__ForceLocal` | `false` | Break-glass override forcing the local login page regardless of the SSO mode. Recovery path when an admin switches to OIDC against a broken/unreachable provider and gets locked out. |

#### OIDC refresh tokens

Optional, opt-in. Enables silent server-side session renewal and provider-side revocation propagation. See [OIDC Refresh Tokens](oidc-refresh-tokens.md) for full detail.

| Setting | `.env` name | `.NET` env variable | Default | Description |
|---|---|---|---|---|
| Use Refresh Tokens | `OIDC_USE_REFRESH_TOKENS` | `Oidc__UseRefreshTokens` | `false` | Master opt-in for brokering the provider's refresh token (silent renewal + revocation propagation). Requires the provider to issue a refresh token. |
| Access Token Minutes | `OIDC_ACCESS_TOKEN_MINUTES` | `Oidc__AccessTokenMinutes` | `30` | Internal JWT lifetime (minutes) for refresh-backed OIDC sessions only. Other logins keep `Jwt__ExpirationMinutes`. |
| Refresh Token Lifetime (days) | `OIDC_REFRESH_TOKEN_LIFETIME_DAYS` | `Oidc__RefreshTokenLifetimeDays` | `30` | PoracleWeb-side absolute cap (days) on a refresh session before a real re-login is forced. |
| Revoked Retention (days) | `OIDC_SESSION_REVOKED_RETENTION_DAYS` | `Oidc__RevokedRetentionDays` | `2` | How long (days) revoked/rotated session rows are retained for replay detection before cleanup deletes them. |
| Offline Access Scope | `OIDC_OFFLINE_ACCESS_SCOPE` | `Oidc__OfflineAccessScope` | `offline_access` | Scope appended to the authorize request so a compliant provider issues a refresh token. Set empty for providers that issue refresh tokens unconditionally or use a non-standard mechanism (e.g. Google's `access_type=offline`). |
| Token Auth Method | `OIDC_TOKEN_AUTH_METHOD` | `Oidc__TokenEndpointAuthMethod` | `client_secret_post` | How client credentials are presented at the token endpoint: `client_secret_post` (form body) or `client_secret_basic` (HTTP Basic header). |

### Databases

| Setting | `.env` name | `.NET` env variable | Description |
|---|---|---|---|
| PoracleWeb.NET DB | `WEB_DB_HOST`, `WEB_DB_PORT`, `WEB_DB_NAME`, `WEB_DB_USER`, `WEB_DB_PASSWORD` | `ConnectionStrings__PoracleWebDb` | MySQL connection to PoracleWeb.NET database (site settings, webhook delegates, quick picks, user geofences). Short vars are auto-composed into a connection string. |
| Scanner DB | `SCANNER_DB_CONNECTION` | `ConnectionStrings__ScannerDb` | Scanner database connection (Golbat). Optional. Used by the gym picker search in raid/gym/egg add dialogs. Provide a full connection string. |

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
| `docker-compose.yml` | Local copy of `docker-compose.yml.example` (gitignored). Uses `env_file: .env` to load all configuration — no per-key `environment:` entries are needed. Edit only to change ports, volumes, or image tags. |
| `poracle_web.site_settings` table | Runtime admin-configurable settings (migrated from deprecated `pweb_settings`) |

!!! warning "PoracleNG must be reachable"
    `Poracle:ApiAddress` must point to a running PoracleNG instance that is reachable from the PoracleWeb.NET container. All alarm tracking, human/profile management, location, area operations, and active hours management are proxied through this API. If PoracleNG is unreachable, alarm operations fail entirely and user management (registration, login, location, areas, profile switch, active hours) also fails. The `Poracle:ApiSecret` must match the `server.apiSecret` value in PoracleNG's config.

!!! note "Secrets"
    `appsettings.Development.json` is gitignored and holds all connection strings, JWT secret, Discord/Telegram credentials, and Poracle API address/secret. Never commit secrets to the repository.

!!! tip "Runtime settings"
    Looking for settings you can change without restarting? See [Site Settings](site-settings.md) for admin-configurable runtime settings like branding, feature toggles, alarm types, and more.
