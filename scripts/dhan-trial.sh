#!/usr/bin/env bash
# Read-only Dhan Data API trial (P0-13). Writes data/trial/report.md (no secrets); raw responses are cached in data/trial/raw.
#   scripts/dhan-trial.sh run                 login if needed, check profile/data plan, test token renewal, fetch history, analyse
#   scripts/dhan-trial.sh run --try-totp      also test the PIN + TOTP route on a separate token (needs those two Keychain items)
#   scripts/dhan-trial.sh run --burst         also fire a small burst (8 calls, no retries) to see the throttle response; off by default
#   scripts/dhan-trial.sh renew               renew the stored token and log how much lifetime was left (run next morning)
#   scripts/dhan-trial.sh login               new browser-based login only
#   scripts/dhan-trial.sh analyze             re-analyse cached responses offline
# Credentials come from the Keychain (scripts/dhan-secrets.sh). Only market-data/auth endpoints are reachable.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
cd "$ROOT/backend"
exec dotnet run --project spikes/XDrishti.DhanTrial -c Release -- "$@"
