# 13 — Reports & UI (React)

## 1. Screens

| Screen | Purpose | Key components |
|--------|---------|----------------|
| **Login / lock screen** | One centred panel: about + non-sensitive service status (no balances before sign-in) on the left, form on the right. Show/hide password, Caps Lock warning, attempts left, live lockout countdown. Idle or manual **lock** keeps the user and asks only for the password ("Welcome back · Unlock"), and survives a page reload; password re-entry for sensitive actions | Form, status strip |
| **Today** | Plan status, live open positions & P&L, armed tickets, alerts, briefing, services health | KPI tiles, ticket cards, live price panel (SignalR) |
| **Plan review** | Approve / modify / reject proposed tickets before cutoff; reserve list; limits check | Ticket cards with chart, bulk approve, countdown to cutoff |
| **Suggestions** | AI / engine suggestions inbox (track instrument, basket changes, promotions) | Cards with reason & evidence, accept / reject |
| **Portfolio** | All holdings/trades across accounts: added date, value, return %, XIRR; group by bucket / basket / account / strategy / sector ([doc 12](12-portfolio-monitoring.md)). **Built in P2-01 / P2-05 (holdings, dates, returns — [ADR 0012](../adr/0012-holdings-acquisition-dates-and-returns-model.md)); grouping by basket / strategy / sector, the treemap, the benchmark chart and live prices follow in later P2 stories** — see *Portfolio screen (P2-01 / P2-05)* below | Header tiles, grid, position drawer (treemap and benchmark chart later) |
| **Instruments** | Master-list search, tracked instruments, coverage & quality, bulk actions ([doc 06](06-instruments-and-baskets.md)). **Built in P1-03 / P1-03a (master list):** the screen fills the viewport under the top bar from `md` up: header, search/filters and the pagination footer stay in place and only the list scrolls (sticky column titles); below `md` the page scrolls normally. The header carries a compact line "Last refreshed 6 min ago · 4,381 active" and a **Refresh master list** split button (*Force full refresh* in its menu); a chip appears only for a run in progress ("Refreshing…"), a failed last run or one with warnings. The detailed *Master list* panel (live phase and rows read polled every 1.5 s only while running, then the result line "+12 added · 3 updated · 5 retired · 4,381 active" or "No changes — the file is unchanged", warnings and errors in words, counts, "Daily at 08:45 IST · next run …") is **not shown until the owner presses Refresh or clicks the last-refreshed line or a chip**, never opens by itself on load, takes focus when it opens, stays after the run so the result can be read, and is dismissed with × (focus returns to the control that opened it; the Refresh button is `aria-disabled` while a run is active, not disabled, so focus is never lost); an empty master shows "No master list yet — refresh it to get started". Debounced search (symbol, name, ISIN), type chips with counts, **Sector as a type-ahead** (counts, *Unclassified*), series select, F&O / ASM/GSM toggles, *Show retired*, *Clear all filters*; the list is the shared **Grid** (50/100 per page, sortable headers with `aria-sort`, keyboard navigation; Series, Sector and Tick are hidden on narrow screens and live in the detail drawer; scrolls sideways on phones) whose rows open a detail drawer (all fields, Dhan id, first/last seen). Search, filters, sort, page and the open instrument live in the URL (shareable, Back works). Tracking and baskets follow in P1-05 / P1-15 | Faceted search, paged table, chips, detail drawer |
| **Baskets** | Create/edit manual & rule-based baskets, members, basket performance & benchmark. **Built in P1-15 (manual baskets):** cards left, members grid right, drag & drop with keyboard and menu alternatives, Undo, look-back date, history drawer — see [doc 06](06-instruments-and-baskets.md) §3.4 | Drag & drop, rule builder with live preview, comparison charts |
| **Trade Plan** | Tickets per account with reasons, reserve list, pre-open check | Ticket cards with Lightweight Charts (entry/stop/target overlays), account switcher |
| **Journal** | Taken/skipped/modified (auto-matched), notes, tags | Data Grid, trade replay chart |
| **Reports** | Landing **Overview** dashboard with auto-insights, report catalogue, **multi-basket comparison**, pivot builder; common filters incl. multi-select baskets | ECharts, heatmaps, Data Grid, export |
| **Strategy Lab** | Editor/form, runs queue, verdict report, version compare | Monaco + schema, progress via SignalR |
| **Learning** | How the system improves: **journey** (live vs frozen model vs plan prices, with problem/fix markers), **lessons** (what went wrong → fix → result), **plan vs actual** execution gap, **setups** (cell heatmap & lifecycle), **smart rules** (market gate, equity throttle, meta-labelling, entry windows, correlation), **tweak & re-run** experiments, **autonomy** settings & change log with monitoring/rollback, **weekly digest** ([doc 09](09-learning-engine.md) §9–§14) | ECharts, lesson cards, knobs + replay progress, change log |
| **Data & Import** | **Built in P1-17a (manual load, [ADR 0016](../adr/0016-market-bars-csv-load-dhan-fetch-and-app-managed-aggregates.md)):** one **Update data** split button (menu: *Load CSV files only*, *Fetch from Dhan only*, *Rebuild aggregates only*, *Re-import all CSV files*; `aria-disabled` with a spinner while a job runs); a **freshness strip** with the **Last tick** (newest stored 1-minute bar in IST with "N days ago"; the card says "Up to date" with a check icon or "Behind by N trading days" with a warning icon, never colour alone), instruments with data, total 1-minute bars (compact), covered period and the Data account with a **Reconnect** link when the Dhan step cannot run; a **pipeline card** with the three steps *CSV files* (n of N loaded, rows, pending, folder-missing hint, "39 repaired, 0 rejected", unknown symbols in a collapsible), *Dhan Data API* (last fetch, bars added, skipped reason) and *Aggregates* (bars per timeframe, last built), each with its last run time and, while a job runs, a progress bar with done/total and the phase message (the overview is polled every 2 s only while a job runs, also one found running on load; a toast tells how it ended); a **coverage table** (shared Grid: symbol, name, first bar, last bar highlighted when behind, 1m bars, source chip, status chip *Current* / *Behind N d* / *Empty*; search, status filter, sortable headers, paging; columns hidden on narrow screens; state in the URL) whose rows open a **drawer** with a timeframe select (1m … 1M) and the latest bars (OHLCV, bars built from, complete/partial); **Recent runs** (start time IST, steps, result, counts, a failed run's message). Upload, preview/undo, import profiles, folder watch, scheduled EOD runs and quality issues follow in P1-07/08/12/14 | Freshness strip, pipeline card, paged coverage table, bars drawer |
| **Accounts** | List + detail with tabs (Overview, Roles, Trading, Portfolio, Data, Connection, Activity), role matrix, shared risk profiles, add-account wizard (Dhan only), data failover ([doc 04](04-accounts-access-and-operations.md) §1.1). The top bar also lists every account with its connection status and a *Connect / Reconnect* action ([ADR 0009](../adr/0009-dhan-consent-flow-only.md)) | Master-detail, tabs, wizard, header menu |
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

## 1e. Visual system (P1-19)

Calm, data-first, trustworthy: cool-neutral base, one deliberate accent. Tokens live in `frontend/src/theme/tokens.ts`
and the prototype's `proto.css`; the MUI theme and the prototype read the same values. All text pairs meet WCAG AA
(4.5:1), UI parts 3:1 — enforced by `theme/contrast.test.ts`.

| Token | Light | Dark | Role |
|-------|-------|------|------|
| background | `#f4f6fa` | `#0b1220` | app canvas (60 %) |
| surface | `#ffffff` | `#131c2e` | cards, dialogs, tables |
| surfaceAlt | `#f1f4f9` | `#182238` | dialog footer, table header, hover rows |
| border | `#dfe3ea` | `#26324a` | hairlines (cards, table rows, dividers) |
| inputBorder | `#7b8798` | `#5f6f8a` | input outlines, unchecked checkbox/radio, switch track (3:1+ on surface, surfaceAlt and background) |
| text | `#0f172a` | `#e6eaf2` | primary text |
| textSecondary | `#475569` | `#9aa7bd` | captions, helper text |
| accent | `#4338ca` | `#9aa5ff` | primary actions, links, selection, focus |
| accentContrast | `#ffffff` | `#0b1220` | text on accent fills |
| accentSoft | `#eceefe` | `#222a52` | dialog header band, selected nav/rows |
| good / bad / warn / info | `#146c36` / `#b91c1c` / `#a35200` / `#0e7490` | `#4ade80` / `#f87171` / `#fbbf24` / `#67e8f9` | gains / losses / warnings / information |

| Type role | Size / weight | Use |
|-----------|---------------|-----|
| h1 / h2 / h3 | 24/700, 18/650, 15/650 (tracking -0.02 to -0.01em) | page, section, card titles |
| body1 / body2 | 14/1.5, 13/1.45 | text, dense data |
| caption / overline | 12/1.4, 11/600 uppercase +0.06em | helper text, section labels |
| button | 14/600 | actions |
| numeric | tabular-nums + `cv11`, `zero` | tables, stat tiles, countdowns, header counts |
| mono | JetBrains Mono Variable | ids, URLs, code |

Fonts: Inter Variable and JetBrains Mono Variable, self-hosted (latin + latin-ext), `font-display: swap`, metric-matched fallbacks.

Rules:
- **60-30-10**: 60 % neutral backgrounds, 30 % surfaces and text, at most 10 % accent. The accent is for primary actions, links, selection and focus - **never for gain/loss** (use good/bad plus a sign or arrow; status is never colour alone).
- **Tabular figures** for every number column; right-align numbers.
- **Focus ring**: 2 px accent outline, 2 px offset, on every interactive element.
- **Elevation**: soft cool shadows in light, border-led in dark; **motion**: 120 ms (hover/press) and 200 ms (panels), honouring `prefers-reduced-motion`.
- Table rows 36 px, header on `surfaceAlt`; selected nav item on `accentSoft` with a 3 px accent marker.

### Icons (P1-20)

Every icon sits in a **chip**: a rounded square tinted from its own colour pair, so colour carries meaning at a glance while the page stays calm (the accent still means "action"). Tokens: `iconHues`, `iconChip`, `navHue` in `tokens.ts`; the prototype reads the same pairs from `ui.js` (`NAVSTYLE`, `C`).

| Size | Box / icon / radius | Where |
|------|---------------------|-------|
| sm | 28 / 17 / 8 px | sidebar items, page-header icon, card and section headers, header accounts menu rows, toasts |
| md | 34 / 17 / 10 px | stat tile (top right) |
| lg | 40 / 22 / 12 px | feature cards (lesson cards) |

- Icon colour: the hue's **strong** value in light mode, its **light** value in dark mode. Chip background: that colour at 12 % (light) / 16 % (dark) over the surface.
- Chips are decorative: the label always sits next to them; a chip has assistive text only when it stands alone.
- Selected sidebar item keeps the `accentSoft` row and accent marker; its chip keeps its own hue. Planned (coming soon) items are coloured too.
- Tile hues follow keywords: money/value indigo, cash/funds amber, gains/ok green, loss/risk rose, time/day sky, data/learning teal, returns violet, automation pink, history slate.
- Header chips reuse the `sm` size (28/17 px), and tile and header tints are unified at 12 % (light) / 16 % (dark): a deliberate deviation from the prototype's 26 px / 18 % header and 11 % / 15 % tile values.
- Toasts: success green, info indigo, error rose status chip on a neutral outlined surface (a deliberate deviation from the prototype's dark toast).

| Hue | Light value | Strong value | Hue | Light value | Strong value |
|-----|-------------|--------------|-----|-------------|--------------|
| today | `#f59e0b` | `#df6714`* | reports | `#c4b5fd` | `#7c3aed` |
| review | `#10b981` | `#059669` | lab | `#f472b6` | `#db2777` |
| suggestions | `#d946ef` | `#8b5cf6` | learning | `#2dd4bf` | `#6366f1` |
| portfolio | `#38bdf8` | `#3b82f6` | accounts | `#60a5fa` | `#2563eb` |
| instruments | `#818cf8` | `#4f46e5` | services | `#38bdf8` | `#6366f1` |
| baskets | `#fb923c` | `#f43f5e` | settings | `#a1a1aa` | `#52525b` |
| data | `#22d3ee` | `#0d9488` | audit | `#fbbf24` | `#b45309` |
| quality | `#a3e635` | `#159f48`* | system status | `#2dd4bf` | `#0f766e` |
| indigo / green | `#818cf8` / `#34d399` | `#4f46e5` / `#059669` | amber / rose | `#fbbf24` / `#fb7185` | `#d07206`* / `#e11d48` |
| sky / violet | `#38bdf8` / `#c084fc` | `#0284c7` / `#7c3aed` | teal / pink / slate | `#2dd4bf` / `#f472b6` / `#94a3b8` | `#0d9488` / `#db2777` / `#475569` |

\* Contrast-adjusted (icon on its own chip over the surface must be at least 3:1 in both modes; enforced by `theme/contrast.test.ts`). Darkened minimally, hue kept: today `#f97316` to `#df6714` (2.48 to 3.01), quality `#16a34a` to `#159f48` (2.89 to 3.00), amber `#d97706` to `#d07206` (2.81 to 3.01). The adjusted values are copied back into the prototype (`ui.js`). The prototype's navigation fallback (`#64748b`) is replaced by the tile `slate` (`#475569`) in the app. Dark-mode light values are all at least 4:1 on their chip (asserted in the test); no exceptions below 3:1.

### Layout and shared list controls (P1-03a)

- **Shell:** the content area is full width on every page (padding only, no max-width); text-heavy blocks keep a readable line length through their own inner widths (page descriptions `90ch`, the roadmap placeholder card). From `md` up the shell is the viewport height and `main` is the one scroll container, so a screen can fill it (`height: 100%`) and scroll a region of its own; below `md` the document scrolls.
- **`Grid`** (`shared/ui/grid`, imported by path so MUI Table stays out of the initial bundle) is the one list control: `columns` (id, header, `accessor`/`render`, `width`/`minWidth`/`grow`, `align`, `numeric` tabular figures, `sortable`, `hideBelow` breakpoint), `rows` + `getRowId`, controlled `sort`/`onSortChange` (`aria-sort`), optional controlled selection (`selectedIds`/`onSelectionChange`: checkbox column, select-all-on-page, shift-click ranges), `onRowClick` + `activeRowId`, `pagination` (rows per page, "1–50 of 3,204"), `loading` (skeleton rows; rows already shown stay, dimmed), `error` + `onRetry`, `emptyState`, `stickyHeader`, `stickyFirstColumn`, `density`. It fills its parent; the body is the scroll container (header and footer stay), and it scrolls sideways when the columns do not fit. Keyboard: rows are one tab stop (row checkboxes are not), Up/Down/Home/End/PageUp/PageDown move, Enter opens, Space toggles selection; only events from the row itself are handled (never with Ctrl/Meta/Alt), so drag handles, inline editors and buttons in cells keep their keys; a new page or sort scrolls to the top and resets the tab stop. Semantics `grid`/`row`/`columnheader`/`gridcell` with `aria-rowindex` (position in the whole list), `aria-colindex`/`aria-colcount`, `aria-multiselectable`. Reuse points: a column with `kind: 'handle'` (drag handles, row menus; excluded from row click and keyboard) and `getRowProps(row, index)` (`sx`, `ref`, `data-*`); rows are memoised and the roving tabindex is kept in the DOM so keys and selection re-render only the rows that changed. Used by Instruments now; P1-05 (selection, bulk actions), P1-15 (basket members) and later lists reuse it.
- **`Typeahead`** (`shared/ui/Typeahead`, MUI Autocomplete, imported by path): options with `label`, optional right-aligned `count` and `hint`; case-insensitive substring filter with the match highlighted; clearable (also by emptying the text); no-options and loading text; `getOptionDisabled`; a value that is not among the options stays selected; combobox/listbox semantics. Used for Sector; also for baskets, accounts and series later.
- **`SearchField`** (`shared/ui/SearchField`, imported by path): the debounced search box of the list screens (commits 300 ms after the last keystroke, follows the value when it changes from outside, clear button); used by Instruments and Portfolio.

### Portfolio screen (P2-01 / P2-05)

Route `/portfolio` (lazy chunk; nothing of it is in the initial bundle). Same viewport-height layout as Instruments from `md` up (header, filters, tiles and sync bar stay; the holdings list scrolls inside its card with the pagination footer fixed; the page scrolls normally below `md`). All view state is in the URL: `account`, `bucket`, `status` (open default, closed, all), `q`, `sort`, `page`, `size` and `holding` (the open drawer).

- **Scope:** *Account* type-ahead (All accounts, or an account with the Portfolio role), *Bucket* type-ahead, a debounced search (symbol, name, ISIN) and an Open / Closed / All toggle. With "All accounts" and more than one account the list gets an *Account* column. Import, Sync now and Auto refresh act on one account: the chosen one, or the only one there is; with several accounts and "All accounts" they ask to pick one first. An account that is not connected shows "Connect the account first" with a link to its Connection tab, and its Import and Sync buttons are off (what is already stored stays visible).
- **Summary tiles** (totals of the chosen account and bucket, from `GET /api/holdings/summary`): Invested · Current value (as of the valuation date) · Unrealised P&L ₹ with % · XIRR · Realised (est.) · Holdings (open, closed, not in master list). Gains and losses always carry a sign and an arrow (▲ +₹18,400, ▼ −₹12,000), never colour alone. An XIRR that cannot be shown is "—" with its reason (*insufficient*: needs 30+ days and money in and out; *n/m*: beyond ±1,000 %; *no data*: no price) as the tile hint and a tooltip. An unpriced holding (no usable price) shows a "No price yet" chip and dashes for value, P&L, % and XIRR (never −100 %), and the totals leave it out with the note "{n} holdings without a price are left out of value, P&L and XIRR."; an XIRR beyond ±1,000 % shows "n/m"; every reason is reachable by keyboard. Under the tiles: a chip "12 lots use the default date" (it lists the oldest purchase dates first, where the default dates are) and the footnote "Dividends, charges and corporate actions are not included."
- **Sync bar:** the last run in words ("Synced 5 min ago — 1 new, 1 closed"; a skipped run shows the reason the API recorded, e.g. "The account is not connected to Dhan."), the auto-refresh setting and next run ("Auto refresh: every 15 min in market hours (Mon–Fri 09:15–15:30 IST) · next run 09:30, 12 Oct IST"), **Sync now** (progress "Syncing…", "already running" and "connect the account first" shown in words, and the safeguard: when Dhan's answer would close all or most holdings the API answers 409 `portfolio.confirmation_required` and a dialog says "Dhan returned no holdings — 5 of 5 would be closed. Confirm only if that is right."; *Confirm and sync* sends the request again with `confirm: true` and the numbers it showed (`expectedOpenHoldings`, `expectedWouldClose`); if Dhan's answer changed meanwhile (409 `portfolio.confirmation_stale`) the dialog reopens with the fresh numbers and "Dhan’s answer changed since you looked — review again", never retrying by itself; the wording uses what Dhan listed: "Dhan listed 0 holdings while 5 are open" / "Dhan listed 2 holdings but 4 of your 6 open holdings are missing") and **Auto refresh** (dialog: Off · Every 5/15/30/60 minutes during market hours · Daily at HH:MM IST). The setting is one shared control, `AutoRefreshSettings`, also used in the account's Portfolio tab and the add-account wizard (default Off); it is saved with `PUT /api/accounts/{id}/portfolio`, sending only the refresh fields (the API keeps every field that is null or left out; the account tab sends `""` for the account's own bucket). A refusal for a field the form has no place for is shown, never swallowed. A run that finished elsewhere (an automatic refresh) reloads the figures by itself (status polled every 60 s when auto refresh is on, every 3 s while a run is in progress).
- **Holdings grid** (the shared `Grid`): Symbol (monospace; "Not in master list" chip; "Closed" chip), Name, Qty, Avg cost, LTP (price time as tooltip), Invested, Value, P&L ₹, P&L %, XIRR, Acquired (earliest open lot date, "Default date" chip, lot count), Bucket, and Account when several accounts are listed; sortable headers use the API's sort fields; Name, Avg cost, Invested, Acquired, Bucket and Account are hidden on narrower screens (the drawer shows everything). Empty states: "No holdings yet — import them from Dhan" (with the Import button), "No holdings match these filters", "No account has the Portfolio role yet".
- **Holding drawer:** the holding's figures, then its lots (acquired date with *Default date* chip and source, qty, unit cost, invested, value, P&L, XIRR, status; closed lots show the sale and an *Estimated* chip). Each lot's date has an edit control: a date field (not before 1 Jan 1990, not in the future, checked here and by the API) saved with `PUT /api/holdings/{id}/lots/{lotId}`; every figure reloads after the save.
- **Import holdings** (dialog, 3 steps shown in its header): 1 *Fetch from Dhan* (`POST …/holdings/preview`; not connected, role off, no bucket and Dhan-unreachable are explained in words with a link or Retry); 2 *Choose dates*: a visible rule line ("Default date: 1 Jan 2025. The first import of an account uses 1 Jan 2025 when nothing is in the system yet; holdings that appear later are dated today automatically."; the default shows *today* once the account already has holdings), a table of what Dhan holds (shown 100 rows per page above 200 rows; bulk dates and the import still cover every row; symbol and name, qty, avg cost, LTP, "Not in master list", "Already imported" rows are marked and left out) with a **date field per row** (max today, min 1 Jan 1990, invalid dates block the import), **Set date for all** and **Set for selected** (row checkboxes, select all); a row the owner leaves alone is sent without a date, so the API gives it the default date and flags it; 3 *Result* (imported / already imported / no longer at Dhan / lots on the default date). The grid and tiles reload.

The prototype (`docs/prototype`) is unchanged: it stays the visual reference for the portfolio tiles and grouping; the screen above is the first real slice of it.

### Top bar (P1-21)

Left to right, as in the prototype: menu toggle (below `md`) · page title (from `sm`) · **Search or jump to… (⌘K / Ctrl+K)** · spacer · **accounts control** · **notification bell** · colour-mode toggle · **user button** (soft-indigo avatar with the owner's initials, name, chevron).

| Element | Behaviour | Real now or later |
|---------|-----------|-------------------|
| Search or jump to | Palette dialog: *Pages* (every navigation item; planned ones marked with their phase), *Accounts*, *Actions* (toggle dark mode, go to Accounts / Settings); substring match, arrow keys, Enter, Esc; combobox + listbox semantics. Icon only below `lg`. | Real. Instruments and baskets join with P1-11 / P1-12. |
| Accounts control | Summary of all accounts with Connect / Reconnect (P1-16d). Doubles as the status element until the pills below exist. | Real |
| Notification bell | Unread badge (max "9+") and a right-hand drawer: severity chip, text, relative time, **Open** link, "Mark all read", empty state "You're all caught up". Items are derived from real state only: account not connected, token expired, token expires within 2 h, API key expires within 30 days / expired. An item's id includes the situation (e.g. the expiry), so a changed situation is new and unread. Read state is a per-browser convenience (`localStorage`). | Real for accounts; alerts, services and plan tickets feed it later (P2 / P1-13 / P7) |
| User button | Avatar (28 px, `accentSoft` background, `accent` 13 px bold initials), name (from `lg`), chevron; menu with name, email, Sign out. | Real |
| Market open + NIFTY pill, services-health pill | Shown in the prototype. | **Later**: need market data (P1-11 / P1-12 / P2-07) and the service framework (P1-13); not faked. |

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
