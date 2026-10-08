#!/usr/bin/env bash
# Serves the clickable prototype (http://localhost:8766) and the docs site (http://localhost:8765). Ctrl-C stops both.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 -m http.server 8766 --directory docs/prototype &
P1=$!
python3 -m http.server 8765 --directory docs/site &
P2=$!
trap 'kill $P1 $P2 2>/dev/null' EXIT
echo "Prototype: http://localhost:8766   Docs: http://localhost:8765"
wait
