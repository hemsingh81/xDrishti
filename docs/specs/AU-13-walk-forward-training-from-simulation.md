# AU-13 — Walk-forward training from the simulation

- **Status:** Built 2026-11-10 (ADR 0249). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-13 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** AU-11, AU-12
- **Decisions:** [ADR 0243](../adr/0243-simulate-everything-is-training-data-fenced-from-the-hold-out-and-from-scorecard-evidence.md), [ADR 0249](../adr/0249-training-from-the-simulation-reuses-the-anchored-purged-quarterly-folds-with-a-session-embargo-keeps-the-hold-out-out-whole-and-registers-a-challenger-that-cannot-be-eligible.md)
- **Design refs:** docs/design/09 §2; ADR 0067; ADR 0072; ADR 0146; ADR 0217; ADR 0220

## Goal
Learn from the whole simulation, honestly: train on the past, score on the next period, and never touch the hold-out.

## Scope
**In:** A training step at job end: labels and features through the existing `BuildTrainingSet`, the existing purged quarterly folds, cell statistics on a separate `training_sim` basis, and a model registered as a challenger only.

**Out (not in this slice):** Promotion, influence on selection (ADR 0217 and ADR 0220 unchanged), hyper-parameter search.

## Acceptance criteria
1. Folds reuse ADR 0067: quarterly, first test 9 months in, train rows purged by label horizon, plus an embargo `Training:EmbargoSessions` (0 to 20, default 5).
2. No row dated in the hold-out block enters any fold (test on the stored set).
3. Features come only from before each decision time, by the existing decision-time feature store.
4. Cell statistics are stored on a `training_sim` basis and never mixed with forward or research bases (ADR 0146 precedent).
5. A model trained here is at most a challenger; it cannot become champion by this path, and `MlMayInfluence` remains false.
6. A training that lacks enough rows answers NOT_EVALUABLE with the missing quantity named, not a weak model.
7. The same inputs give the same ids, and a second build writes nothing.
8. A summary states folds, rows, out-of-fold score beside the baseline, and that the hold-out was not read.

## Contract
- **API:** `GET /api/simulate-all/{id}/training`.
- **Data:** Reuses training-set and model tables; a `basis` value `training_sim` on cell snapshots (migration `AddTrainingSimBasis`).
- **UI:** A training card on the report screen.
- **Events/jobs:** Runs after job completion; resumable by input hash.

## Non-functional
Memory-bounded loading (the VM is 10 GiB); measured in the build.

## Safety
**SAFETY (ML).** ADR 0217 and ADR 0220 are unchanged; every influence switch stays off. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Purge and embargo boundary tests by hand-made dates; hold-out exclusion; idempotence; a pin that this path cannot designate a champion.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
