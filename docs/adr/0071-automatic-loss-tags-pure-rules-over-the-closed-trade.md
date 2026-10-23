# 0071 — Automatic loss tags: pure rules over the closed trade

- **Status:** Proposed (spec [P6-15](../specs/P6-15-automatic-loss-tags.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0053](0053-the-same-entries-comparison-and-the-mae-mfe-diagnostics.md) (MAE/MFE over the whole trade), [ADR 0064](0064-cell-statistics-bayesian-shrinkage-recency-weights-and-point-in-time-snapshots.md) (cells, unit of R), ADR 0069 (the other read-only `xd learn` verb).

## Context
Doc 09 row 9 asks for tags on every loss and for a growing tag to open a lesson. Several inputs it names do not exist yet (regime, event calendar, real fills).

## Decision
1. **Pure tagger** (`LossTagger.Tag`, rule version `lt-2`) over a closed `SimTrade`: seven tags (gap through stop, gave back profit, high slippage, late entry, quick stop, wide stop, normal variance), each carrying the measured value, the threshold and a fixed sentence with the numbers. It reads nothing after the exit and nothing outside the trade. A loss with no tag is **untagged**, reported as such; `NormalVariance` is only given when no other tag fits and the stop-out is a real loss (R below 0) within 1.25 R; a cost-only loss is untagged unless another rule fits. Late entry uses the repo decision-minute convention (decision instant minus one minute, 14:30 or later) and only when the fill is on the decision date. Gap is measured against the initial stop.
2. **A loss is R below 0 in the cell's unit** (net if charged, else filled). Tags overlap; counts per cell therefore exceed the loss count.
3. **Descriptions are never features.** Nothing is stored and no tag is offered to P6-04; a tag knows the outcome by construction.
4. **Growing-tag detector:** per cell, the last W losses against the W before (W 30): tag on at least 5 and share up at least 0.2; `NormalVariance` is never flagged. It yields a candidate in the report; no lesson is opened (P6-16). The number of comparisons (cells with at least 2W losses only) and the expected chance flags (assumed 5 per cent each) are printed.
5. **Not built, printed every time:** against regime (P6-02), news/results (P15), execution gap and decision drag (P7, P10, P11), and the learned-window "wrong time of day" (P6-14 is a proposal only; late entry is a fixed 14:30 cut-off).
6. **Read-only verb** `xd learn loss-tags`, handler `AnalyzeLossTagsHandler`, events 4530 and 4531. No migration, no endpoint.

## Consequences
- P6-16 can read `LossTagAnalyzer` for its tail-loss and growing-tag lesson sources without a schema.
- Changing any rule or default raises `RuleVersion`.
- Thresholds are judgement defaults; the owner may change them per run on the command line.

## Honest limits
MAE/MFE are whole-trade, so event order inside a trade is unknown. Tags describe, not explain. Chance flags are expected from the detector.
