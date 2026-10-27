# The assistant eval (spec P9-09, ADR 0143)

One measurement of whether the local model and the current prompts are good enough to be the assistant. Everything here is synthetic: fake symbols, no real holding, account or price.

| File | What it is |
|---|---|
| `fixture/fixture.json` | The whole world of the eval: instruments EVA..EVH2, two stored runs (ORB 15m with 24 trades, VWAP pullback 15m with 16), a plan for 2026-03-03, two holdings, one statistics cell, lessons and hypotheses. Its SHA-256 is the `fixture_hash`. |
| `sets/data-questions.jsonl` | The 30 data questions with their expected answers (numbers as exact fractions, grading rounds). |
| `sets/announcements.schema.json` | The row format of the event set; the 200 rows arrive with P15 (until then the set is `not_evaluable`). |
| `thresholds.json` | The pass thresholds of doc 14 section 5, in a file so a change is reviewed. |

Grading is code (no model judge): exact normalised numbers, words, honest-state phrases, a conflict check in the sentences that carry the answer, and a check that every figure is in a tool result, the question or the allowed list. Rates are printed with Wilson 95 % intervals; two runs are compared with an exact sign test on the discordant cases. 30 cases cannot separate 27 from 24 correct.

The report engine answers "insufficient" below 30 trades and the fixture groups are smaller, so a correct answer computes the figure from the stored trades; the model-free oracle test runs the engine with small minimums.

Model-free tests (`scripts/check.sh`) cover the graders, statistics, thresholds, the fixture and its oracle. A model run is an owner act and is described in the P9-09b section of this file once the runner exists.
