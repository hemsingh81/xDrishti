#!/usr/bin/env bash
# Runs the `cost-sheet` verb on the host: store, show and check the owner's broker charge sheet (P3-03f, ADR 0044).
#   scripts/cost-sheet.sh load sheet.json            # a new id, or the same rates with another label/source/checked note
#   scripts/cost-sheet.sh load sheet.json --replace  # overwrite other rates under an existing id (a new set of rates is normally a new id)
#   scripts/cost-sheet.sh show dhan-2026-10
#   scripts/cost-sheet.sh check dhan-2026-10 ~/reports/fy2425.csv ~/reports/fy2526.csv   # compare with your ORIGINAL global transaction report files; writes nothing
#   scripts/cost-sheet.sh derive ~/reports/fy2425.csv ~/reports/fy2526.csv [--out PATH] [--split 0.7]   # CS-01: propose a sheet from your ORIGINAL report files; writes data/cost-sheets/derived-<date>.json (0600) and its .residuals.csv, nothing else, no database row
# A file argument may be relative to where you stand: it is made absolute before the script changes directory.
# Exit codes: 0 done (a check with rows beyond the tolerance is still 0), 1 refused (nothing written), 2 wrong command line or configuration, 3 failure or a concurrent write, 130 cancelled.
# Start the stack first with deploy/xd-up.sh and the developer database port (compose.dev.yaml, 127.0.0.1:5433). The one secret is the database password,
# read from the Keychain item xdrishti/db/app; no broker key and no owner secret is read. The sheet is not a secret; the report files hold personal lines and are only read here.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export DOTNET_ENVIRONMENT=Development
if password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w 2>/dev/null)"; then
  export Database__Password="$password"
else
  echo "cost-sheet.sh: the Keychain has no item xdrishti/db/app (start the stack once with deploy/xd-up.sh)" >&2
  exit 2
fi
unset password
# every argument that is an existing file becomes an absolute path (the script changes directory below)
args=()
prev=""
for arg in "$@"; do
  if [[ "$prev" == "--out" && "$arg" != /* ]]; then
    args+=("$PWD/$arg")   # a relative --out is relative to where you stand, not to the project the script changes into
  elif [[ -f "$arg" ]]; then
    args+=("$(cd "$(dirname "$arg")" && pwd)/$(basename "$arg")")
  else
    args+=("$arg")
  fi
  prev="$arg"
done
# derive writes under the repository's data/ unless --out says otherwise (the script changes directory below)
if [[ "${args[0]:-}" == "derive" ]]; then
  has_out=0
  for arg in "${args[@]}"; do [[ "$arg" == "--out" ]] && has_out=1; done
  if [[ $has_out -eq 0 ]]; then
    args+=(--out "$ROOT/data/cost-sheets/derived-$(date +%F).json")
  fi
fi
cd "$ROOT/backend/src/XDrishti.Api"
exec dotnet run -c Release --no-launch-profile -- cost-sheet "${args[@]+"${args[@]}"}"
