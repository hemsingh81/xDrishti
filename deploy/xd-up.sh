#!/usr/bin/env bash
# Starts the xDrishti stack on Podman Desktop.
#   1. Ensures the Podman machine is running.
#   2. Reads secrets from the macOS Keychain (creates strong random ones on first run).
#   3. Writes them as files for the containers (private folder, never in the repo or images).
#   4. Builds images and starts the stack, waits until healthy, prints the URLs.
# Usage: deploy/xd-up.sh [--no-dev] [--no-build]
set -euo pipefail

cd "$(dirname "$0")"
DEV=1; BUILD=1
for arg in "$@"; do
  case "$arg" in
    --no-dev) DEV=0 ;;
    --no-build) BUILD=0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

export PODMAN_COMPOSE_WARNING_LOGS=false
KEYCHAIN_ACCOUNT="xdrishti"
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$HOME/Library/Application Support/xDrishti/secrets}"
export XD_HTTP_PORT="${XD_HTTP_PORT:-8080}" XD_SEQ_PORT="${XD_SEQ_PORT:-5341}" XD_DB_PORT="${XD_DB_PORT:-5433}"

say() { printf '\033[1;34m▸\033[0m %s\n' "$*"; }

# 1) Podman machine
command -v podman >/dev/null || { echo "Podman is not installed (Podman Desktop)." >&2; exit 1; }
if ! podman info >/dev/null 2>&1; then
  say "Starting Podman machine…"
  podman machine start >/dev/null
fi

# 2) Secrets: macOS Keychain is the source of truth.
secret() { # secret <keychain-service> → prints the value, creating it on first use
  local service="$1" value
  if value=$(security find-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$service" -w 2>/dev/null); then
    printf '%s' "$value"; return
  fi
  value=$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | cut -c1-32)
  security add-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$service" -w "$value" -U >/dev/null
  say "Created Keychain item '$service'" >&2
  printf '%s' "$value"
}

# 3) Secret files for the containers.
umask 077
mkdir -p "$XD_SECRETS_DIR"
chmod 700 "$XD_SECRETS_DIR"
write_secret() { # write_secret <file> <keychain-service>
  printf '%s' "$(secret "$2")" > "$XD_SECRETS_DIR/$1"
  chmod 644 "$XD_SECRETS_DIR/$1" # readable by the container user; the folder itself is private (700)
}
write_secret db_owner_password "xdrishti/db/owner"
write_secret db_app_password "xdrishti/db/app"

# 4) Build and start.
files=(-f compose.yaml); [ "$DEV" = 1 ] && files+=(-f compose.dev.yaml)
if [ "$BUILD" = 1 ]; then
  # One image at a time: the api and worker images share the .NET build stage, built once and reused from cache.
  say "Building images (first run takes a few minutes)…"
  podman compose "${files[@]}" --profile ops --parallel 1 build
fi
say "Starting containers and waiting until healthy…"
podman compose "${files[@]}" --profile ops up -d --wait --wait-timeout 300

cat <<INFO

  xDrishti is running
  ─────────────────────────────────────────────
  App            http://localhost:${XD_HTTP_PORT}
  System status  http://localhost:${XD_HTTP_PORT}/system
  API docs       http://localhost:${XD_HTTP_PORT}/api/docs
  API status     http://localhost:${XD_HTTP_PORT}/api/system/status
  Logs (Seq)     http://localhost:${XD_SEQ_PORT}
INFO
[ "$DEV" = 1 ] && echo "  PostgreSQL     localhost:${XD_DB_PORT}  db=xdrishti  user=xd_app (password: Keychain 'xdrishti/db/app')"
echo
