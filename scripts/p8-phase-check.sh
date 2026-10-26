#!/usr/bin/env bash
# The P8 phase check, local summary (spec P8-17, ADR 0126). Read only: it writes nothing but the interaction result under data/p8-evidence/.
#   1. the server check (xd reports phase-check) on the stored runs
#   2. the interaction-budget test (vitest, src/features/reports/phase) recorded for this commit
#   3. the summary: server criteria + bundle.json + browser-checklist.jsonl + interaction.json + stopwatch.json + the OpenAPI contract
# The owner's stopwatch is the owner's act: node frontend/scripts/p8-evidence.mjs stopwatch --seconds .. --interactions .. (see the ADR). Never invented here.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export XD_EVIDENCE_DIR="${XD_EVIDENCE_DIR:-$ROOT/data/p8-evidence}"
mkdir -p "$XD_EVIDENCE_DIR"

server_text="$XD_EVIDENCE_DIR/server-check.txt"
if "$ROOT/scripts/xd.sh" reports phase-check >"$server_text"; then :; else
  echo "p8-phase-check: the server check did not run (stack up? deploy/xd-up.sh); the criteria it feeds read NOT EVALUABLE" >&2
  : >"$server_text"
fi

cd "$ROOT/frontend"
vitest_json="$XD_EVIDENCE_DIR/vitest-phase.json"
# A stale passing file must never be stamped current: delete it first, and the recorder refuses a run of fewer than 3 tests.
rm -f "$vitest_json" "$XD_EVIDENCE_DIR/interaction.json"
npx vitest run src/features/reports/phase --reporter=json --outputFile="$vitest_json" >/dev/null 2>&1 || true
if [ -s "$vitest_json" ]; then node scripts/p8-evidence.mjs interaction --from "$vitest_json"; fi

node scripts/p8-evidence.mjs summary --server-text "$server_text"
