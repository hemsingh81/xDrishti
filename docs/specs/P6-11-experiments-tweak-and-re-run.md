# P6-11 — Experiments: tweak and re-run

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-11 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 4 d
- **Design refs:** docs/design/09-learning-engine.md §12 (rules of honest experiments) · ADR 0072 · ADRs 0054 (folds), 0059 (ledger, "few folds" cap), 0067 (harness, hold-out), 0070 (record pattern)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **What an experiment compares.** The replay that makes a candidate (a knob changed) is the existing Lab / `xd simulate` run. An experiment takes two **stored runs** (baseline and candidate) made on the same data and judges them. The replay is deterministic and has no random number, so "common random numbers" means **the same entries**: the candidate may skip entries of the baseline (a selection threshold) but never add one (`experiments.entries_added`), and a trade is paired by its trade key. Knobs that change which entries exist are therefore not comparable here.
2. **One knob, kinds.** `parameter`, `exit` (free text values) and `slippage`. The `--from`/`--to` values are labels: numeric values compare and key as numbers (4, 4.0 and 04 are one value). Other cost knobs wait for a consumer.
3. **Made on the same data, nothing but the knob differing.** The two runs must share the engine version, record and trade schema versions, window (from/to), universe kind and label, the CSV-adjusted acknowledgement, the strategy id and the cost sheet (id and rates hash). The strategy definition hash must match too, except for a `parameter` or `exit` knob (they change the definition); the fill rules must be identical, except that a `slippage` knob may differ in the `slippage.*` fields only (never the entry limit or the gap rule). Otherwise `experiments.assumptions_differ`: a hidden cheaper assumption could win unseen.
4. **Cheaper slippage cannot win.** For a slippage knob the direction is **read from the two stored runs' canonical fills JSON** (`ticks`, `bar_range_frac`, `stop_exit_extra_ticks`), not from the typed values: any component lower means `CheaperAssumption`, whatever the numbers, including components that cannot be ordered (some lower, some higher: reported as such); only equal-or-higher with one higher is judged on the numbers. Identical or unreadable fills are refused. A cheaper one is still recorded (it is a look).
5. **Folds.** The fold windows of ADR 0054 (first test 9 months in, quarterly tests) over the entry dates. There is no training, so no purge; the hold-out (last 6 months, as P6-05) is cut with the same purge of trades whose exit reaches into it, taking the later of the two runs' exits. A fold is used when both sides have at least 10 trades in it (`min_fold_trades`; a command line can raise it, never lower it below 10). **Better** needs, in at least `ceil(2/3 x used folds)` folds (4 of 6) and pooled over the used folds, a mean-R gain above `0.02 + 0.006 x sqrt(trials)` R per trade (doc 09 §12 rule 5); **fewer than 6 used folds is at best Inconclusive**, and a command line cannot lower that floor. (Doc 09 says 12 folds and 8 better; the plan scales it to the data, P6-00 question 3.)
6. **Total-R rule.** Total R of the candidate, after the expected live gap, may fall at most 10 % below the baseline's (doc 09 §12 rule 4; P6-00 row). A candidate that trades much less at a higher expectancy fails here.
7. **Expected live gap.** 0.02 R per trade, **an assumption** until P10 measures live fills (a command line can raise it, never lower it below 0.02: zero would flatter a candidate that trades more); charged per trade to both sides after the fold means are compared, so it matters in the totals (a candidate that trades less is charged less) and is printed. It is not a measured number.
8. **Trials = the ledger.** Trials = experiments recorded in the last 30 days (other than this one) plus one; `--trials` can only raise it. The count and the insert happen under one global advisory lock in the ledger append, which recounts inside the lock; if another experiment was recorded meanwhile the judgement is run again with the ledger's number (a few tries, then `experiments.busy`), so no two experiments share a trial number. The ledger is a sibling table `research.experiments` (insert-only), not `lab.test_ledger` (that one counts Lab strategy variants; ADR 0072 says why).
9. **Idempotence.** The experiment key is the hash of the two run keys, the knob and the rules; the same experiment again, including the loser of a concurrent identical request, is answered with the stored text and outcome and counts once. Changing any rule or value is another experiment and a new trial.
10. **Timing is not claimed.** The 10-minute target is for "replay of a knob change and comparison". This slice measures and stores only the time to read both runs and judge them; the replay is a separate run whose time is not recorded anywhere. The record and the report say **not measured**. A measurement on the owner's data is left to the phase check (P6-18).
11. **Surface.** Verb `xd learn experiment`; read endpoints `GET /api/experiments` and `/{id}`. No write endpoint and no screen. Not wired to the hypothesis loop (P6-10) or model knobs (P6-07 scoring has no knob replay yet).
12. ADR 0072; log ids 4540 to 4543; migration `AddExperiments`.

## Goal
Change one knob, re-run, and get an honest verdict against the baseline on the same entries, with the number of experiments tried recorded so that each further look must clear a higher bar.

## Scope
**In:** pure `Domain/Learning` (`ExperimentKnob`, `ExperimentOptions`, `ExperimentJudge`, report); `Application/Learning` command `RunExperiment` (reads stored runs, checks assumptions, hold-out, trials from the ledger, records) and the two read queries; port `IExperimentLedger`; `research.experiments` and migration; endpoints; verb; ADR; docs.
**Out:** producing the candidate run (Lab / `xd simulate`), a write endpoint or screen, model-knob replay, the hypothesis state machine (P6-10), shadow and apply (P10), measuring the replay time.

## Acceptance criteria
1. Hand table: one fold with baseline 1.0 R and candidate 1.5 R over 10 trades gives difference 0.5, required 0.026, totals by hand after the 0.02 gap, and is Inconclusive (1 fold). *`ExperimentJudgeTests`.*
2. Nine folds all better by a clear margin: Better; with five folds, Inconclusive even when every fold is better, and `--min-folds` below 6 is raised to 6 and says so.
3. A cheaper slippage (8 to 4 bps) is `CheaperAssumption` though all nine folds are better; a dearer one is judged on the numbers.
4. The required improvement is `0.02 + 0.006 sqrt(trials)` (trials 1, 4, 9, 100 give 0.026, 0.032, 0.038, 0.080); a 0.035 R gain is Better at 1 and 4 trials and not at 9.
5. Higher expectancy on a tenth of the trades with every fold better is NotBetter because total R after the gap falls more than 10 %.
6. Determinism: the same trades in any order give the same text. Bad input (no trades, duplicate key, non-finite R, bad options, unchanged value, non-numeric slippage, hold-out touched) is a failure with a code.
7. Handler: a clear gain is recorded as Better with a text that says the replay time was not measured; the same experiment again is not judged twice and counts once; **each distinct experiment in 30 days raises the trials 1, 2, ... 9** and the ninth fails where the first passed; an experiment older than 30 days no longer counts; the hold-out is cut and stated; a candidate with entries the baseline lacks, a different cost sheet, or (not for slippage) a different fill basis is refused; a bad request is refused before any read; an unknown run is not found. *`RunExperimentHandlerTests`.*
8. Integration: the migration applies and goes down; `xd_app` can only INSERT and SELECT on `research.experiments`; the ledger records once, counts as a trial and is read back by both endpoints; a run that is not stored writes nothing. *`ExperimentLedgerApiTests`, `ResearchGrantsTests`, `ResearchStoreMigrationTests`.*
9. The verb parses and refuses bad lines. *`LearnExperimentOptionsTests`.* Architecture tests, `scripts/gen-api.sh` and `scripts/check.sh` green.

## Contract
- **API:** `GET /api/experiments?limit=` (1 to 200, default 50; 422 `learning.invalid`), `GET /api/experiments/{id}` (404 `experiments.not_found`) → `ExperimentView`.
- **Data:** `research.experiments` (key, knob, two run keys with foreign keys to `sim_runs.run_key`, dates, hold-out start, outcome, trials, folds, required improvement, trades, pooled means, totals after the gap, the gap, `compare_ms`, reasons, report, parameters, who, when); insert-only for `xd_app`; check: `Better` needs at least 6 used folds. Migration `AddExperiments` (with `Down`).
- **Verb:** `xd learn experiment --baseline RUNKEY --candidate RUNKEY --knob NAME --kind parameter|exit|slippage --from V --to V [--hold-out-months N] [--min-fold-trades N] [--live-gap-r X] [--trials N]`; exit codes as the other `xd` commands.

## Non-functional
Pure judge, no clock or random, sums in a fixed order. Reading two runs is a stream; thousands of trades judge in milliseconds. Logs carry ids, counts, codes and milliseconds, never a trade or a knob value.

## Test plan
As the acceptance list: hand tables, the two honesty rules, determinism, ledger increments over a fake ledger, real-database tests for grants, migration and endpoints.

## Honest limits
Replayed history, modelled fills; the knob was chosen by looking at this history, so the folds show spread over time, not out-of-sample proof for the choice (ISS-19). With about 2.1 years and a 6-month hold-out only about 3 folds exist, so **on today's data every experiment is at best Inconclusive**, and the report says so. The live gap is an assumption. An experiment that is refused or fails before it is judged is not recorded and is not a trial, though it may have been looked at. With `--hold-out-months 0` the outcome is labelled and is at best Inconclusive, never Better. The 30-day trial count sees only experiments recorded here (not knob changes tried elsewhere). The "replay and comparison under 10 minutes" criterion is **not measured**. Not exercised on the owner's real stored runs in this slice.

## Open questions
None blocking. For the owner: whether the live gap should be a fixed 0.02 R or scale with the slippage knob once live fills exist; whether to add a write endpoint when the Lab page needs one.

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] `scripts/gen-api.sh`; migration with `Down`; ADR 0072, doc 09, roadmap, site rebuilt
