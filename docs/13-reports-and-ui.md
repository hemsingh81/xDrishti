# 13 — Reports & UI (React)

## 1. Screens

| Screen | Purpose | Key components |
|--------|---------|----------------|
| **Login / lock screen** | One centred panel: about + non-sensitive service status (no balances before sign-in) on the left, form on the right. Show/hide password, Caps Lock warning, attempts left, live lockout countdown. Idle or manual **lock** keeps the user and asks only for the password ("Welcome back · Unlock"), and survives a page reload; password re-entry for sensitive actions | Form, status strip |
| **Today** | Plan status, live open positions & P&L, armed tickets, alerts, briefing, services health | KPI tiles, ticket cards, live price panel (SignalR) |
| **Plan review** | Approve / modify / reject proposed tickets before cutoff; reserve list; limits check | Ticket cards with chart, bulk approve, countdown to cutoff |
| **Suggestions** | AI / engine suggestions inbox (track instrument, basket changes, promotions) | Cards with reason & evidence, accept / reject |
| **Portfolio** | All holdings/trades across accounts: added date, value, return %, XIRR; group by bucket / basket / account / strategy / sector ([doc 12](12-portfolio-monitoring.md)) | Header tiles, tree-grid, treemap, value-vs-benchmark chart, position drawer |
| **Instruments** | Master-list search, tracked instruments, coverage & quality, bulk actions ([doc 06](06-instruments-and-baskets.md)) | Faceted search, Data Grid, chips |
| **Baskets** | Create/edit manual & rule-based baskets, members, basket performance & benchmark | Drag & drop, rule builder with live preview, comparison charts |
| **Trade Plan** | Tickets per account with reasons, reserve list, pre-open check | Ticket cards with Lightweight Charts (entry/stop/target overlays), account switcher |
| **Journal** | Taken/skipped/modified (auto-matched), notes, tags | Data Grid, trade replay chart |
| **Reports** | Landing **Overview** dashboard with auto-insights, report catalogue, **multi-basket comparison**, pivot builder; common filters incl. multi-select baskets | ECharts, heatmaps, Data Grid, export |
| **Strategy Lab** | Editor/form, runs queue, verdict report, version compare | Monaco + schema, progress via SignalR |
| **Learning** | How the system improves: **journey** (live vs frozen model vs plan prices, with problem/fix markers), **lessons** (what went wrong → fix → result), **plan vs actual** execution gap, **setups** (cell heatmap & lifecycle), **smart rules** (market gate, equity throttle, meta-labelling, entry windows, correlation), **tweak & re-run** experiments, **autonomy** settings & change log with monitoring/rollback, **weekly digest** ([doc 09](09-learning-engine.md) §9–§14) | ECharts, lesson cards, knobs + replay progress, change log |
| **Data & Import** | CSV upload/preview/commit/undo, import profiles, coverage per timeframe, EOD fetch runs, quality issues | Dropzone, preview grid, coverage heatmap |
| **Accounts** | List + detail with tabs (Overview, Roles, Trading, Portfolio, Data, Connection, Activity), role matrix, shared risk profiles, add-account wizard, data failover ([doc 04](04-accounts-access-and-operations.md) §1.1) | Master-detail, tabs, wizard |
| **Data quality** | Every tracked instrument: coverage, validation status, aggregates vs 1-minute, official-daily match, repair ([doc 05](05-data-and-brokers.md) §10b) | Status table, inspect drawer with stored-vs-recomputed table |
| **Config** | Trading config editor, validate, diff, activate, history | Monaco YAML + JSON Schema, diff view |
| **Assistant** | Chat over your data; answers show numbers, query and chart | Streaming chat panel |
| **Services** | Every background service: editable schedule (type, time, days, window, interval, run-after), retries, timeout, catch-up, parameters; dependency checks; trading-day timeline; run now, pause, history | Status table, configure dialog, timeline |
| **Audit** | Logins, configuration changes, approvals, service actions | Filterable log |

Every analytic screen has a **lens selector** — *All trades · By basket · By instrument · By individual trade ·
By strategy · By account* ([doc 06](06-instruments-and-baskets.md) §4) — plus a common filter bar: **date range** ·
source (backtest / paper / live) · account · **basket** · style · strategy · timeframe · side · exit policy ·
regime · symbol · sector · plan status · discretionary vs system trades.

## 1b. Clickable prototype

A working front-end prototype of these screens lives in [`prototype/index.html`](../prototype/index.html) (open it
directly in a browser — offline, demo data, no backend; services and the live feed are simulated in the
browser). It demonstrates the login (demo credentials prefilled); Accounts (list/detail tabs, role matrix, risk
profiles, wizard, data failover); plan review with approve / modify / reject, the pre-trade checklist and
**auto-approval at the cutoff**; suggestions inbox; portfolio with per-account tiles, Action center, price alerts,
XIRR and animated grouping; instruments (one scrollable grid with sticky header and symbol column) & baskets; CSV
import with aggregation; the Data quality window; the **Learning** screen (journey, lessons with loss tags, plan vs actual with
fills ledger, setups, smart rules, tweak & re-run, autonomy, weekly digest); live tickets filled with simulated
slippage and graded on fills; Reports (overview with auto-insights, basket comparison, pivot and 10 more
reports); Strategy Lab; Services with schedule configuration, dependency checks, timeline and catch-up; settings
with password re-entry; the audit log — responsive from phones to 4K monitors, light and dark.

**Visual language** (carried into the React app): Inter font and a standard, calm palette (Tailwind *slate/gray*
neutrals with one *indigo* accent; green/red/amber only for meaning), a light sidebar with a soft colour-tinted
icon per screen, tiles with tinted icons and count-up numbers, collapsible cards, animated dialogs/drawers/toasts
(respecting *reduce motion*), light and dark themes, and scrollable data grids with sticky headers instead of
pagination. In MUI this is one theme object (palette, shape, typography) shared by every screen.

**Navigation for speed:** a command palette (**⌘K / Ctrl K** or **/**) searches pages, Learning tabs, actions
(run nightly, digest, lock, theme), baskets and every instrument (opens its chart drawer); **g + letter** jumps to
a page (g t Today, g r Plan review, g p Portfolio, g l Learning …); **?** lists all shortcuts; the browser tab
title shows the current screen.

## 1c. Reports experience

- **Landing = Overview:** KPI tiles (trades, win rate, expectancy, total R & ₹, profit factor, max drawdown),
  **auto-insights** in plain sentences ([doc 15](15-smart-insights-and-automation.md) §6), equity & drawdown,
  monthly results, strategy × timeframe heatmap, expectancy by basket, R distribution, win rate by weekday.
- **Left-hand catalogue:** Overview · Basket comparison · Strategies · Exit policies · Side & regime · Calendar &
  time · MAE/MFE · Calibration · Instruments · Accounts · Decisions · Pivot builder · Trade list.
- **Global filters:** date range with presets, **multi-select baskets**, side, style, account.
- **Basket comparison:** choose 2+ baskets → side-by-side KPIs (trades, win rate, expectancy, total R, profit
  factor, max drawdown, net ₹, best strategy, buy-and-hold return), overlaid cumulative-R curves, expectancy by
  strategy per basket, basket × side and basket × style heatmaps.
- **Pivot builder:** any two dimensions (strategy, timeframe, basket, side, style, exit, regime, account,
  instrument, sector, weekday, month, decision) × metric (expectancy, total R, win rate, trades, profit factor).

## 1d. Responsive design (all monitors)

| Width | Layout |
|-------|--------|
| < 480 px (phones) | Single column, 2-up KPI tiles, larger touch targets, off-canvas navigation |
| 481–860 px (small tablets) | Single column, off-canvas navigation, wrapping filters |
| 861–1200 px (tablets / small laptops) | Icon-only sidebar (expandable), 1–2 column grids |
| 1201–1679 px (laptops / desktops) | Full sidebar (collapsible), 2–3 column grids |
| 1680–2559 px (large / QHD) | No max-width cap, 2-column plan tickets, taller charts, larger base font |
| ≥ 2560 px (4K / ultrawide) | Up to 4 columns of tickets/cards, larger charts and font |

Rules: no horizontal page scroll at any width (wide tables scroll inside their card), fluid grids
(`auto-fit`/`minmax`), charts resize with their containers, sidebar state remembered per device.

## 2. Report catalogue

| # | Report | Answers | Visuals |
|---|--------|---------|---------|
| R1 | Next-day plan | What to do tomorrow and why | Ticket cards, mini-charts, reserve list |
| R2 | Daily scorecard | How did yesterday's plan do | R per ticket, trigger hit/missed |
| R3 | Performance overview | Am I making money | Equity (R, ₹), underwater drawdown, KPI tiles |
| R4 | Strategy × timeframe matrix | What works | Expectancy heatmap, split by side |
| R5 | Exit-policy comparison | 1:2 vs 1:3 vs trail vs pyramid per cell | Grouped bars on identical entries |
| R6 | Side analysis | Longs vs shorts | Side-by-side KPIs and equity |
| R7 | Regime analysis | What works in which market | Cell × regime heatmap, regime timeline |
| R8 | MAE/MFE | Stops too tight? targets too greedy? | Scatter, histograms with target markers |
| R9 | R distribution | Payoff shape | Histogram |
| R10 | Calendar & time-of-day | When it works | Calendar heatmap, weekday/hour/expiry bars |
| R11 | Symbol & sector | Where it works | Treemap / ranked bars |
| R12 | Calibration | Are probabilities honest | Reliability diagram |
| R13 | Selector effectiveness | Does top-3 beat the rest | Primary/reserve/rejected, top-k curves |
| R14 | Backtest vs paper vs live | Does reality match research | Overlaid equity, slippage distribution |
| R15 | Monte Carlo risk | Expected drawdowns | Fan chart, percentiles |
| R16 | Learning changelog | What changed and why | Timeline |
| R17 | Account comparison | How each account performs | Per-account KPIs and equity |
| R18 | Data quality | Can tonight's data be trusted | Check status, flagged symbols |
| R19 | Discipline | Am I following the plan | Taken vs recommended, cost of deviations in R |
| R20 | Basket comparison | Which basket works best, for which style/strategy | Side-by-side KPIs, basket × strategy heatmap, vs basket buy-and-hold & Nifty |
| R21 | Instrument scorecard | Which instruments consistently work / fail | Ranked table, sparkline equity per instrument |
| R22 | Portfolio overview | What is my portfolio worth and how is it doing | Value, invested, P&L, XIRR, alpha; allocation by bucket/basket/sector |
| R23 | Portfolio returns | Returns and XIRR by group over time | XIRR by group bars, value vs benchmark, drawdown |
| R24 | Realised P&L | What did closed trades make | FIFO realised P&L by period / bucket / instrument, charges breakdown |
| R25 | Data coverage | Do I have complete data for every tracked instrument and timeframe | Coverage heatmap, import & EOD history |
| R26 | Decisions review | Do my approvals/rejections add value vs the system's proposal | Approved vs rejected vs lapsed outcomes in R |
| R27 | Basket comparison | Which basket works best and why | Multi-select baskets: KPIs, overlaid equity, strategy × basket, buy & hold |
| R28 | Pivot | Any dimension × any dimension | Heatmap with metric selector |
| R29 | Data quality | Is every timeframe correct | Validation status, stored vs recomputed |
| R30 | Learning journey | Is the system actually getting better | Weekly expectancy: live vs frozen-model counterfactual vs plan prices; problem→fix bands; cumulative uplift |
| R31 | Lessons | What went wrong and how it was fixed | Lesson cards (problem → diagnosis → fix → result), before/after metric, walk-forward evidence |
| R32 | Execution gap | Where the edge is lost between plan and fills | Waterfall (plan → slippage → missed → overrides → charges → actual), by strategy and month |
| R33 | Experiments | Which settings were tried and what they did | Experiment history with Δ expectancy, folds better, verdict, status |
| R34 | Loss tags | Why trades lose | Tag counts and R lost by tag, linked lessons |
| R35 | Smart-rule evidence | Is each rule still earning its place | Throttle: equity with/without; meta: R taken vs skipped; gate: R by VIX/breadth bucket; windows: R by hour; correlation matrix |

**Standard KPIs:** trades · win rate · avg win/loss R · expectancy · profit factor · total R · net ₹ after
costs · max drawdown (R, %) · Sharpe/Sortino · avg holding · exposure · longest loss streak.

**Drill-down:** heatmap cell → cell detail (equity, exit comparison, MAE/MFE, regime split) → trade list →
trade replay (entry, stop, target, trail path, exit reason, feature snapshot).

## 3. Exports & delivery

Plan: page + PDF/HTML. Portfolio: CSV/Excel of positions, transactions and realised P&L. Any report: CSV/Excel. Weekly review pack (PDF): R3, R4, R5, R12, R13, R16 + assistant
summary. Notifications appear in-app (SignalR); optional external channel only if you enable one.

## 4. Implementation notes

- Report queries live in the .NET Reporting module; heavy aggregates via SQL materialised views refreshed
  nightly; responses are chart-ready series.
- Lightweight Charts for anything price-based; ECharts for analytics.
- The SPA is a PWA so it can be "installed" on your phone (LAN access).
