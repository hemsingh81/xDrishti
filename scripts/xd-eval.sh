#!/usr/bin/env bash
# Runs the assistant eval (P9-09, ADR 0143): an owner act, on demand. Nothing in the stack starts it.
#   scripts/xd-eval.sh prepare                      creates and seeds the fixture database and starts the eval tool server xd-mcp-eval and the eval's API over the fixture xd-api-eval (scripts/eval-db.sh)
#   scripts/xd-eval.sh run [--set S]... [--repeats N]   runs the eval ONCE (the model must be running: scripts/xd-ai.sh start; the assistant's master switch must be ON in the app) and saves the report to data/eval/<run>/
#   scripts/xd-eval.sh grade --answers FILE --target hermes   grades another assistant's answers from a file (no model)
#   scripts/xd-eval.sh filings-extract --from YYYY-MM-DD --to YYYY-MM-DD [--limit N]   classifies stored announcements with the local model (P15-08: one request at a time, no tool; the model must be running and the assistant's master switch ON in the app; reads the filings store read-only, writes only filings.extractions)
#   scripts/xd-eval.sh announcements sample|import|show ...   the announcement set D (P15-07): the sample for labelling, the owner's labels, the state (no model; mounts data/eval, the repo's eval/ read-write for the manifest, and the real filings store read-only; reads the real filings tables read-only)
#   scripts/xd-eval.sh compare RUN [--baseline RUN] | accept RUN [--reason TEXT] | show RUN [--json|--md|--answers]
#   scripts/xd-eval.sh stop                         removes xd-mcp-eval and xd-api-eval
# The verb runs in a one-shot container on the stack's internal network (the model and the tool server are reachable nowhere else). It holds the database password of the runtime role, the model key and the eval tool token,
# never the owner's data; it reads only the fixture database through xd-mcp-eval. Exit codes of `xd eval`: 0 done (run: PASS), 1 refused, 2 wrong command line, 3 failure, 4 a run that is not PASS.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.local/bin:$PATH"
export PODMAN_COMPOSE_WARNING_LOGS=false
SUPPORT="$HOME/Library/Application Support/xDrishti"
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$SUPPORT/secrets}"
COMPOSE=(podman compose -f "$ROOT/deploy/compose.yaml" --profile eval)
EVAL_MCP="xdrishti-xd-mcp-eval-1"
EVAL_API="xdrishti-xd-api-eval-1"
MCP_IMAGE="localhost/xdrishti/mcp:${XD_VERSION:-0.1.0}"
verb="${1:-}"

health() { podman inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1" 2>/dev/null || echo none; }
# set D's labelled file (git-ignored, 0600) is mounted read-only so that `run` can read it when it exists
LABELLED_DIR="$ROOT/data/eval/sets"
xd_eval() {
  local mounts=()
  [[ -d "$LABELLED_DIR" ]] && mounts=(-v "$LABELLED_DIR:/data/eval-data/sets:ro" -e Eval__DataFolder=/data/eval-data)
  # sample.json (no text) gives the inclusion weights that set D recomputes from the labelled rows (P15-08)
  [[ -d "$LABELLED_DIR" && -d "$ROOT/data/eval/announcements" ]] && mounts+=(-v "$ROOT/data/eval/announcements:/data/eval-data/announcements:ro")
  "${COMPOSE[@]}" run --rm --no-deps ${mounts[@]+"${mounts[@]}"} xd-eval xd eval "$@"
}

case "$verb" in
  prepare)
    "$ROOT/scripts/eval-db.sh"
    podman image exists "$MCP_IMAGE" || { echo "Building the tool server image (once) ..."; "${COMPOSE[@]}" build xd-mcp-eval; }
    # The eval's API over the fixture (BK-12b, ADR 0267): the basket reports of the eval tool server come from it, never from the production API. About 300 to 500 MB while the eval runs.
    "${COMPOSE[@]}" up -d --no-deps --wait --wait-timeout 120 xd-api-eval xd-mcp-eval
    echo "ready: xd-api-eval, xd-mcp-eval (fixture database)"
    ;;
  stop)
    "${COMPOSE[@]}" rm -sf xd-mcp-eval xd-api-eval >/dev/null 2>&1 || podman rm -f "$EVAL_MCP" "$EVAL_API" >/dev/null 2>&1 || true
    echo "stopped: xd-mcp-eval and xd-api-eval removed"
    ;;
  run)
    [[ "$(health "$EVAL_MCP")" == "healthy" ]] || { echo "xd-mcp-eval is not running: scripts/xd-eval.sh prepare" >&2; exit 1; }
    [[ "$(health xdrishti-xd-llm-1)" == "healthy" ]] || { echo "The model server is not running: scripts/xd-ai.sh start" >&2; exit 1; }
    out="$(mktemp)"
    trap 'rm -f "$out"' EXIT
    rc=0
    xd_eval "$@" 2>&1 | tee "$out" || rc=$?
    run_id="$(sed -n 's/^run \([0-9a-f-]\{36\}\):.*/\1/p' "$out" | head -1)"
    if [[ -n "$run_id" ]]; then
      dir="$ROOT/data/eval/$run_id"
      mkdir -p "$dir"
      xd_eval show "$run_id" --json > "$dir/report.json"
      xd_eval show "$run_id" --md > "$dir/report.md"
      xd_eval show "$run_id" --answers > "$dir/answers.jsonl"
      echo "report: $dir (report.json, report.md, answers.jsonl)"
    fi
    exit "$rc"
    ;;
  grade)
    shift
    args=(); file=""
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == "--answers" && $# -ge 2 ]]; then file="$(cd "$(dirname -- "$2")" && pwd)/$(basename -- "$2")"; args+=(--answers /answers.jsonl); shift 2; else args+=("$1"); shift; fi
    done
    [[ -f "$file" ]] || { echo "usage: $0 grade --answers FILE --target hermes" >&2; exit 2; }
    "${COMPOSE[@]}" run --rm --no-deps -v "$file:/answers.jsonl:ro" xd-eval xd eval grade ${args[@]+"${args[@]}"}
    ;;
  announcements)
    # Writes data/eval (the sample, the labelled text; 0700/0600) and eval/sets (the committed manifest) and reads the filings store: mounted for this verb only, on other mount points than the compose file's read-only eval folder.
    shift
    mkdir -p -m 700 "$ROOT/data/eval/announcements" "$ROOT/data/eval/sets"
    args=(); file=""
    if [[ "${1:-}" == "import" && -n "${2:-}" ]]; then file="$(cd "$(dirname -- "$2")" && pwd)/$(basename -- "$2")"; args=(import /label-file.csv); shift 2; fi
    mounts=(-v "$ROOT/data/eval:/data/eval-data" -v "$ROOT/eval:/data/eval-rw" -v "$ROOT/data/filings/files:/data/filings/files:ro")
    [[ -n "$file" ]] && mounts+=(-v "$file:/label-file.csv:ro")
    "${COMPOSE[@]}" run --rm --no-deps ${mounts[@]+"${mounts[@]}"} -e Eval__Folder=/data/eval-rw -e Eval__DataFolder=/data/eval-data -e Filings__FilesPath=/data/filings/files xd-eval xd eval announcements ${args[@]+"${args[@]}"} "$@"
    ;;
  filings-extract)
    # The verb of P15-08: the model and the real filings store, nothing else. The text files are mounted read-only; the only write is the insert-only table filings.extractions.
    shift
    [[ "$(health xdrishti-xd-llm-1)" == "healthy" ]] || { echo "The model server is not running: scripts/xd-ai.sh start" >&2; exit 1; }
    "${COMPOSE[@]}" run --rm --no-deps -v "$ROOT/data/filings/files:/data/filings/files:ro" -e Filings__FilesPath=/data/filings/files xd-eval xd filings extract "$@"
    ;;
  compare|accept|show)
    xd_eval "$@"
    ;;
  *)
    echo "usage: $0 prepare | run [--set S]... [--repeats N] | grade --answers FILE --target T | compare RUN | accept RUN [--reason TEXT] | announcements sample|import|show | filings-extract --from D --to D [--limit N] | show RUN | stop" >&2
    exit 2
    ;;
esac
