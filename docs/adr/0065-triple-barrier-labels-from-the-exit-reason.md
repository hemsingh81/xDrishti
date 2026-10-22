# 0065 — Triple-barrier labels read from the exit reason

- **Status:** Accepted (spec [P6-03](../specs/P6-03-triple-barrier-labels.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0037](0037-simulation-conventions-and-the-two-stage-seam.md) family (`SimTrade`, `ExitReason`), ADR 0045 (result store). ADR 0064 belongs to another slice.

## Context
Doc 09 layer 3 trains the meta-model on triple-barrier labels: target, stop and time limit. The walker already settles which barrier was hit first, on minute bars.

## Decision
1. **Pure labeller over `SimTrade`** (`TripleBarrier.Label`), rule version `tb-1`. It reads the exit reason, not prices, so it cannot see more than the barrier definition: the same label whatever the path after or around the barrier (property test).
2. **Label 1 only for `Target`.** `Stop`, `SquareOff` (session end), `MaxHold`, `EmaExit` and `TimeStop` are 0, a profitable time exit included (P6-00 question 6, recommended). `EndOfData` is unresolved and left out, as statistics already do. A trade without a target level has no upper barrier and is left out and counted.
3. **R at resolution is a second target**, on a basis: net when the trade was charged, filled when modelled, else exact (doc 09 §9). The label does not depend on the basis.
4. **Legs.** A trade with legs is labelled by its last exit and its initial target, flagged `HasLegs`; whether a partial leg touched the target is not inferred. Swing time exits use `MaxHold` the same way.
5. **Horizon.** The label carries the decision instant and the exit-minute instant so that a purged walk-forward can keep train labels clear of the test window. Minutes to resolution are wall-clock minutes from the fill minute (overnight included); `SessionsHeld` counts sessions.
6. **Training-set record** is a pure `TrainingSet` (rule version, sorted run keys, date range, counts, labels, SHA-256 id) built by `TrainingSetBuilder`. A duplicate trade key or an empty result is a failure.

## Consequences
- No migration now: `research.training_sets` (insert-only for `xd_app`, with the Down) arrives with `trade_features` in P6-04, where the set has a consumer. Deviation from the P6-00 row, which listed the record in this slice.
- Changing any rule raises `RuleVersion`; old sets keep their id.
- A partial-then-stop trade that made money is label 0; its R still shows the result.
