#!/usr/bin/env bash
# Runs the `context` verb on the host: write and show the market context (NIFTY 50 / INDIA VIX daily figures, P3-01d, ADR 0033).
#   scripts/context.sh refresh    # write the days that became final since the last run (the data job does this after every update; idempotent, insert-only)
#   scripts/context.sh coverage   # print the stored days, the first readable date of index_trend / regime / vix_pct, the skipped days and any missing prerequisite (writes nothing)
# Exit codes: 0 done (a missing prerequisite is printed with its fix and is still 0), 1 refused, 2 wrong command line or configuration, 3 failure, 130 cancelled.
# Start the stack first with deploy/xd-up.sh and the developer database port (compose.dev.yaml, 127.0.0.1:5433). The one secret is the database password,
# read from the Keychain item xdrishti/db/app; no broker key and no owner secret is read.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export DOTNET_ENVIRONMENT=Development
if password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w 2>/dev/null)"; then
  export Database__Password="$password"
else
  echo "context.sh: the Keychain has no item xdrishti/db/app (start the stack once with deploy/xd-up.sh)" >&2
  exit 2
fi
unset password
cd "$ROOT/backend/src/XDrishti.Api"
exec dotnet run -c Release --no-launch-profile -- context "$@"
