#!/usr/bin/env bash
# Runs the `verify-pack` verb on the host: draws the verification pack for the owner's hand check of the simulator (P3-09, ADR 0046).
# Runs each named strategy with its registered defaults over the stored 1-minute bars, draws about 20 trades at random (balanced over strategy, instrument and year) plus one trade of each
# edge category that occurs, and writes the pack under data/exports/verify/<pack-id>/ (cards, index.html, check-log.csv, engine-values.csv, README.txt, pack.json, manifest.json). It writes nothing to the database.
#   scripts/verify-pack.sh --strategies ORB --basket Lab --from 2024-04-01 --to 2026-04-30 --seed p3-check-1 --gross
#   scripts/verify-pack.sh --check data/exports/verify/<pack-id>/check-log.csv     # compare the filled log with the engine's values (reads files only)
# The SEED IS YOURS: write it down before the first draw (letters, digits, '.', '_' and '-', at most 40); it has no default. Use the next name (p3-check-2, ...) after any fix.
# The cost choice is required: --gross (no charges, labelled 'costs: none') or --costs <sheet-id> [--capital N] [--profile standard|conservative] [--max-cost-r X].
# Exit codes: 0 done (check: complete), 1 refused or incomplete (nothing written), 2 wrong command line, configuration or unreadable input, 3 failure, 130 cancelled.
# Start the stack first with deploy/xd-up.sh and the developer database port (compose.dev.yaml, 127.0.0.1:5433). The one secret is the database password, read from the Keychain item
# xdrishti/db/app; no broker key and no owner secret is read. The check mode reads no database and needs no password. Quote a symbol list: --symbols 'M&M,RELIANCE'.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export DOTNET_ENVIRONMENT=Development
# The export root is configuration, never a flag.
export Export__Directory="$ROOT/data/exports"
if password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w 2>/dev/null)"; then
  export Database__Password="$password"
elif [[ "${1:-}" != "--check" ]]; then
  echo "verify-pack.sh: the Keychain has no item xdrishti/db/app (start the stack once with deploy/xd-up.sh)" >&2
  exit 2
fi
unset password
# The script changes directory below, so a relative path of --check is made absolute against where the owner stands.
caller="$PWD"
args=()
while (($#)); do
  case "$1" in
    --check)
      args+=("$1")
      shift
      if (($#)); then
        [[ "$1" = /* ]] && args+=("$1") || args+=("$caller/$1")
        shift
      fi
      ;;
    --check=*)
      path="${1#--check=}"
      [[ "$path" = /* ]] && args+=("--check=$path") || args+=("--check=$caller/$path")
      shift
      ;;
    *)
      args+=("$1")
      shift
      ;;
  esac
done
cd "$ROOT/backend/src/XDrishti.Api"
exec dotnet run -c Release --no-launch-profile -- verify-pack ${args[@]+"${args[@]}"}
