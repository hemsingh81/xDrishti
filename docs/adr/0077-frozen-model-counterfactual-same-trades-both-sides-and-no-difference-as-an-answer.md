# 0077 — Frozen-model counterfactual: the same trades on both sides, and "no difference" as an answer

- **Status:** Proposed (spec [P6-12](../specs/P6-12-frozen-model-counterfactual.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (metrics, embargo, purge, hold-out), [ADR 0068](0068-the-ml-net-trainer-adapter-pav-calibration-on-a-held-back-slice-and-honest-explanations.md) (the trainer), [ADR 0075](0075-meta-labelling-policy-thresholds-from-earlier-folds-and-does-not-help-as-an-answer.md) (take/skip effect on realised R). ADR 0076 belongs to another slice.

## Context
Doc 09 wants to show whether the system is improving by comparing a frozen copy of an older model with the one that keeps learning. A comparison on different trades, or a frozen model that quietly saw later labels, or a verdict from two quarters of noise, would flatter continued learning. The data hold about two years, so "stopped learning" can be about a year at most.

## Decision
1. **Pure core** `CounterfactualEvaluator.Evaluate(rows, model, options, validation)` in `Domain/Learning`. Frozen side: one fit on rows dated before the cut minus the embargo and resolved (plus one bar) before the period's first decision, never refitted. Retrained side: refitted at the start of each window (default 3 months, expanding) on everything resolved before it, under the same embargo and purge. Both score exactly the same later trades.
2. **Period** is the requested age (1 to 12 months, default 12) before the end of the development data, never earlier than the minimum train months (9) after the first trade; the months actually used are printed. The hold-out is cut by the use case before anything is scored.
3. **Evidence:** P6-05 metrics per side, per window and pooled, and the realised R of taking trades scored at or above the base rate of the side's own train set (fixed rule, no search, so no leak), kept and skipped beside take-everything.
4. **Judgement** on windows in which the retrained model has seen labels the frozen one has not (the first window has identical models and is not judged): at least 3 such windows else `Inconclusive`; the paired per-trade log loss gain must beat a margin in `ceil(2/3)` of them and pooled, and a standard-error bound, both widened by `MultipleTesting.Factor(trials)`, for `RetrainingHelps` (mirror: `FrozenBetter`); else `NoDifference`. Disagreement with the realised-R measure caps at `Inconclusive`. The minimum of 3 judged windows (not the 6 folds of P6-05) is a knob: a year of quarterly windows cannot give more, and the report prints it.
5. **Evidence only.** Read-only verb `xd learn counterfactual` (handler `EvaluateCounterfactual`, log ids 4590 and 4591), hold-out 6 months by default, no champion needed, no migration. Plan-price comparison and the Journey screen wait for P7 and P8.

## Consequences
- With the 6-month hold-out the period is under a year today (about 7 to 10 months), giving 2 to 3 judged windows; the answer is often `Inconclusive`, and the same code gives verdicts on longer history (ISS-19).
- "Continued learning does not measurably help" is a legitimate result and must not be read as proof of no benefit.
- Net R is on modelled fills. Not exercised on the owner's real stored runs in this slice.
