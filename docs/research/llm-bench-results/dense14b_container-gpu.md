# LLM benchmark — dense14b_container-gpu

Model `qwen3-14b` · 2026-10-10 00:46 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 165.0 | 20.4 | 6.4 | 15.9 | 20.4 / 20.4 / 20.5 tok/s |
| 8,192 | 8386 | 134.1 | 17.1 | 62.6 | 74.5 | 17.1 / 17.1 / 17.2 tok/s |
| briefing (~4K → 300 words) | 3840 | 140.0 | 18.9 | 27.5 | 53.8 | 18.9 / 18.9 / 18.7 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 17.1 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 27.5 s | **fail** |
| 300-word briefing, end to end | ≤ 45 s | 53.8 s | **fail** |
| Load from warm disk | ≤ 60 s | 44.3 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
