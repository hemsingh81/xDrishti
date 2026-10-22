# P6-05 — Validation harness and baselines

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-05 of [P6-00](P6-00-learning-engine-and-ml-plan.md)
- **Design refs:** docs/design/09-learning-engine.md §1, §2, §6 · ADR 0067 · ADRs 0054 (folds), 0059 (gates and the "few folds" cap), 0065 (label horizon)

## Assumptions (taken as recommended)
- P6-00 question 3: **quarterly folds** as in ADR 0054 (first test window 9 months in, 3-month tests), the rule scaled to fewer folds (`folds_better_share` 2/3, 4 of 6), printed with the result.
- P6-00 question 4: **fewer than 6 used folds cap a passing candidate at "inconclusive"**, never "eligible" (as P5-03).
- P6-00 question 2: **the last 6 months are reserved as a hold-out**, used once at the phase check (P6-18). Consequence stated plainly: with about 2.1 years the development part is about 1.6 years, which gives roughly 3 quarterly folds, so until more history exists every verdict will be at most inconclusive. The owner may shorten the hold-out or extend the data (ISS-19); the code takes both as arguments.
- The margins (log loss 0.005, Brier 0.002, widened by `MultipleTesting.Factor(trials)`), the 7-day embargo and the 10 calibration bins are my choices where P6-00 says "a margin"; all are options and are printed.
- No features exist yet (P6-04), so the harness takes rows with a feature array; the logistic baseline works on whatever is there (none: it is the base rate). The cell-rate baseline is the shrunk win rate of the cell inside the train rows, not the stored P6-01 snapshot (that needs P6-04's point-in-time link).
- No migration, API, CLI or log events: pure Domain code. (Event ids 4490 to 4499 are unused.)
- ADR number 0067.

## Goal
A pure, deterministic harness that takes labelled rows, cuts purged walk-forward folds, scores any scorer against three baselines, and says honestly whether a model earns its place.

## Scope
**In:** `ValidationRow`/`ValidationRows.From(TrainingSet, contexts)`, `WalkForwardValidator.Compare`, `ValidationOptions`, folds (windows of `ExitSelector.Windows`, purge by label horizon plus one bar, embargo in days), `MetricsCalculator` (log loss, Brier, AUC, calibration error in pp over equal-mass bins, lift), baselines `BaseRateScorer`, `CellRateScorer`, `LogisticScorer` (no package), the always-take summary, `EligibilityRule`, `HoldOut.Reserve`, a text report with the honest limits.
**Out:** features (P6-04), ML.NET and calibration (P6-06), model registry (P6-07), API/CLI/UI.

## Acceptance criteria
1. Metrics equal hand-computed values (log loss, Brier, AUC with ties, ECE in pp, lift); a certain wrong probability has a finite log loss.
2. Base-rate, cell-rate and logistic baselines match hand tables; the logistic fit is deterministic and handles no features, one class and missing values.
3. Folds start 9 months after the first trade, tile quarterly and expand; a train row whose resolved instant plus one bar reaches the first decision of the test window is purged; rows within the embargo are dropped; a fold with too few train rows is not used; no usable fold is the failure `validation.no_folds`.
4. No leak: a scorer sees only rows dated before the window and resolved before its first decision; changing later folds' labels or dropping later rows does not change an earlier fold.
5. Determinism: the same rows in any order give the same report text.
6. Eligibility rule by hand table: beats both baselines (log loss and Brier, by margin) in at least `ceil(2/3 folds)` folds and pooled, pooled ECE within 5 pp, enough train rows; fewer than 6 folds gives inconclusive; otherwise `CannotBeatBaselines` with the reasons; more trials widen the margins.
7. A scorer with no skill cannot beat the baselines; a perfect (cheating) scorer is inconclusive on 5 folds and eligible when 5 are enough.
8. Hold-out: the last 6 months are cut, development rows whose horizon reaches it are purged and counted, and `Compare` refuses a row on or after `HoldOutStart` (`validation.holdout_touched`).
9. Bad input (duplicate key, label not 0/1, feature width, duplicate scorer name, bad options, no rows, probability outside [0,1]) is a failure with a code.

## Contract
- **API / Data / UI / jobs:** none. No migration.

## Non-functional
Pure functions: no clock, no random, no I/O. Sequential sums for reproducible floating point.

## Test plan
`MetricsAndScorersTests`, `EligibilityRuleTests`, `WalkForwardValidatorTests` (Domain.Tests).

## Open questions
None blocking. For the owner: with the 6-month hold-out the development data give about 3 folds (see assumptions).

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Design docs/ADR updated
