# LLM benchmark — main30b_container-gpu

Model `qwen3-30b-a3b` · 2026-10-10 00:11 +05:30

## Throughput

| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |
|---|---|---|---|---|---|---|
| 1,024 | 1051 | 368.8 | 52.0 | 2.9 | 6.7 | 51.6 / 52.0 / 54.4 tok/s |
| 8,192 | 8386 | 312.4 | 40.9 | 26.9 | 31.3 | 41.7 / 40.9 / 40.3 tok/s |
| 30,000 | 30886 | 132.6 | 24.0 | 233.0 | 240.7 | 24.6 / 24.0 / 23.7 tok/s |
| briefing (~4K → 300 words) | 3840 | 374.4 | 47.3 | 10.3 | 20.0 | 47.3 / 47.1 / 48.6 tok/s |

## Capability smoke

| Check | Result | Detail |
|---|---|---|
| tool calls valid | pass | 10/10 |
| json mode | pass | valid JSON object |
| needle in ~30K tokens | pass | prompt tokens 29490 |
| embeddings | pass | 768 dimensions |

## Thresholds

| Measure | Target | Actual | |
|---|---|---|---|
| Generation speed at ~8K context | ≥ 15 tok/s | 40.9 tok/s | pass |
| Time-to-first-token, ~4K prompt | ≤ 15 s | 10.3 s | pass |
| 300-word briefing, end to end | ≤ 45 s | 20.0 s | pass |
| Load from warm disk | ≤ 60 s | 61.3 s | **fail** |
| Tool-call validity | ≥ 9 / 10 | 10/10 | pass |
