#!/usr/bin/env bash
# Runs the assistant eval (P9-09, ADR 0143): an owner act, on demand. Nothing in the stack starts it.
#   scripts/xd-eval.sh prepare                      creates and seeds the fixture database and starts the eval tool server xd-mcp-eval (scripts/eval-db.sh)
#   scripts/xd-eval.sh run [--set S]... [--repeats N]   runs the eval ONCE (the model must be running: scripts/xd-ai.sh start; the assistant's master switch must be ON in the app) and saves the report to data/eval/<run>/
#   scripts/xd-eval.sh grade --answers FILE --target hermes   grades another assistant's answers from a file (no model)
#   scripts/xd-eval.sh compare RUN [--baseline RUN] | accept RUN [--reason TEXT] | show RUN [--json|--md|--answers]
#   scripts/xd-eval.sh stop                         removes xd-mcp-eval
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
MCP_IMAGE="localhost/xdrishti/mcp:${XD_VERSION:-0.1.0}"
verb="${1:-}"

health() { podman inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1" 2>/dev/null || echo none; }
xd_eval() { "${COMPOSE[@]}" run --rm --no-deps xd-eval xd eval "$@"; }

case "$verb" in
  prepare)
    "$ROOT/scripts/eval-db.sh"
    podman image exists "$MCP_IMAGE" || { echo "Building the tool server image (once) ..."; "${COMPOSE[@]}" build xd-mcp-eval; }
    "${COMPOSE[@]}" up -d --no-deps --wait --wait-timeout 120 xd-mcp-eval
    echo "ready: xd-mcp-eval (fixture database)"
    ;;
  stop)
    "${COMPOSE[@]}" rm -sf xd-mcp-eval >/dev/null 2>&1 || podman rm -f "$EVAL_MCP" >/dev/null 2>&1 || true
    echo "stopped: xd-mcp-eval removed"
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
    "${COMPOSE[@]}" run --rm --no-deps -v "$file:/answers.jsonl:ro" xd-eval xd eval grade "${args[@]}"
    ;;
  compare|accept|show)
    xd_eval "$@"
    ;;
  *)
    echo "usage: $0 prepare | run [--set S]... [--repeats N] | grade --answers FILE --target T | compare RUN | accept RUN [--reason TEXT] | show RUN | stop" >&2
    exit 2
    ;;
esac
