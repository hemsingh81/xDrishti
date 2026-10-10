# Container load times, Qwen3-30B-A3B (Q4_K_M, 18.6 GB), 32K context, GPU, 24 GB VM — 2026-10-10 01:29 +0530

Measured by scripts/llm-wait.sh (container start until /health answers). No other benchmark running; no model downloads running.

| Condition | Load ms |
|---|---|
| host folder (virtiofs), VM page cache dropped | 37019 |
| host folder (virtiofs), second load (cache warm) | 38079 |
| VM volume xd-models, VM page cache dropped | 29095 |
| VM volume xd-models, warm #1 | 104788 |
| VM volume xd-models, warm #2 | 99549 |
| gpt-oss-20b, host folder, cache warm | 40283 |
