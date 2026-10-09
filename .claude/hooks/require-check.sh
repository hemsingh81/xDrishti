#!/usr/bin/env bash
# Stop: if code changed since the last green scripts/check.sh, ask Claude to run it before finishing.
# Gives one nudge per turn (stop_hook_active) so it can never loop.
set -uo pipefail
root="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
input="$(cat)"
[ "$(jq -r '.stop_hook_active // false' <<<"$input")" = "true" ] && exit 0

cd "$root" || exit 0
current="$(scripts/_fingerprint.sh)"
[ "$current" = "clean" ] && exit 0
stamp="$(git rev-parse --absolute-git-dir)/xd-check-pass"
[ -f "$stamp" ] && [ "$(cat "$stamp")" = "$current" ] && exit 0

echo "Code changed since the last green scripts/check.sh. Run scripts/check.sh and fix any failure" >&2
echo "(or tell the user why it could not be run) before finishing." >&2
exit 2
