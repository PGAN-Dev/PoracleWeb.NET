# Development Setup

## 1. Clone and install dependencies

```bash
git clone https://github.com/PGAN-Dev/PoracleWeb.NET.git
cd PoracleWeb.NET

# Install frontend dependencies
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm install
cd ../../..
```

## 2. Configure secrets

Create `Applications/PGAN.Poracle.Web.Api/appsettings.Development.json` (gitignored):

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
    "RedirectUri": "http://localhost:4200/auth/discord/callback",
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

## 3. Run the application

You need two terminals — one for the backend API and one for the Angular dev server:

=== "Backend API"

    ```bash
    cd Applications/PGAN.Poracle.Web.Api
    dotnet run
    ```

    Starts on **http://localhost:5048**. Swagger/OpenAPI is available in development mode.

=== "Frontend"

    ```bash
    cd Applications/PGAN.Poracle.Web.App/ClientApp
    npm start
    ```

    Starts on **http://localhost:4200**. The Angular dev server proxies API requests to the .NET backend.

Open **http://localhost:4200** in your browser.

## 4. Running tests

```bash
# Frontend tests (Jest)
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm test

# Backend tests (xUnit)
dotnet test
```

## 5. Linting and formatting

```bash
cd Applications/PGAN.Poracle.Web.App/ClientApp

# Check lint
npm run lint

# Check formatting
npm run prettier-check

# Auto-fix lint issues
npx eslint --fix src/

# Auto-format code
npm run prettier-format
```

## Build commands

```bash
# Build entire solution (from solution root)
dotnet build

# Angular production build
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm run build

# Angular watch mode
npm run watch
```
