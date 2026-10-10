# LLM benchmark — gptoss20b_native-metal

Model `gpt-oss-20b` · 2026-10-10 01:20 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1106 | 715.4 | 72.3 | 1.6 | 5.1 | 71.6 / 72.3 / 72.5 tok/s |
| 8,192 | 8173 | 792.7 | 66.9 | 10.4 | 13.7 | 66.9 / 66.9 / 67.3 tok/s |
| briefing (~4K → 300 words) | 3821 | 838.5 | 69.9 | 4.6 | 11.7 | 69.9 / 68.3 / 70.3 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | **fail** | JsonReaderException |
| needle in ~30K tokens | pass | prompt tokens 29000 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 66.9 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 4.6 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 11.7 s | pass |
| Load from warm disk | ≤ 60 s | 6.2 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
