# P8-07 — Basket benchmarks

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-07 of [P8-00](P8-00-reports-and-ui-plan.md)
- **Design refs:** docs/design/13-reports-and-ui.md §2 (R20, R27: "vs basket buy-and-hold & Nifty") · docs/design/06-instruments-and-baskets.md §4 · ISS-22 (survivorship) · ADR 0099 · P8-00 D1, D3, D4, Q13
- **Needs:** P8-06 (built, ADR 0100: the DTO `MetricView(State, Number, Reason)`, the `from`/`to`/`basket` query names and the `RunSelection` default run set that supplies the default window), P8-05 (built). Uses existing ports only: `IBasketQueries`, `IBarQueries.ReadDailyBarsAsync`, `IPortfolioBenchmark`.
- **Size:** 3 agent-days
- **Safety:** no. One read endpoint over stored bars and basket membership; no write, no job, nothing leaves the app.

## Goal
Beside a basket's trading result the owner sees what simply holding the same basket would have done over the same dates, and what the benchmark index did, each with the number of instruments behind it and the reasons any instrument was left out, so a "good" basket result is not mistaken for a rising market.

## Scope
**In:** a pure `Domain/Reporting/BuyAndHold` (equal weight, no rebalancing, price return), an Application handler that composes membership, daily bars and the benchmark, `GET /api/reports/benchmarks`, DTOs, tests, ADR.
**Out:** dividends and total return; rebalanced or weighted baskets; a rupee amount; the screen column (P8-09 shows it); trading-result KPIs (P8-06); a corporate-action adjustment of bars (stated as a limit); the owner's portfolio benchmark chart (exists in Portfolio).

## Acceptance criteria
1. Given a basket of three included instruments with first and last daily closes A 100 to 110, B 50 to 45, C 200 to 250 over the window, when its buy-and-hold is computed, then it is the mean of the three returns, (+10 %, -10 %, +25 %) = **+8.3333 %**, shown by clients as +8.33 %, with `included` 3.
2. Given an instrument with no daily bars (`no_bars`), one whose first bar is more than `MaxStartGapDays` after the window start (`starts_late`), one whose last bar is more than that before the window end (`ends_early`), and one whose daily closes jump by more than `DiscontinuityThreshold` in one session (`discontinuity`), then each is excluded, counted, and listed with its symbol and reason; the basket figure uses the rest.
3. Given fewer than `MinMembers` (3) included instruments, then the figure is `insufficient` (`n` = included, `min` = 3) with no number; given none, `empty`; given a non-finite result (a zero first close), the instrument is excluded as `bad_price`, never producing a number.
4. Given a window, then each instrument's start is its first daily bar on or after `from` and its end its last daily bar on or before `to`; a window of one session or with start not before end is `not_evaluable` (reason stated).
5. Given the benchmark in force (`IPortfolioBenchmark.GetAsync`, the owner's choice or Nifty 50), when it has daily bars, then its price return over the same window is returned with its symbol; when it has none or the index is not in the master, `unavailable` with the reason (including where the owner can track it, as text; this endpoint tracks nothing).
6. Given overlapping baskets (one instrument in two), then each basket's figure includes the instrument and the response sets `nonAdditive` true; no total across baskets is returned.
7. Given membership, then it is the members as of an explicit date: the `membersAsOf` parameter, default the India date of the newest run that contributed trades to that basket in the report's default run set, else today; the response prints the date and says "members as of that date, not as of each trading day" (survivorship, ISS-22). A basket archived or unknown: 404 `reports.not_found`.
8. Given `GET /api/reports/benchmarks?basket=…&from=…&to=…`, then 422 `reports.invalid` for more than 12 baskets, a missing window (neither given nor derivable from the runs), `from` after `to`, or a malformed id; the window defaults to the first entry and last exit date of the default run set when `from`/`to` are absent and says so in `window.source`.
9. Every response carries an evidence block (basis `price return, dividends not included`, `n` = included instruments, range = the window, limits, `unavailable` inputs); a test fails if one lacks it. The same stored bars give identical bytes.
10. The handler reads at most `MaxInstruments` (300) instruments per basket and `MaxBaskets` (12) baskets per call; more is 422. Bars are read per instrument as a day range, one query per instrument, with the baskets of the call sharing the instruments they share (each instrument read once).

## Contract
- **API:** `GET /api/reports/benchmarks` (tag `Reports`, name `GetReportBenchmarks`), parameters `basket` (ids, comma separated, required; the built reports use the same name), `from`, `to`, `membersAsOf`. Response `ReportBenchmarksView`:
  - `Window { From, To, Source: "query"|"runs" }`, `MembersAsOf`, `Baskets: BasketBenchmarkView[]`, `Index: IndexBenchmarkView`, `NonAdditive`, `MinMembers`, `Evidence: BenchmarkEvidenceView { Basis, N, Range, Limits, UnavailableInputs }` (the built `ReportEvidenceView` describes runs and trades, which a benchmark has none of, so this slice defines its own small block with the same field meanings).
  - `BasketBenchmarkView { BasketId, BasketName, Members, Included, BuyAndHold: MetricView, Excluded: [{ Symbol, Reason }] }` — `Reason` is `no_bars|starts_late|ends_early|discontinuity|bad_price`.
  - `IndexBenchmarkView { InstrumentId?, Symbol?, IsDefault, Return: MetricView }`.
  - Problem codes: `reports.invalid` 422, `reports.not_found` 404.
- **Domain (pure):** `BuyAndHold.Compute(IReadOnlyList<InstrumentSeries>, BuyAndHoldOptions)` → `BuyAndHoldResult(Return: Metric, Included, Excluded)`, where `InstrumentSeries(Symbol, IReadOnlyList<DailyClose>)` and `DailyClose(Date, Close)`; `BuyAndHoldOptions(MinMembers, MaxStartGapDays, DiscontinuityThreshold)`. No I/O, no clock; reuses `Metric` and `MetricState`.
- **Application:** `GetReportBenchmarksQuery` / `GetReportBenchmarksHandler` (internal sealed, registered in `DependencyInjection.cs`).
- **Settings** (`Reports:Benchmark`, `[Range]`, `ValidateOnStart`): `MinMembers` 3, `MaxStartGapDays` 7, `DiscontinuityThreshold` 0.25, `MaxInstruments` 300, `MaxBaskets` 12.
- **Data / migration:** none. **UI / events / jobs:** none.

## Non-functional
p95 under 1.5 s for 6 baskets of 50 instruments on the seeded fixture (about 300 day-range reads; the range is a few hundred daily bars each); the number of bar reads is logged. Log event `report.benchmark` (baskets, instruments, excluded, ms; next free event id block at build time). No content in logs. Read only, session protected.

## Test plan
- **Unit (Domain):** the three-instrument table above; four exclusion reasons each alone and together; fewer than three included; a zero first close; window of one session; permutation of instruments gives the same figure; adding an included instrument changes only the mean (by hand: add D 10 to 12 = +20 % to the three above gives (10 - 10 + 25 + 20)/4 = +11.25 %).
- **Application (fakes):** membership as of a date comes from `MembersAsync(basketId, null, asOf)`; an instrument in two baskets is read once (count the fake's calls); benchmark with and without bars; archived basket 404; window derived from runs.
- **Integration (Testcontainers):** seed bars for the three instruments, a basket with a member removed after the as-of date (still counted), a benchmark index; assert the response and the evidence block; 401 without a session; contract snapshot.
- **Properties:** the figure is a number if and only if the state is value; never NaN.
- **Architecture:** `Domain/Reporting` pure; no order-named member; no new Infrastructure type.

## Decisions (researched)
1. **Equal weight, no rebalancing, price return.** Question: what does "buy and hold the basket" mean? Chosen: buy every included member at the first close and hold: the basket return is the mean of the members' price returns. Reason: it is the simplest benchmark a person can check by hand and the prototype's column ("Buy & hold") is exactly this; dividends are not stored with bars, so total return would be invented. Rejected: capitalisation weights (no market cap data); daily rebalancing (a different strategy). Source: prototype `app-pages-3.js` basket comparison, doc 13 §1c.
2. **Membership as of one date, said on screen.** Chosen: the date of the newest contributing run (else today), because the trades of a basket run use the members of that run (P8-00 Q13). The honest limit: members as of that date, not each trading day, so survivorship stays (ISS-22), and it is printed.
3. **Exclude, count and name; never repair.** An instrument that starts late, ends early, has no bars or a one-session jump is excluded with the reason. Chosen discontinuity threshold 0.25 (setting): NSE equity price bands are 2, 5, 10 or 20 percent and stocks in F&O have no fixed band (a dummy 10 % operating range), so a larger one-day close-to-close move is nearly always an unadjusted split or bonus; UNVERIFIED for a genuine event on a no-band stock, which this rule would wrongly exclude (named in the limits). Source: [Business Standard, circuit filters](https://www.business-standard.com/article/markets/sebi-expands-lists-of-stocks-with-circuit-filters-114020601214_1.html) and [Chittorgarh NSE circuit filters](https://www.chittorgarh.com/newportal/stock-nse-circuit-filters.asp), read 2026-10-24 (secondary sources, dated; the exact current bands are an NSE circular, UNVERIFIED). Rejected: adjusting prices (the corporate-action machinery is the data module's, not reports'); including everything (a fake -50 % hides in a mean).
4. **At least 3 included instruments for a number.** A mean of one or two stocks is not a basket benchmark; below it the state is `insufficient` with n. Rejected: showing it with a caveat (a number with n 1 invites a read).
5. **No composite and no "excess" figure.** Two numbers side by side (basket hold, index); the screen does not subtract them (Q12 spirit; avoids a third unexplained figure).
6. **Benchmark index from the owner's portfolio setting**, not a new setting: one benchmark across the app (`IPortfolioBenchmark`, ADR 0020 §2). Rejected: a Reports-only benchmark choice.
7. **No new port or table.** Everything needed exists; adding a port for a composition would only hide it.

## Risks and honest limits
- Survivorship: members as of one date, not as of the dates replayed; a basket built from today's winners shows an inflated hold (ISS-22).
- Daily bars are as stored; an unadjusted corporate action below the threshold, or a genuine large move above it, distorts one instrument (listed in the limits).
- The hold return and the trading result cover the same window but are different things (one position per signal versus a hold); the screen words must not call the difference "alpha" or "outperformance".
- The index return ignores dividends, like the basket.
- On a basket of a few instruments the figure is `insufficient`; that is the expected result for small baskets.

## Open questions
None. The default benchmark is Nifty 50 until the owner picks another in Portfolio.

## Done when
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors
- [ ] `scripts/gen-api.sh` run; no migration
- [ ] ADR (next free number at build time), roadmap row P8-07, `scripts/build-docs.sh`

## Review fixes 2026-10-25
- Aligned to the BUILT P8-06 (ADR 0100): `basket` not `baskets`; `MetricView` reused; the evidence block is a benchmark-specific `BenchmarkEvidenceView` because `ReportEvidenceView` is run/trade shaped; the default window comes from `IReportTradeSource.ListRunsAsync` + the built default run set.
- No dependency on P8-02b (no frontend).
