# 0102 — Basket benchmarks are an equal-weight price-return hold over members as of one stated date, with every exclusion named

- **Status:** Proposed (spec [P8-07](../specs/P8-07-basket-benchmarks.md), owner approved the plans; built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** [0099](0099-the-report-engine-is-a-pure-core-with-one-population-rule-six-honest-states-and-a-ranking-by-the-credible-lower-bound.md) (`Metric`, six states), [0100](0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md) (the report endpoints, `RunSelection`, `MetricView`), [0020](0020-portfolio-value-history-benchmark-and-charts.md) section 2 (the benchmark in force).

## Context
A basket's trading result is easily mistaken for a rising market. The owner needs, beside it, what simply holding the same basket did over the same dates and what the benchmark index did.

## Decision
1. **Pure rule.** `Domain/Reporting/BuyAndHold.Compute(series, from, to, options)`: each included instrument is bought at its first daily close on or after the window start and held to its last close on or before the end; the figure is the mean of the price returns, in percent (equal weight, no rebalancing, no dividends). No I/O, no clock. The window is a parameter of `Compute` (the spec listed only the options) because the start and end rules need it.
2. **Exclude, name, never repair.** `no_bars`, `too_few_bars` (fewer than two daily bars), `starts_late` / `ends_early` (more than `MaxStartGapDays`, 7, from the window end), `discontinuity` (a one-session close-to-close move above 0.25, almost always an unadjusted split or bonus; UNVERIFIED for a genuine event on a stock without a price band, which it wrongly excludes) and `bad_price` (a close that is not positive and finite). Fewer than `MinMembers` (3) included gives `insufficient`, none `empty`, a window that does not start before it ends `not_evaluable`; a number exists only in state `value`.
3. **Membership as of one printed date.** `membersAsOf`, default the India date of the newest run, in the default run set and with closed trades, whose universe is one of the asked baskets, else today. It is members as of that date, not as of each trading day (survivorship, ISS-22). An archived, unknown or portfolio (system) basket is 404 `reports.not_found`, because only `IBasketQueries` is used.
4. **Window.** Each bound says where it came from (`fromSource`, `toSource`; `source` is `query` only when both were given). `from` and `to`, else the earliest start and latest end of the stored windows of the default run set's runs that hold closed trades (`window.source` is `runs`). This is the run windows from the stored headers, not the first entry and last exit of the trades, so no trade is read.
5. **One read per instrument.** Bars are read as a day range once per instrument however many baskets share it; the benchmark index uses the same cache. Baskets are `nonAdditive`, no total.
6. **Index.** `IPortfolioBenchmark.GetAsync` (the owner's portfolio choice or Nifty 50); no bars, a master without the index, or an unusable window gives `unavailable` with the reason and where the owner can track it (text only; nothing is tracked here).
7. **Evidence.** `BenchmarkEvidenceView` (basis, distinct included instruments, range, limits, unavailable inputs): the run-shaped `ReportEvidenceView` does not fit. Settings `Reports:Benchmark` (`ReportBenchmarkOptions`, bound with `ValidateOnStart` in Infrastructure, defaults as the spec). Log `report.benchmark` (event 4802): counts and milliseconds only.
8. **Caps.** `MaxInstruments` per basket (300), `MaxDistinctInstruments` per call (600) and `MaxWindowDays` (3700) are refused with 422; exclusions are keyed by instrument id (a symbol can repeat).
9. **No new port, table or migration;** read only, session protected.

## Consequences
- No "excess" or composite figure is returned; the screen (P8-09) shows two numbers side by side.
- A basket of fewer than three usable instruments never shows a number.

## Not verified
The p95 target (1.5 s for 6 baskets of 50 instruments) was not measured. The integration test covers members without bars; the arithmetic is covered by unit tests. The daily-bar source is the stored `1D` aggregate and is as adjusted as stored.
