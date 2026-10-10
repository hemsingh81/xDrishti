# LLM benchmark — small_native-metal

Model `qwen3-8b` · 2026-10-09 23:27 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 389.2 | 42.0 | 2.7 | 8.0 | 41.8 / 42.0 / 42.3 tok/s |
| 8,192 | 8386 | 356.2 | 34.9 | 23.6 | 29.2 | 34.7 / 34.9 / 34.9 tok/s |
| 30,000 | 30886 | 195.8 | 22.4 | 157.8 | 166.1 | 21.9 / 22.4 / 22.9 tok/s |
| briefing (~4K → 300 words) | 3840 | 365.5 | 38.2 | 10.5 | 23.6 | 32.9 / 38.2 / 38.7 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 34.9 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 10.5 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 23.6 s | pass |
| Load from warm disk | ≤ 60 s | 6.4 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
