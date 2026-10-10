# LLM benchmark — main30b_container-gpu_cpuload

Model `qwen3-30b-a3b` · 2026-10-10 00:19 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 8,192 | 8386 | 261.3 | 37.7 | 32.1 | 36.8 | 37.7 / 38.1 / 37.5 tok/s |
| briefing (~4K → 300 words) | 3840 | 310.4 | 43.3 | 12.4 | 23.2 | 43.9 / 42.9 / 43.3 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 37.7 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 12.4 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 23.2 s | pass |
| Load from warm disk | ≤ 60 s | 44.5 s | pass |
| Tool-call validity | ≥ 9 / 10 | not measured | — |
