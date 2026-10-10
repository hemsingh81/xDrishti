#!/usr/bin/env bash
# Waits until an LLM server answers /health and prints how long it took (ms) — the "load time" for the benchmark.
#   scripts/llm-wait.sh http://127.0.0.1:8082 [timeout-seconds]
set -euo pipefail
url="${1:?url}"; timeout="${2:-300}"
python3 - "$url" "$timeout" <<'PY'
import sys, time, urllib.request
url, timeout = sys.argv[1], float(sys.argv[2])
start = time.monotonic()
while time.monotonic() - start < timeout:
    try:
        with urllib.request.urlopen(url + "/health", timeout=2) as r:
            if r.status == 200:
                print(int((time.monotonic() - start) * 1000)); sys.exit(0)
    except Exception:
        time.sleep(0.25)
sys.exit("server did not become healthy in time")
PY
