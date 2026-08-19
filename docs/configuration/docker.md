# Docker Compose

PoracleWeb.NET ships `docker-compose.yml.example` as a template — copy it to `docker-compose.yml` on first install (`cp docker-compose.yml.example docker-compose.yml`). The compose file loads all user-configurable values from `.env` via `env_file`, so it rarely needs local edits. The `.env` file itself is the same format used by standalone mode — see the [Configuration Reference](reference.md) for the full list of settings.

## Container settings

| Setting | Value |
|---|---|
| **Port mapping** | Host `${PORT:-8082}` → Container `8080`. Set `PORT` in `.env` to change. |
| **Volumes** | `./data` for avatar/DTS cache persistence, Poracle config directory (read-only) |
| **Health check** | HTTP check every 30s with 15s startup grace period |
| **Resource limits** | 2 CPUs, 2GB memory |
| **Logging** | JSON file driver, 10MB max per file, 3 file rotation |
| **Restart policy** | `unless-stopped` |

## Example docker-compose.yml

```yaml
services:
  poracleweb.net:
    image: ghcr.io/pgan-dev/poracleweb.net:latest
    ports:
      - "${PORT:-8082}:8080"
    env_file:
      - .env
    environment:
      # Container-side paths only; everything else is loaded from .env.
      # JWT issuer/audience default to "PoracleWeb" / "PoracleWeb.App" via Program.cs —
      # override by setting JWT_ISSUER / JWT_AUDIENCE in .env if needed.
      - DTS_SOURCE_DIR=/poracle-config
      - DATA_DIR=/app/data
    volumes:
      - ./data:/app/data
      - ${PORACLE_CONFIG_DIR:-./data}:/poracle-config:ro
    restart: unless-stopped
```

## Network requirements

Inside your own network, the PoracleWeb.NET container must be able to reach:

- **PoracleNG API** (`Poracle:ApiAddress`) -- all alarm tracking writes are proxied through this endpoint. If the containers are on the same Docker network, use the service name (e.g., `http://poracleng:3030`). If on different hosts, use the host IP/domain.
- **MySQL** -- for `humans`/`profiles` tables and the `poracle_web` database.
- **Golbat API** (`Golbat:ApiAddress`) -- optional. When configured, enables Pokemon availability indicators.
- **Scanner database** (`ConnectionStrings:ScannerDb`) -- optional. Backs the gym picker in the raid/gym/egg dialogs and the dashboard weather panel. Without it both are simply absent.
- **Koji** (`Koji:ApiAddress`) -- required for admin geofences and region lookups. A user can still draw and use a private [custom geofence](../features/custom-geofences/index.md) without it: if Koji is unreachable the feed still serves user geofences from the local database, but approving one to a public area, and the region auto-detection on the draw page, both need Koji.

Outbound to the internet:

- **`discordapp.com` and `cdn.discordapp.com`** -- Discord OAuth sign-in, role lookups, avatars, and the geofence review forum posts. Unavoidable if Discord login is enabled.
- **The geocoder** -- the address search and reverse lookup proxy to whatever `providerURL` PoracleNG's config names (usually a Nominatim instance). Switch it off with the `disable_nominatim` site setting.
- **`raw.githubusercontent.com`** (Masterfile-Generator) -- the game master data behind the Pokemon, move and item pickers, fetched on the first request after a cold start and cached in memory. Not switchable; without it the pickers fall back to whatever is already cached.
- **`api.github.com` and `raw.githubusercontent.com`** -- the version check behind the Versions card on **Admin > Settings**. Two anonymous GETs, made when an admin opens the card and cached six hours afterwards, nothing sent. On a locked-down egress policy it fails silently and the card cannot tell you whether an update exists. Turn it off with the `disable_update_check` site setting.

## Volume mounts

### Data directory

Mounted at `DATA_DIR` (`/app/data` in the image). It persists:

- DataProtection keys. These encrypt the stored OIDC refresh tokens, so losing them signs every SSO user out on the next container recreate.
- Cached Discord avatars
- Cached DTS template files

See [Paths](reference.md#paths) for the environment variables behind these.

### Poracle config directory

Mount your PoracleNG `config/` directory as read-only for DTS template preview functionality:

```yaml
volumes:
  - /path/to/PoracleNG/config:/poracle-config:ro
```

## Building locally

Using the convenience script:

```bash
./scripts/docker.sh build     # Build from source
./scripts/docker.sh start     # Start the container
./scripts/docker.sh update    # Rebuild and recreate
./scripts/docker.sh clean     # Force rebuild (no cache)
./scripts/docker.sh logs      # Tail logs
./scripts/docker.sh stop      # Stop the container
```

Or with raw Docker commands:

```bash
docker build -t poracleweb.net:latest .
docker compose up -d
docker compose up -d --force-recreate
docker build --no-cache -t poracleweb.net:latest .
```

!!! warning "Building from source builds whatever is checked out"
    These commands build your working tree. On a fresh clone that is `main`, which tracks releases; on `develop` it is unreleased work. Check out a release tag first (`git checkout "$(git describe --tags --abbrev=0)"`), or skip the build entirely and use the published `ghcr.io/pgan-dev/poracleweb.net:latest` image, which only moves when a release is published.
