#!/usr/bin/env bash
# Benchmarks an OpenAI-compatible local LLM server (llama.cpp / RamaLama) — P0-15. Writes data/llm-bench/<label>.json and .md.
#   scripts/llm-bench.sh --url http://127.0.0.1:8080 --label qwen3-30b-a3b_container-gpu --smoke
# Options: --model NAME  --ctx 1024,8192,30000  --repeats 3  --gen 256  --embeddings-url URL  --load-ms N  --no-think  --out DIR
set -euo pipefail
source "$(dirname "$0")/_env.sh"
cd "$ROOT/backend"
exec dotnet run --project spikes/XDrishti.LlmBench -c Release -- "$@"
