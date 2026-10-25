# P8-09 — Basket comparison, pivot builder and lenses

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-09 of [P8-00](P8-00-reports-and-ui-plan.md)
- **Design refs:** docs/design/13-reports-and-ui.md §1c (basket comparison, pivot builder), §2 (R20, R27, R28), lens selector paragraph of §1 · docs/design/06-instruments-and-baskets.md §4 · prototype `docs/prototype/assets/app-pages-3.js` (views `baskets` and `pivot`) · P8-00 D3, D4, Q13 · ADR 0099
- **Needs:** P8-08 (frame, filter bar, `useReportQuery`, `metricFromApi`, `BarList`), P8-06 (built, ADR 0100: `groups`, `pivot`, `equity`, `what-works`, `runs`), P8-07 (benchmarks, for the buy-and-hold column; without it the column is `unavailable` and the rest works), **P8-02b** (extracts `HeatGrid` into `shared/analytics`; the built Learning screen has only its own `Heatmap`/`HeatTile`).
- **Size:** 3.5 agent-days
- **Safety:** no. GET only; the read-only folder test covers `features/reports`. Basket choice, lens and pivot axes are URL state.

## Goal
The owner picks two or more baskets and sees them side by side with the honest numbers (trades, win rate, expectancy, total R, profit factor, max drawdown, best strategy by credible bound, buy and hold, the index), overlaid cumulative R, expectancy by strategy per basket, basket-by-side and basket-by-style grids; and can build any two-dimension pivot with a metric, with cells below the minimum masked and labelled, through one lens selector.

## Scope
**In:** two catalogue entries (Basket comparison, Pivot builder) and the lens selector for the Reports screen; panels, URL state, fixtures, tests.
**Out:** a server change; a net rupee column (see Decisions 4); the trade list and drill-down (P8-11: a cell opens a side panel until then); the other catalogue reports (P8-10); a total across baskets; saving a pivot as a named view (not planned).

## Acceptance criteria
1. Given fewer than two baskets chosen, then the view says "Choose at least two baskets to compare" and requests nothing for the comparison; given 2 to 6, it loads; given a seventh, the control refuses it with the stated limit (6: the chart has six line roles).
2. Given two baskets sharing an instrument, then both include it, the page prints "A trade in several baskets appears under each; totals are not additive" (from `nonAdditive` and the limits), no sum row exists, and the equity chart legend says the curves overlap.
3. Given the comparison table, then each row shows trades, win rate, expectancy, total R, profit factor, max drawdown, each as a kit metric with its n; a basket row below 30 trades shows `insufficient` for every figure but the trade count; the row order is by expectancy lower bound where it exists, then by trades (never alphabetical presented as ranking), and the first row is not labelled "best" unless its lower bound is positive and the What works answer ranks it ("best" is the server's rank, not a page rule).
4. Given the "Best strategy" column, then it is the top ranked cell for that basket in `what-works?lens=basket,strategy`, or the words "none ranked" with the reason (not enough trades / not enough wins and non-wins); never a strategy chosen from an unranked cell.
5. Given the buy-and-hold and index columns, then they come from `GET /api/reports/benchmarks` for the same window; excluded instruments are listed in an expandable line "N instruments left out (reasons)"; given P8-07 is absent or the call fails, the columns show `unavailable` and the table is otherwise complete.
6. Given the overlaid cumulative-R chart, then it draws one series per basket from `equity?lens=basket` (one point per exit date, the existing `TimeSeriesChart`, series told apart by colour role and dash), a basket with no trade is absent with a note, and the chart table lists the same numbers.
7. Given "Expectancy by strategy per basket", then a bar list per strategy with one row per basket; a basket with fewer than the minimum shows its state in the row, not a bar.
8. Given the basket-by-side and basket-by-style grids (`/pivot?lens=basket,side` and `lens=basket,style`, rows first), then they are heat grids whose cells are buttons named "basket, side: expectancy, n, state"; cells below the minimum are hatched and labelled, never coloured by a value; empty cells say "No trades".
9. Given the pivot builder, then the row and column selects offer only the dimensions of the built lens whitelist (basket, instrument, strategy, timeframe, side, style, exit, month, weekday; sector and hour once P8-10 adds them); regime and account appear disabled with the reason taken from the server's 422 `reports.dimension_unavailable` message (read once from `groups?lens=regime`); "decision" is absent with the line "Decisions are plan tickets: see Learning, Decisions"; choosing the same dimension twice is prevented; the metric select offers expectancy_r (default), expectancy_lower_r, total_r, win_rate, profit_factor, max_drawdown_r, trades (snake-case wire names of the built `metric` parameter, shown with words).
10. Given a pivot of more than 2,000 cells, then the server's 422 `reports.invalid` message is shown with "Choose a smaller dimension"; given an instrument-by-instrument pivot of more than 40 rows, the grid scrolls inside its card and rows are not truncated.
11. Given a pivot cell with n = 3, then it is masked (`insufficient`, "n 3 of at least 30") and no number or colour scale value appears; the colour scale is computed only from value cells, symmetric around zero, and a legend states "hatched = insufficient data".
12. Given the lens selector (All trades, By basket, By instrument, By strategy, By individual trade, By account), then each maps to `by` of the groups table ("All trades" to the summary; "By individual trade" to the trade list entry, shown as "coming with P8-11" until it exists; "By account" disabled with the server's reason), and the selection lives in the URL (`lens=`).
13. Given a heat cell or table row is activated, then (until P8-11) a side panel shows the group's KPIs, evidence and runs behind it, with the line "Trades of this cell: drill-down arrives with P8-11"; when P8-11 exists the panel's link opens it (a catalogue-registry change only).
14. Same-dimension and double-count refusals from the server (`reports.double_count`, `reports.dimension_unavailable`) are shown with their message and a way back to the previous valid state; `reports.double_count` explains "group by exit as well" and offers to add it.
15. Phone: at 360 px the table scrolls inside its card with the basket name column fixed first; the chart height is at least 220 px; touch targets 44 px; no horizontal page scroll.

## Contract
- **UI:** catalogue ids `baskets` and `pivot`, a `LensSelector` in the page header of the Reports screen (visible for the group-based reports), `view=baskets|pivot`, `cmp=<basket ids>` (2 to 6), `rows=`, `columns=`, `metric=`, `lens=` in the URL (the page's own names; the request sends `lens=<rows>,<columns>` and the snake-case `metric`). Components (one per file, ≤ 250 lines): `BasketComparison`, `BasketPicker`, `ComparisonTable`, `BasketEquityPanel`, `StrategyByBasketPanel`, `BasketGrids`, `PivotBuilder`, `PivotAxes`, `PivotGrid`, `CellPanel`, `LensSelector`. Lib: `lensToQuery`, `pivotLimits`.
- **API read only:** `GET /api/reports/groups?lens=basket`, `pivot` (`lens=basket,side` and `lens=basket,style`, `lens=<rows>,<columns>`), `equity?lens=basket`, `what-works?lens=basket,strategy`, `benchmarks` (P8-07), `runs`. The comparison sends the same filters as the rest of the screen; `basket` is set to the compared basket ids (comma separated). A basket in a group part is its id: names come from `GET /api/baskets`.
- **Shared:** `HeatGrid` from `shared/analytics` (created by P8-02b): `role="grid"`, roving focus, cell buttons, hatched insufficient cells, legend. No new shared component here.
- **Events / jobs / data:** none.

## Non-functional
No more than 6 requests for the comparison (groups, equity, what-works, benchmarks, two pivots); each cached 5 minutes. The compared baskets are limited to 6 and the pivot to 2,000 cells so a response stays within a few hundred KB. Chart points: at most about 520 per series, stated. First-load JavaScript is untouched (lazy `reports` chunk only).

## Test plan
- **Screen tests (MSW, `pinClock`):** two baskets sharing an instrument (note shown, no total row); a basket below the minimum (states, no figure); the best-strategy cell ranked and "none ranked" with the reason; benchmarks present, absent (404) and with exclusions; an empty basket; the overlaid chart table; a seventh basket refused.
- **Pivot tests:** a 3 by 3 pivot by hand: rows B1, B2, B3; columns ORB, VWAP, BASE; cells n = 40, 3, 0 / 12, 55, 31 / 0, 0, 90 with values only for n >= 30 (40, 55, 31, 90 are values; 3 and 12 are `insufficient`; the three n = 0 are `empty`); assert 4 value cells, 2 masked, 3 empty, none with a digit but n; the colour scale ignores masked cells; keyboard path through the grid (arrows, Home, End).
- **Refusals:** same dimension twice prevented; `reports.dimension_unavailable` and `reports.double_count` messages; 422 on too many cells.
- **Property:** across every `MetricView` state a cell/row renders without a digit for a non-value (shared helper with P8-08).
- **Read-only, markup-in-text, contrast (new hatch pattern pair), bundle** as P8-08.
- **Browser evidence:** P8-16 checklist for these two views.

## Decisions (researched)
1. **Rank rows by the credible lower bound, not by the raw mean.** Question: how are baskets ordered and who is "best"? Chosen: the server's ranking (P8-05 `FindWhatWorks`) decides "best"; the table sorts by lower bound then n. Reason: ordering by raw expectancy rewards thin lucky baskets (P8-00 D5, ADR 0099). Rejected: the prototype's sort by raw expectancy with a "best" chip.
2. **Six baskets at most.** The chart has six colour roles with three dash styles; more would rely on colour alone. Source: `shared/ui/chart/types.ts` (`ChartColor`, `ChartDash`). Rejected: unlimited series with a palette cycle.
3. **Overlap is shown, not hidden.** Baskets overlap by design (doc 06 §4); per-basket results double count shared instruments, so no total and an explicit note (Q13). Rejected: de-duplicating shared instruments into one basket (it changes the question).
4. **No "Net ₹" column.** The replay trades are unsized unless costed, and nothing is costed on confirmed rates yet (ISS-25); a rupee figure would be invented. The prototype's column is replaced by nothing, with one line saying why.
5. **The pivot offers only server-available dimensions.** The list is the built lens whitelist; regime/account appear disabled with the server's words (the 422 message) and nothing is silently empty. "Decision" is not a trade attribute.
6. **Masking is the server's.** The pivot masks cells below 30 trades; the browser draws what it was given, so a colour scale cannot be fed by a thin cell.
7. **Heat grid as an HTML grid, not an ECharts heatmap.** WAI-ARIA's grid pattern gives arrow/Home/End/Ctrl+Home/Ctrl+End movement, one tab stop and `grid/row/gridcell/columnheader/rowheader` roles ([APG grid pattern](https://www.w3.org/WAI/ARIA/apg/patterns/grid/), read 2026-10-24); a canvas heatmap cannot give each cell an accessible name or a hatched state. Cost: no chart chunk.
8. **Lens = a mapping onto `by`,** not a second query model (P8-00 D4: one `ReportQuery`).
9. **P8-00 Q13, Q12, Q10, Q19** taken as recommended (membership as of the run, no composite, "no evidence" is an answer, gross unless confirmed).

## Risks and honest limits
- Baskets with a few instruments have a few hundred trades at most; expect `insufficient` rows and a "none ranked" best strategy.
- Basket membership is as of each run (stored or reconstructed from basket history, said in the limits); survivorship stands (ISS-22); a hold return flatters a basket assembled from winners.
- The compared curves share instruments and dates, so they are correlated; the chart does not test a difference.
- A pivot looks at many cells; the multiple-testing count is on the What works view, not on a pivot, and the pivot says "descriptive, not a ranking" under the grid.
- Layout unverified in a real browser until P8-16.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any non-value shown as a digit or colour)
- [ ] No contract change; no migration
- [ ] Roadmap row P8-09; ADR addendum on the six-basket limit if the build changes it; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- Aligned to the BUILT P8-06 (ADR 0100): `lens` replaces `by`/`rows`/`columns` on the wire (pivot lens = exactly two dimensions), `basket` replaces `baskets`, metric names are snake case, there is no `facets.dimensions`; the equity endpoint caps at 40 series (the screen's own cap is 6).
- Prerequisite P8-02b (`HeatGrid` extraction) added; there is no `HeatGrid` in `shared/analytics` today.
- Insufficient cells print "n of at least N" with N = `minTrades` of the `what-works` response (the built `MetricView` has no minimum).
- P8-18 will have to make the built default run set skip experiment candidate runs; the comparison then names runs explicitly through `runs`.
