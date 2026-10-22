# P5-04 — Full test: walk-forward over timeframe, side and exit

- **Status:** Approved (built 2026-10-22, ADR 0060) (owner's standing run instruction of 2026-10-22, "proceed without waiting": every open question below is taken as recommended). Depends on [P5-02](P5-02-lab-runs-job-run-record-and-quick-test.md) (built, ADR 0058) and [P5-03](P5-03-test-ledger-and-the-gates.md) (built, ADR 0059); uses the comparison of [P4-05](P4-05-same-entries-comparison-and-mae-mfe-diagnostics.md) (ADR 0053) and the per-cell choice of [P4-06](P4-06-per-cell-exit-choice-walk-forward.md) (ADR 0054).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 4 of [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §3 (full test), §4 (verdict), §5 (ledger), §6 (`lab.lab_results`) · 16-configuration.md `strategy_lab`, `qualification_gates`, `exit_walk_forward` · ADRs 0053, 0054, 0058, 0059.

## Assumptions (taken as the recommended answers; none is an owner-only decision)
1. **A cell is a timeframe and a side.** The strategy is run once per declared timeframe as a same-entries comparison of the candidate exits; the trades of each side are separated afterwards, so side needs no engine change. The exit is chosen **per cell** by the P4-06 walk-forward. Timeframe and side are not themselves chosen among: every declared cell is judged, and the verdict pools them.
2. **The exit candidates are a Lab setting,** `StrategyLab:FullExits` (flags of the verb, default `FIXED_RR:rr=2|1.5|2.5|3`; the first is the default exit). The strategy's own exit is **not** a candidate: the comparison replaces the exits of the strategy, and the report says so.
3. **The verdict is the evaluator of P5-03 on the pooled out-of-sample trades** of the walk-forward choice of every cell (trades of the winner of each fold that entered in that fold's test window, the numbers of the P4-06 pooled figure). A pooled fold is a test window (same dates); it is positive when the pooled R of its trades is above 0. The method is `walk_forward`; `full_period` figures are printed per cell and candidate under the label "exploration, in-sample" and never reach the evaluator.
4. **Calibration error is not applicable** to a rules strategy (no predicted probabilities, doc 09). The gate is given 0 and the verdict carries the limit "calibration gate not applicable: no probability model". Max drawdown is the largest peak-to-trough fall of the cumulative R of the pooled out-of-sample trades in exit order.
5. **Costs are mandatory.** A full test is refused (422 `lab.invalid_input`) when the charge sheet of the settings is not stored or not in force on the first day; a gross verdict would be a verdict on numbers no one trades.
6. **Window and universe.** All stored data (first to last stored session) unless posted; the whole universe, no sample (the quick-test cap of 30 does not apply). How long that takes on the owner's data is not measured (P5-00 risk 3).
7. **Storage.** Each candidate run of each timeframe (the stored runs are 4 candidates x the declared timeframes) is stored whole in `research.sim_runs` as the quick test does (keys in `sim_run_keys`); the comparison and the selection records are not stored separately, their summary is in `lab.lab_results`.
8. **Variants.** The ledger count of the family at the end of the run (this run's row exists since the post) feeds the evaluator through `MultipleTesting.Effective`.

## Goal
`POST /api/lab/runs` with method `full` runs the walk-forward over the whole stored window, picks the exit of each timeframe/side cell by out-of-sample risk-adjusted expectancy, and ends with a PASS / MARGINAL / FAIL verdict from the P5-03 evaluator, with a reason per gate, the variant count, the honest limits and the folds shown. Only out-of-sample numbers can PASS.

## Scope
**In:** method `full` in `LabRunner` (stages validate, compile, `full_test`, `verdict`); `LabFullTest` service; pure `OosEvidenceBuilder` (Domain); `lab.lab_results` (migration `AddLabResults`, insert-only for `xd_app`) and `ILabResultStore`; the verdict and the cells in `GET /api/lab/runs/{id}`; settings `FullExits`; progress for the full stage; labels; ADR 0060; docs.
**Out:** robustness, sensitivity, Monte Carlo (P5-05); report page/HTML (P5-06); the page (P5-08b); applying the choice to a stored strategy; regimes; hold-out (ISS-19, P5-00 Q4: the full test uses all data and says "no hold-out").

## Acceptance criteria
1. Given method `full`, then the run id is queued with the key of a different method (same input with `quick` is another run); the config text carries the exits and the selection options.
2. Given no stored charge sheet, then the post is 422 `lab.invalid_input` (assumption 5).
3. Given a fixture with enough history, then the run succeeds and `GET` returns a verdict with six gates, `variantCount`, `adjustmentFactor`, limits, the cells with their folds and the `fullPeriod` block labelled exploration.
4. Evidence is built from the winners' test-window trades only: a hand-made selection gives the exact trades, expectancy, profit factor, drawdown, fold and positive-fold counts (unit table).
5. A cell with no choice contributes no trades; a run in which no cell has a choice gets 0 OOS trades and FAIL, with the reason.
6. The same data and settings give the same result (deterministic); a run that is cancelled stores no result and no `sim_runs` row.
7. `full_period` figures never reach the evaluator (a test: the evidence is identical when full-period numbers change).
8. `lab.lab_results`: migration up and down; `xd_app` can INSERT and SELECT, never UPDATE or DELETE (grants test); one row per run (unique).
9. Quick runs are unchanged (their tests stay green); `verdict` is null for them.
10. The architecture tests stay green; no strategy text, trade or secret in a log line.

## Contract
- **API:** `POST /api/lab/runs` accepts `method: "full"`. `GET /api/lab/runs/{id}` returns `verdict`: `{ outcome, variantCount, adjustmentFactor, evidence, gates[], limits[], cells[], fullPeriod[], labels[] }` or null (quick, not finished). OpenAPI regenerated.
- **Data:** `lab.lab_results (id, lab_run_id unique fk, strategy_id, strategy_hash, method, outcome, variant_count, adjustment_factor, result jsonb (the verdict document), created_at)`. Migration `AddLabResults`.
- **Events/jobs:** same dispatcher; stage names `full_test`, `verdict`; log `lab.result.stored` (4449).

## Non-functional
Deterministic. Memory: the comparison of one timeframe is in memory at a time (all instruments x candidates). Progress: instruments done of instruments x timeframes. No network.

## Test plan
Unit (Domain): `OosEvidenceBuilder` tables (pooling, folds, drawdown, no-choice, infinite profit factor). Unit (Application): `LabFullTest` with fake handlers (cells split by side, evidence reaches the evaluator, variant count used); settings config text. Integration: post full, refusal without a sheet, run to the end on the invented fixture (no folds, FAIL with reason), result row, view, grants, migration down. Architecture: unchanged rules.

## Open questions (all taken as recommended)
1. Pool the cells into one verdict, or one verdict per cell? Recommended: one pooled verdict, the cells shown. 2. Candidate grid as a setting? Recommended: yes. 3. Refuse a gross full test? Recommended: yes. 4. Store candidate runs? Recommended: yes. 5. Calibration gate not applicable? Recommended: yes, said in the verdict.

## Done when
- [x] Questions answered, status Approved
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green
- [ ] `scripts/gen-api.sh`; migration with `Down`; ADR 0060; docs 11, 16, roadmap; `scripts/build-docs.sh`
