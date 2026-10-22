# 0062 — Robustness: sensitivity, Monte Carlo, by year and a sign-flip baseline

- **Status:** Accepted (spec [P5-05](../specs/P5-05-robustness-sensitivity-monte-carlo-by-year-random-baseline.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built on 2026-10-22). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-22.
- **Relation to other ADRs:** [ADR 0059](0059-the-test-ledger-and-the-verdict-evaluator.md) (ledger, variants), [ADR 0060](0060-the-full-test-walk-forward-over-timeframe-side-and-exit.md) (the full test and `lab.lab_results`), [ADR 0058](0058-lab-runs-the-quick-test-the-run-identity-and-the-restart-rule.md) (runs).

## Context
A PASS on a few hundred out-of-sample trades says little about whether the edge survives moving a parameter, a different order of the same trades, or chance. P5-00 asks for a robustness block beside the verdict.

## Decision
1. **Method `robust`** is the full test plus a `robustness` stage (stage names validate, compile, full_test, robustness). It is opt-in because the sweep costs up to 17 plain runs. Its own run key (method and config text with the robustness keys).
2. **Additive.** The block is an optional member `robustness` of the verdict document in `lab.lab_results` (jsonb; no migration; old documents have none). The outcome and the gates are never changed by it.
3. **Sensitivity.** One-at-a-time sweep of the first up to four declared parameters at factors `StrategyLab:Robustness:SweepSteps` (default 0.8 0.9 1.1 1.2), whole numbers rounded, duplicates and the declared value dropped, at most 16 points. Each point and the base are plain runs (`RunSimulationQuery`) of the first candidate exit on the run's default timeframe, costed, in-sample; not stored in `research.sim_runs`. Judged by `SensitivityAnalysis`: stable, fragile, no_base_edge, inconclusive (every valid point keeps a positive mean R of at least half the base). A point the engine refuses is shown with its error and does not fail the run.
4. **Each point is a ledger variant.** After the last cancel check the runner appends one `lab.test_ledger` row per point, method `sweep`, with a derived variant hash (sha256 of the version hash and `name=value`) and a derived run id, so a retry writes nothing twice. They raise the family's variant count for the next run (the run itself was judged before). The ledger keeps its shape.
5. **Monte Carlo and the baseline** read the out-of-sample trades the verdict was judged on (`OosEvidenceBuilder.Trades`). A seeded SplitMix64 generator (platform independent) with the seed from the sha256 of the run key. Bootstrap: percentiles of the mean R and the drawdown, the share of non-positive means. Baseline: a **sign-flip null** (each trade keeps its size, its sign is a coin flip), p-value `(k + 1) / (draws + 1)`.
6. **By year** of entry of the same trades. **`by_regime`** is the text `unavailable: the market-context job (P3-01d) is not built`.

## Limits of the baseline and the ledger rows
- The sign-flip baseline is lenient: it tests the mean R against 0 given the trade sizes, while real random entries with a 2R target lose money before costs; and the out-of-sample trades are winners of an exit selection, so selection bias is not nulled. The contract field is `signFlipBaseline` (`SignFlipBaselineView`) and the block's limits say so. Its seed is the run seed XOR a constant so its draws do not repeat those of the Monte Carlo.
- Sweep ledger rows carry a synthetic `lab_run_id` (derived from the run and the point, not a real run): filter `method = 'sweep'` when reading the ledger by run. A retry after a crash between the sweep rows and the result can see up to 16 more variants (the rows of the first attempt count).
- A parameter with a refused or zero-trade point is `inconclusive`, never `stable`; one whose steps all vanish is `inconclusive` with `no points`.

## Deviation
P5-00 names a "random-entry baseline". The engine has no random-entry mode; adding one is an engine change. The sign-flip null answers a narrower question (is the mean R larger than chance with these trade sizes) and says so in the document. A true random-entry run is future work.

## Consequences
- OpenAPI and frontend types are regenerated (`RobustnessView` and its parts).
- Ledger counts rise by up to 16 per robust run; thresholds tighten with them (ADR 0059), by design.
- `LabRunner` takes `LabRobustness` and `ITestLedger`.

## Honest limits
- The sweep is in-sample on the whole window; no walk-forward per point; one parameter at a time.
- The bootstrap treats trades as independent; clusters of losses are understated.
- The baseline is not a random-entry run.
- About three partial calendar years: by-year figures are thin; no regime split.
- Not timed on the owner's data; a robust run over a large basket may be slow (P5-00 risk 3).
