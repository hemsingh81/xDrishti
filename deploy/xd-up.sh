#!/usr/bin/env bash
# Starts the xDrishti stack on Podman Desktop.
#   1. Ensures the Podman machine is running.
#   2. Reads secrets from the macOS Keychain (creates strong random ones on first run). The owner's sign-in items
#      (xdrishti/auth/owner-email|owner-name|owner-password) are never invented: they are asked for at a terminal
#      (hidden input for the password) or must exist already — see scripts/auth-secrets.sh.
#   3. Writes them as files for the containers (private folder, never in the repo or images).
#   4. Builds images and starts the stack, waits until healthy, prints the URLs.
# Usage: deploy/xd-up.sh [--no-dev] [--no-build] [--owner-reset]
#   --owner-reset  after the stack is up, replace the owner's password with the one now in the Keychain and
#                  sign the owner out everywhere (change it first with scripts/auth-secrets.sh).
set -euo pipefail

cd "$(dirname "$0")"
DEV=1; BUILD=1; OWNER_RESET=0
for arg in "$@"; do
  case "$arg" in
    --no-dev) DEV=0 ;;
    --no-build) BUILD=0 ;;
    --owner-reset) OWNER_RESET=1 ;;
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

# Strict reader for the owner's items: never creates or overwrites anything (a locked Keychain or a denied prompt must not
# be able to replace the real password with a random one).
strict_secret() { # strict_secret <keychain-service> → prints the value or exits
  local value
  value=$(security find-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$1" -w 2>/dev/null) && [ -n "$value" ] && { printf '%s' "$value"; return; }
  echo "Could not read Keychain item '$1' (locked keychain, denied access or empty). Nothing was changed." >&2
  echo "Unlock the Keychain / allow access, or re-enter it with scripts/auth-secrets.sh, then start again." >&2
  exit 1
}

# The owner's sign-in credentials come from the Keychain and are never generated. Missing items are asked for when
# there is a terminal (the password with hidden input, handled by `security` itself so it never appears in
# arguments, history or logs); without a terminal this fails with a pointer to scripts/auth-secrets.sh.
ensure_auth_item() { # ensure_auth_item <keychain-service> <label> <plain|hidden>
  local service="$1" label="$2" kind="$3" value
  security find-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$service" >/dev/null 2>&1 && return
  if [ ! -t 0 ]; then
    echo "Keychain item '$service' ($label) is missing and there is no terminal to ask for it." >&2
    echo "Run scripts/auth-secrets.sh in your own terminal first, then start the stack again." >&2
    exit 1
  fi
  say "First start: the owner's $label is needed (stored in the Keychain as '$service')."
  if [ "$kind" = hidden ]; then
    security add-generic-password -U -a "$KEYCHAIN_ACCOUNT" -s "$service" -w
  else
    read -r -p "  $label: " value
    [ -n "$value" ] || { echo "$label must not be empty." >&2; exit 1; }
    security add-generic-password -U -a "$KEYCHAIN_ACCOUNT" -s "$service" -w "$value" >/dev/null
  fi
}
ensure_auth_item "xdrishti/auth/owner-email" "sign-in e-mail" plain
ensure_auth_item "xdrishti/auth/owner-name" "display name" plain
ensure_auth_item "xdrishti/auth/owner-password" "sign-in password" hidden

# 3) Secret files for the containers.
umask 077
mkdir -p "$XD_SECRETS_DIR"
chmod 700 "$XD_SECRETS_DIR"
write_secret() { # write_secret <file> <keychain-service>
  printf '%s' "$(secret "$2")" > "$XD_SECRETS_DIR/$1"
  chmod 644 "$XD_SECRETS_DIR/$1" # readable by the container user; the folder itself is private (700)
}
write_strict_secret() { # same as write_secret but never generates or overwrites a Keychain item
  printf '%s' "$(strict_secret "$2")" > "$XD_SECRETS_DIR/$1"
  chmod 644 "$XD_SECRETS_DIR/$1"
}
# The owner files exist only while the stack is being brought up: they are emptied again on exit (compose still finds the files).
scrub_auth_secrets() { for f in auth_owner_email auth_owner_name auth_owner_password; do : > "$XD_SECRETS_DIR/$f" 2>/dev/null || true; done; }
trap scrub_auth_secrets EXIT
write_secret db_owner_password "xdrishti/db/owner"
write_secret db_app_password "xdrishti/db/app"
# Read by xd-migrate (provisions the owner when none exists) and xd-owner-reset only — not by the running API.
write_strict_secret auth_owner_email "xdrishti/auth/owner-email"
write_strict_secret auth_owner_name "xdrishti/auth/owner-name"
write_strict_secret auth_owner_password "xdrishti/auth/owner-password"

# 4) Build and start.
files=(-f compose.yaml); [ "$DEV" = 1 ] && files+=(-f compose.dev.yaml)
if [ "$BUILD" = 1 ]; then
  # One image at a time: the api and worker images share the .NET build stage, built once and reused from cache.
  say "Building images (first run takes a few minutes)…"
  podman compose "${files[@]}" --profile ops --parallel 1 build
fi
say "Starting containers and waiting until healthy…"
podman compose "${files[@]}" --profile ops up -d --wait --wait-timeout 300

if [ "$OWNER_RESET" = 1 ]; then
  say "Replacing the owner's password from the Keychain and signing out all sessions…"
  podman compose "${files[@]}" --profile tools run --rm xd-owner-reset
fi

cat <<INFO

  xDrishti is running
  ─────────────────────────────────────────────
  App            http://localhost:${XD_HTTP_PORT}   (sign in with the owner account from the Keychain)
  System status  http://localhost:${XD_HTTP_PORT}/system
  API docs       http://localhost:${XD_HTTP_PORT}/api/docs
  Delivery plan  http://localhost:${XD_HTTP_PORT}/prototype/delivery/
  Prototype      http://localhost:${XD_HTTP_PORT}/prototype/
  Design docs    http://localhost:${XD_HTTP_PORT}/docs/
  API status     http://localhost:${XD_HTTP_PORT}/api/system/status
  Logs (Seq)     http://localhost:${XD_SEQ_PORT}
INFO
[ "$DEV" = 1 ] && echo "  PostgreSQL     localhost:${XD_DB_PORT}  db=xdrishti  user=xd_app (password: Keychain 'xdrishti/db/app')"
echo
