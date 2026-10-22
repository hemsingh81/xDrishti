# 0064 — Cell statistics: Bayesian shrinkage, recency weights and point-in-time snapshots

- **Status:** Accepted (spec [P6-01](../specs/P6-01-cell-statistics-bayesian-and-recency.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended in P6-00; built on 2026-10-23). The text of this ADR has not been read by the owner.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0045](0045-the-result-store-sim-runs-and-sim-trades.md) (insert-only result store, the pattern for grants), [ADR 0054](0054-the-per-cell-exit-choice-by-walk-forward.md) (the exit choice, whose cell identity this keeps), [ADR 0061](0061-the-xd-cli-is-a-verb-of-the-api-host-behind-a-thin-script.md) (the `xd` verb).

## Context
Doc 09 layer 1 asks for per-cell statistics (n, win rate, expectancy, profit factor, drawdown, MAE/MFE) with a Beta prior shrunk toward a parent group, a credible lower bound as the confidence, and recency weighting. Everything later in P6 (features, baselines, the model's cell-stats-only baseline, lifecycle) reads them, so the rule that a statistic at a date may not see later trades has to hold here.

## Decision
1. **Pure core in `Domain/Learning`.** `CellStatsCalculator.Compute(cells, asOf, options)` has no clock, I/O or random. Only trades whose exit date is on or before `asOf` are read. Windows: all, 250 and 60 calendar days ending at `asOf` (the older edge excluded).
2. **Weights and prior.** Weight `0.5^(days since exit / half-life)` (180). Win = R above 0. The system rate `r_sys` is the weighted win rate of all cells in the window (0.5 if none); the strategy rate is `(W_s + s r_sys)/(N_s + s)`; the cell posterior is `Beta(s r_str + W, s(1 - r_str) + N - W)` with strength `s` 20; the confidence is the lower end of the equal-tailed 90 % interval (5 % quantile, by bisection over a continued-fraction incomplete beta). Parent groups include the cell's own trades. Averages, expectancy and profit factor use the weights; counts, drawdown (running R in exit order) and MAE/MFE quantiles (type 7) do not.
3. **Cell = `strategy|tf|side|style|exit|regime`.** Strategy, timeframe and side come from the trades; style and exit are labels the caller gives per run; regime is `*` until P6-02. R is net of charges when the run is costed, else filled-price R, and each row says which. Two runs making one cell are refused.
4. **Insert-only snapshots.** `research.cell_stats` (migration `AddCellStats`, `xd_app` select and insert only, Down drops it). Rows carry the knobs and the sources text; the snapshot key is the SHA-256 of as-of, knobs and sources, so the same inputs are a no-op. Reads show the newest snapshot.
5. **Compute by verb, read by API.** `xd learn cells` calls the compute handler; `GET /api/cells` and `/api/cells/{key}` read. Every row carries its basis, evidence flag ("not enough evidence" under `min_trades`), knobs and a limits text.

## Consequences
- Cell statistics at any past date are reproducible, which P6-04 (point-in-time features) relies on.
- Most cells will read "not enough evidence" on about 2 years of data; shrinkage keeps them from looking better than they are, but the intervals still assume independent trades.
- A POST endpoint, regime cells, a screen and automatic recomputation are later slices.
- Log event ids 4470 to 4472 are used.
- Not exercised on the owner's real stored runs in this slice.
