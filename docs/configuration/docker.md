# Docker Compose

The `docker-compose.yml` provides a production-ready container configuration. The `.env` file it reads is the same format used by standalone mode — see the [Configuration Reference](reference.md) for the full list of settings.

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
    environment:
      # All settings are loaded from .env — see docker-compose.yml for the full list
      - ConnectionStrings__PoracleDb=Server=${DB_HOST:-host.docker.internal};Port=${DB_PORT:-3306};...
    volumes:
      - ./data:/app/data
      - ${PORACLE_CONFIG_DIR:-./data}:/poracle-config:ro
    restart: unless-stopped
```

## Network requirements

The PoracleWeb.NET container must be able to reach:

- **PoracleNG API** (`Poracle:ApiAddress`) -- all alarm tracking writes are proxied through this endpoint. If the containers are on the same Docker network, use the service name (e.g., `http://poracleng:3030`). If on different hosts, use the host IP/domain.
- **MySQL** -- for `humans`/`profiles` tables and the `poracle_web` database.
- **Golbat API** (`Golbat:ApiAddress`) -- optional. When configured, enables Pokemon availability indicators. The container must be able to reach the Golbat scanner API.

## Volume mounts

### Data directory

The `./data` directory persists:

- Cached Discord avatars
- Cached DTS template files

### Poracle config directory

Mount your PoracleJS `config/` directory as read-only for DTS template preview functionality:

```yaml
volumes:
  - /path/to/PoracleJS/config:/poracle-config:ro
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
