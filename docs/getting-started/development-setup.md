# Development Setup

## 1. Clone and install dependencies

```bash
git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
cd PoracleWeb.NET

# Install frontend dependencies (from root)
./scripts/dev.sh install
```

Or manually: `cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm install`

## 2. Configure secrets

=== ".env file (recommended)"

    Run the interactive setup to create and configure your `.env` at the project root:

    ```bash
    ./scripts/setup.sh

    # Or copy the template manually and edit
    cp .env.example .env
    ```

    The same `.env` format works for development, Docker, and standalone mode. `Program.cs` loads `.env` from the current working directory and auto-translates short variable names (`DB_HOST`, `JWT_SECRET`, `DISCORD_CLIENT_ID`, etc.) into .NET's configuration format — no need to write full connection strings or edit `appsettings` manually. `./scripts/dev.sh api` (and `start`) export the root `.env` before launching `dotnet run`, so values are picked up regardless of which directory `dotnet run` resolves to.

=== "appsettings.Development.json"

    Create `Applications/Pgan.PoracleWebNet.Api/appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "PoracleDb": "Server=localhost;Port=3306;Database=poracle;User=root;Password=your_password;AllowZeroDateTime=true;ConvertZeroDateTime=true",
    "PoracleWebDb": "Server=localhost;Port=3306;Database=poracle_web;User=root;Password=your_password;AllowZeroDateTime=true;ConvertZeroDateTime=true",
    "ScannerDb": ""
  },
  "Jwt": {
    "Secret": "your-development-secret-key-at-least-32-characters-long"
  },
  "Discord": {
    "ClientId": "your_discord_client_id",
    "ClientSecret": "your_discord_client_secret",
    "FrontendUrl": "http://localhost:4200",
    "BotToken": "your_discord_bot_token",
    "GuildId": "your_discord_guild_id",
    "GeofenceForumChannelId": ""
  },
  "Telegram": {
    "Enabled": false,
    "BotToken": "",
    "BotUsername": ""
  },
  "Poracle": {
    "ApiAddress": "http://localhost:3030",
    "ApiSecret": "your_poracle_secret",
    "AdminIds": "your_discord_user_id"
  },
  "Koji": {
    "ApiAddress": "http://localhost:8080",
    "BearerToken": "your_koji_bearer_token",
    "ProjectId": 1,
    "ProjectName": "your_koji_project_name"
  }
}
```

!!! warning "PoracleNG must be running"
    `Poracle:ApiAddress` must point to a running PoracleNG instance. All alarm tracking writes are proxied through this API. `Poracle:ApiSecret` must match PoracleNG's `server.apiSecret` config value.

## 3. Run the application

=== "Both at once (recommended)"

    ```bash
    ./scripts/dev.sh start
    ```

    Starts both the API and Angular dev server in one terminal. Output is prefixed with `[api]` and `[app]`. Press Ctrl+C to stop both.

=== "Separate terminals"

    **Backend API:**

    ```bash
    ./scripts/dev.sh api
    # or: cd Applications/Pgan.PoracleWebNet.Api && dotnet run
    ```

    Starts on **http://localhost:5048**. Swagger/OpenAPI is available in development mode.

    **Frontend:**

    ```bash
    ./scripts/dev.sh app
    # or: cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm start
    ```

    Starts on **http://localhost:4200**. The dev server proxies `/api/*` and `/auth/*` to the API on `http://localhost:5048` via `Applications/Pgan.PoracleWebNet.App/ClientApp/proxy.conf.json` (`changeOrigin: false` so the original `Host` header is preserved — this matters for OAuth callback URIs, which Discord matches by literal string against your registered redirect URI).

    The Angular environment uses an empty `apiUrl` in dev (`environment.development.ts`), so all HTTP calls are same-origin from the browser's view. This makes the dev server behave identically to the production single-port deployment that serves the Angular build out of the API's `wwwroot`.

    To use a different dev port (e.g. to match an existing Discord OAuth registration on `http://localhost:8082`):

    ```bash
    npx ng serve --proxy-config proxy.conf.json --port 8082
    ```

    The dev server port must be present in your Discord application's **Redirects** list for OAuth login to work. The default `4200` matches `Discord:FrontendUrl` in `appsettings.json`.

Open **http://localhost:4200** in your browser.

## 4. Running tests

```bash
# All tests (backend + frontend)
./scripts/dev.sh test

# Or individually:
dotnet test                                                    # Backend (xUnit)
cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm test   # Frontend (Jest)
```

## 5. Linting and formatting

```bash
# Check lint + formatting
./scripts/dev.sh lint

# Or manually:
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm run lint             # ESLint check
npm run prettier-check   # Prettier check
npx eslint --fix src/    # Auto-fix lint issues
npm run prettier-format  # Auto-format code
```

## Build commands

```bash
# Full production build (API + Angular, outputs to ./publish)
./scripts/dev.sh build

# Or manually:
dotnet build                                                          # Build .NET solution
cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm run build     # Angular production build
cd Applications/Pgan.PoracleWebNet.App/ClientApp && npm run watch     # Angular watch mode
```
