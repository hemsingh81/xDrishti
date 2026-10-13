# P2-08 — Portfolio grouping (account · bucket · sector · basket) and allocation charts

- **Status:** Approved (owner, 2026-10-13: "approved as written", after an analysis of the ADR and spec — gaps settled in ADR 0021 'Review before approval' and criteria 6a, 10a, 13a below) · decision [ADR 0021](../adr/0021-portfolio-grouping-and-allocation-charts.md) (Accepted)
- **Phase / roadmap row:** P2 — "group by bucket/basket/account/sector, tree-grid, charts" · after P2-05 (returns), P2-06 (day change), P2-07 (chart wrapper)
- **Design refs:** docs/design/12-portfolio-monitoring.md §3 (per group), §5 (baskets vs buckets), §10 (Group by, tree-grid, charts), docs/design/06-instruments-and-baskets.md (baskets, sectors)

## Goal
The owner can look at the portfolio by account, bucket, sector or basket: one row per group with what is invested, what it is worth, profit/loss, XIRR, day change and weight, each expandable to its holdings, plus charts of the allocation and of the sector exposure by account.

## Scope
**In:**
- Read model + `GET /api/holdings/groups` (partition, per-group figures, additive flag); `groupBy`/`groupKey` filter on the holdings list.
- Screen: **Group by** control (None · Account · Bucket · Sector · Basket) in the scope bar, group rows with figures and a weight bar, expand to holdings (existing columns/drawer), sort of groups, additivity note for baskets, URL state.
- Charts: allocation donut and sector-by-account stacked bar in a second lazy ECharts chunk, with accessible summary and table alternative.

**Out (not in this slice):** grouping by strategy (no plans/positions yet), the treemap, column chooser, export, lots/transactions as tree levels (lots stay in the drawer), group-level history charts, group alerts, editing baskets/buckets from here.

## Acceptance criteria
**Read model (pure calculators reused; table-driven tests)**
1. Given holdings in two accounts and three buckets, grouping by account/bucket/sector partitions every holding exactly once; per group: invested, value, unrealised P&L (₹, %), realised P&L, XIRR (from the union of the group's lot flows, not an average), day change (₹, %, excluded count), holdings count, weight % = group value ÷ scope total value; `additive: true`.
2. For additive groupings, Σ group invested / value / P&L equals the summary's for the same filters; unpriced holdings are counted (`unpriced`) and left out of value/P&L/XIRR exactly as in the summary; closed holdings follow the `status` filter.
3. Sector grouping uses the master's sector; a holding with no sector or not mapped to the master is in **Unclassified** (`key: "~none"`).
4. Basket grouping: a holding appears in every basket that currently contains its instrument (portfolio system baskets excluded) and in **No basket** otherwise; `additive: false`; weights are shares of the scope total; the same holding is counted in each group's figures and once in the scope totals.
5. Group XIRR/states agree with `ReturnsCalculator` for the same flows (`ok | insufficient | n_m | no_data`, or `no_price` for a group or total of only unpriced holdings, whose value, P&L and XIRR are null); a one-holding group's XIRR equals that holding's.
6. `GET /api/holdings/groups?by=account|bucket|sector|basket&accountId&bucketId&status&q&sort` → `{by, additive, groups[{key, name, holdings, unpriced, invested, value, pnl, pnlPct, realised, xirr, xirrState, dayChange, dayChangePct, dayChangeExcluded, weightPct}], total{…}}`, sorted by `sort` (`value` default, `invested`, `pnl`, `pnlPct`, `xirr`, `dayChange`, `name`, `-` for descending, nulls last), ≤ 200 groups; `400 portfolio.invalid` for an unknown `by`/`sort`; read-only; < 300 ms for 500 holdings.
6a. `stackBy` (`account` when `by=sector`, `sector` when `by=account`; any other combination is `400 portfolio.invalid`) adds, per group, `parts[{key, name, value}]` (value of that group's holdings in each stack part; parts sum to the group's value) — the data of the stacked bar. Group keys: account id, bucket id, sector name, basket id; `~none` for Unclassified / No basket (never a real name).
7. `GET /api/holdings` accepts `groupBy` + `groupKey` (account id, bucket id, sector name, basket id, or `~none`) in addition to the existing filters, returning that group's holdings with the usual paging/sort; unknown key → empty page.

**Screen**
8. The scope bar gains **Group by** (None · Account · Bucket · Sector · Basket); `groupBy` is in the URL (default none = today's flat grid unchanged); changing it keeps the other filters.
9. With a grouping on, the grid shows **group rows**: name, holdings count, invested, value, P&L (₹ over %, sign + icon), XIRR, day change, a weight bar with %; sortable via the API fields; sticky header, fills the viewport like the current grid; phone: group cards.
10a. An expanded group loads its holdings 50 at a time with a **Show N more** row at its end (no scroller inside the page scroll); sort/search/status follow the scope; collapsing keeps the cache.
10. A group row expands/collapses (button with `aria-expanded`, keyboard: Enter/Space, arrow navigation optional) to its holdings using the existing columns, drawer, sort and paging; children are fetched on first expand and cached; "Expand all"/"Collapse all" for ≤ 20 groups.
11. Basket grouping shows the note "Baskets overlap — totals do not add up to the portfolio"; a total row ("All holdings") is always shown above the groups with the scope totals.
12. Loading, error with Retry, empty ("No holdings in this scope") states; a group with only unpriced holdings shows "—" and "N holdings without a price".
13. **Charts** card (above the groups, collapsible, state remembered per session): an **allocation donut** of the current grouping (top 8 + "Other", % of value, labels by name and %, legend buttons) and, for Account and Sector groupings, a **sector exposure by account stacked bar**; follows the scope filters; each chart has `role="img"` summary and a "Show as table" alternative; colours from theme tokens (categorical palette defined in the theme, light/dark, never colour alone — labels/legend/table carry the names); loads lazily (own chunk) when the card is visible; reduced motion respected.
13a. The categorical palette (8 + Other) is a theme token set; `theme/contrast.test.ts` covers it for light and dark; the stacked bar shows the sector × account matrix from `parts` and hides when the grouping is Bucket/Basket/None.
14. Bundle: initial chunk ≤ 200 KB gzip (unchanged ± 1 KB); the categorical chart chunk adds ≤ 60 KB gzip beyond the shared ECharts core; the time-series chart chunk does not grow; responsive 360 px → 4K; a11y checked.

## Contract
- **API:** as in 6–7; DTOs in `ApiJsonContext`; `scripts/gen-api.sh`; problem code `portfolio.invalid`.
- **Data:** no migration (partitions computed on read from holdings, lots, the master's sector and basket members).
- **UI:** `features/portfolio` (GroupBy control, GroupGrid, GroupRow, charts card, hooks, lib) + `shared/ui/chart` categorical wrapper (`CategoryChart` lazy chunk, donut/stacked bar) reusing the ADR 0020 patterns and the ESLint rule (echarts imported only inside the chart canvases).
- **Events/jobs:** none.

## Non-functional
- One read for holdings+lots and one for the sector/basket membership per request; partitioning and calculators in memory; no per-group queries.
- Security: read-only; inputs validated (`by`, `sort`, ids, `groupKey` length); nothing sensitive.
- Observability: log scope size, group count and duration (no figures).

## Test plan
- Domain/application unit: partitioning for each `by`, sector Unclassified, basket overlap and No basket, group XIRR via flows, additive totals vs summary, sorting with nulls last, 200-group cap, unknown by/sort.
- Integration (TimescaleDB): groups over real holdings/lots with two accounts, buckets, sectors and baskets; Σ groups = summary for additive groupings; the holdings list with `groupBy`/`groupKey`; basket membership changes show at once.
- Frontend (Vitest + MSW): Group by control and URL, group rows and figures, expand/collapse + children caching, basket note, total row, empty/error/unpriced states, charts card (mock lazy ECharts; summary/table alternative), sorting, phone cards.
- Architecture: calculators stay pure; echarts imported only in chart canvases (existing ESLint rule extended).
- Visual check (light, dark, phone) against a mock API; owner check with real data.

## Open questions (owner) — answered 2026-10-13: all as written
1. **Groupings:** Account · Bucket · Sector · Basket · None now; Strategy later when plans exist — OK?
2. **Baskets as a lens:** a holding counts in every basket it belongs to, flagged "totals do not add up", plus "No basket" — OK?
3. **Groups computed on the server** (new endpoint) rather than in the browser — OK?
4. **Charts:** allocation donut + sector-by-account stacked bar, no treemap yet, in a second lazy chunk (≤ 60 KB extra) — OK?
5. **Default view stays the flat grid** (Group by = None); the choice lives in the URL — OK?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`)
- [ ] Design docs updated (12 §3/§5/§10, 13 Portfolio row), ADR 0021 accepted
- [ ] Deployed; the owner groups real holdings by sector and basket and checks the totals against the flat view
