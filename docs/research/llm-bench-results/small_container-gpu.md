# LLM benchmark — small_container-gpu

Model `qwen3-8b` · 2026-10-09 23:12 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 250.6 | 34.8 | 4.2 | 10.4 | 34.8 / 35.0 / 34.5 tok/s |
| 8,192 | 8386 | 232.0 | 29.1 | 36.2 | 43.3 | 29.1 / 29.1 / 29.1 tok/s |
| 30,000 | 30886 | 128.0 | 19.6 | 241.3 | 249.5 | 19.6 / 19.6 / 19.3 tok/s |
| briefing (~4K → 300 words) | 3840 | 290.7 | 32.0 | 13.2 | 28.8 | 32.1 / 32.0 / 32.0 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 29.1 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 13.2 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 28.8 s | pass |
| Load from warm disk | ≤ 60 s | 10.1 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
