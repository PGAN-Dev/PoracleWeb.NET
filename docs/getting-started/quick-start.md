# Quick Start (Docker)

This is the recommended way to run PoracleWeb in production.

## 1. Create environment file

Copy the example and fill in your values:

```bash
cp .env.example .env
```

Edit `.env` with your configuration:

```env
# Database — your existing Poracle MySQL instance
DB_HOST=host.docker.internal    # Use host IP if not on same machine
DB_PORT=3306
DB_NAME=poracle
DB_USER=root
DB_PASSWORD=your_db_password

# JWT Secret — generate a random string, minimum 32 characters
JWT_SECRET=generate-a-long-random-secret-key-at-least-32-chars

# Discord OAuth2 — create an app at https://discord.com/developers/applications
# Set the redirect URI to: http://your-domain:8082/auth/discord/callback
DISCORD_CLIENT_ID=your_discord_client_id
DISCORD_CLIENT_SECRET=your_discord_client_secret
DISCORD_BOT_TOKEN=your_discord_bot_token      # Optional: enables avatar display
DISCORD_GUILD_ID=your_discord_server_id        # Optional: for guild-specific features

# Poracle API — your running PoracleNG/PoracleJS instance
# CRITICAL: All alarm tracking writes are proxied through this API.
# PoracleNG must be reachable from the PoracleWeb container.
PORACLE_API_ADDRESS=http://host.docker.internal:3030
PORACLE_API_SECRET=your_poracle_api_secret      # Must match PoracleNG's server.apiSecret
PORACLE_ADMIN_IDS=your_discord_user_id         # Comma-separated admin Discord IDs

# Poracle config directory — mount for DTS template previews
PORACLE_CONFIG_DIR=/path/to/PoracleJS/config

# Optional: PoracleWeb DB for custom geofences (separate from Poracle DB)
WEB_DB_HOST=host.docker.internal
WEB_DB_PORT=3306
WEB_DB_NAME=poracle_web
WEB_DB_USER=root
WEB_DB_PASSWORD=your_db_password

# Optional: Koji geofence API (required for custom geofences)
KOJI_API_ADDRESS=http://host.docker.internal:8080
KOJI_BEARER_TOKEN=your_koji_bearer_token
KOJI_PROJECT_ID=1
KOJI_PROJECT_NAME=your_koji_project_name

# Optional: Discord forum channel for geofence submission threads
DISCORD_GEOFENCE_FORUM_CHANNEL_ID=

# Optional: Scanner DB for nest/Pokemon data
# SCANNER_DB_CONNECTION=Server=host.docker.internal;Port=3306;Database=rdmdb;User=root;Password=your_password

# Optional: Telegram authentication
TELEGRAM_ENABLED=false
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
```

## 2. Start with pre-built image

Pull the latest image from GitHub Container Registry and start:

```bash
docker pull ghcr.io/pgan-dev/poracleweb.net:latest
```

Update `docker-compose.yml` to use the registry image:

```yaml
services:
  poracle-web:
    image: ghcr.io/pgan-dev/poracleweb.net:latest
    # ...rest of config
```

Then start:

```bash
docker compose up -d
```

The app will be available at **http://localhost:8082**.

## 3. Build from source (alternative)

If you want to build the Docker image locally instead of pulling from the registry:

```bash
# Build and tag the image
docker build -t poracleweb.net:latest .

# Update docker-compose.yml to use local image:
#   image: poracleweb.net:latest

# Start the container
docker compose up -d
```

To force a clean rebuild:

```bash
docker build --no-cache -t poracleweb.net:latest .
docker compose up -d --force-recreate
```

## Updating

=== "Pre-built image (ghcr.io)"

    ```bash
    docker pull ghcr.io/pgan-dev/poracleweb.net:latest
    docker compose up -d --force-recreate
    ```

=== "Built from source"

    ```bash
    git pull
    docker build -t poracleweb.net:latest .
    docker compose up -d --force-recreate
    ```
