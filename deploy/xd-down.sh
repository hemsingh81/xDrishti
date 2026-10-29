#!/usr/bin/env bash
# Stops the xDrishti stack. Data volumes are kept unless --purge is given.
set -euo pipefail
cd "$(dirname "$0")"
export PODMAN_COMPOSE_WARNING_LOGS=false
# The Hermes PoC (P9-10) is never stopped from here (and never started by the stack): only point at its own script.
if podman container exists xdrishti-xd-hermes-1 2>/dev/null; then echo "Note: the Hermes PoC container is still up; stop it with scripts/hermes-poc.sh down" >&2; fi
export XD_MODELS_DIR="${XD_MODELS_DIR:-$HOME/Library/Application Support/xDrishti/models}"
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$HOME/Library/Application Support/xDrishti/secrets}"
if [ "${1:-}" = "--purge" ]; then
  read -r -p "Delete ALL xDrishti data (database, logs)? Type 'delete': " answer
  [ "$answer" = "delete" ] || { echo "Cancelled."; exit 1; }
  podman compose -f compose.yaml -f compose.dev.yaml --profile ops --profile ai --profile live down --volumes
else
  podman compose -f compose.yaml -f compose.dev.yaml --profile ops --profile ai --profile live down
fi
