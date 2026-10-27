#!/usr/bin/env bash
# Starts, stops and inspects the assistant's two optional services: the local model server `xd-llm` (P9-01, ADR 0005, 0136) and the read-only tool server `xd-mcp` (P9-02, ADR 0137). On demand: nothing in the stack starts them, and neither is part of deploy/xd-up.sh.
#   scripts/xd-ai.sh start [main|gptoss|qwen-instruct] [--tools-only]   starts xd-mcp and xd-llm (default model main; switches the model when another one runs; does nothing when it already runs);
#                                                                       --tools-only starts xd-mcp alone (no model file needed)
#   scripts/xd-ai.sh stop                                               removes both containers (frees the GPU memory)
#   scripts/xd-ai.sh status                                             both containers (state, health), the model file, host swap, VM free memory, which model files are present
#   scripts/xd-ai.sh logs [llm|mcp]                                     follows a server log (default llm)
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
MCP_CONTAINER="xdrishti-xd-mcp-1"
MCP_IMAGE="localhost/xdrishti/mcp:${XD_VERSION:-0.1.0}"
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
container_health() { podman inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1" 2>/dev/null || echo none; }
mcp_running() { [[ "$(podman inspect -f '{{.State.Running}}' "$MCP_CONTAINER" 2>/dev/null || true)" == "true" ]]; }

# Starts the tool server alone. It needs the stack's database (xd-db healthy and migrated, which gave the two read-only roles their login) and three secret files written by deploy/xd-up.sh. --no-deps: compose must not start the one-shot xd-migrate again.
start_tools() {
  local f
  for f in db_mcp_read_password db_mcp_sql_password mcp_token; do
    [[ -s "$XD_SECRETS_DIR/$f" ]] || { echo "The secret file $f is missing or empty: run deploy/xd-up.sh once (it creates the Keychain items and sets the database logins)." >&2; return 1; }
  done
  podman info >/dev/null 2>&1 || { echo "The Podman machine is not running: start it (podman machine start) or run deploy/xd-up.sh." >&2; return 1; }
  [[ "$(container_health xdrishti-xd-db-1)" == "healthy" ]] || { echo "The database is not running: start the stack with deploy/xd-up.sh first." >&2; return 1; }
  if mcp_running && [[ "$(container_health "$MCP_CONTAINER")" == "healthy" ]]; then echo "ready: xd-mcp (already running)"; return 0; fi
  podman image exists "$MCP_IMAGE" || { echo "Building the tool server image (once) ..."; "${COMPOSE[@]}" build xd-mcp; }
  if ! "${COMPOSE[@]}" up -d --no-deps --wait --wait-timeout 120 xd-mcp; then
    echo "The tool server did not become healthy within 120 s. Last log lines:" >&2
    podman logs --tail 15 "$MCP_CONTAINER" >&2 || true
    echo "If it says the login failed, the read-only roles have no password yet: run deploy/xd-up.sh (migrate sets them)." >&2
    return 1
  fi
  echo "ready: xd-mcp"
}
served_file() { podman inspect -f '{{range .Config.Cmd}}{{.}} {{end}}' "$CONTAINER" 2>/dev/null | tr ' ' '\n' | grep -m1 '/mnt/models/' | sed 's#.*/##' || true; }

cmd="${1:-}"; shift || true
TOOLS_ONLY=0; ARGS=()
for a in "$@"; do if [[ "$a" == "--tools-only" ]]; then TOOLS_ONLY=1; else ARGS+=("$a"); fi; done
set -- "${ARGS[@]+"${ARGS[@]}"}"
case "$cmd" in
  start)
    if [[ "$TOOLS_ONLY" == 1 ]]; then start_tools; exit $?; fi
    describe "${1:-main}"
    dir="$(folder_of "$FILE")"
    if [[ -z "$dir" ]]; then
      echo "model_missing: $FILE is not in '$LIBRARY_DIR' or '$FETCH_DIR'. Get it with: $GET" >&2
      exit 2
    fi
    [[ -s "$XD_SECRETS_DIR/llm_api_key" ]] || { echo "The API key file is missing: run deploy/xd-up.sh once (it creates the Keychain item xdrishti/llm/api-key)." >&2; exit 1; }
    podman info >/dev/null 2>&1 || { echo "The Podman machine is not running: start it (podman machine start) or run deploy/xd-up.sh." >&2; exit 1; }
    start_tools || exit 1
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
    if podman container exists "$MCP_CONTAINER" 2>/dev/null; then
      "${COMPOSE[@]}" rm -sf xd-mcp >/dev/null 2>&1 || podman rm -f "$MCP_CONTAINER" >/dev/null
      echo "stopped: xd-mcp removed"
    else
      echo "stopped: xd-mcp was not running"
    fi
    ;;
  status)
    if podman container exists "$CONTAINER" 2>/dev/null; then
      echo "container: $(podman inspect -f '{{.State.Status}}' "$CONTAINER"), health: $(health), model: $(served_file), alias: $ALIAS, context: $CTX"
    else
      echo "container: not running"
    fi
    if podman container exists "$MCP_CONTAINER" 2>/dev/null; then
      echo "tools: $(podman inspect -f '{{.State.Status}}' "$MCP_CONTAINER"), health: $(container_health "$MCP_CONTAINER")"
    else
      echo "tools: not running (start with: scripts/xd-ai.sh start --tools-only)"
    fi
    for name in main gptoss qwen-instruct; do
      describe "$name"
      if [[ -n "$(folder_of "$FILE")" ]]; then echo "model $name: present ($FILE)"; else echo "model $name: model_missing ($FILE; get it with: $GET)"; fi
    done
    echo "host swap: $(sysctl -n vm.swapusage)"
    echo "VM memory: $(podman machine ssh -- free -m 2>/dev/null | awk '/^Mem:/ {printf "total %s MB, used %s MB, available %s MB", $2, $3, $7}' || true)"
    ;;
  logs)
    case "${1:-llm}" in
      llm) exec podman logs -f "$CONTAINER" ;;
      mcp) exec podman logs -f "$MCP_CONTAINER" ;;
      *) echo "usage: $0 logs [llm|mcp]" >&2; exit 2 ;;
    esac ;;
  *) echo "usage: $0 start [main|gptoss|qwen-instruct] [--tools-only] | stop | status | logs [llm|mcp]" >&2; exit 2 ;;
esac
