# 0067 — Validation harness and baselines: purged folds and an honest eligibility rule

- **Status:** Proposed (spec [P6-05](../specs/P6-05-validation-harness-and-baselines.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0054](0054-the-per-cell-exit-choice-by-walk-forward.md) (the fold windows), [ADR 0059](0059-the-test-ledger-and-the-verdict-evaluator.md) (few folds cap the verdict; `MultipleTesting`), [ADR 0065](0065-triple-barrier-labels-from-the-exit-reason.md) (label horizon). ADR 0066 belongs to another slice.

## Context
P6-00 D4: a model is eligible only if it beats the cell-statistics score and a logistic regression out of sample, is calibrated, and had enough trades; otherwise the system keeps cell statistics. The data are about 2.1 years (ISS-19), so the rule must say what few folds are worth.

## Decision
1. **Pure Domain harness** (`Domain/Learning`): `WalkForwardValidator.Compare(rows, candidates, options)` returns a `ValidationReport` or a failure code. Candidates implement `IScorerFactory` (`Fit(train)` returns `IRowScorer`), the port P6-06 adapts ML.NET to. A scorer receives only the train rows.
2. **Folds** are the windows of `ExitSelector.Windows` (expanding, first test 9 months in, 3-month tests, short last window dropped): one fold definition (P6-00 D3). A train row is dropped when it entered within 7 calendar days before the window (embargo; calendar days because the harness has no calendar) and when its **resolved instant plus one bar** reaches the first decision of the test window (purge by label horizon). Both counts are in the report.
3. **Baselines:** base rate (Laplace-corrected train rate), cell rate (cell win rate shrunk to the base rate with strength 20, unseen cell = base rate), logistic regression (standardised features, ridge 1, Newton steps, own Gaussian elimination, no package). Always-take (win rate and mean R of every trade in the test window) is reported per fold. The two baselines of the rule are cell rate and logistic; base rate is the floor shown beside them.
4. **Metrics:** log loss (clamped at 1e-15), Brier, AUC (tie-averaged ranks, null with one class), expected calibration error in pp over 10 equal-mass bins (equal-mass bins over scores, tied scores kept in one bin; lift averages over ties at the top-fifth boundary, so a constant scorer has lift 1), lift of the top fifth.
5. **Eligibility** (`EligibilityRule.Judge`): "beats" means log loss and Brier both lower than the baseline's by more than the margin (0.005, 0.002, times `MultipleTesting.Factor(trials)`). Eligible needs: both baselines beaten in at least `ceil(2/3 x used folds)` folds, both beaten on the pooled out-of-sample rows, pooled ECE at most 5 pp, at least `min_trades` train rows. A candidate that passes with fewer than 6 used folds is **inconclusive**, never eligible. Else `CannotBeatBaselines` with every reason; the system then keeps cell statistics. The margins and shares are my choices, options, and printed.
6. **Hold-out:** `HoldOut.Reserve` cuts the last 6 months (from `last date - 6 months + 1 day`), purging development rows whose horizon reaches the hold-out's first decision. `ValidationOptions.HoldOutStart` makes `Compare` refuse any row on or after it. That the hold-out is used once is a record kept by the phase check (P6-18), not by this code.
7. No migration, API, CLI or log events.

Review fixes: a logistic fit that does not reach tolerance in 50 Newton steps prints a line in the report limits (`IFitNote`); the verdict's train-row count is the last used fold's train size (the largest, standing for the final model); a default feature array reads as empty.

## Consequences
- With the 6-month hold-out the development data give about 3 quarterly folds, so no verdict can be "eligible" on today's data; the report says "fewer than 6 folds". A longer history (ISS-19) or a shorter hold-out is the owner's lever.
- The cell-rate baseline is computed from train rows, not from the stored snapshot of P6-01; P6-04 can swap in the snapshot's value as a feature.
- Folds are keyed by entry date; a swing trade is protected by the horizon purge, not by the embargo.
