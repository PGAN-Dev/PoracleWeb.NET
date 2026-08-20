# Quick Start (Docker)

This is the recommended way to run PoracleWeb.NET in production.

!!! tip "Not using Docker?"
    See the [Standalone Setup](standalone-setup.md) guide for running PoracleWeb.NET directly — no Docker or .NET experience required.

## 1. Get the files

```bash
git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
cd PoracleWeb.NET

# Check out the newest release. Without this you are on whatever the default
# branch resolves to; a tag pins you to a released version.
git checkout "$(git describe --tags --abbrev=0)"
```

Or download and extract a release. Everything below runs from this root directory.

!!! warning "`develop` is the beta channel"
    `develop` receives every merged pull request and is published as the `:beta` Docker image. `main` only moves when a release is published. It is where changes soak before a release, so it can contain work that has never been in a released version. Build from a release tag unless you specifically want to test unreleased changes — and if you do, prefer the `:beta` image over building from source so you get the exact artifact that was tested.

## 2. Create your `.env` and `docker-compose.yml`

Both files ship as `.example` templates so your local copies aren't clobbered by upstream updates. All user configuration lives in `.env`; `docker-compose.yml` rarely needs changes because it loads settings from `.env` via `env_file`.

```bash
# Interactive setup (recommended — generates JWT secret, prompts for values)
./scripts/setup.sh

# Or copy the templates manually and edit
cp .env.example .env
cp docker-compose.yml.example docker-compose.yml
```

Open `.env` in any editor and fill in the values below. Lines starting with `#` are comments.

### Required settings

```env
# Port — the port you'll access PoracleWeb.NET on (http://your-server:8082)
PORT=8082

# Database — your existing Poracle MySQL/MariaDB instance
DB_HOST=localhost           # IP or hostname of your database server
DB_PORT=3306                # MySQL port
DB_NAME=poracle             # Your Poracle database name
DB_USER=root
DB_PASSWORD=your_db_password

# PoracleWeb.NET database — a separate database for PoracleWeb.NET's own data
# Create this database first (see step 3 below), tables are auto-created
WEB_DB_HOST=localhost
WEB_DB_PORT=3306
WEB_DB_NAME=poracle_web
WEB_DB_USER=root
WEB_DB_PASSWORD=your_db_password

# JWT Secret — any random string, at least 32 characters
# Generate one: openssl rand -base64 48
JWT_SECRET=generate-a-long-random-secret-key-at-least-32-chars

# Discord OAuth2 — create an app at https://discord.com/developers/applications
# Set the OAuth2 redirect URI to: http://your-server:8082/api/auth/discord/callback
DISCORD_CLIENT_ID=your_discord_client_id
DISCORD_CLIENT_SECRET=your_discord_client_secret
# Optional: only needed for avatar caching and the geofence review forum posts.
DISCORD_BOT_TOKEN=your_discord_bot_token

# Poracle API — your running PoracleNG instance
# This is how PoracleWeb.NET talks to Poracle. It must be reachable from the container.
PORACLE_API_ADDRESS=http://host.docker.internal:3030
PORACLE_API_SECRET=your_poracle_api_secret    # Must match PoracleNG's server.apiSecret
PORACLE_ADMIN_IDS=your_discord_user_id        # Comma-separated Discord user IDs for admin access

# CORS origin — the URL you'll access PoracleWeb.NET from.
# Required in production (the container exits on startup if missing).
CORS_ORIGIN=http://localhost:8082
```

!!! warning "PoracleNG 5.1.0 or newer"
    Point `PORACLE_API_ADDRESS` at PoracleNG 5.1.0 or later. Older servers have no column for per-alarm delivery scope, the PVP mega evolution filter or the minimum time-left filter, so those three controls save without an error and change nothing. PoracleWeb logs an error at startup and shows the detected version on **Admin → Settings**.

### Optional settings

```env
# Poracle config directory — mount for DTS template previews
PORACLE_CONFIG_DIR=/path/to/PoracleNG/config

# Koji geofence API (required for custom geofences feature)
KOJI_API_ADDRESS=http://host.docker.internal:8080
KOJI_BEARER_TOKEN=your_koji_bearer_token
KOJI_PROJECT_ID=1
KOJI_PROJECT_NAME=your_koji_project_name

# Discord forum channel for geofence submission threads
DISCORD_GEOFENCE_FORUM_CHANNEL_ID=

# Scanner DB for gym picker in raid/egg dialogs (optional)
# SCANNER_DB_CONNECTION=Server=host.docker.internal;Port=3306;Database=golbat;User=root;Password=your_password

# Telegram authentication (optional)
TELEGRAM_ENABLED=false
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
```

See the [Configuration Reference](../configuration/reference.md) for the full list of settings.

### Public URL and reverse proxies

`PUBLIC_URL` is the origin people reach the instance on, and it pins the OAuth callback for both Discord and OIDC instead of letting the app guess one from each incoming request. Whatever you put here is what the provider must have registered. It is an origin only — no trailing path, no query — and an unusable value stops the app at startup rather than producing a callback the provider silently refuses.

```env
PUBLIC_URL=https://alerts.example.com
```

Leave it unset if the container is exposed directly, or if people reach it on several hostnames and you want the callback to follow whichever one they used. It is also the base for the links PoracleWeb puts in geofence review threads, so set it if you use the Discord submission forum.

If anything terminates TLS in front of the container — nginx, Caddy, Traefik, a Cloudflare Tunnel — also name it. `X-Forwarded-For` and `X-Forwarded-Proto` are believed only from declared addresses, because a header trusted from anyone lets a caller invent a new address per request and hand itself a fresh rate-limit allowance on the sign-in endpoints.

```env
# One or both. Comma-separated. Use the address the proxy connects FROM.
PROXY_KNOWN_PROXIES=127.0.0.1
PROXY_KNOWN_NETWORKS=172.18.0.0/16
```

With neither set, the app falls back to the connection address. That is safe but wrong in two visible ways: every user behind the proxy shares one sign-in rate-limit bucket, and callback URLs are built from the scheme the app received — `http://` — which Discord and OIDC providers reject. `PUBLIC_URL` fixes the callback half on its own; only these two fix the rate-limit bucket.

### Version checking

The Versions card under **Admin → Settings** compares your PoracleWeb and PoracleNG versions against the latest releases. Opening that page makes two anonymous GETs, to `api.github.com` and `raw.githubusercontent.com`, with no identifiers sent. There is no scheduler behind it: the result is cached for six hours, so an instance whose admins never open the page never calls GitHub at all. If your egress policy blocks it, or you'd rather it didn't happen, switch on **Do not check for updates** (`disable_update_check`) under **Admin → Settings**.

## 3. Create the PoracleWeb.NET database

PoracleWeb.NET needs its own database (separate from your Poracle bot database). Tables are created automatically on first start — you just need to create the empty database:

```bash
# If you ran ./scripts/setup.sh, it offered to create this for you.
# Otherwise, run manually:
mysql -u root -p -e "CREATE DATABASE IF NOT EXISTS poracle_web CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;"
```

Or in a MySQL client:

```sql
CREATE DATABASE poracle_web CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
```

!!! note
    This database is separate from your Poracle database. PoracleWeb.NET never modifies the Poracle DB schema.

## 4. Start

=== "Pre-built image (recommended)"

    ```bash
    docker compose pull
    docker compose up -d
    ```

=== "Build from source"

    ```bash
    ./scripts/docker.sh build
    ./scripts/docker.sh start
    ```

    Or without the scripts: `docker build -t poracleweb.net:latest . && docker compose up -d`

The app is now running at **http://localhost:8082** (or whatever port you set in `.env`).

Check the logs to make sure everything started cleanly:

```bash
docker compose logs -f
```

## 5. Set up Discord OAuth2

1. Go to your [Discord Developer Portal](https://discord.com/developers/applications) and select your app
2. Under **OAuth2**, add a redirect URI: `http://your-server:PORT/api/auth/discord/callback`
   (e.g., `http://192.168.1.50:8082/api/auth/discord/callback`)
3. Make sure the client ID and secret in your `.env` match

See the [Discord OAuth2 guide](discord-oauth.md) for detailed instructions.

## Changing the port

Set `PORT` in your `.env` file:

```env
PORT=9090
```

Then restart:

```bash
docker compose up -d
```

The app will now be available at `http://your-server:9090`. Remember to update your Discord OAuth2 redirect URI to match the new port.

## Updating

=== "Pre-built image"

    ```bash
    docker compose pull
    docker compose up -d --force-recreate
    ```

=== "Built from source"

    ```bash
    git fetch --tags
    git checkout "$(git describe --tags --abbrev=0 origin/main)"
    ./scripts/docker.sh update
    ```

    Or without the script: `docker build -t poracleweb.net:latest . && docker compose up -d --force-recreate`

    !!! note "A plain `git pull` tracks the beta channel"
        `git pull` moves you to the newest commit on your current branch. On `develop` that is the beta channel. Fetch tags and check the newest one out to stay on released code.

## Common issues

**Container won't start / exits immediately**
: Check logs with `docker compose logs`. Usually a missing or invalid required setting in `.env`.

**Container crashes: "Configuration 'Cors:AllowedOrigins' is required"**
: Set `CORS_ORIGIN` in your `.env` to the URL you access PoracleWeb.NET from (e.g., `CORS_ORIGIN=http://192.168.1.50:8082`). This is required in production mode.

**Can't connect to database**
: If your database is on the host machine (not in Docker), set `DB_HOST=host.docker.internal` in `.env`. The default in `.env.example` is `localhost` (for standalone use); Docker users connecting to the host must change this.

**Poracle API errors / alarms don't save**
: `PORACLE_API_ADDRESS` must be reachable from inside the container. If Poracle runs on the host, use `http://host.docker.internal:3030`. If it's on another machine, use that machine's IP.

**Discord login fails**
: The redirect URI in your Discord app must match exactly: `http://your-server:PORT/api/auth/discord/callback`. Check both the port and the hostname/IP. If you serve the site over HTTPS through a reverse proxy and Discord reports the callback as `http://`, set `PUBLIC_URL` — see [Public URL and reverse proxies](#public-url-and-reverse-proxies) above and [the troubleshooting entry](../troubleshooting.md#sign-in-fails-with-invalid-oauth2-redirect_uri).
