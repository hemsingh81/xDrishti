#!/usr/bin/env bash
# PostToolUse (Edit|Write|MultiEdit): keep the file formatted so scripts/check.sh never fails on style.
set -uo pipefail
root="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
file="$(jq -r '.tool_input.file_path // ""')"
[ -f "$file" ] || exit 0
source "$root/scripts/_env.sh"

case "$file" in
  "$root"/backend/*.cs)
    (cd "$root/backend" && dotnet format whitespace XDrishti.slnx --include "${file#"$root"/backend/}" >/dev/null 2>&1) || true ;;
  "$root"/frontend/src/*|"$root"/frontend/*.ts|"$root"/frontend/*.json|"$root"/frontend/*.js)
    (cd "$root/frontend" && [ -x node_modules/.bin/prettier ] && node_modules/.bin/prettier --write --log-level silent "$file") || true ;;
esac
exit 0
