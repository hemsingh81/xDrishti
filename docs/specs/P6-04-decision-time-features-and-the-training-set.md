# P6-04 — Decision-time features and the training set

- **Status:** Approved (2026-10-23, under the owner's standing run instruction of 2026-10-22: proceed without waiting; every open question taken as recommended in P6-00). Built the same day.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model. Slice P6-04 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 3 d.
- **Design refs:** docs/design/09-learning-engine.md §3 (decision-time snapshot), §4 (labels), §9 · 05-data-and-brokers.md §9 (`market.context_daily`) · 10-trade-selection.md · ADRs 0064 (cell statistics, point in time), 0065 (labels), 0066 (this slice), 0045 (insert-only result store)

## Assumptions (taken as P6-00 recommended; the owner may reverse any later)
1. **D2 of P6-00:** features are a side table `research.trade_features`, keyed by (run key, trade key, catalog version). `SimTrade` stays at schema 5 and `EntrySnapshotJson` stays null. No change to the walker, the oracle or any golden file.
2. **The catalog is `tf-1`, 41 features, not the "56 names" of the plan.** The 56 are the indicator catalog (names with parameters); a model needs a fixed, scale-free vector. `tf-1` picks 23 bar/session/calendar features at the trade's own timeframe (RSI, ATR%, ADX, DI, EMA distances, MACD histogram%, Bollinger width and position, ROC, relative volume, VWAP distance, 20-bar range position, Supertrend direction, gap, previous-close distance, day-range position, previous-day range, day-open distance, bar range and body, weekday), 9 market-context features, 4 plan features (minute of day, side sign, stop distance %, planned reward to risk) and 5 cell-statistics features (n, posterior win rate, lower bound, expectancy, enough). Prices enter only as ratios, so a corporate action or a price level changes nothing. The list is pinned by a test; a change raises the version.
3. **Context features are unknown until P3-01d.** `market.context_daily` is not loaded by any store yet, so the 9 context features (including `is_expiry_day`, which needs the expiry dates, and the regime) are NaN, and are not required by default. The extractor takes a `MarketContext` and reads them when one is given; the adapter passes none. This is stated on every training set (the required list is in the record).
4. **The rule for unknowns:** a labelled trade with no stored feature row, or with a *required* feature NaN, is dropped and counted (per feature). Nothing is filled or imputed. Default required = every bar, plan and cell feature except the cell expectancy (unknown for an empty cell). The first sessions of an instrument drop out while the indicators warm up (ADX needs 28 bars of the trade's timeframe, the previous-day fields need one earlier session).
5. **Cell features are computed at build time, not stored:** they depend on which runs feed the cells (the one-run-per-strategy rule of P6-01). For each decision date the pure calculator is run with `as_of = decision date - 1 day`, so only trades that exited strictly before the decision day are read; the trade itself can never be in its own cell features. The cell is the P6-01 cell: strategy, timeframe, side, plus the style and exit label the caller gives per run; regime `*` until P6-02. Prior strength and the other knobs are the doc 16 defaults, overridable, and recorded in the set.
6. **The regime as a feature** comes from the context (assumption 3), not from a `RegimeTagger` (P6-02 stays a separate slice). The P6-02 row said "regime tag on the features side table, decided by P6-04": decided here that `regime` is a column of the catalog.
7. **The training-set record moves here from P6-03:** `research.training_sets`, insert-only, one row per set id; the *rows* are not stored (they are rebuilt from the stored runs and features; the SHA-256 id proves it). `SessionsHeld` and the horizon instants ride on every row; ADR 0065 asks for `SessionsHeld` over minutes for swing.
8. **Bars fed to the frame:** every stored 1-minute bar of the instrument from its first one, adjusted by the applied corporate actions as the replay does, but without the replay's session classification (sessions it left out are fed). A warm-up can differ slightly from what the scanner saw. Stated limit.
9. Computation by verbs (`xd learn features`, `xd learn training-set`), no endpoint (a job that reads thousands of rows and bars is not a request, as for P6-01). No API change, so no `gen-api`.
11. **Stored rows are never refreshed:** `ON CONFLICT DO NOTHING`; a catalog version bump is the only refresh. A stored row whose decision instant differs from the trade's is dropped and counted as without features.
12. **Plan features after a later corporate action:** `risk_pct` and `planned_rr` mix the adjusted close with the run's stop and target, so they are exact only while no action was applied since the run (stated limit).
13. **The set id also hashes the sources string and the cell-statistics options.**
10. ADR number 0066 (0067 belongs to another slice); log ids 4480 to 4485.

## Goal
For every closed simulated trade, the numbers a model could have known at the minute the entry was decided, stored once and reproducibly; and a training set that joins them to the triple-barrier label, with a stated, counted rule for what is left out.

## Scope
**In:** pure `Domain/Learning` (`TradeFeatureCatalog`, `TradeFeatureDecision`, `TradeFeatureRow`, `PointInTimeCells`, `TrainingRow`, `TrainingData`, `TrainingDataBuilder`); the extractor `Simulation/Scanning/TradeFeatureExtractor` over the feature frame; Application use cases `ExtractTradeFeaturesHandler` and `BuildTrainingSetHandler` (shared `CellSourceReader`, refactored out of the P6-01 handler), ports `ITradeFeatureExtractor`, `ITradeFeatureStore`, `ITrainingSetStore`; Infrastructure adapter and stores; migration `AddTradeFeaturesAndTrainingSets` (both tables insert-only for `xd_app`, with Down); verbs `xd learn features` and `xd learn training-set`; ADR 0066; docs.
**Out:** the regime tagger (P6-02), loading the daily context (P3-01d), baselines and folds (P6-05), any model, an endpoint or screen, storing the training rows, feature selection or scaling.

## Acceptance criteria
1. The catalog `tf-1` is pinned by name and position; names are unique; stored features come before cell features; no name is an outcome (exit, result, R, MAE/MFE, label). *`TradeFeatureCatalogTests`.*
2. **No leak (property):** for seeded weeks of bars and decisions on three days and two sides, the features equal those computed with the bars cut at the decision instant, and equal those computed with every bar from the decision on replaced by extreme values; a control that changes a bar before the decision does change them. *`TradeFeatureExtractorTests`, 10 seeds.*
3. Plan features follow the decision: weekday, minute of day, side sign, stop distance %, planned reward to risk (hand values from the bars); no target gives NaN reward to risk. *Same.*
4. A decision late in the week has every default-required bar and plan feature known; the first session has the previous-day features unknown; the context features are NaN without a context; a daily decision does not throw and leaves the intraday-only features NaN; one trade's features do not depend on the other decisions given with it; a split after the decision day leaves ratio features unchanged within rounding. *Same.*
5. **Point in time:** a trade that exited the day before the decision counts, one that exits on the decision day does not; adding or changing trades that exit on or after the decision date changes nothing; an empty cell reads n 0, the prior and an unknown expectancy; `enough` turns on at `min_trades`. *`PointInTimeCellsTests`; also through the handler: the n of four consecutive trades is 0, 1, 2, 3.*
6. The join: row = whole catalog; a trade without features or with a required feature unknown is dropped and counted (per feature); an optional unknown is kept as NaN; an unknown name, a stored row of the wrong length, a duplicate key and an empty result are failures; an infinite required value counts as unknown; the id is stable, order-free and changes with any value, the required set, the sources or the cell options. *`TrainingDataBuilderTests`.*
7. Extraction use case: the extractor is told only (key, timeframe, decision instant, side, stop, target); rows are stored; instruments are read once each; no bars is counted and gets no row; bad key refused before any read; unknown run not found. *`ExtractTradeFeaturesHandlerTests`.*
8. Build use case: labels and unresolved/no-target counts, drops, idempotent second build, refusals (bad names, run, empty) before or without writing. *`BuildTrainingSetHandlerTests`.*
9. On a real database: features round-trip with NaN and a second write adds nothing; the training set is built from a stored run and its record written once; as `xd_app` insert works, update/delete/truncate fail on both tables; the migration is on the append-only list and the table/index/constraint lists are updated. *`TrainingSetStoreTests`, `ResearchGrantsTests`, `ResearchStoreMigrationTests`.*
10. The two verbs parse and refuse bad lines. *`LearnFeaturesOptionsTests`.* Architecture tests green; `scripts/check.sh` green.

## Contract
- **API:** none.
- **Data:** `research.trade_features` (unique `(run_key, trade_key, feature_version)`, `feature_values double precision[]`, NaN = unknown; check on key shape and a non-empty array), `research.training_sets` (unique `set_id`, counts, knobs, sources, required list; check `positives + negatives = row_count`). Migration `AddTradeFeaturesAndTrainingSets`, Down drops both.
- **Verbs:** `xd learn features --run RUNKEY [--run ...]`; `xd learn training-set --run RUNKEY:STYLE:EXIT [--run ...] [--require NAME,...] [--half-life-days N] [--prior-strength N] [--min-trades N]`. Exit codes as the other `xd` commands.
- **Events:** log ids 4480 to 4485 (counts, 12-character keys, codes, milliseconds; no price, no trade).

## Non-functional
Extraction is one pass over an instrument's bars per timeframe present (the loop stops at the last decision), no allocation per step beyond the frame's. The cell features cost one calculator run per distinct decision date (milliseconds each). Pure functions in Domain and Simulation: no clock, no random.

## Test plan
As the criteria. Seeded property over bars; hand tables for the join and the point-in-time rule; real database for the SQL and the grants.

## Honest limits
Replayed history, modelled fills, about 2 years, in-sample on the data shown. The set is biased toward fixed reward-to-risk strategies (trades without a target are left out, counted in the record). Context and regime features are unknown until the daily context is loaded, so a model trained now sees no regime. Warm-up drops the first sessions of each instrument. The cell features' parent groups include the cell's own earlier trades (stated in ADR 0064) and treat trades as independent. The bars fed differ from the scanner's by the sessions the replay left out. Not exercised on the owner's real stored runs and bars in this slice (the end-to-end path is covered with fabricated trades and synthetic bars; the extractor with real bars is untested).

## Open questions
None (all taken as recommended, see Assumptions).

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green (see the report)
- [x] Migration added (no API change, so no contract change)
- [x] Design docs/ADR updated
