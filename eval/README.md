# The assistant eval (spec P9-09, ADR 0143)

One measurement of whether the local model and the current prompts are good enough to be the assistant. Everything here is synthetic: fake symbols, no real holding, account or price.

| File | What it is |
|---|---|
| `fixture/fixture.json` | The whole world of the eval: instruments EVA..EVH2, two stored runs (ORB 15m with 24 trades, VWAP pullback 15m with 16), a plan for 2026-03-03, two holdings, one statistics cell, lessons and hypotheses. Its SHA-256 is the `fixture_hash`. |
| `sets/data-questions.jsonl` | The 30 data questions with their expected answers (numbers as exact fractions, grading rounds). |
| `sets/announcements.schema.json` | The row format of the event set D (200 labelled announcements; until they exist the set is `not_evaluable`). The labelled text is in the git-ignored `data/eval/sets/announcements.jsonl`; only `sets/announcements.manifest.json` (ids and hashes, no text) is committed. |
| `sets/announcement-baseline.json` | The pre-registered category baseline of set D (P15-07): exchange category to `material` or `non_material`, committed before the first sample. |
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

## Labelling announcements (P15-07; an owner act, UNVERIFIED 2 to 3 hours for 200)

1. Put the announcements csv files into the filings folder (header `symbol,broadcast_ts,category,subject,text`, sidecar `kind announcement`, symbol `*`; see `scripts/filings-drop.sh`); the paused service `filings-ingest` loads them ("Run now" after you resume it).
2. Check `sets/announcement-baseline.json`: the category names must be the ones in your files. **Edit it now, before the sample; after the sample it is fixed** (its hash is in the sample, the manifest and the eval fingerprint, and `import` refuses a changed baseline).
3. `scripts/xd-eval.sh announcements sample --from YYYY-MM-DD --to YYYY-MM-DD` writes `data/eval/announcements/to-label.csv` (240 rows: `a001` to `a200` main, `a201` to `a240` reserve, in a fixed order) and `sample.json`. The seed is 1337 unless you pass `--seed`. A sample that exists is never replaced by another one.
4. Write `material`, `non_material` or `unsure` in the `label` column of every main row (do not edit any other column; an edited cell is refused). An `unsure` is replaced by the next reserve row, which you then label too.
5. `scripts/xd-eval.sh announcements import FILE.csv` checks the file, writes `data/eval/sets/announcements.jsonl` and `sets/announcements.manifest.json`; `scripts/xd-eval.sh announcements show` prints the counts. `xd eval run --set events` then classifies every labelled row with the local model (needs the model running and the assistant's master switch on; 200 rows x repeats, about 50 minutes at 3 repeats, UNVERIFIED) and grades it: set D passes only with at least 200 rows, at least 30 predicted material (`events.min_predicted_material`), precision of material at least 0.80 and a better result than the category baseline by the exact two-sided sign test (`events.baseline_sign_p`, 0.05). The report prints recall, both Wilson intervals, the baseline's precision and recall, b, c, p, the unstable cases, the population-weighted precision and the hostile-set counts; a pass whose interval reaches under 0.80 says weak evidence. Without `baseline` the set is `not_evaluable`.

**Definition of material.** An announcement is **material** if a reasonable holder of the stock, reading only this text, would want to know before the next trading session because it can change the company's expected earnings, cash, ownership, control, legal standing or financing: results and their outcome, a rating action, a large order or contract, an acquisition, merger, demerger or sale of a business, fund raising or a buyback, a change of auditor or of a director or senior officer, litigation or a regulatory order, a default, a fraud or a fire or a stop of operations. It is **non_material** if it is routine or procedural: a trading-window notice, a newspaper publication, a certificate or compliance filing, an investor-meet intimation, a meeting notice, a lost share certificate, a clarification that adds no new fact. Judge the text, not the category and not the price move. When the text is too short or too vague to judge, write `unsure`.

**Edit the file in a plain-text editor, or import every column as text in a spreadsheet.** A subject or text that starts with `=`, `+`, `-` or `@` is exported with a leading apostrophe so that a spreadsheet does not run it as a formula; the import takes that apostrophe off again. A spreadsheet that reformats dates, trims text or changes line breaks makes the row refused (`eval.text_changed`).

Third-party text: the labelled file is data. Nothing under `eval/` contains an announcement's text, and the eval never executes or follows anything in it. The label is one person's judgement; precision on this sample is a stratified estimate (half the rows are drawn from the material-looking categories), not the precision on all announcements.

