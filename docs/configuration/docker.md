# Docker Compose

The `docker-compose.yml` provides a production-ready container configuration.

## Container settings

| Setting | Value |
|---|---|
| **Port mapping** | Host `8082` → Container `8080` |
| **Volumes** | `./data` for avatar/DTS cache persistence, Poracle config directory (read-only) |
| **Health check** | HTTP check every 30s with 15s startup grace period |
| **Resource limits** | 2 CPUs, 2GB memory |
| **Logging** | JSON file driver, 10MB max per file, 3 file rotation |
| **Restart policy** | `unless-stopped` |

## Example docker-compose.yml

```yaml
services:
  poracle-web:
    image: ghcr.io/pgan-dev/poracleweb.net:latest
    ports:
      - "8082:8080"
    env_file:
      - .env
    volumes:
      - ./data:/app/data
      - ${PORACLE_CONFIG_DIR}:/app/poracle-config:ro
    restart: unless-stopped
```

## Network requirements

The PoracleWeb container must be able to reach:

- **PoracleNG API** (`Poracle:ApiAddress`) -- all alarm tracking writes are proxied through this endpoint. If the containers are on the same Docker network, use the service name (e.g., `http://poracleng:3030`). If on different hosts, use the host IP/domain.
- **MySQL** -- for `humans`/`profiles` tables and the `poracle_web` database.

## Volume mounts

### Data directory

The `./data` directory persists:

- Cached Discord avatars
- Cached DTS template files

### Poracle config directory

Mount your PoracleJS `config/` directory as read-only for DTS template preview functionality:

```yaml
volumes:
  - /path/to/PoracleJS/config:/app/poracle-config:ro
```

### SSH key (optional)

For remote server management, mount an SSH private key:

```yaml
volumes:
  - ~/.ssh/id_ed25519:/app/ssh_key:ro
```

Or in `docker-compose.override.yml`:

```yaml
services:
  poracle-web:
    volumes:
      - ~/.ssh/id_ed25519:/app/ssh_key:ro
```

## Building locally

```bash
# Build from source
docker build -t poracleweb.net:latest .

# Force clean rebuild
docker build --no-cache -t poracleweb.net:latest .

# Start
docker compose up -d

# Force recreate
docker compose up -d --force-recreate
```
