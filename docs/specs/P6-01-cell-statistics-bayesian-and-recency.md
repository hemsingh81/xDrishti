# P6-01 — Cell statistics: Bayesian and recency

- **Status:** Approved (2026-10-23, under the owner's standing run instruction of 2026-10-22: proceed without waiting; every open question taken as recommended in P6-00). Built the same day.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model. Slice P6-01 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 3 d.
- **Design refs:** docs/design/09-learning-engine.md layer 1 · 16-configuration.md (`learning.min_trades`, `recency_half_life_days`, `bayes_prior`) · 03-target-architecture.md §5 (`research.cell_stats`) · ADR 0064 · ADRs 0045 (insert-only result store), 0054 (exit choice), 0061 (`xd` verb)

## Assumptions (taken as P6-00 recommended; the owner may reverse any later)
1. **Parent group (P6-00 question 5):** a cell is shrunk toward its strategy; the strategy toward the whole system. Strength 20 (doc 16). The parent groups contain the cell's own trades (stated limit).
2. **Style and exit labels come from the caller.** A stored run carries neither a style nor an exit-policy label as a typed column; the verb names them per run (`RUNKEY:STYLE:EXIT`), as the exit choice is told its style. A run is one cell family: two runs that make the same cell are refused. Regime is `*` until P6-02.
3. **A win is R above 0** (a trade at exactly 0 is not a win). R is net of charges when the run is costed, else the filled-price R; every row says which ("net R" or "gross R").
4. **Point in time:** at an as-of date only trades that exited on or before it are read (India date of the exit). The windows are 250 and 60 calendar days ending at the as-of date (the older edge excluded).
5. **Credible level 0.90:** the confidence is the 5 % quantile of the posterior Beta.
6. **Recency weight** `0.5 ^ (days since exit / half-life)`, half-life 180 days (doc 16). It weights the win rate, averages, expectancy and profit factor; counts, drawdown and quantiles are unweighted.
7. **Snapshots are insert-only** and keyed by the SHA-256 of (as-of, knobs, sources); reads show the newest snapshot (as-of, then creation time). No `exit_choices`/regime/model fields yet.
8. **The computation is a verb, not an endpoint** (`xd learn cells`): a POST that reads thousands of stored trades is a job, and nothing in P6-01 needs the screen to trigger it. The API reads only.

## Goal
For every cell the system has trades for, a reproducible, point-in-time table of how it has done in three windows, with its win rate shrunk toward its parent group so a handful of trades cannot look like a skill, and with the evidence level stated on every row.

## Scope
**In:** pure `Domain/Learning` (`CellStatsCalculator`, `BetaDistribution`, `CellStatsOptions`, `CellId`, windows); `Application/Learning` (compute handler, list/get handlers, errors, log ids 4470-4472); port `ICellStatsStore` and its Npgsql adapter; migration `AddCellStats` (`research.cell_stats`, insert-only for `xd_app`, with Down); `GET /api/cells`, `GET /api/cells/{key}`; verb `xd learn cells`; `gen-api`; ADR 0064; docs.
**Out:** regime cells (P6-02), the `by_regime` Lab block, lifecycle/drift (P6-08/09), a screen (P8), choosing among exits (P4-06 does that), any use of the numbers to change a plan (P7), a POST endpoint, cell statistics inside the Lab run.

## Acceptance criteria
1. Beta CDF and quantile equal the closed forms (Beta(1,1), (2,1), (1,2), symmetric cases). *Tests: `BetaDistributionTests`.*
2. Hand table: three trades at weights 1, 0.5, 0.25 give effective n 1.75, posterior 1.25/1.75, expectancy 1.0, average win 1.8, average loss -1, profit factor 4.5, drawdown 1, MAE quantiles 1 and 1.8. *`CellStatsCalculatorTests`.*
3. Credible bounds of Beta(2,1) are sqrt(0.05) and sqrt(0.95). *Same.*
4. Prior: two cells of one strategy (10 wins / 10 losses, strength 10) read 0.75 and 0.25; two strategies read 0.875 and 0.125 (strategy rate 0.75 / 0.25 toward system 0.5). *Same.*
5. Windows: exits 59/60/249/250 days old fall in the 60-day, 250-day and all windows as stated (older edge excluded). *Same.*
6. **No leak:** a trade that exits after the as-of date changes nothing. *Calculator and handler tests.*
7. An empty cell reads the prior, `N = 0`, nulls and "not enough evidence"; a zero R is not a win; no loss gives no profit factor; drawdown follows exit order. *Calculator tests.*
8. The handler groups a stored run into cells by strategy, timeframe and side with the caller's style and exit, is idempotent by snapshot key, refuses bad keys/styles/exits/options/duplicate cells/unknown runs before or without writing, and logs counts only. *`ComputeCellStatsHandlerTests`.*
9. Read handlers: filters, window validation (`learning.invalid`), `learning.cell_not_found`, the view carries basis, evidence, knobs and the limits text. *`CellQueryHandlersTests`.*
10. On a real database as `xd_app`: insert and select work; update, delete, truncate fail; the migration is on the append-only list. *`ResearchGrantsTests`, `ResearchStoreMigrationTests` (updated).*
11. End to end: store a run, compute, list, get one cell (3 windows), filter, newer snapshot is shown, 404/422, anonymous is 401. *`CellStatsApiTests`.*
12. The verb parses and prints; a refusal exits 1, a bad line 2. *`LearnCellsOptionsTests`.*
13. Contract regenerated; Architecture tests green; `scripts/check.sh` green.

## Contract
- **API:** `GET /api/cells?strategy=&window=` → `CellStatsView[]` of the newest snapshot (empty before the first). `GET /api/cells/{key}` (URL-encoded `strategy|tf|side|style|exit|regime`) → the windows of that cell, or 404 `learning.cell_not_found`. Window outside `all|250d|60d` → 422 `learning.invalid`.
- **Data:** `research.cell_stats` (plain table; unique `(snapshot_key, cell_key, window_code)`; checks on key, window, `wins <= n`); migration `AddCellStats`; insert-only for `xd_app`. Down drops the table.
- **Verb:** `xd learn cells --as-of YYYY-MM-DD --run RUNKEY:STYLE:EXIT [--run ...] [--half-life-days N] [--prior-strength N] [--min-trades N]`; exit codes as the other `xd` commands.
- **UI:** none.

## Non-functional
Pure calculation, no clock or random. A few thousand trades compute in milliseconds (the Beta quantile is bisection, 200 steps, two per row). Logs carry the snapshot key (12 characters), counts and milliseconds only. No secret.

## Test plan
As the acceptance list. Hand tables above; tiny-n and empty cells; integration on a real TimescaleDB for grants and the endpoints.

## Honest limits
Replayed history, modelled fills, about 2 years, in-sample on the data shown: a description of the past, not a forecast (printed on every row). Most cells will read "not enough evidence" at `min_trades` 30. The parent groups include the cell's own trades, which pulls a cell slightly toward itself. The credible interval treats trades as independent, which overlapping or correlated trades are not. Not exercised on the owner's real stored runs in this slice.

## Open questions
None (all taken as recommended, see Assumptions).

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green (see the report)
- [x] Contract regenerated; migration added
- [x] ADR 0064, doc 09, roadmap, site rebuilt
