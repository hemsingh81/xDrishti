#!/usr/bin/env bash
# All quality gates — must pass before every commit (Definition of Done, docs/engineering).
#   backend: format check, build (warnings = errors), unit + architecture + integration tests, proxy config
#   frontend: type check, lint, format check, tests, production build, bundle guard
#
# Opt-in fast mode (iteration only; the FULL run, no flag, stays required before merge):
#   scripts/check.sh --changed [base]   base defaults to main
#   format + build as usual; then only the backend test projects whose own folder or referenced src projects (transitively)
#   changed versus base (committed, staged, unstaged, untracked), always plus XDrishti.Architecture.Tests; a change to
#   backend files outside src/* and tests/* (props, global.json, slnx) selects every test project. Frontend checks run
#   only if frontend/ changed. Proxy config check always runs. A fast run does NOT record the green fingerprint.
set -euo pipefail
source "$(dirname "$0")/_env.sh"

CHANGED=0 BASE=main
if [ "${1:-}" = "--changed" ]; then
  CHANGED=1 BASE="${2:-main}"
elif [ $# -gt 0 ]; then
  echo "usage: scripts/check.sh [--changed [base]]" >&2; exit 2
fi

# Changed paths versus base: committed, staged, unstaged, untracked. Fails loudly on a bad base.
changed_files() {
  git -C "$ROOT" rev-parse --verify --quiet "$BASE^{commit}" >/dev/null || { echo "check.sh: unknown base '$BASE'" >&2; return 1; }
  git -C "$ROOT" merge-base "$BASE" HEAD >/dev/null || { echo "check.sh: no merge base with '$BASE'" >&2; return 1; }
  { git -C "$ROOT" diff --no-renames --name-only "$BASE"...HEAD; git -C "$ROOT" diff --no-renames --name-only HEAD; git -C "$ROOT" ls-files --others --exclude-standard; } | sort -u
}

# Reads changed paths on stdin; prints the test project names to run in --changed mode, one per line.
# Narrow only for backend/src/<Project>/ (plus its dependants) and backend/tests/<X>Tests/ (when no other project links it);
# anything else (Shared, SimOracle, props, global.json, slnx, docs, eval, scripts, deploy, ...) selects every test project.
# frontend/ is handled by the frontend step and selects nothing here.
select_test_projects() {
  local files all=0 f name
  files="$(cat)"
  # referenced project names of a src/tests project, one per line
  refs() { # $1 = project name; looks under src/ first, then tests/
    local csproj="$ROOT/backend/src/$1/$1.csproj"
    [ -f "$csproj" ] || csproj="$ROOT/backend/tests/$1/$1.csproj"
    grep -ho 'ProjectReference Include="[^"]*"' "$csproj" 2>/dev/null | sed -E 's#.*[\\/]([^\\/]+)\.csproj"#\1#'; }
  deps() { # $1 = project; prints it and all its transitive dependencies (src or test side)
    local seen=" " queue="$1" n r
    while [ -n "$queue" ]; do
      n="${queue%% *}"; queue="${queue#"$n"}"; queue="${queue# }"
      case "$seen" in *" $n "*) continue;; esac
      seen="$seen$n "
      for r in $(refs "$n"); do queue="$queue $r"; done
      queue="${queue# }"
    done
    echo "$seen"
  }
  while IFS= read -r f; do
    case "$f" in
      "") ;;
      frontend/*) ;;
      backend/src/*/*) ;;
      backend/tests/*Tests/*)
        name="${f#backend/tests/}"; name="${name%%/*}"
        # a folder another test project embeds or links (..\Name\) cannot be narrowed
        if grep -lE "\.\.[\\/]$name[\\/]" "$ROOT"/backend/tests/*/*.csproj 2>/dev/null | grep -qv "/$name/"; then all=1; fi ;;
      *) all=1 ;;
    esac
  done <<<"$files"
  for dir in "$ROOT"/backend/tests/*/; do
    name="$(basename "$dir")"
    case "$name" in *Tests) ;; *) continue;; esac
    [ -f "$dir$name.csproj" ] || continue
    local pick=$all d r
    [ "$name" = "XDrishti.Architecture.Tests" ] && pick=1
    if [ "$pick" -eq 0 ]; then
      if printf '%s\n' "$files" | grep -q "^backend/tests/$name/"; then pick=1; else
        for r in $(refs "$name"); do
          for d in $(deps "$r"); do
            if printf '%s\n' "$files" | grep -q "^backend/src/$d/"; then pick=1; fi
          done
        done
      fi
    fi
    [ "$pick" -eq 1 ] && echo "$name" || true
  done
}

[ "$CHANGED" -eq 0 ] || changed_files >/dev/null || exit 1

# Run from backend/ so its global.json (SDK pin, Microsoft Testing Platform) applies.
cd "$ROOT/backend"
say "Backend: format"
dotnet format XDrishti.slnx --verify-no-changes
say "Backend: build"
dotnet build XDrishti.slnx -c Release --nologo -v q
say "Backend: tests (integration tests use TimescaleDB in Podman)"
"$ROOT/scripts/clean-test-containers.sh" || true
rc=0
if [ "$CHANGED" -eq 1 ]; then
  FILES="$(changed_files)" || exit 1
  PROJECTS="$(select_test_projects <<<"$FILES")"
  say "Changed-only vs $BASE, test projects: $(echo $PROJECTS)"
  for p in $PROJECTS; do
    dotnet test --project "tests/$p/$p.csproj" -c Release --no-build || rc=$?
  done
else
  dotnet test --solution XDrishti.slnx -c Release --no-build || rc=$?
fi
"$ROOT/scripts/clean-test-containers.sh" || true
[ "$rc" -eq 0 ] || exit "$rc"
"$ROOT/scripts/check-proxy.sh"

if [ "$CHANGED" -eq 1 ] && ! printf '%s\n' "$FILES" | grep -q '^frontend/'; then
  say "Frontend unchanged vs $BASE: skipped (fast mode; run the full gate before merge)"
  say "Changed-only checks passed (NOT the full gate)"
  exit 0
fi

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

if [ "$CHANGED" -eq 1 ]; then
  say "Changed-only checks passed (NOT the full gate)"
  exit 0
fi

# Record this green run so the Stop hook knows the current changes were verified.
"$ROOT/scripts/_fingerprint.sh" >"$(git -C "$ROOT" rev-parse --absolute-git-dir)/xd-check-pass"

say "All checks passed"
