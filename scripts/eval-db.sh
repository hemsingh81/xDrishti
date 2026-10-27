#!/usr/bin/env bash
# Creates and seeds the eval's fixture database `xdrishti_eval` (P9-09, ADR 0143) inside the stack's database server. A developer step; it never touches the database `xdrishti` (the owner's data).
#   scripts/eval-db.sh          create the database when missing, migrate it, seed the fixture (all idempotent: seeding twice changes nothing)
#   scripts/eval-db.sh reset    DROP and recreate the fixture database (it holds only synthetic data) and seed it again
# Needs the stack running (deploy/xd-up.sh) and the API image built. Every secret stays in the Keychain files of xd-up.sh; nothing is printed.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.local/bin:$PATH"
export PODMAN_COMPOSE_WARNING_LOGS=false
SUPPORT="$HOME/Library/Application Support/xDrishti"
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$SUPPORT/secrets}"
DB_CONTAINER="xdrishti-xd-db-1"
COMPOSE=(podman compose -f "$ROOT/deploy/compose.yaml" --profile eval)
NAME="xdrishti_eval"

podman info >/dev/null 2>&1 || { echo "The Podman machine is not running: start it (podman machine start) or run deploy/xd-up.sh." >&2; exit 1; }
[[ "$(podman inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$DB_CONTAINER" 2>/dev/null || echo none)" == "healthy" ]] || { echo "The database is not running: start the stack with deploy/xd-up.sh first." >&2; exit 1; }
for f in db_owner_password db_app_password db_mcp_read_password db_mcp_sql_password eval_mcp_token; do
  [[ -s "$XD_SECRETS_DIR/$f" ]] || { echo "The secret file $f is missing or empty: run deploy/xd-up.sh once." >&2; exit 1; }
done
podman image exists "localhost/xdrishti/api:${XD_VERSION:-0.1.0}" || { echo "The API image is missing: run deploy/xd-up.sh (it builds the images)." >&2; exit 1; }

psql_owner() { podman exec -i "$DB_CONTAINER" psql -v ON_ERROR_STOP=1 -U xd_owner "$@"; }

if [[ "${1:-}" == "reset" ]]; then
  echo "Dropping $NAME (synthetic data only) ..."
  psql_owner -d postgres -c "DROP DATABASE IF EXISTS $NAME WITH (FORCE)" >/dev/null
fi
if [[ "$(psql_owner -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$NAME'")" != "1" ]]; then
  echo "Creating $NAME ..."
  psql_owner -d postgres -c "CREATE DATABASE $NAME OWNER xd_owner" >/dev/null
  # What deploy/db/init/10-roles.sh gave the main database: connect, default privileges of the runtime role, a closed public schema.
  psql_owner -d "$NAME" >/dev/null <<SQL
GRANT CONNECT ON DATABASE $NAME TO xd_app;
ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app;
ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT USAGE, SELECT ON SEQUENCES TO xd_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
fi
echo "Migrating $NAME ..."
"${COMPOSE[@]}" run --rm --no-deps xd-migrate-eval
echo "Seeding the fixture ..."
"${COMPOSE[@]}" run --rm --no-deps xd-eval-seed
echo "ready: $NAME holds the eval fixture."
