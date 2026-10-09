#!/usr/bin/env bash
# Stops the xDrishti stack. Data volumes are kept unless --purge is given.
set -euo pipefail
cd "$(dirname "$0")"
export PODMAN_COMPOSE_WARNING_LOGS=false
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$HOME/Library/Application Support/xDrishti/secrets}"
if [ "${1:-}" = "--purge" ]; then
  read -r -p "Delete ALL xDrishti data (database, logs)? Type 'delete': " answer
  [ "$answer" = "delete" ] || { echo "Cancelled."; exit 1; }
  podman compose -f compose.yaml -f compose.dev.yaml --profile ops down --volumes
else
  podman compose -f compose.yaml -f compose.dev.yaml --profile ops down
fi
