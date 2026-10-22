# P6-03 — Triple-barrier labels

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-03 of [P6-00](P6-00-learning-engine-and-ml-plan.md)
- **Design refs:** docs/design/09-learning-engine.md §1 (layer 3), §4, §9 · ADR 0065

## Assumptions (taken as recommended)
- P6-00 question 6: a trade that ends at the session end or the max hold is label 0 even in profit; label 1 only when the target ended the trade. R at resolution is kept as a second target.
- The label is read from the walker's `ExitReason`; no price is re-read, so nothing beyond the barrier definition can leak.
- (Landed in [P6-04](P6-04-decision-time-features-and-the-training-set.md), ADR 0066.) The `training_sets` table is **not** created here (no consumer yet); the record is a pure domain type and is persisted with the features in P6-04. Reversible, and the roadmap row is amended to say so.
- ADR number 0065 (0064 is used by another slice).

## Goal
A pure, deterministic labeller that turns finished simulated trades into 0/1 triple-barrier labels with R and time to resolution, and a training-set record over many trades.

## Scope
**In:** `TripleBarrier.Label(trade[, basis])`, `TripleBarrierLabel`, `BarrierOutcome`, `LabelBasis`, `TrainingSetBuilder.Build` and `TrainingSet` (domain, `Research`). Rules for every exit reason, legs and swing exits; basis per doc 09 §9.
**Out:** persistence/migration of `training_sets` (P6-04), features, API, UI.

## Acceptance criteria
1. Target -> 1; Stop, SquareOff, MaxHold, EmaExit, TimeStop -> 0 with their outcome; EndOfData -> failure `label.unresolved`; no target level -> `label.no_target`. (Hand table test.)
2. R is Exact/Filled/Net by basis, default net when charged, filled when modelled, else exact; the label never depends on the basis; Net without costing -> `label.no_net_r`.
3. Minutes to resolution and the resolution instant (horizon end) are reported; the decision instant is carried.
4. A trade with legs is labelled by its last exit and initial target and flagged `HasLegs`.
5. No-leak property: changing MAE/MFE, exit price, filled R, stop/target levels or the trigger bar does not change the label.
6. A training set counts positives, negatives, unresolved and no-target, has sorted run keys and labels, a date range, rule version, and a SHA-256 id that is stable, order-free and changes with any label; duplicate key and empty result are failures.

## Contract
- **API / Data / UI:** none. No migration (see assumptions).

## Non-functional
Pure functions, no clock, no random, no I/O.

## Test plan
`TripleBarrierTests` (Domain.Tests): the tables above.

## Open questions
None.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Design docs/ADR updated
