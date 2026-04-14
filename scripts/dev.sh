#!/usr/bin/env bash
# PoracleWeb.NET — Development convenience commands
# Run from anywhere: ./scripts/dev.sh <command>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
API_DIR="$ROOT/Applications/Pgan.PoracleWebNet.Api"
APP_DIR="$ROOT/Applications/Pgan.PoracleWebNet.App/ClientApp"
DATA_DIR="$ROOT/Data/Pgan.PoracleWebNet.Data"

# Colors
if [ -t 1 ]; then
  BOLD='\033[1m' DIM='\033[2m' GREEN='\033[32m' RED='\033[31m' CYAN='\033[36m' RESET='\033[0m'
else
  BOLD='' DIM='' GREEN='' RED='' CYAN='' RESET=''
fi

usage() {
  echo -e "${BOLD}PoracleWeb.NET Dev${RESET} — development commands from the project root"
  echo ""
  echo "Usage: ./scripts/dev.sh <command>"
  echo ""
  echo "Commands:"
  echo "  install    Install frontend (npm) dependencies"
  echo "  api        Start the .NET API server"
  echo "  app        Start the Angular dev server"
  echo "  start      Start both API and Angular in parallel"
  echo "  test       Run all tests (backend + frontend)"
  echo "  lint       Run ESLint and Prettier checks"
  echo "  build      Production build (API + Angular)"
  echo "  db:create  Create the poracle_web database"
  echo "  db:migrate Add a new EF Core migration (usage: dev.sh db:migrate MigrationName)"
  echo ""
}

cmd_install() {
  echo -e "${CYAN}Installing frontend dependencies...${RESET}"
  cd "$APP_DIR" && npm install
}

load_env() {
  # Export .env from repo root so Program.cs (which reads its CWD) sees the values
  # regardless of which directory dotnet run is launched from. Parses line-by-line
  # to preserve values with spaces (unlike `source`, which executes them).
  [ -f "$ROOT/.env" ] || return 0
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"  # strip CRLF
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" != *=* ]] && continue
    local key="${line%%=*}"
    local value="${line#*=}"
    # trim leading/trailing whitespace from key
    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    [ -z "$key" ] && continue
    # strip matching surrounding single or double quotes from value
    if [[ "$value" =~ ^\"(.*)\"$ ]]; then value="${BASH_REMATCH[1]}"
    elif [[ "$value" =~ ^\'(.*)\'$ ]]; then value="${BASH_REMATCH[1]}"
    fi
    export "$key=$value"
  done < "$ROOT/.env"
}

cmd_api() {
  echo -e "${CYAN}Starting .NET API (http://localhost:5048)...${RESET}"
  load_env
  cd "$API_DIR" && dotnet run
}

cmd_app() {
  echo -e "${CYAN}Starting Angular dev server (http://localhost:4200)...${RESET}"
  cd "$APP_DIR" && npm start
}

cmd_start() {
  echo -e "${CYAN}Starting API + Angular dev server...${RESET}"
  echo -e "${DIM}  API:     http://localhost:5048${RESET}"
  echo -e "${DIM}  Angular: http://localhost:4200${RESET}"
  echo -e "${DIM}  Press Ctrl+C to stop both.${RESET}"
  echo ""

  load_env
  # Start both processes, prefix output with labels
  (cd "$API_DIR" && dotnet run 2>&1 | sed "s/^/[api] /") &
  API_PID=$!
  (cd "$APP_DIR" && npm start 2>&1 | sed "s/^/[app] /") &
  APP_PID=$!

  # Clean up both on exit
  cleanup() {
    echo ""
    echo -e "${DIM}Stopping...${RESET}"
    kill "$API_PID" "$APP_PID" 2>/dev/null || true
    wait "$API_PID" "$APP_PID" 2>/dev/null || true
  }
  trap cleanup INT TERM

  wait
}

cmd_test() {
  local exit_code=0

  echo -e "${CYAN}Running backend tests...${RESET}"
  (cd "$ROOT" && dotnet test --verbosity minimal) || exit_code=$?

  echo ""
  echo -e "${CYAN}Running frontend tests...${RESET}"
  (cd "$APP_DIR" && npm test -- --watchAll=false) || exit_code=$?

  if [ $exit_code -eq 0 ]; then
    echo -e "\n${GREEN}All tests passed.${RESET}"
  else
    echo -e "\n${RED}Some tests failed.${RESET}"
    exit $exit_code
  fi
}

cmd_lint() {
  echo -e "${CYAN}Running lint checks...${RESET}"
  cd "$APP_DIR"
  npm run lint
  npm run prettier-check
  echo -e "${GREEN}Lint checks passed.${RESET}"
}

cmd_build() {
  local publish_dir="$ROOT/publish"

  echo -e "${CYAN}Building .NET API...${RESET}"
  cd "$ROOT" && dotnet publish "$API_DIR/Pgan.PoracleWebNet.Api.csproj" -c Release -o "$publish_dir"

  echo -e "${CYAN}Building Angular app...${RESET}"
  cd "$APP_DIR" && npm run build

  echo -e "${CYAN}Copying frontend to publish/wwwroot...${RESET}"
  mkdir -p "$publish_dir/wwwroot"
  cp -r "$APP_DIR/dist/ClientApp/browser/"* "$publish_dir/wwwroot/"

  echo -e "${GREEN}Build complete: $publish_dir${RESET}"
}

cmd_db_create() {
  load_env

  local host="${WEB_DB_HOST:-${DB_HOST:-localhost}}"
  local port="${WEB_DB_PORT:-${DB_PORT:-3306}}"
  local user="${WEB_DB_USER:-${DB_USER:-root}}"
  local pass="${WEB_DB_PASSWORD:-${DB_PASSWORD:-}}"
  local name="${WEB_DB_NAME:-poracle_web}"

  echo -e "${CYAN}Creating database '${name}' on ${host}:${port}...${RESET}"

  if ! command -v mysql &>/dev/null; then
    echo -e "${RED}mysql CLI not found.${RESET} Create it manually:"
    echo "  mysql -h $host -P $port -u $user -p -e \"CREATE DATABASE IF NOT EXISTS \`${name}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;\""
    exit 1
  fi

  mysql -h "$host" -P "$port" -u "$user" -p"$pass" \
    -e "CREATE DATABASE IF NOT EXISTS \`${name}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;" \
    && echo -e "${GREEN}Database '${name}' created (or already exists).${RESET}" \
    || { echo -e "${RED}Failed to create database.${RESET}"; exit 1; }
}

cmd_db_migrate() {
  local migration_name="${2:-}"
  if [ -z "$migration_name" ]; then
    echo -e "${RED}Usage: ./scripts/dev.sh db:migrate MigrationName${RESET}"
    echo "  Example: ./scripts/dev.sh db:migrate AddUserPreferences"
    exit 1
  fi

  echo -e "${CYAN}Adding EF Core migration: ${migration_name}${RESET}"
  cd "$ROOT" && dotnet ef migrations add "$migration_name" \
    --context PoracleWebContext \
    --project "$DATA_DIR" \
    --startup-project "$API_DIR" \
    --output-dir Migrations/PoracleWeb

  echo -e "${GREEN}Migration '${migration_name}' created.${RESET}"
  echo -e "${DIM}Files: $DATA_DIR/Migrations/PoracleWeb/${RESET}"
  echo -e "${DIM}It will be applied automatically on next app startup.${RESET}"
}

# ─────────────────────────────────────────────────────────────────────────────

case "${1:-}" in
  install)    cmd_install ;;
  api)        cmd_api ;;
  app)        cmd_app ;;
  start)      cmd_start ;;
  test)       cmd_test ;;
  lint)       cmd_lint ;;
  build)      cmd_build ;;
  db:create)  cmd_db_create ;;
  db:migrate) cmd_db_migrate "$@" ;;
  *)          usage; exit 1 ;;
esac
