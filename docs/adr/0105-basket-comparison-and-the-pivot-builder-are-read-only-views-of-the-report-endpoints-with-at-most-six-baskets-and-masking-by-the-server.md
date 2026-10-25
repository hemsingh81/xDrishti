# 0105 — Basket comparison and the pivot builder are read-only views of the report endpoints, with at most six baskets and masking by the server

- **Status:** Proposed (spec [P8-09](../specs/P8-09-basket-comparison-pivot-builder-and-lenses.md), built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** [0104](0104-the-reports-screen-is-a-lazy-read-only-route-whose-filters-live-in-the-address-and-whose-figures-all-pass-through-the-kit.md) (the screen), [0100](0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md) (endpoints), [0102](0102-basket-benchmarks-are-an-equal-weight-price-return-hold-over-members-as-of-one-stated-date-with-every-exclusion-named.md) (buy and hold).

## Decision
1. **No server change.** Two catalogue entries (`view=baskets`, `view=pivot`) in the lazy `reports` chunk; the address holds `cmp`, `rows`, `columns`, `metric`, `lens`.
2. **At most six baskets**, because the chart has six colour roles told apart by three dash styles. A seventh option is disabled and the limit is printed. Baskets overlap by design: the page prints that totals are not additive and has no total row.
3. **Order by the credible lower bound, then trades.** The What works answer ranks basket-by-strategy cells, not baskets, so the page says "Top ranked cell: <strategy>" on the basket of the top ranked cell, and only when its lower bound is positive (the server's rank, not a page rule). The best strategy column is the top ranked cell of the basket or "None ranked: <the server's reason>".
4. **Buy and hold and the index come from `GET /api/reports/benchmarks`;** any failure makes those columns `unavailable` and the rest stays. There is no rupee column (replayed trades are unsized and not costed on confirmed rates).
5. **The pivot offers the built whitelist only.** Regime and account are listed as not offered with the server's own 422 message; decisions are not a trade attribute. The same dimension twice is prevented (the other axis' option is disabled). Masking is the server's: an `insufficient` cell is hatched and prints "n of at least N", with N the What works `minTrades`; the colour scale reads value cells only and only for R metrics.
6. **The lens selector is in the page header of the Pivot builder view only** (the basket comparison is fixed to the basket lens). A lens is a mapping onto the groups `lens` parameter** (summary for all trades); individual trade waits for P8-11 and account is refused by the server.

## Consequences
- A basket with too few trades for a total has no curve; the chart names it instead of drawing a flat line.
- The compared baskets replace the screen's Baskets filter on the comparison view, and the page says so.
- "Group by exit as well" on a double-count refusal replaces the pivot columns with exit (a pivot has two dimensions at most).
- A cell opens a side panel with its figure, evidence and runs; its trades arrive with P8-11. The panel shows all KPIs only for a table row (a group); a pivot cell has only its own figure and n.
- Up to 7 requests for the comparison (groups, equity, What works with its basket-by-strategy lens, benchmarks, two pivots, plus the screen's own What works), each cached five minutes.
