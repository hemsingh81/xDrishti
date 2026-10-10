# LLM benchmark — main30b_native-metal

Model `qwen3-30b-a3b` · 2026-10-10 00:31 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 631.1 | 73.5 | 1.7 | 4.1 | 73.6 / 73.5 / 73.4 tok/s |
| 8,192 | 8386 | 536.0 | 54.3 | 15.7 | 19.2 | 54.2 / 54.9 / 54.3 tok/s |
| 30,000 | 30886 | 210.3 | 27.5 | 146.9 | 153.5 | 26.2 / 28.1 / 27.5 tok/s |
| briefing (~4K → 300 words) | 3840 | 568.5 | 54.1 | 6.8 | 15.9 | 50.5 / 54.1 / 56.1 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 54.3 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 6.8 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 15.9 s | pass |
| Load from warm disk | ≤ 60 s | 9.5 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
