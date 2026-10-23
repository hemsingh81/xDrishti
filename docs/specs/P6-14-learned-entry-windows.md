# P6-14 — Learned entry windows

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended in P6-00). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-14 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 2 d
- **Design refs:** docs/design/09-learning-engine.md §14 (honesty rules), row 7 of the feature table · ADR 0069 · ADRs 0064 (shrinkage), 0067 (folds, purge, hold-out), 0065 (labels)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **Proposal only.** Nothing is stored, no strategy or scanner is changed; applying a window is a hypothesis for P6-10/P6-11 and the plan in P7. So no migration and no endpoint: a read-only verb `xd learn windows` prints the proposal. (P6-00 says "stored per cell"; storing waits until a consumer exists, because the result is a pure function of the stored runs and recomputes in milliseconds.)
2. **Entry time** is the minute of the bar that completed at the decision instant (decision instant minus 60 s, India time), the same convention as the trade date of the training set. Buckets are `bucket_minutes` (30) wide from 09:15; 13 buckets; trades decided outside the regular session are counted and not bucketed.
3. **Outcome = the triple-barrier label** (P6-03), as for the rest of P6, not R above 0. Mean R is reported beside it.
4. **Window = a contiguous run of buckets.** Score = win rate shrunk toward the cell's own win rate with strength 20 (the prior of P6-01, `(wins + s p0)/(n + s)`). A window needs `min_window_trades` 30 trades, a shrunk-rate lift over the cell rate above `min_lift` 0.03, non-empty first and last buckets, and **no hole**: no bucket inside whose own shrunk rate is below the cell rate (the plateau rule in the spirit of P4-06: a spike with filler does not count). Best score wins; ties go to more trades, then the earlier start.
5. **Stability** uses the shared folds of P6-05 (quarterly, first test 9 months in, 7-day embargo, purge by label horizon plus one bar). In each fold the window is re-learnt on the train rows only and judged on the test rows: it holds when its win rate beats the cell's trades outside it, with at least `min_fold_trades` 5 on both sides. Stable needs `ceil(2/3 x evaluable)` folds held. **Review fixes:** a fold holds only when inside minus outside rate exceeds `min_lift` out of sample; a pooled out-of-sample lift (rates pooled over the evaluable folds) must also exceed `min_lift`, else `NoStableWindow`. The folds judge the PROCEDURE (window re-learnt per fold), not the final window; the report counts fold windows that overlap or equal the final one. Window bounds are evaluation-bar minutes (decision minus 60 s); feature 32 (`minute_of_day`) is the decision minute, one later. The limits print `N cells compared, expect about alpha*N false proposals` (alpha = chance coin-flip folds give the required holds). `NotEnoughEvidence` names the trades dropped as out of session.
6. **Honest outcomes:** `NotEnoughEvidence` (cell below 30 trades), `NoStableWindow` (no admissible window, or it held in too few folds: keep the strategy's own time rules), `Inconclusive` (fewer than 2 evaluable folds, or fewer than 6 shared folds exist, as P6-05), `Proposed`. A window that drops more than half the cell's trades is flagged (doc 09 §14).
7. **Hold-out:** the verb keeps the last 6 months out (`--hold-out-months`, 0 keeps none and the report says so).

## Goal
Per cell, a proposed entry-time window with its evidence (trades, shrunk rate, lift, mean R inside and outside, folds held), or the honest statement that no stable window exists.

## Scope
**In:** pure `Domain/Learning` (`EntryWindowLearner`, options, proposal, fold, bucket, report with a fixed text); `Application/Learning` query and handler `ProposeEntryWindows` (reads stored runs, labels, hold-out), log ids 4510 and 4511; verb `xd learn windows`; ADR 0069; docs.
**Out:** persistence, endpoint, screen, regime split, applying a window to the scanner or a strategy, wiring into the plan (P7), the hypothesis loop (P6-10), a model.

## Acceptance criteria
1. Hand table: buckets of 20 trades with 16, 10 and 4 wins give the window of the first bucket, shrunk rate 0.8, lift 0.3, kept share 1/3 (flagged), mean R inside by hand; the prior pulls a 10-trade window toward the cell rate by the stated formula. *`EntryWindowLearnerTests`.*
2. A cell under 30 trades is `NotEnoughEvidence`; a flat cell has `NoStableWindow`; a pooled window that needs a weak bucket inside is refused.
3. A window that holds in every fold is `Proposed` when the folds suffice and capped at `Inconclusive` otherwise (fewer than 6 folds); a window good only in train has 0 folds held and `NoStableWindow`.
4. **No leak:** changing the labels of later folds leaves an earlier fold identical; a train row whose horizon reaches the test window is purged.
5. Determinism: the same rows in any order give the same text.
6. Bad input (no rows, bad options, duplicate key, label not 0/1, row in the hold-out) is a failure with a code.
7. The handler turns a stored run into labelled rows by cell, cuts the hold-out and states it, refuses a bad request before any read and an unknown run. *`ProposeEntryWindowsHandlerTests`.*
8. The verb parses and refuses bad lines. *`LearnWindowsOptionsTests`.*
9. Architecture tests and `scripts/check.sh` green.

## Contract
- **API / Data / UI:** none. No migration.
- **Verb:** `xd learn windows --run RUNKEY:STYLE:EXIT [--run ...] [--bucket-minutes N] [--min-trades N] [--min-lift X] [--prior-strength N] [--hold-out-months N]`; exit codes as the other `xd` commands.

## Non-functional
Pure functions, no clock or random; sums over integers, so the result is reproducible. Thousands of trades run in milliseconds. Logs carry counts and milliseconds only.

## Test plan
As the acceptance list; hand tables and generated 14-month series for the folds.

## Honest limits
Replayed history, modelled fills, in-sample on the data shown. All contiguous windows of a cell are compared, so the best is optimistic by selection; the lift floor, the prior, the no-hole rule and the folds are the guards, not a proof. With the 6-month hold-out about 3 folds exist, so on today's data no window can read `Proposed`, at best `Inconclusive` (the report says so). Folds judge by the win rate; R can disagree and is shown. Not exercised on the owner's real stored runs in this slice.

## Open questions
None blocking. For the owner: bucket width (30 min) and whether to store proposals once P7 reads them.

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] ADR 0069, doc 09, roadmap, site rebuilt
