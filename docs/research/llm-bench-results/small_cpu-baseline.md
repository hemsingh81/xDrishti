# LLM benchmark — small_cpu-baseline

Model `qwen3-8b` · 2026-10-09 23:30 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 111.5 | 24.3 | 9.5 | 12.1 | 23.6 / 24.9 tok/s |
| briefing (~4K → 300 words) | 3840 | 94.1 | 16.0 | 40.9 | 72.1 | 15.8 / 16.3 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | not measured | — |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 40.9 s | **fail** |
| 300-word briefing, end to end | ≤ 45 s | 72.1 s | **fail** |
| Load from warm disk | ≤ 60 s | 6.4 s | pass |
| Tool-call validity | ≥ 9 / 10 | not measured | — |
