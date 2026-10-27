#!/usr/bin/env bash
# Starts, stops and inspects the local model server `xd-llm` (P9-01, ADR 0005, 0136). On demand: nothing in the stack starts it, and it is never part of deploy/xd-up.sh.
#   scripts/xd-ai.sh start [main|gptoss|qwen-instruct]   default main; switches the model when another one runs; does nothing when it already runs
#   scripts/xd-ai.sh stop                                removes the container (frees the GPU memory)
#   scripts/xd-ai.sh status                              state, health, model file, host swap, VM free memory, which model files are present
#   scripts/xd-ai.sh logs                                follows the server log
# A missing model file is reported as `model_missing` (exit 2); this script never downloads anything - scripts/llm-models.sh (main, gptoss) and scripts/fetch-model.sh (qwen-instruct) do, and only when YOU run them.
# The API shows the state on GET /api/assistant/llm-status once the deployment flag XD_ASSISTANT_LLM=true is set for xd-api.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.local/bin:$PATH"
export PODMAN_COMPOSE_WARNING_LOGS=false
SUPPORT="$HOME/Library/Application Support/xDrishti"
export XD_SECRETS_DIR="${XD_SECRETS_DIR:-$SUPPORT/secrets}"
LIBRARY_DIR="${XD_MODELS_DIR:-$SUPPORT/models}"
FETCH_DIR="$ROOT/data/models-llm"
CONTAINER="xdrishti-xd-llm-1"
COMPOSE=(podman compose -f "$ROOT/deploy/compose.yaml" --profile ai)
CTX="${XD_LLM_CTX:-16384}"
ALIAS="xd-main"

# name -> model file | chat-template kwargs | how the file is obtained
describe() {
  case "$1" in
    main)          FILE="Qwen3-30B-A3B-Q4_K_M.gguf";               KWARGS='{"enable_thinking":false}'; GET="scripts/llm-models.sh main" ;;
    gptoss)        FILE="gpt-oss-20b-MXFP4.gguf";                  KWARGS='{}';                        GET="scripts/llm-models.sh gptoss" ;;   # a reasoning model: effort left at its default
    qwen-instruct) FILE="Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf"; KWARGS='{}';                        GET="scripts/fetch-model.sh" ;;          # non-thinking by design
    *) echo "unknown model '$1' (main, gptoss, qwen-instruct)" >&2; exit 2 ;;
  esac
}
folder_of() { # folder_of <file> -> the folder holding it, or nothing
  local dir
  for dir in "$LIBRARY_DIR" "$FETCH_DIR"; do [[ -f "$dir/$1" ]] && { printf '%s' "$dir"; return; }; done
  return 0
}
running() { [[ "$(podman inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null || true)" == "true" ]]; }
health() { podman inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$CONTAINER" 2>/dev/null || echo none; }
served_file() { podman inspect -f '{{range .Config.Cmd}}{{.}} {{end}}' "$CONTAINER" 2>/dev/null | tr ' ' '\n' | grep -m1 '/mnt/models/' | sed 's#.*/##' || true; }

cmd="${1:-}"; shift || true
case "$cmd" in
  start)
    describe "${1:-main}"
    dir="$(folder_of "$FILE")"
    if [[ -z "$dir" ]]; then
      echo "model_missing: $FILE is not in '$LIBRARY_DIR' or '$FETCH_DIR'. Get it with: $GET" >&2
      exit 2
    fi
    [[ -s "$XD_SECRETS_DIR/llm_api_key" ]] || { echo "The API key file is missing: run deploy/xd-up.sh once (it creates the Keychain item xdrishti/llm/api-key)." >&2; exit 1; }
    podman info >/dev/null 2>&1 || { echo "The Podman machine is not running: start it (podman machine start) or run deploy/xd-up.sh." >&2; exit 1; }
    if running && [[ "$(served_file)" == "$FILE" && "$(health)" == "healthy" ]]; then
      echo "ready: $ALIAS, context $CTX, 0 s (already running $FILE)"; exit 0
    fi
    running && { echo "Stopping the running model server to switch to $FILE ..."; "${COMPOSE[@]}" rm -sf xd-llm >/dev/null 2>&1 || podman rm -f "$CONTAINER" >/dev/null; }
    export XD_MODELS_DIR="$dir" XD_LLM_MODEL_FILE="$FILE" XD_LLM_ALIAS="$ALIAS" XD_LLM_CTX="$CTX" XD_LLM_KWARGS="$KWARGS"
    begin=$SECONDS
    echo "Starting $FILE (loading takes 20 to 110 s; the Mac may be swapping) ..."
    if ! "${COMPOSE[@]}" up -d --wait --wait-timeout 330 xd-llm; then
      echo "The model server did not become healthy within 330 s. Last log lines:" >&2
      podman logs --tail 15 "$CONTAINER" >&2 || true
      exit 1
    fi
    # The key must really be required: /v1/models without it has to be refused (inside the container; nothing is published).
    code="$(podman exec "$CONTAINER" curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/v1/models || true)"
    if [[ "$code" != "401" ]]; then
      echo "SAFETY: the server answered $code (not 401) to a request without the API key. Stopping it." >&2
      "${COMPOSE[@]}" rm -sf xd-llm >/dev/null 2>&1 || podman rm -f "$CONTAINER" >/dev/null
      exit 1
    fi
    echo "ready: $ALIAS, context $CTX, $((SECONDS - begin)) s"
    ;;
  stop)
    if podman container exists "$CONTAINER" 2>/dev/null; then
      "${COMPOSE[@]}" rm -sf xd-llm >/dev/null 2>&1 || podman rm -f "$CONTAINER" >/dev/null
      echo "stopped: xd-llm removed"
    else
      echo "stopped: xd-llm was not running"
    fi
    ;;
  status)
    if podman container exists "$CONTAINER" 2>/dev/null; then
      echo "container: $(podman inspect -f '{{.State.Status}}' "$CONTAINER"), health: $(health), model: $(served_file), alias: $ALIAS, context: $CTX"
    else
      echo "container: not running"
    fi
    for name in main gptoss qwen-instruct; do
      describe "$name"
      if [[ -n "$(folder_of "$FILE")" ]]; then echo "model $name: present ($FILE)"; else echo "model $name: model_missing ($FILE; get it with: $GET)"; fi
    done
    echo "host swap: $(sysctl -n vm.swapusage)"
    echo "VM memory: $(podman machine ssh -- free -m 2>/dev/null | awk '/^Mem:/ {printf "total %s MB, used %s MB, available %s MB", $2, $3, $7}' || true)"
    ;;
  logs) exec podman logs -f "$CONTAINER" ;;
  *) echo "usage: $0 start [main|gptoss|qwen-instruct] | stop | status | logs" >&2; exit 2 ;;
esac
