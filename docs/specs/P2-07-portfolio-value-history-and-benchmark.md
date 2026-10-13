# P2-07 — Portfolio value history, benchmark (Nifty 50) and max drawdown

- **Status:** Approved (owner, 2026-10-13: "approved as written" — all five open questions answered with the defaults) · decision [ADR 0020](../adr/0020-portfolio-value-history-benchmark-and-charts.md) (Accepted)
- **Phase / roadmap row:** P2 — "daily valuations, benchmark" (value history + benchmark XIRR/alpha + drawdown) · after P2-05 (returns), P2-06 (quotes), P1 data (daily bars, adjustments)
- **Design refs:** docs/design/12-portfolio-monitoring.md §3 (per group: benchmark XIRR, alpha, max drawdown), §4, §10 (charts: portfolio value vs the same cash flows in Nifty 50), 13-reports-and-ui.md (Portfolio screen)

## Goal
The Portfolio screen shows how the portfolio's value grew over time against what was invested and against the same money put into Nifty 50, with benchmark XIRR, alpha and max drawdown — for all accounts, one account or one bucket.

## Scope
**In:**
- Pure calculators (`Application/Portfolio/Returns`): value/invested series from lots × adjusted daily closes, benchmark series (same flows in the index), unit-NAV series, max drawdown, benchmark XIRR and alpha.
- Read API: `GET /api/portfolio/history`, extra summary fields; read ports over lots and stored 1D bars (adjusted on read); settings for the benchmark instrument (default master-list NIFTY 50).
- **Track my holdings** and **Track the benchmark** actions (existing tracking command, bulk).
- Frontend: shared lazy `TimeSeriesChart` (ECharts, ADR 0020 §5) and the Portfolio history card (range chips, series toggles, coverage line, drawdown note), tiles Benchmark XIRR / Alpha / Max drawdown.

**Out (not in this slice):** a stored `valuations_daily` snapshot table and any scheduled valuation job, grouping by basket/sector/strategy and the allocation/treemap charts (P2-08), per-holding price charts with buy/sell markers, intraday value, dividends/charges in the flows, benchmark choice UI beyond the setting, rolling returns, exports.

## Acceptance criteria
**Calculators (pure, no I/O; table-driven tests)**
1. Value on a day = Σ qty × adjusted close over lots held that day (`acquired_on ≤ d`, `closed_on` null or `> d`); invested = Σ qty × unit cost of the same lots; a lot bought and sold the same day never counts; a day with no close carries the last earlier close forward; a holding without any bar is left out and counted (`unpriced`), never valued at zero; the dates are the calendar's trading days from the scope's earliest lot (+ today's live point when given).
2. A split/bonus confirmed on an instrument changes the pre-ex-date values through the adjusted closes, so a lot's value is continuous across the ex-date (test with a 1:2 split); editing a lot's acquisition date moves where its value starts.
3. Benchmark: each flow buys/sells index units at that day's close; benchmark value = units × close; **benchmark XIRR** uses the same flows with the benchmark value as the closing inflow; **alpha** = XIRR − benchmark XIRR in percentage points; states `ok | unavailable (no bars / starts after the first flow) | insufficient | n_m` consistent with the XIRR states of ADR 0012; XIRR/benchmark XIRR agree with the reference tool values (≤ 1e-6) for ≥ 3 committed sample sets.
4. NAV series: units issued at the NAV of the buy day (start 100); NAV(d) = value(d) ÷ units(d); a pure cash-flow day does not change NAV; **max drawdown** = largest peak-to-trough fall of NAV in percent with its dates; flat and strictly rising series give 0; benchmark NAV/drawdown by the same method. Tests incl. a series with a deposit during a drawdown.
5. Totals consistency: the last history point's value/invested equals `GET /api/holdings/summary` value/invested for the same scope (when every holding is covered and priced from the same prices).

**API**
6. `GET /api/portfolio/history?accountId&bucketId&from&to&resolution=day|week|month` → points `{date, value, invested, benchmarkValue|null, nav, benchmarkNav|null}`, `coverage {holdings, covered, unpriced}`, `partial`, `benchmark {state, symbol, reason}`, `maxDrawdown {pct, from, to}`, `benchmarkMaxDrawdown`, `firstDate`, `lastPriceAt`; week/month = last trading day of the period; ≤ 2,000 points (coarser resolution is chosen/required beyond that: `400 portfolio.invalid`); unknown account/bucket → empty result like the holdings list; read-only, projections, typically about 100 ms server-side (measured about 90 ms for 300 holdings × 521 days; the test bounds are generous on purpose); session only.
7. `GET /api/holdings/summary` adds `benchmarkXirr`, `alpha`, `benchmarkState` for the same scope; unchanged for existing fields.
8. Benchmark setting: the benchmark instrument is stored in settings (default: the master-list NIFTY 50) and read by the calculators; changing it is allowed for any master-list index (settings API, audited); an unknown or non-index instrument is `400`.
9. Track actions use the existing bulk tracking command: *Track my holdings* tracks the open holdings' instruments in scope that are mapped to the master and untracked; *Track the benchmark* tracks the benchmark instrument; both are idempotent and answer how many were added; no job is started (the next *Update data* builds the bars; the response says so).

**Screen**
10. The Portfolio screen gets a **History** card under the tiles: a time-series chart with **Value**, **Invested** and **Nifty 50 (same cash flows)** lines, range chips (1M · 3M · 6M · 1Y · All, default All; resolution chosen automatically), series toggles in a legend, tooltip with date and the three values (₹ formatting), zoom, follows the scope bar (account/bucket); loading, error with Retry, empty ("No history yet") states.
11. Tiles: **Benchmark XIRR**, **Alpha** (pp, sign + icon) and **Max drawdown** (% with dates in a tooltip); "—" with the reason when unavailable.
12. A coverage line: "History covers 41 of 43 holdings" with **Track my holdings** when some have no bars, "Benchmark needs Nifty 50 history" with **Track the benchmark** when it has none, "N lots use the default date, so the history starts on 1 Jan 2025" when applicable, "Today's point uses the latest price (10:35 IST)" while the market is open; buttons disabled while running, toast with the result and the hint to run *Update data*.
13. `TimeSeriesChart` (shared/ui): lazy ECharts chunk, tree-shaken, theme tokens for colours (light/dark), no hard-coded hex, reduced-motion aware, resizes with the container, `role="img"` with an accessible summary and a visually-hidden data table (or a "Show as table" toggle) for the series, keyboard-focusable legend toggles; no layout shift while the chunk loads (fixed-height skeleton).
14. Bundle: the initial chunk stays ≤ 200 KB gzip; the ECharts chunk ≤ 170 KB gzip and loads only when the card is opened/visible; responsive 360 px → 4K (the chart takes the width, chips wrap, legend below on a phone).

## Contract
- **API:** as in 6–9; DTOs in `ApiJsonContext`; `scripts/gen-api.sh`; problem codes `portfolio.invalid`.
- **Data:** no migration for history; one settings key for the benchmark instrument (settings store — find the existing mechanism, ADR 0002; migration only if a new row type/table is unavoidable, named `AddPortfolioBenchmarkSetting`).
- **UI:** `features/portfolio` (History card, tiles, coverage line, hooks) + `shared/ui/TimeSeriesChart` and `shared/lib` chart helpers; one new pinned npm dependency (`echarts`, `save-exact`), tree-shaken imports only.
- **Events/jobs:** none (read models only).

## Non-functional
- Bars read as one query for all instruments of the scope over the range from the 1D aggregates (no per-holding or per-day queries); adjustment factors read once per instrument; memory bounded (≤ ~1M points); result cached for 60 s per scope/range key (invalidated by lot edits, syncs and corporate-action changes).
- Security: read-only; no secrets; inputs validated (dates, ids, resolution).
- Observability: a log line with scope size, days, points and duration (no figures).
- **Implementation notes (review, 2026-10-13):** (1) A request that would read more than 2,000,000 daily prices (instruments × trading days from 14 days before the earliest lot to today, estimated before anything is read) is `400 portfolio.invalid` (field `scope`); the adjusted closes can be cached per instrument if this ever grows. (2) The index has no live quote: on a live day the portfolio's point uses today's prices while the benchmark lines end at the index's last close (`benchmark.asOf`, summary `benchmarkAsOf`), and the summary's `alphaBasis` is `live_vs_last_close` when the portfolio's newest price is of a later trading day than that close, otherwise `last_close`. (3) Chart and XIRR put a cash flow into the index by one rule: at the close of the first trading day on or after its date; a lot bought and sold before a trading day comes leaves no flows. (4) Max drawdown is measured within the requested range (`rangeFrom`/`rangeTo` are its first and last day). (5) Anything that saves lots, holdings, accounts (first import date, Portfolio role, archive), quotes, the benchmark setting or corporate actions drops the cache.

## Test plan
- Domain/application unit: all calculators (see 1–5), coverage counting, resolution thinning, benchmark states, benchmark setting validation; handler tests with in-memory ports.
- Integration (TimescaleDB): history over real aggregates with a confirmed split and an edited lot date; totals equal the summary; one query per request (query-count guard); the benchmark index tracked/untracked; track actions idempotent; performance guard (generous bound).
- Frontend (Vitest + MSW): card states, range chips request the right range, toggles, coverage line variants with actions, tiles variants, chart wrapper (mock the lazy ECharts module; a11y summary/table), dark-mode token use, phone layout.
- Architecture: calculators in Application/Portfolio/Returns depend on nothing outside Domain/Application; the chart library is imported only inside the wrapper (ESLint rule).
- Visual check (light, dark, phone) against a mock API, then by the owner with real holdings.

## Open questions (owner) — answered 2026-10-13: all as written
1. **Compute on read** (no snapshot table or valuation job) as in ADR 0020 — OK?
2. **Benchmark = Nifty 50** via the same cash flows, set once in settings and fetched like any tracked instrument — OK?
3. **Holdings must be tracked** (have bars) to appear in the history; the screen offers one-click *Track my holdings* / *Track the benchmark* — or should every holding be tracked automatically at sync?
4. **Chart library:** ECharts, lazy-loaded behind one wrapper (a new dependency of about 130–170 KB gzip, not in the initial load) — OK, or prefer Lightweight Charts / a hand-drawn SVG chart for this first one?
5. **History starts at the lots' dates** (default 1 Jan 2025 for the first import until edited) — OK, with the note on the card?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration only if needed
- [ ] Design docs updated (12 §3/§4/§10, 13 Portfolio screen), ADR 0020 accepted, `echarts` pinned
- [ ] Deployed; the owner tracks holdings + benchmark, runs *Update data* and checks the chart against a spreadsheet for one account
