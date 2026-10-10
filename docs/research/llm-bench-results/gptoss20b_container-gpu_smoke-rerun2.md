# LLM benchmark — gptoss20b_container-gpu_smoke-rerun2

Model `gpt-oss-20b` · 2026-10-10 01:43 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1106 | 399.3 | 62.8 | 2.9 | 3.0 | 62.8 tok/s |
| briefing (~4K → 300 words) | 3821 | 477.2 | 60.9 | 8.1 | 16.2 | 60.9 tok/s |

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
| Time-to-first-token, ~4K prompt | ≤ 15 s | 8.1 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 16.2 s | pass |
| Load from warm disk | ≤ 60 s | 25.5 s | pass |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
