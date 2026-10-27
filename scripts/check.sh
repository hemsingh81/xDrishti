#!/usr/bin/env bash
# All quality gates — must pass before every commit (Definition of Done, docs/engineering).
#   backend: format check, build (warnings = errors), unit + architecture + integration tests, proxy config
#   frontend: type check, lint, format check, tests, production build, bundle guard
set -euo pipefail
source "$(dirname "$0")/_env.sh"

# Run from backend/ so its global.json (SDK pin, Microsoft Testing Platform) applies.
cd "$ROOT/backend"
say "Backend: format"
dotnet format XDrishti.slnx --verify-no-changes
say "Backend: build"
dotnet build XDrishti.slnx -c Release --nologo -v q
say "Backend: tests (integration tests use TimescaleDB in Podman)"
"$ROOT/scripts/clean-test-containers.sh" || true
dotnet test --solution XDrishti.slnx -c Release --no-build
"$ROOT/scripts/clean-test-containers.sh" || true
"$ROOT/scripts/check-proxy.sh"

cd "$ROOT/frontend"
# Reinstall only when dependencies are missing or the lock file changed since the last install.
if [ ! -f node_modules/.package-lock.json ] || [ package-lock.json -nt node_modules/.package-lock.json ]; then
  say "Frontend: install"
  rm -rf node_modules && npm ci --no-audit --prefer-offline >/dev/null
fi
say "Frontend: typecheck, lint, format, tests"
npm run check
say "Frontend: build"
npm run build >/dev/null
say "Frontend: bundle guard (first-load JavaScript <= 200 KB gzip)"
# The measurement is recorded for the P8 phase check (scripts/p8-phase-check.sh); data/ is git-ignored.
XD_EVIDENCE_DIR="$ROOT/data/p8-evidence" npm run size --silent

# Record this green run so the Stop hook knows the current changes were verified.
"$ROOT/scripts/_fingerprint.sh" >"$(git -C "$ROOT" rev-parse --absolute-git-dir)/xd-check-pass"

say "All checks passed"
