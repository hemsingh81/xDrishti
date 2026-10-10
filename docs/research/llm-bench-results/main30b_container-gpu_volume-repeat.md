# LLM benchmark — main30b_container-gpu_volume-repeat

Model `qwen3-30b-a3b` · 2026-10-10 01:37 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 389.0 | 56.1 | 2.7 | 6.2 | 56.1 / 56.3 / 56.0 tok/s |
| 8,192 | 8386 | 328.6 | 42.5 | 25.6 | 29.8 | 42.5 / 42.4 / 42.6 tok/s |
| briefing (~4K → 300 words) | 3840 | 397.2 | 50.0 | 9.7 | 19.0 | 49.9 / 50.0 / 50.2 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 42.5 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 9.7 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 19.0 s | pass |
| Load from warm disk | ≤ 60 s | 99.5 s | **fail** |
| Tool-call validity | ≥ 9 / 10 | not measured | — |
