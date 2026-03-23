# Server Management

Admins can monitor and restart PoracleJS instances remotely from the "Poracle Servers" admin page.

## How it works

1. **Health check** — pings each server's API endpoint. Any HTTP response = online, no response = offline.
2. **Restart** — executes an SSH command (default: `pm2 restart all`) on the remote server.
3. Each server can have a **custom restart command** (e.g., for macOS where PM2 isn't in PATH).

## Component diagram

```mermaid
graph TB
    subgraph PoracleWeb
        UI[Poracle Servers Page<br/>Status cards + Restart buttons]
        Svc[PoracleServerService<br/>Health check + SSH restart]
    end

    UI --> Svc

    subgraph Servers
        S1[Main Server<br/>Linux<br/>API :3030]
        S2[Secondary Server<br/>macOS<br/>API :3035]
    end

    Svc -->|HTTP health check| S1
    Svc -->|HTTP health check| S2
    Svc -->|SSH + pm2 restart all| S1
    Svc -->|SSH + custom restart cmd| S2
```

## Restart flow

```mermaid
flowchart TD
    A[Admin clicks Restart] --> B{Confirm Dialog}
    B -->|Cancel| Z[No action]
    B -->|Confirm| C[POST /api/admin/poracle/servers/host/restart]
    C --> D[SSH to server with key<br/>30 second timeout]
    D --> E{Result}
    E -->|Exit code 0| F[Success: Server restarted<br/>Status refreshed]
    E -->|Timeout 30s| G[Warning: Kill process tree<br/>Timed out]
    E -->|SSH error| H[Error: Failed<br/>Check key/host]
```

## Setup

### 1. SSH key

Mount a private key that has access to your PoracleJS servers:

```yaml
# docker-compose.yml
volumes:
  - /path/to/your/ssh_key:/app/ssh_key:ro
```

Or in `docker-compose.override.yml`:

```yaml
services:
  poracle-web:
    volumes:
      - ~/.ssh/id_ed25519:/app/ssh_key:ro
```

### 2. Server configuration

Add servers via environment variables in `.env`:

```env
# Server 1 (Linux)
PORACLE_SERVER_1_NAME=Main
PORACLE_SERVER_1_HOST=10.0.3.178
PORACLE_SERVER_1_API=http://10.0.3.178:3030
PORACLE_SERVER_1_SSH_USER=root
# PORACLE_SERVER_1_RESTART_CMD=pm2 restart all   # default

# Server 2 (macOS — needs full PATH for PM2)
PORACLE_SERVER_2_NAME=Secondary
PORACLE_SERVER_2_HOST=10.0.6.107
PORACLE_SERVER_2_API=http://10.0.6.107:3035
PORACLE_SERVER_2_SSH_USER=hokiepokedad
PORACLE_SERVER_2_RESTART_CMD=PATH=/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin pm2 restart all
```

### 3. SSH access

Ensure the SSH key is authorized on each PoracleJS server (`~/.ssh/authorized_keys`).

### 4. Firewall

The Docker container needs:

- **SSH access** (port 22) to each PoracleJS server
- **HTTP access** to each server's API port for health checks
