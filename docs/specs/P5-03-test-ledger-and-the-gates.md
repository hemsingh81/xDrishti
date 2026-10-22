# P5-03 — Test ledger and the gates

- **Status:** Built (2026-10-22; approved by the owner the same day, "approve all": the six questions stand as recommended). Depends on [P5-02](P5-02-lab-runs-job-run-record-and-quick-test.md) (built).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 3 of [P5-00](P5-00-strategy-lab-plan.md) (D3, D4; phase criteria 4 and 5).
- **Design refs:** docs/design/11-strategy-lab.md §4 (verdict: PASS / MARGINAL / FAIL, each gate ✓/✗), §5 (ledger; thresholds tighten as variants grow; only walk-forward OOS can PASS), §6 · 16-configuration.md `qualification_gates`, `strategy_lab.multiple_testing_adjustment` · ADR 0058 (run identity).

## Goal
Every Lab run of a strategy family is counted in a per-family test ledger, and a pure evaluator turns a set of out-of-sample numbers plus the family's variant count into PASS / MARGINAL / FAIL with one reason per gate. Nothing here produces the numbers (walk-forward is P5-04); the evaluator is tested on hand tables.

## Scope
**In:**
1. `lab.test_ledger` (insert-only for `xd_app`) and migration `AddLabTestLedger`; `ITestLedger` port (append, count by family, list by family).
2. A ledger row is appended when a Lab run is created (not per retry): family, strategy hash, parent hash, run id, run key, universe label, window, method, created_at. Idempotent by run id.
3. `VerdictEvaluator` in Domain (pure: no clock, no I/O): inputs `QualificationGates`, `OosEvidence` (oos trades, expectancy R, profit factor, folds, positive folds, max drawdown R, calibration error pp, method), variant count; output `Verdict { Outcome, Gates[ { name, required, adjusted, actual, passed, reason } ], VariantCount, Limits[] }`.
4. The multiple-testing adjustment (D2) when `multiple_testing_adjustment` is true; off means variant count 1.
5. The honest-limit rules: method other than `walk_forward` cannot PASS (label exploration); fewer folds than `min_folds_for_pass` caps at MARGINAL.
6. `GET /api/lab/strategies/{id}/ledger` (rows, count, the adjustment factor now in force) and `POST /api/lab/verdict/preview` is **not** added (no state to preview from yet).
7. Settings `StrategyLab:Gates` (defaults = doc 16), `MinFoldsForPass` (6), `MultipleTesting`.

**Out:** producing OOS numbers (P5-04), storing results (`lab.lab_results`, P5-04), robustness (P5-05), the report (P5-06), any page (P5-08b). The Lab run's `verdict` stays `null` until P5-04 calls the evaluator.

**Size.** About 2.5 agent-days (P5-00). Cut order: the ledger read endpoint, then `MinFoldsForPass` setting (constant); never the append or the OOS-only rule.

## Decisions
- **D1. Family and variant.** The family is the strategy id; a variant is a distinct strategy hash within it, so re-running the same hash on another universe is one variant, not two. Count = distinct hashes in the ledger for the family (including the one being judged). Ledger rows still record every run.
- **D2. Tightening rule (recommended).** With N variants, factor `f = 1 + 0.25·log2(N)` (N=1 → 1, 4 → 1.5, 16 → 2). `min_oos_trades × f` (rounded up); `min_expectancy_R × f`; `min_profit_factor` becomes `1 + (pf − 1)·f`; `min_positive_folds_pct`, `max_drawdown_R`, `max_calibration_error_pp` unchanged. The adjusted value is printed beside the configured one.
- **D3. Outcomes.** PASS: every gate passes and no limit applies. FAIL: any hard gate fails by more than 25 % of its adjusted threshold, or `oos_trades` < half the adjusted minimum. Otherwise MARGINAL. Every gate carries a reason string with numbers.
- **D4. Honest limits.** Not `walk_forward` → at most MARGINAL and the limit text "exploration, not evidence". Folds < `MinFoldsForPass` → at most MARGINAL with the fold count. Both can apply.
- **D5. Ledger starts at the first Lab run;** P3/P4 runs are not back-filled (P5-00 Q6).

## Acceptance criteria
1. Migration applies on a clean database; `xd_app` can INSERT and SELECT on `lab.test_ledger`, never UPDATE or DELETE (grants test).
2. Starting a Lab run appends one ledger row; the same post twice (idempotent run) leaves one row; a failed or cancelled run's row stays.
3. Variant count counts distinct hashes of a family; the same hash on two universes counts once.
4. Adjustment factor matches D2 for N = 1, 2, 4, 16 (hand table); with `multiple_testing_adjustment` false the factor is 1.
5. Evaluator hand tables: all gates met → PASS; one hard gate missed slightly → MARGINAL; missed by > 25 % or trades < half → FAIL; every gate has a reason with actual and adjusted values.
6. Same evidence passes at N = 1 and fails (MARGINAL or FAIL) at N = 16 (tightening is visible).
7. `full_period` evidence never PASSes; 5 folds with otherwise passing numbers gives MARGINAL with the limit text; 6 folds can PASS.
8. The evaluator is deterministic and reads no clock (property test: same inputs, same output; reflection/architecture test that Domain has no I/O dependency).
9. `GET /api/lab/strategies/{id}/ledger` returns rows, variant count and factor; unknown id → 404; OpenAPI regenerated.
10. No strategy text in a log line; architecture tests green.

## Contract
- **API:** `GET /api/lab/strategies/{id}/ledger` → `{ strategyId, variantCount, adjustmentFactor, rows: [ { runId, runKey, strategyHash, parentHash, version, universe, window, method, createdAt } ] }`.
- **Data:** `lab.test_ledger (id, strategy_id, version, strategy_hash, parent_hash, lab_run_id unique, run_key, universe_label, window_from, window_to, method, created_at)`; index `(strategy_id, created_at)`.
- **Events/jobs:** log event ids from the Lab range, `lab.ledger.appended`.

## Test plan
Unit: factor table, each gate, outcome mapping, limits, determinism. Integration: migration, grants, append on start, idempotency, family count, endpoint. Edge: N = 0 treated as 1; zero trades; NaN/negative profit factor; gates config with a zero threshold.

## Open questions (recommendation first)
1. **Tightening formula** D2 (`1 + 0.25·log2 N`)? Recommended: yes; it is a documented, tunable constant, not a statistical claim.
2. **FAIL margin** of 25 %? Recommended: yes.
3. **`MinFoldsForPass` = 6** (P5-00 Q3)? Recommended: yes until ISS-19 is resolved.
4. **Variant = distinct hash** (D1) rather than every run? Recommended: yes.
5. **Ledger append at run creation** rather than at success? Recommended: yes (failed attempts still count as looks).
6. **Ledger starts at the first Lab run** (D5)? Recommended: yes.

## Done when
- [ ] Questions answered, status Approved
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors
- [ ] `scripts/gen-api.sh`; migration with `Down`; ADR 0059; docs 11 §5/§6, 16, roadmap updated; `scripts/build-docs.sh`
