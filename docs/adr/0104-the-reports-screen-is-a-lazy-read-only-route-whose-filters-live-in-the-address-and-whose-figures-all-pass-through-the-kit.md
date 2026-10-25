# 0103 — The Reports screen is a lazy read-only route whose filters live in the address and whose figures all pass through the kit

- **Status:** Proposed (spec [P8-08](../specs/P8-08-reports-screen-overview-and-what-works.md), built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** [0100](0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md) (the endpoints it reads), [0099](0099-the-report-engine-is-a-pure-core-with-one-population-rule-six-honest-states-and-a-ranking-by-the-credible-lower-bound.md) (six states).

## Decision
1. **A feature folder `features/reports`, one lazy route `/reports`.** Only GET calls; `readOnly.test.ts` fails on a mutation, a non-GET call or a feature import. The only eager additions are one route object and the removal of `phase: 'P8'` from the nav entry.
2. **The address is the view.** `view`, `from`, `to`, `basket`, `side`, `style`, `exit`, `strategy`, `timeframe`, `symbol`, `runs`, `basis`; malformed values are dropped on reading, the server judges the rest. Date presets are measured back from the newest `windowEnd` among the runs, never from today.
3. **Every figure is a kit state.** `metricFromApi` maps the server's six states; a `value` without a number is `unavailable`; an `insufficient` figure without a known n and minimum is `not_evaluable` with the server's reason (no digit). A group that is not a value draws no chart and no summary number.
4. **Net only when the server allows it.** `basis=net` is sent only when `GET /api/reports/runs` says net is possible; otherwise the page prints the server's reason and stays gross. A 422 `reports.net_*`, or a 409/422 about the run set, shows the server's message with a one-click way back (gross, or the default run set).
5. **Bars and columns are CSS.** `shared/analytics/BarList` and `ColumnBars` draw from existing theme colours (`good`, `bad`, `accent`); no chart library beyond the existing lazy equity chart. Contrast pairs for those fills are in `contrast.test.ts`.

## Measured
- First-load bundle (entry plus modulepreload) after the slice: 196.1 KB gzip of 200 KB (3.9 KB to spare); the route adds one route object.
- Lazy chunk `ReportsPage`: 27.5 KB raw, **8.75 KB gzip** (target 60 KB). This is the route budget for later slices to grow into.

## Interaction budget (for P8-17)
From a cold `/reports`, the filtered answer takes 3 interactions: click Reports, open the Baskets control, pick a basket. The Baskets control is a multi-select whose menu stays open and hides the page from assistive technology, so closing it (Escape) is a 4th step before the chip and the answer can be read by role. The test therefore counts up to **4** (the P8-00 limit), and P8-17 should count the Escape step too.

## Consequences
- The Data used panel lists every run and why a run is left out; the picker writes `runs=`.
- The strategy-by-timeframe cell has no drill-down yet (its opener is a no-op until P8-11).
- The experiment candidate runs are still in the default run set until P8-18 changes `RunSelection`; the screen shows the server's note unchanged.
