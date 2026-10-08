#!/usr/bin/env bash
# Rebuilds docs/site from docs/design/*.md
set -euo pipefail
cd "$(dirname "$0")/../tools/docs-site"
[ -d node_modules ] || npm install --no-audit --no-fund
npm run build
