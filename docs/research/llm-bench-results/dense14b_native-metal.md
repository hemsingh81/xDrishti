# LLM benchmark — dense14b_native-metal

Model `qwen3-14b` · 2026-10-10 00:55 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 225.2 | 26.0 | 4.7 | 11.8 | 26.0 / 25.8 / 26.0 tok/s |
| 8,192 | 8386 | 198.5 | 21.9 | 42.3 | 51.6 | 22.6 / 21.9 / 20.3 tok/s |
| briefing (~4K → 300 words) | 3840 | 201.1 | 22.6 | 19.1 | 41.2 | 22.0 / 22.6 / 23.5 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 21.9 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 19.1 s | **fail** |
| 300-word briefing, end to end | ≤ 45 s | 41.2 s | pass |
| Load from warm disk | ≤ 60 s | 4.7 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
