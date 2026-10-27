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

| `sets/plans.jsonl` | The 10 historical plans of the briefing set: stored `BriefingFacts` and what a good text keeps. |
| `sets/ideas.jsonl` | The 10 ideas of the drafting set with their assertions (8 drafts, 1 decline, 1 decline or partial). |

Model-free tests (`scripts/check.sh`) cover the graders, statistics, thresholds, the fixture and its oracle, and an end to end run with a scripted model.

## Running it (an owner act; nothing starts it by itself)

1. `deploy/xd-up.sh` once (it writes the eval token from the Keychain item `xdrishti/mcp/eval-token`).
2. `scripts/xd-eval.sh prepare` creates and seeds the fixture database `xdrishti_eval` (synthetic rows only; `scripts/eval-db.sh reset` recreates it) and starts the eval tool server `xd-mcp-eval`.
3. `scripts/xd-ai.sh start` starts the model (and `main` thinking off is the profile of the gate), and the assistant's master switch must be ON in the app. The runner never sets it.
4. `scripts/xd-eval.sh run` (all sets, three repeats; about two hours, UNVERIFIED) prints each set's rates with their 95 % intervals and the verdict, and writes `data/eval/<run>/report.json`, `report.md` and `answers.jsonl`. Exit code 0 only for a gating PASS. `--repeats 1` is a look only: it can never be accepted.
5. `scripts/xd-eval.sh compare <run>` compares with the accepted baseline (paired, exact sign test; it never blocks by itself), and `scripts/xd-eval.sh accept <run>` makes a gating PASS the baseline (needs `--reason` when a set got worse). `GET /api/assistant/evals/status` and the Assistant status then say whether the accepted run is still current: a new model, runtime, prompt version, set, threshold or fixture makes it stale.

A model that is down is reported as `model_unavailable` and the partial run is never a pass. Answers of another assistant (Hermes, P9-10) are graded from a file with `scripts/xd-eval.sh grade --answers FILE --target hermes`.

`eval/probes/injection.jsonl` (P9-10): the six injection probes of the Hermes PoC, graded by `ProbeGrader` (domain, tested). The fixture variant that carries the hostile text is not built yet, so `xd eval grade` does not grade probes; `xd eval grade` accepts an optional `tool_results` array per answer line.
