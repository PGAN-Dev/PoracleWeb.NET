# Standalone Setup (no Docker)

This guide is for running PoracleWeb.NET directly on a machine without Docker. If you're coming from a Node.js or Go background, think of this as the equivalent of `node server.js` or `go run main.go` — but for .NET.

You'll configure everything in a single `.env` file at the project root and run a single command. No need to dig into subdirectories.

## What you need

| Requirement | Install | Think of it as... |
|---|---|---|
| **.NET 10 Runtime** | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0) | Like installing Node.js or Go |
| **MySQL / MariaDB** | Your existing Poracle database server | Same DB your Poracle bot uses |
| **PoracleNG 5.1.0+** | Already running with REST API enabled | The bot this app talks to |
| **Discord App** | [discord.com/developers](https://discord.com/developers/applications) | OAuth2 for user login |

!!! warning "Why the PoracleNG version matters"
    Per-alarm delivery scope, the PVP mega evolution filter and the minimum time-left filter write columns that only exist from PoracleNG 5.1.0. On an older server those three controls save without an error and change nothing. PoracleWeb logs an error at startup when it detects one, and reports the version it found on **Admin → Settings**.

!!! tip "Runtime vs SDK"
    You only need the **ASP.NET Core Runtime** to run a pre-built release. The **.NET SDK** is only needed if you want to build from source.

## 1. Get the app

=== "Clone a release tag (recommended)"

    Releases are source-only — no prebuilt archives are attached, so clone the tag you want and build it:

    ```bash
    git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git poracleweb
    cd poracleweb

    # Newest release tag. Or `git checkout vX.Y.Z` to pin a specific one.
    git checkout "$(git describe --tags --abbrev=0)"
    ```

    Then build it with the commands in the next tab. Skip the checkout to stay on `main`, which always
    points at the most recent release. If you would rather not build at all, the
    [Docker image](../configuration/docker.md) is the prebuilt option.

=== "Build from source"

    ```bash
    git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
    cd PoracleWeb.NET

    # Check out the newest release. `develop` is the beta channel and carries
    # merged-but-unreleased work.
    git checkout "$(git describe --tags --abbrev=0)"

    # Build everything with the convenience script
    ./scripts/dev.sh build

    cd publish
    ```

    Or manually:

    ```bash
    dotnet publish Applications/Pgan.PoracleWebNet.Api -c Release -o ./publish
    npm install --prefix Applications/Pgan.PoracleWebNet.App/ClientApp
    npx --prefix Applications/Pgan.PoracleWebNet.App/ClientApp ng build --configuration production
    cp -r Applications/Pgan.PoracleWebNet.App/ClientApp/dist/ClientApp/browser/* ./publish/wwwroot/

    cd publish
    ```

## 2. Configure

Create a `.env` file in the directory where you'll run the app. This is the **same format** as the Docker setup — one file, same variable names.

```bash
# Interactive setup (recommended — if you cloned the repo)
./scripts/setup.sh

# Or copy manually and edit
cp .env.example .env
```

Or create `.env` from scratch:

```env
# Port — the port PoracleWeb.NET listens on (like PORT in Express/Gin)
PORT=8082

# Database — your existing Poracle MySQL/MariaDB instance
DB_HOST=localhost
DB_PORT=3306
DB_NAME=poracle
DB_USER=root
DB_PASSWORD=your_db_password

# PoracleWeb.NET database — a separate DB for PoracleWeb.NET's own data
WEB_DB_HOST=localhost
WEB_DB_PORT=3306
WEB_DB_NAME=poracle_web
WEB_DB_USER=root
WEB_DB_PASSWORD=your_db_password

# JWT Secret — any random string, at least 32 characters
JWT_SECRET=generate-a-long-random-secret-key-at-least-32-chars

# Discord OAuth2
DISCORD_CLIENT_ID=your_discord_client_id
DISCORD_CLIENT_SECRET=your_discord_client_secret
DISCORD_BOT_TOKEN=your_discord_bot_token

# Poracle API
PORACLE_API_ADDRESS=http://localhost:3030
PORACLE_API_SECRET=your_poracle_api_secret
PORACLE_ADMIN_IDS=your_discord_user_id

# CORS origin — required when ASPNETCORE_ENVIRONMENT=Production (e.g., the systemd unit below).
# Omit or comment out when running in Development mode.
CORS_ORIGIN=http://localhost:8082
```

!!! info "How `.env` works here"
    Docker Compose reads `.env` natively. For standalone mode, the app loads `.env` from the working directory on startup — same file, same format, no extra tools. Variables already set in your environment take precedence over `.env` values.

!!! tip "Same `.env` works everywhere"
    The app automatically translates short env var names (`DB_HOST`, `JWT_SECRET`, `DISCORD_CLIENT_ID`, etc.) into the format .NET expects. The same `.env` file works for both Docker and standalone mode — no need to write full connection strings manually.

### Create the PoracleWeb.NET database

PoracleWeb.NET needs its own database (separate from the Poracle bot database). Tables are created automatically on first start.

```sql
CREATE DATABASE poracle_web CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
```

!!! note
    This database is separate from your Poracle database. PoracleWeb.NET never modifies the Poracle DB schema.

## 3. Run

```bash
dotnet Pgan.PoracleWebNet.Api.dll
```

That's it. The app starts on the port from your `.env` file (default `http://localhost:8082`).

### Changing the port

Edit `PORT` in `.env`:

```env
PORT=9090
```

Or pass it as an env var:

```bash
PORT=9090 dotnet Pgan.PoracleWebNet.Api.dll
```

Or use the .NET-style config:

```bash
dotnet Pgan.PoracleWebNet.Api.dll --Server:Port=9090
```

## 4. Run as a service

### systemd (Linux)

Create `/etc/systemd/system/poracleweb.service`:

```ini
[Unit]
Description=PoracleWeb.NET
After=network.target mysql.service

[Service]
Type=exec
WorkingDirectory=/opt/poracleweb
ExecStart=/usr/bin/dotnet /opt/poracleweb/Pgan.PoracleWebNet.Api.dll
Restart=always
RestartSec=10
User=poracleweb
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable poracleweb
sudo systemctl start poracleweb
sudo systemctl status poracleweb

# View logs
journalctl -u poracleweb -f
```

Place your `.env` file in `/opt/poracleweb/` (the `WorkingDirectory`) and the app will pick it up automatically.

### pm2

```bash
cd /opt/poracleweb
pm2 start "dotnet Pgan.PoracleWebNet.Api.dll" --name poracleweb
pm2 save
```

### Windows Service

Use [NSSM](https://nssm.cc/):

```powershell
nssm install PoracleWeb.NET "C:\Program Files\dotnet\dotnet.exe" "C:\poracleweb\Pgan.PoracleWebNet.Api.dll"
nssm set PoracleWeb.NET AppDirectory "C:\poracleweb"
nssm start PoracleWeb.NET
```

Place your `.env` file in `C:\poracleweb\` (the `AppDirectory`) and the app will pick it up automatically.

## 5. Verify

```bash
# Health check
curl http://localhost:8082/

# Which build is running (anonymous)
curl http://localhost:8082/api/version

# Master data the SPA loads on startup (anonymous)
curl http://localhost:8082/api/masterdata/pokemon
```

Open `http://your-host:8082` in a browser. You should see the login page.

## Reverse proxy (optional)

If you want to put PoracleWeb.NET behind nginx or Caddy (just like you might with a Node.js app):

=== "nginx"

    ```nginx
    server {
        listen 80;
        server_name poracle.example.com;

        location / {
            proxy_pass http://127.0.0.1:8082;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
    ```

=== "Caddy"

    ```
    poracle.example.com {
        reverse_proxy 127.0.0.1:8082
    }
    ```

When using a reverse proxy, set `CORS_ORIGIN=https://poracle.example.com` in your `.env` (replacing any `http://localhost:...` value you already have) and update your Discord OAuth2 redirect URI to match the public URL.

You also have to tell the app which proxy to believe. `X-Forwarded-For` and `X-Forwarded-Proto` are only honoured from declared addresses, because a header believed from anyone lets a caller name a different address on each request and hand itself a fresh rate-limit allowance on the sign-in endpoints:

```bash
# One or both. Comma-separated. Use the address the proxy connects FROM.
PROXY_KNOWN_PROXIES=127.0.0.1
PROXY_KNOWN_NETWORKS=172.18.0.0/16
```

Leave both unset and the app falls back to the connection address, which is safe but wrong in two visible ways: every user behind the proxy shares one rate-limit bucket, and OAuth callback URLs are built from the scheme the app *received* — `http://` — so Discord and OIDC providers reject the sign-in with an invalid `redirect_uri`.

For the sign-in half of that you can skip the inference entirely and name the URL:

```bash
PUBLIC_URL=https://poracle.example.com
```

`PUBLIC_URL` is the origin users type in, and the one you register with Discord or your OIDC provider. It must be an origin only — no trailing path — and an unusable value stops the app at startup rather than producing a callback the provider silently refuses. Set it if you have a single public address; leave it unset if people reach the instance on several hostnames and you want the callback to follow whichever one they used.

## Troubleshooting

**"Configuration 'ConnectionStrings:PoracleDb' is required"**
: The app can't find your database connection string. Make sure `.env` has the `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, and `DB_PASSWORD` variables set. The app auto-composes them into a full connection string.

**"Could not ensure PoracleWeb.NET database tables exist"**
: The `poracle_web` database doesn't exist or the connection string is wrong. Create it with the SQL command above.

**Alarm operations fail / "PoracleNG unreachable"**
: `PORACLE_API_ADDRESS` in your `.env` must point to a running PoracleNG instance. All alarm tracking is proxied through it — there's no fallback.

**Port already in use**
: Change `PORT` in `.env` or pass a different port on the command line.

**Discord login redirect fails**
: Make sure the redirect URI configured in your Discord application's OAuth2 settings matches exactly: `http://your-host:PORT/api/auth/discord/callback`, including the port.

**`.env` not loading**
: The app reads `.env` from the **working directory** — the directory you're in when you run the command. Make sure `.env` is in that directory. Variables already set in your shell environment take precedence over `.env` values.
