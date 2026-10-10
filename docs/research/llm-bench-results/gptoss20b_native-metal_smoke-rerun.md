# LLM benchmark — gptoss20b_native-metal_smoke-rerun

Model `gpt-oss-20b` · 2026-10-10 01:21 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1106 | 777.4 | 72.8 | 1.5 | 1.6 | 72.8 tok/s |
| briefing (~4K → 300 words) | 3821 | 840.2 | 70.0 | 4.6 | 11.7 | 70.0 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29000 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | not measured | — |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 4.6 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 11.7 s | pass |
| Load from warm disk | ≤ 60 s | not measured | — |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
