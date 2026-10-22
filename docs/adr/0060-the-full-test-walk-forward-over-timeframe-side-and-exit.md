# 0060 — The full test: walk-forward over timeframe, side and exit, and the verdict

- **Status:** Accepted (spec [P5-04](../specs/P5-04-full-test-walk-forward-over-timeframe-side-exit.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built on 2026-10-22). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-22.
- **Relation to other ADRs:** [ADR 0053](0053-the-same-entries-comparison-and-the-mae-mfe-diagnostics.md) (the comparison), [ADR 0054](0054-the-per-cell-exit-choice-by-walk-forward.md) (the per-cell choice), [ADR 0058](0058-lab-runs-the-quick-test-the-run-identity-and-the-restart-rule.md) (runs), [ADR 0059](0059-the-test-ledger-and-the-verdict-evaluator.md) (the evaluator and the ledger), [ADR 0045](0045-the-result-store-sim-runs-and-sim-trades.md).

## Context
The quick test is exploration. A Lab run needs a method whose numbers can pass: walk-forward, out of sample, costed, judged by the evaluator of P5-03 with the family's variant count.

## Decision
1. **Method `full`** in `POST /api/lab/runs` (own run key: the method and a config text with the candidate exits and the selection options). The window is all stored data unless posted; the universe is the whole resolved list (no sample). Costs are mandatory: without a stored, in-force charge sheet the post is 422 `lab.invalid_input` (and the runner fails the run `lab.costs_required` if the sheet vanished meanwhile).
2. **Cells.** For each timeframe the strategy declares, `RunComparisonQuery` runs the candidate exits on the same entries (`StrategyLab:FullExits`, `--exit` flags, default `FIXED_RR:rr=2|1.5|2.5|3`, the first is the default exit; the strategy's own exit is not a candidate). The trades of each side are split off, so a cell is timeframe x side, and `ExitSelector.Select` (default options) chooses the exit per cell. Nothing chooses among cells: all are judged.
3. **Evidence** (`OosEvidenceBuilder`, Domain, pure). Only trades of a fold's winner that entered in that fold's test window count. Pooled folds are the test windows by date; positive when the pooled R is above 0; drawdown is the largest peak-to-trough of cumulative R in exit order; profit factor with no loss is infinite (served as null, the gate reason says it). Method `walk_forward`. Full-period (in-sample) figures per cell and candidate are served under the label "exploration: in-sample on the data shown, not evidence" and never reach the evaluator.
4. **Verdict.** `VerdictEvaluator.Evaluate` with the gates and `MinFoldsForPass` of the settings and the ledger's variant count of the family through `MultipleTesting.Effective`. The calibration-error gate is not applicable to a rules strategy: it is given 0 and the verdict says so in its limits. A run in which no cell has a choice has 0 out-of-sample trades and FAILs, with the reasons.
5. **Storage.** Each candidate run is stored whole in `research.sim_runs` (actor `lab:full`; keys in `sim_run_keys`) after the last cancel check; then `lab.lab_results` (migration `AddLabResults`, reversible, insert-only for `xd_app`, one row per run, foreign key to `lab_runs`) takes the verdict document, then the run ends `succeeded`. `GET /api/lab/runs/{id}` serves `verdict` for a succeeded full run; it is null for a quick run, a list item, or an unfinished run. The comparison and selection records are not stored separately (the cell summary is in the verdict).
6. **Stages and progress.** validate, compile, `full_test`; progress counts instruments done over instruments x timeframes (`RunComparisonQuery` gained the optional `Progress`). Log `lab.result.stored` (4449).

## Consequences
- OpenAPI and frontend types are regenerated (`LabVerdictView` and its parts).
- The existing tests that deleted `lab.lab_runs` now delete `lab.lab_results` first (foreign key).
- A full test of a many-instrument basket is heavy (instruments x candidates x timeframes in memory per timeframe); not timed on the owner's data (P5-00 risk 3).

## Honest limits
- About 2.1 years and no hold-out (ISS-19): with 9 months of minimum training and 3-month test windows there are about 4 folds, fewer than `MinFoldsForPass` (6), so the verdict is capped at MARGINAL whatever the numbers; the limit is printed.
- Pooling the cells gives one verdict; a cell that loses is hidden inside a pool that wins. The cells are served beside it.
- The candidate list is fixed and small; the exit of the strategy as written is not tested.
- Calibration is not tested for lack of a model.
