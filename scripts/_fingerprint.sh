#!/usr/bin/env bash
# Prints a hash of uncommitted changes under the code folders, or "clean" when there are none.
# scripts/check.sh stores it after a green run; the Stop hook (.claude/hooks/require-check.sh) compares.
set -euo pipefail
cd "$(dirname "$0")/.."
paths=(backend frontend deploy scripts)
[ -z "$(git status --porcelain -- "${paths[@]}")" ] && { echo clean; exit 0; }
{
  git diff HEAD -- "${paths[@]}"
  git ls-files -o --exclude-standard -- "${paths[@]}" | while IFS= read -r f; do printf '%s\n' "$f"; shasum "$f"; done
} | shasum | cut -d' ' -f1
