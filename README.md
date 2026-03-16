# PGAN.Poracle.Web

A web application for managing Poracle Pokemon GO notification alarms. Users authenticate via Discord OAuth2 or Telegram and configure personalized alert filters (Pokemon, Raids, Quests, Invasions, Lures, Nests, Gyms) through a browser-based UI.

## Tech Stack

- **Backend**: .NET 10 / ASP.NET Core Web API, EF Core with MySQL
- **Frontend**: Angular 21, Angular Material (Material Design 3), Leaflet maps
- **Auth**: Discord OAuth2, Telegram Bot Login, JWT bearer tokens
- **CI/CD**: GitHub Actions, Docker (ghcr.io)

## Quick Start (Docker)

1. Create a `.env` file in the project root:

```env
# Required
DB_HOST=host.docker.internal
DB_PORT=3306
DB_NAME=poracle
DB_USER=root
DB_PASSWORD=your_db_password
JWT_SECRET=your_jwt_secret_min_32_chars
DISCORD_CLIENT_ID=your_discord_app_client_id
DISCORD_CLIENT_SECRET=your_discord_app_client_secret
PORACLE_API_ADDRESS=http://host.docker.internal:4Pokemon
PORACLE_API_SECRET=your_poracle_api_secret
PORACLE_ADMIN_IDS=your_discord_user_id

# Optional
DISCORD_BOT_TOKEN=              # Enables avatar display
SCANNER_DB_CONNECTION=          # Scanner DB for nest/Pokemon data
TELEGRAM_ENABLED=false
TELEGRAM_BOT_TOKEN=
TELEGRAM_BOT_USERNAME=
PORACLE_CONFIG_DIR=./data       # Path to Poracle config dir (for DTS templates)
```

2. Start the container:

```bash
docker compose up -d
```

The app will be available at `http://localhost:8082`.

## Development Setup

### Prerequisites

- .NET 10 SDK
- Node.js 22+
- MySQL database with Poracle schema

### Running Locally

```bash
# Build the solution
dotnet build

# Run the API (http://localhost:5048)
cd Applications/PGAN.Poracle.Web.Api
dotnet run

# In a separate terminal, run the Angular dev server (http://localhost:4200)
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm install
npm start
```

Configure connection strings and secrets in `Applications/PGAN.Poracle.Web.Api/appsettings.Development.json` (gitignored).

### Running Tests

```bash
cd Applications/PGAN.Poracle.Web.App/ClientApp
npm test
```

### Building from Source with Docker

Uncomment the `build` section in `docker-compose.yml` and comment out the `image` line, then:

```bash
docker compose up -d --build
```

## Project Structure

```
PGAN.Poracle.Web.slnx
├── Core/                        # Business logic layer
│   ├── Core.Abstractions/       # Interfaces (IRepository, IService, IUnitOfWork)
│   ├── Core.Models/             # DTOs
│   ├── Core.Mappings/           # AutoMapper profiles
│   ├── Core.Repositories/       # Data access
│   ├── Core.Services/           # Business logic
│   └── Core.UnitsOfWork/        # Unit of work pattern
├── Data/                        # Data access layer
│   ├── Data/                    # EF Core DbContext, Entities, Configurations
│   └── Data.Scanner/            # Optional scanner DB context
└── Applications/                # Application layer
    ├── Web.Api/                 # ASP.NET Core host, Controllers, DI config
    └── Web.App/ClientApp/       # Angular 21 SPA
```

## Configuration Reference

All configuration can be provided via environment variables (Docker) or `appsettings.json`:

| Setting | Env Variable | Description |
|---|---|---|
| `ConnectionStrings:PoracleDb` | `ConnectionStrings__PoracleDb` | MySQL connection to Poracle database |
| `ConnectionStrings:ScannerDb` | `ConnectionStrings__ScannerDb` | Optional scanner database connection |
| `Jwt:Secret` | `Jwt__Secret` | JWT signing key (min 32 characters) |
| `Discord:ClientId` | `Discord__ClientId` | Discord OAuth2 application client ID |
| `Discord:ClientSecret` | `Discord__ClientSecret` | Discord OAuth2 application client secret |
| `Discord:BotToken` | `Discord__BotToken` | Optional, enables Discord avatar display |
| `Telegram:Enabled` | `Telegram__Enabled` | Enable Telegram authentication |
| `Telegram:BotToken` | `Telegram__BotToken` | Telegram bot token |
| `Telegram:BotUsername` | `Telegram__BotUsername` | Telegram bot username |
| `Poracle:ApiAddress` | `Poracle__ApiAddress` | Poracle API base URL |
| `Poracle:ApiSecret` | `Poracle__ApiSecret` | Poracle API shared secret |
| `Poracle:AdminIds` | `Poracle__AdminIds` | Comma-separated Discord admin user IDs |

## CI/CD

Pushing to `main` triggers a GitHub Actions workflow that builds a Docker image and publishes it to `ghcr.io`. Images are tagged with `latest` and the git commit SHA.
