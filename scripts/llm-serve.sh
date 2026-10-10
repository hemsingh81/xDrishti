#!/usr/bin/env bash
# Starts/stops a local LLM server for the P0-15 benchmark. Everything binds to 127.0.0.1 only.
#   scripts/llm-serve.sh native    <model> [ctx]   host llama.cpp with Metal GPU          → http://127.0.0.1:8081
#   scripts/llm-serve.sh cpu       <model> [ctx]   host llama.cpp, GPU layers 0 (baseline) → http://127.0.0.1:8083
#   scripts/llm-serve.sh container <model> [ctx]   pinned RamaLama image, GPU via libkrun  → http://127.0.0.1:8082
# The embedding model uses port 8084 in native/cpu mode (so it can run beside a chat model); container mode serves chat models only. XD_MODELS_VOLUME=1 reads from the Podman volume xd-models.
#   scripts/llm-serve.sh stop                      stop all of the above
# <model>: embed | small | dense | gptoss | main   (files from scripts/llm-models.sh); ctx defaults to 32768 (embed: 2048)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUPPORT="$HOME/Library/Application Support/xDrishti"
MODELS="$SUPPORT/models"
LLAMA="$SUPPORT/llm/llama.cpp-b11530"
RUN="$ROOT/data/llm-bench"
IMAGE="quay.io/ramalama/ramalama@sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e"   # = tag 0.25.0
export PATH="$HOME/.local/bin:$PATH"
mkdir -p "$RUN"

file_for() {
  case "$1" in
    embed) echo "nomic-embed-text-v1.5.Q8_0.gguf" ;;
    small) echo "Qwen3-8B-Q4_K_M.gguf" ;;
    dense) echo "Qwen3-14B-Q4_K_M.gguf" ;;
    gptoss) echo "gpt-oss-20b-MXFP4.gguf" ;;
    main) echo "Qwen3-30B-A3B-Q4_K_M.gguf" ;;
    *) echo "unknown model '$1'" >&2; exit 2 ;;
  esac
}

stop_all() {
  for pidfile in "$RUN"/server-*.pid; do
    [[ -f "$pidfile" ]] || continue
    pid="$(cat "$pidfile")"
    # Only signal the process if it really is our llama-server (a stale pid file must never hit an unrelated process).
    [[ "$(ps -p "$pid" -o comm= 2>/dev/null)" == *llama-server* ]] && kill "$pid" 2>/dev/null || true
    rm -f "$pidfile"
  done
  podman rm -f xd-llm-bench >/dev/null 2>&1 || true
}

kind="${1:-}"
[[ "$kind" == "stop" ]] && { stop_all; echo "stopped"; exit 0; }
model="${2:?model required}"
file="$(file_for "$model")"
ctx="${3:-32768}"
[[ "$model" == "embed" ]] && ctx=2048
path="$MODELS/$file"
[[ -f "$path" ]] || { echo "Model file missing: $path (run scripts/llm-models.sh $model)" >&2; exit 1; }
extra=()
[[ "$model" == "embed" ]] && extra+=(--embedding --pooling mean)

case "$kind" in
  native|cpu)
    port=8081; layers=999
    [[ "$kind" == "cpu" ]] && { port=8083; layers=0; }
    [[ "$model" == "embed" ]] && port=8084
    log="$RUN/server-$kind-$model.log"
    nohup "$LLAMA/llama-server" -m "$path" -c "$ctx" -ngl "$layers" -np 1 --host 127.0.0.1 --port "$port" --jinja --no-webui ${extra[@]+"${extra[@]}"} >"$log" 2>&1 &
    echo $! >"$RUN/server-$kind-$model.pid"
    echo "llama-server ($kind) pid $! on http://127.0.0.1:$port — log $log"
    ;;
  container)
    [[ "$model" == "embed" ]] && { echo "container mode serves chat models only; use: $0 cpu embed" >&2; exit 2; }
    port=8082
    # Our own `podman run`: RamaLama's `serve` publishes the port on 0.0.0.0; here it is bound to 127.0.0.1 explicitly.
    # The RamaLama image supplies llama.cpp with Mesa's Venus Vulkan driver (GPU through libkrun); /dev/dri is passed in.
    podman rm -f xd-llm-bench >/dev/null 2>&1 || true
    # XD_MODELS_VOLUME=1 reads the model from the Podman volume xd-models (VM disk) instead of the host folder (virtiofs).
    mount="$MODELS"; [[ "${XD_MODELS_VOLUME:-0}" == "1" ]] && mount="xd-models"
    podman run -d --name xd-llm-bench --device /dev/dri -p "127.0.0.1:$port:8080" -v "$mount:/mnt/models:ro" "$IMAGE" \
      llama-server --host 0.0.0.0 --port 8080 -m "/mnt/models/$file" -c "$ctx" -ngl 999 -np 1 --jinja --no-webui -lv 4 ${extra[@]+"${extra[@]}"} >/dev/null
    echo "container xd-llm-bench on http://127.0.0.1:$port (published on 127.0.0.1 only)"
    ;;
  *) echo "usage: $0 native|cpu|container <model> [ctx] | stop" >&2; exit 2 ;;
esac
