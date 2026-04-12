#!/usr/bin/env bash
# PoracleWeb.NET — Docker convenience commands
# Run from anywhere: ./scripts/docker.sh <command>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
if [ -t 1 ]; then
  BOLD='\033[1m' DIM='\033[2m' GREEN='\033[32m' CYAN='\033[36m' RESET='\033[0m'
else
  BOLD='' DIM='' GREEN='' CYAN='' RESET=''
fi

usage() {
  echo -e "${BOLD}PoracleWeb.NET Docker${RESET} — Docker commands from the project root"
  echo ""
  echo "Usage: ./scripts/docker.sh <command>"
  echo ""
  echo "Commands:"
  echo "  build    Build the Docker image from source"
  echo "  start    Start the container (docker compose up -d)"
  echo "  stop     Stop the container (docker compose down)"
  echo "  logs     Tail container logs"
  echo "  update   Rebuild and recreate the container"
  echo "  clean    Rebuild from scratch (no cache)"
  echo ""
}

dc() {
  docker compose -f "$ROOT/docker-compose.yml" --env-file "$ROOT/.env" "$@"
}

cmd_build() {
  echo -e "${CYAN}Building Docker image...${RESET}"
  docker build -t poracleweb.net:latest "$ROOT"
  echo -e "${GREEN}Image built: poracleweb.net:latest${RESET}"
}

cmd_start() {
  echo -e "${CYAN}Starting container...${RESET}"
  dc up -d
  echo -e "${GREEN}Started. View logs: ./scripts/docker.sh logs${RESET}"
}

cmd_stop() {
  echo -e "${CYAN}Stopping container...${RESET}"
  dc down
  echo -e "${GREEN}Stopped.${RESET}"
}

cmd_logs() {
  dc logs -f
}

cmd_update() {
  cmd_build
  echo -e "${CYAN}Recreating container...${RESET}"
  dc up -d --force-recreate
  echo -e "${GREEN}Updated and running.${RESET}"
}

cmd_clean() {
  echo -e "${CYAN}Rebuilding from scratch (no cache)...${RESET}"
  docker build --no-cache -t poracleweb.net:latest "$ROOT"
  echo -e "${GREEN}Clean build complete: poracleweb.net:latest${RESET}"
}

# ─────────────────────────────────────────────────────────────────────────────

case "${1:-}" in
  build)  cmd_build ;;
  start)  cmd_start ;;
  stop)   cmd_stop ;;
  logs)   cmd_logs ;;
  update) cmd_update ;;
  clean)  cmd_clean ;;
  *)      usage; exit 1 ;;
esac
