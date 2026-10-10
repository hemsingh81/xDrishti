# LLM benchmark — gptoss20b_container-gpu

Model `gpt-oss-20b` · 2026-10-10 01:17 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1106 | 403.2 | 63.9 | 2.8 | 6.7 | 63.5 / 64.0 / 63.9 tok/s |
| 8,192 | 8173 | 462.4 | 56.8 | 17.8 | 21.8 | 57.0 / 56.8 / 56.5 tok/s |
| briefing (~4K → 300 words) | 3821 | 477.0 | 60.9 | 8.1 | 16.2 | 60.9 / 60.6 / 60.9 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | **fail** | JsonReaderException |
| needle in ~30K tokens | pass | prompt tokens 29000 |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 56.8 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 8.1 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 16.2 s | pass |
| Load from warm disk | ≤ 60 s | 67.5 s | **fail** |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
