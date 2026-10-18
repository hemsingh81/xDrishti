#!/usr/bin/env bash
# Runs the `simulate` verb on the host: a built-in strategy over a few instruments or a basket, from the stored 1-minute bars (P3-04b2, ADR 0041).
# Writes the files of the run under data/exports/sim/<run-id>/ and prints the banner and a summary. It writes nothing to the database.
#   scripts/simulate.sh --strategy ORB --symbols 'RELIANCE,TCS' --from 2025-01-01 --to 2025-12-31
#   scripts/simulate.sh --strategy ORB --basket Banks --from 2025-01-01 --to 2025-12-31 --param rr=2
#   scripts/simulate.sh --strategy ORB --symbols 'RELIANCE,TCS' --from 2025-01-01 --to 2025-12-31 --cards 5   # trade cards: default 200, 0 for none (P3-04c)
#   scripts/simulate.sh --list-strategies
# Exit codes: 0 done, 1 refused (nothing written), 2 wrong command line or configuration, 3 failure, 130 cancelled.
# Start the stack first with deploy/xd-up.sh and the developer database port (compose.dev.yaml, 127.0.0.1:5433). The one secret is the
# database password, read from the Keychain item xdrishti/db/app; no broker key and no owner secret is read. Quote a symbol list: --symbols 'M&M,RELIANCE'.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export DOTNET_ENVIRONMENT=Development
# The export root is configuration, never a flag.
export Export__Directory="$ROOT/data/exports"
if password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w 2>/dev/null)"; then
  export Database__Password="$password"
elif [[ "${1:-}" != "--list-strategies" ]]; then
  echo "simulate.sh: the Keychain has no item xdrishti/db/app (start the stack once with deploy/xd-up.sh)" >&2
  exit 2
fi
unset password
cd "$ROOT/backend/src/XDrishti.Api"
exec dotnet run -c Release --no-launch-profile -- simulate "$@"
