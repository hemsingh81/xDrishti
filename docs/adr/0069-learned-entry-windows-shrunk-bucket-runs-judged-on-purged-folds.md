# 0069 — Learned entry windows: shrunk bucket runs judged on purged folds

- **Status:** Proposed (spec [P6-14](../specs/P6-14-learned-entry-windows.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0064](0064-cell-statistics-bayesian-shrinkage-recency-weights-and-point-in-time-snapshots.md) (the shrinkage), [ADR 0065](0065-triple-barrier-labels-from-the-exit-reason.md) (labels), [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (folds, purge, hold-out), [ADR 0061](0061-the-xd-cli-is-a-verb-of-the-api-host-behind-a-thin-script.md) (the verb). ADR 0068 belongs to another slice.

## Context
Doc 09 feature 7: the ticket's validity window becomes the best entry hour of a setup, learnt from trades. With a few thousand trades split into cells, an hour with a lucky streak is the main danger, so the learning must shrink, require a plateau and be checked out of sample, and "no stable window" must be an ordinary answer.

## Decision
1. **Pure core** `EntryWindowLearner.Learn(rows, options, validation)` in `Domain/Learning` over the `ValidationRow`s of P6-05 (label, R, decision instant, resolved instant, cell). Entry time is the minute of the bar that completed at the decision instant; buckets of 30 minutes from 09:15.
2. **Window** = contiguous run of buckets. Score = label rate shrunk toward the cell's own rate with strength 20. Admissible: at least 30 trades, lift over the cell rate above 0.03, non-empty end buckets, and no bucket inside whose own shrunk rate is below the cell rate (no hole). Best score; ties to more trades, then earlier start.
3. **Stability** on the shared folds of ADR 0067 (same windows, embargo and horizon purge). In each fold the window is learnt on its train rows alone and judged on its test rows: it holds when its rate beats the cell's trades outside it (at least 5 on each side). Needed: `ceil(2/3 x evaluable)` folds. Fewer than 2 evaluable folds, or fewer than 6 shared folds, caps the outcome at inconclusive (as P5-03 and ADR 0067).
4. **Outcomes:** not enough evidence, no stable window, inconclusive, proposed. A window dropping more than half the cell's trades is flagged. Every report carries the honest-limits sentences, the data range and the fold count.
5. **Proposal only, nothing stored.** No table, migration or endpoint: the result is a pure function of the stored runs. The read-only verb `xd learn windows` (handler `ProposeEntryWindows`, log ids 4510 and 4511) prints it, after cutting a 6-month hold-out (`--hold-out-months`, 0 keeps none and says so). Applying a window is for P6-10/P6-11 and P7.

## Consequences
- On about 2 years of data with the hold-out there are about 3 folds, so no window can read "proposed" yet; the same code gives proposals on longer history (ISS-19).
- Comparing every contiguous window of a cell is a multiple-comparison search; the guards (prior, lift floor, no hole, folds) reduce but do not remove selection optimism.
- The folds judge by the label rate; mean R is shown but does not decide.
- Log event ids 4510 and 4511 are used. Not exercised on the owner's real stored runs in this slice.
