#!/usr/bin/env bash
# PoracleWeb.NET — Interactive first-time setup
# Creates and configures your .env file from .env.example
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$ROOT/.env"
ENV_EXAMPLE="$ROOT/.env.example"

# Colors (disabled if not a terminal)
if [ -t 1 ]; then
  BOLD='\033[1m' DIM='\033[2m' GREEN='\033[32m' YELLOW='\033[33m' CYAN='\033[36m' RESET='\033[0m'
else
  BOLD='' DIM='' GREEN='' YELLOW='' CYAN='' RESET=''
fi

header() { echo -e "\n${BOLD}${CYAN}$1${RESET}"; }
info()   { echo -e "${DIM}$1${RESET}"; }
ok()     { echo -e "${GREEN}$1${RESET}"; }
warn()   { echo -e "${YELLOW}$1${RESET}"; }

# Prompt for a value with a default
ask() {
  local var_name="$1" prompt="$2" default="$3" value
  read -rp "  $prompt [${default:-empty}]: " value
  value="${value:-$default}"
  echo "$value"
}

# Update a key=value in the .env file
set_env() {
  local key="$1" value="$2"
  if grep -q "^${key}=" "$ENV_FILE" 2>/dev/null; then
    # Escape special chars for sed replacement
    local escaped_value
    escaped_value=$(printf '%s\n' "$value" | sed 's/[&/\]/\\&/g')
    if [[ "$OSTYPE" == "darwin"* ]]; then
      sed -i '' "s|^${key}=.*|${key}=${escaped_value}|" "$ENV_FILE"
    else
      sed -i "s|^${key}=.*|${key}=${escaped_value}|" "$ENV_FILE"
    fi
  else
    echo "${key}=${value}" >> "$ENV_FILE"
  fi
}

# ─────────────────────────────────────────────────────────────────────────────

echo -e "${BOLD}PoracleWeb.NET Setup${RESET}"
echo "This will create and configure your .env file."
echo "Press Enter to accept the default value shown in brackets."

# Step 1: Create .env
header "1. Environment file"
if [ -f "$ENV_FILE" ]; then
  warn "  .env already exists — will update values in place."
else
  if [ -f "$ENV_EXAMPLE" ]; then
    cp "$ENV_EXAMPLE" "$ENV_FILE"
    ok "  Copied .env.example -> .env"
  else
    touch "$ENV_FILE"
    warn "  .env.example not found — creating empty .env"
  fi
fi

# Step 2: Port
header "2. Application port"
PORT=$(ask PORT "Port" "8082")
set_env "PORT" "$PORT"

# Step 3: JWT Secret
header "3. JWT Secret"
CURRENT_JWT=$(grep '^JWT_SECRET=' "$ENV_FILE" 2>/dev/null | cut -d'=' -f2-)
if [ -n "$CURRENT_JWT" ] && [ "$CURRENT_JWT" != "generate-a-long-random-secret-key-at-least-32-chars" ]; then
  info "  JWT secret is already set."
else
  if command -v openssl &>/dev/null; then
    JWT_SECRET=$(openssl rand -base64 48)
  else
    JWT_SECRET=$(tr -dc 'A-Za-z0-9!@#$%^&*' < /dev/urandom | head -c 64 2>/dev/null || echo "change-me-$(date +%s)-$(head -c 32 /dev/urandom | base64)")
  fi
  set_env "JWT_SECRET" "$JWT_SECRET"
  ok "  Generated random JWT secret."
fi

# Step 4: Database
header "4. Poracle database"
info "  Your existing Poracle MySQL/MariaDB instance."
DB_HOST=$(ask DB_HOST "Host" "localhost")
DB_PORT=$(ask DB_PORT "Port" "3306")
DB_NAME=$(ask DB_NAME "Database name" "poracle")
DB_USER=$(ask DB_USER "User" "root")
DB_PASSWORD=$(ask DB_PASSWORD "Password" "")
set_env "DB_HOST" "$DB_HOST"
set_env "DB_PORT" "$DB_PORT"
set_env "DB_NAME" "$DB_NAME"
set_env "DB_USER" "$DB_USER"
set_env "DB_PASSWORD" "$DB_PASSWORD"

header "5. PoracleWeb.NET database"
info "  A separate database for PoracleWeb.NET's own data."
info "  Uses the same server by default — only change if different."
WEB_DB_HOST=$(ask WEB_DB_HOST "Host" "$DB_HOST")
WEB_DB_PORT=$(ask WEB_DB_PORT "Port" "$DB_PORT")
WEB_DB_NAME=$(ask WEB_DB_NAME "Database name" "poracle_web")
WEB_DB_USER=$(ask WEB_DB_USER "User" "$DB_USER")
WEB_DB_PASSWORD=$(ask WEB_DB_PASSWORD "Password" "$DB_PASSWORD")
set_env "WEB_DB_HOST" "$WEB_DB_HOST"
set_env "WEB_DB_PORT" "$WEB_DB_PORT"
set_env "WEB_DB_NAME" "$WEB_DB_NAME"
set_env "WEB_DB_USER" "$WEB_DB_USER"
set_env "WEB_DB_PASSWORD" "$WEB_DB_PASSWORD"

# Step 6: Discord
header "6. Discord OAuth2"
info "  Create an app at https://discord.com/developers/applications"
info "  Set redirect URI: http://your-host:${PORT}/api/auth/discord/callback"
DISCORD_CLIENT_ID=$(ask DISCORD_CLIENT_ID "Client ID" "")
DISCORD_CLIENT_SECRET=$(ask DISCORD_CLIENT_SECRET "Client Secret" "")
DISCORD_BOT_TOKEN=$(ask DISCORD_BOT_TOKEN "Bot Token" "")
set_env "DISCORD_CLIENT_ID" "$DISCORD_CLIENT_ID"
set_env "DISCORD_CLIENT_SECRET" "$DISCORD_CLIENT_SECRET"
set_env "DISCORD_BOT_TOKEN" "$DISCORD_BOT_TOKEN"

# Step 7: Poracle API
header "7. Poracle API"
info "  Your running PoracleNG instance."
PORACLE_API_ADDRESS=$(ask PORACLE_API_ADDRESS "API address" "http://localhost:3030")
PORACLE_API_SECRET=$(ask PORACLE_API_SECRET "API secret (must match PoracleNG server.apiSecret)" "")
PORACLE_ADMIN_IDS=$(ask PORACLE_ADMIN_IDS "Admin Discord user ID(s), comma-separated" "")
set_env "PORACLE_API_ADDRESS" "$PORACLE_API_ADDRESS"
set_env "PORACLE_API_SECRET" "$PORACLE_API_SECRET"
set_env "PORACLE_ADMIN_IDS" "$PORACLE_ADMIN_IDS"

# Step 8: Optionally create poracle_web database
header "8. Create poracle_web database"
echo -n "  Create the poracle_web database now? (requires mysql CLI) [y/N]: "
read -r CREATE_DB
if [[ "$CREATE_DB" =~ ^[Yy] ]]; then
  if command -v mysql &>/dev/null; then
    echo "  Creating database '${WEB_DB_NAME}' on ${WEB_DB_HOST}:${WEB_DB_PORT}..."
    mysql -h "$WEB_DB_HOST" -P "$WEB_DB_PORT" -u "$WEB_DB_USER" -p"$WEB_DB_PASSWORD" \
      -e "CREATE DATABASE IF NOT EXISTS \`${WEB_DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;" \
      2>/dev/null && ok "  Database created." || warn "  Could not create database. Create it manually."
  else
    warn "  mysql CLI not found. Create the database manually:"
    echo "  CREATE DATABASE ${WEB_DB_NAME} CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;"
  fi
else
  info "  Skipped. Create it manually before first run:"
  echo "  CREATE DATABASE ${WEB_DB_NAME} CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;"
fi

# Done
header "Setup complete!"
echo ""
echo "  Your .env file is ready at: $ENV_FILE"
echo ""
echo "  Next steps:"
echo "    Docker:     ./scripts/docker.sh build && ./scripts/docker.sh start"
echo "    Standalone: dotnet Pgan.PoracleWebNet.Api.dll"
echo "    Dev:        ./scripts/dev.sh start"
echo ""
echo "  Don't forget to set your Discord OAuth2 redirect URI to:"
echo "    http://your-host:${PORT}/api/auth/discord/callback"
echo ""
