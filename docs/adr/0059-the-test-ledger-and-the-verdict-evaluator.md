# 0059 — The test ledger and the verdict evaluator

- **Status:** Accepted (spec [P5-03](../specs/P5-03-test-ledger-and-the-gates.md), approved by the owner on 2026-10-22, questions 1 to 6 as recommended; built on 2026-10-22). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-22.
- **Relation to other ADRs:** [ADR 0058](0058-lab-runs-the-quick-test-the-run-identity-and-the-restart-rule.md) (the Lab runs the ledger counts), [ADR 0056](0056-stored-strategies-the-lab-tables-the-seed-and-loading-by-version.md) (the stored versions and their hashes), [ADR 0045](0045-the-result-store-sim-runs-and-sim-trades.md) (insert-only tables for `xd_app`).

## Context
Testing many variants of a strategy until one passes is the main way a back-test lies. The Lab must count every look at a family and make the bar higher as the count grows, and a verdict must say honestly what kind of evidence it rests on. P5-04 will produce out-of-sample numbers; this slice builds the count and the judge, and tests the judge on hand tables.

## Decision
1. **`lab.test_ledger`** (migration `AddLabTestLedger`, reversible): `id, strategy_id, version, strategy_hash, parent_hash, lab_run_id (unique), run_key, universe_label, window_from, window_to, method, created_at`; index `(strategy_id, created_at)`. `xd_app` has SELECT and INSERT only (the migration revokes the default UPDATE, DELETE and TRUNCATE and does not call the schema-wide grant; a grants test pins it). No foreign key to `lab_runs`, so the ledger outlives any change to that table.
2. **Append** (`ITestLedger`, `TestLedger`): `StartLabRunHandler` appends a row for the run it just created. It also appends for a post that returns an existing run, because the append is `ON CONFLICT (lab_run_id) DO NOTHING`; a run whose first append failed is healed by the next post. A failed or cancelled run keeps its row; starting it again is a new run and a new row (a new look). Not per retry of a job. The ledger starts at the first Lab run; earlier simulation runs are not back-filled.
3. **Family and variant (D1).** The family is the strategy id; N is `count(distinct strategy_hash)` in the family's ledger, including the one judged, so the same hash on another universe is one variant while every run stays a row.
4. **Tightening (D2).** `f = 1 + 0.25·log2(N)` (`MultipleTesting.Factor`; N below 1 is 1). Minimum trades × f (rounded up, after rounding to 9 places against floating noise), minimum expectancy × f, minimum profit factor `1 + (pf − 1)·f`. The fold share, drawdown and calibration gates are unchanged. It is a documented, tunable constant, not a statistical claim. With `StrategyLab:MultipleTesting` false the evaluator is given N = 1 (`MultipleTesting.Effective`) and the endpoint shows factor 1.
5. **Outcomes (D3)** (`VerdictEvaluator`, Domain, pure). PASS: every gate passes and no limit applies. FAIL: the trades are below half the adjusted minimum, or any other gate is missed by more than 25 % of its adjusted threshold (below `threshold − 0.25·|threshold|` for a minimum, above `threshold + 0.25·|threshold|` for a maximum), or a number is NaN. Otherwise MARGINAL. The trades gate uses the half rule only (the 25 % rule would make it redundant). A zero threshold is met by a value that is not below it and missed hard by any value below. An infinite profit factor passes; a negative one fails. Each gate returns name, configured value, adjusted value, actual, passed and a reason with the numbers (invariant culture).
6. **Honest limits (D4).** A method other than `walk_forward` caps at MARGINAL with the limit "exploration, not evidence"; fewer folds than `MinFoldsForPass` (6, until ISS-19) caps at MARGINAL with the fold count. Both can apply and are listed; a FAIL stays a FAIL.
7. **Settings** (`StrategyLab:Gates:*`, `MinFoldsForPass`, `MultipleTesting`), bound in `LabSettingsBinder`, defaults from doc 16, out of range stops the host at start.
8. **API.** `GET /api/lab/strategies/{id}/ledger` returns `{ strategyId, variantCount, adjustmentFactor, rows[] }` (rows newest first); an unknown family is 404 `lab.not_found`. No preview endpoint (no state to preview from yet). A Lab run's `verdict` stays null until P5-04 calls the evaluator.
9. **Log.** `lab.ledger.appended` (event id 4448): run id, strategy id and version only; never a strategy text.

## Consequences
- Every look is counted before its result is known, so a look that fails still raises the bar for the next one.
- The evaluator is tested without a database and is deterministic (an architecture test pins that its types touch no I/O, clock or random source).
- Nothing calls the evaluator yet; P5-04 does, with walk-forward numbers.
- The 25 % margin, the 0.25 weight and the six folds are constants a later decision may change; the settings carry the gates and the fold minimum, the weight and the margin are code constants.
