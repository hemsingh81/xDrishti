# P2-03 — Trade import (CSV and manual) and lot reconstruction

- **Status:** Approved (owner, 2026-10-13: "approved as written", after an analysis of the ADR and spec — gaps settled in ADR 0023 'Review before approval' and criteria 2a, 6a, 12a, 13a, 16a below) · decision [ADR 0023](../adr/0023-trade-import-and-lot-reconstruction.md) (Accepted)
- **Phase / roadmap row:** P2 — "transactions, FIFO lots, CSV/manual trade import" (the P2-03 named in doc 12 §1) · after P2-01/P2-05 (lots, returns), P2-09
- **Design refs:** docs/design/12-portfolio-monitoring.md §1 (data sources), §2 (domain model), §3 (where acquisition dates come from), §9 (data model), 05-data-and-brokers.md, ADR 0012, ADR 0017 §5

## Goal
The owner loads a broker tradebook (CSV) or adds trades by hand, sees what that would change, and rebuilds a holding's lots from the real buy and sell dates, so returns, XIRR, the value history and the benchmark use true dates and real sale prices instead of one default date.

## Scope
**In:**
- `portfolio.transactions` (immutable except void), `portfolio.trade_batches`, migration, audit events.
- Pure CSV reader (header aliases, date/number formats, skips, errors) and the downloadable template.
- Pure lot reconstructor (opening lot, FIFO, closed pieces, split/bonus conversion with applied corporate actions) and its dry-run preview.
- API: preview, import (± rebuild), batches + undo, a holding's trades, manual add, void, rebuild-lots (dry run), template.
- Frontend: Trades drawer (import wizard with preview, history with Undo, Add trade), Trades section in the holding drawer, read-only marking of lots that come from trades.

**Out (not in this slice):** the Dhan trade-history/ledger sync, cash flows and dividends, charges in cost or XIRR (stored only), broker-specific CSV profiles beyond the generic one, editing a transaction, positions/intraday P&L, creating closed holdings from trades, tax reports.

## Acceptance criteria
**CSV reader (pure; table-driven tests with committed sample files)**
1. A file with the required columns — date, symbol or ISIN, side, quantity, price — under any of the documented header aliases (case/punctuation-insensitive) is parsed; optional columns (exchange, trade/order id, time, brokerage/charges/taxes columns summed into `charges`) are used when present; unknown columns are ignored. A missing required column refuses the file (`400 portfolio.invalid`, field `file`, the missing names and the accepted aliases in the message).
2. Dates `yyyy-MM-dd`, `dd-MM-yyyy`, `dd/MM/yyyy`, `dd-MMM-yyyy` are accepted, `MM/dd/yyyy` is never guessed (an impossible `dd/MM` such as 13/25/2024 is a bad row); a trade date in the future is a bad row; numbers accept `.` decimals and `,` thousands; quantity and price must be > 0, charges ≥ 0; side accepts buy/sell/b/s/purchase/sale (case-insensitive); symbols are upper-cased and trimmed; `NSE:` prefixes and `-EQ` series suffixes are stripped.
2a. Quantities must be whole numbers (a fractional quantity is a bad row); a `total charges` column is used as is, else component columns (brokerage, taxes, stamp duty, …) are summed, never both; times are India time.
3. Rows for non-equity segments (derivatives, commodity, currency — detected by a segment/instrument column, or by symbols with expiry/strike patterns) and BSE-only rows are **skipped with a reason**; bad rows are reported (line number, reason; at most 200 listed, with the total) and never imported; duplicates inside one file are kept when they differ by trade id/time/line and counted when identical lines have the same trade id.
4. Limits: UTF-8 (BOM tolerated), ≤ 10 MB, ≤ 20,000 data rows (else `400`), cancellation honoured; no row content is logged.
5. The template endpoint returns a CSV with the canonical headers and two example rows.

**Reconstruction (pure; table-driven)**
6. For a holding with quantity `Q`, average cost `A` and trades: `P0 = Q − (Σbuys − Σsells)` after split/bonus conversion; `P0 < 0` → no change and result `quantity_mismatch {Q, net}`; otherwise lots = opening lot `P0` (date = first-import date, or the day before the first trade when earlier; flagged default; unit cost `c0 = (A × (P0 + B) − BC) / P0` with `B` the shares and `BC` the cost of all the buys of the file on today's basis — Indian brokers keep `A` by the weighted-average-cost method, so a sale does not change it and `c0` holds whether or not sales used the opening lot up; the pieces sold from the opening lot cost `c0` too; falls back to `A`, flagged `costNote: "opening_cost_fallback"`, when `c0` is not above 0; absent when `P0 = 0`), then one lot per buy, sells consuming open lots FIFO into closed pieces (close price = sell price, `closed_on` = trade date, `estimated = false`); same-day order: buys before sells, then time, then file order; open quantities sum to `Q`.
6a. A quantity that is not whole after the split/bonus conversion → `quantity_mismatch` (reason `fractional_after_adjustment`); a security matching more than one holding of the account → `ambiguous` (not applied); a security matching neither a holding nor a master-list NSE share/ETF is *unmatched* (stored, not applied).
7. Sells consume the opening lot first (it is the oldest) and a sell larger than all open quantity at that point is a `quantity_mismatch`; partial sales split lots like the existing model (closed piece + open remainder with the same date and cost).
8. Conversion: a trade dated before an **applied** corporate action's ex-date of the same instrument has quantity and price converted by the action's factor (cumulative, in ex-date order); the result carries `adjustedFor: n`; suggested/ignored actions are not used; a case with a split and a bonus is covered.
9. Idempotence and determinism: rebuilding twice gives identical lots; rebuilding with no trades gives one lot of `Q` at `A` dated by the first-import date, flagged default (the state before any import).
10. After a rebuild the figures hold: Σ open lot quantity = `Q` always; with **no sales** in the file and an opening lot, invested = Σ open qty × unit cost = `Q × A` to within the rounding of the opening unit cost to 4 decimals (± 0.0001 × the opening quantity + 0.01); **with sales** the first-in first-out cost basis of the trades can differ from the broker's average-cost figure by nature (and without an opening lot the real buy costs stand) — the rebuild block shows `openCostAfter`, `brokerCost` and `costDelta` (= `openCostAfter − brokerCost`) so the screen can say "cost basis differs from broker by ₹x"; realised P&L = Σ closed pieces (real sale prices, not estimated); XIRR/returns are recomputed from the new lots by the existing calculators.

**Storage and API**
11. Transactions are stored on **Import** only (preview stores nothing), one row per new trade with identity `external_id` (`t:yyyyMMdd:` + the trade id when the file has one — exchanges number trades per day, so the same id on two days is two trades — else a hash of (account, date, time, symbol, side, qty, price, occurrence index within the file)); importing the same file again stores nothing and reports `alreadyImported`; a batch row records file name, SHA-256, counts (parsed, imported, duplicates, skipped, bad), status and who/when.
12. `POST /api/portfolio/trades/preview` (multipart: `accountId` of a Portfolio-role account, `file`) → `{file {rows, parsed, skipped{byReason}, bad[], duplicates}, securities[{symbol, isin, matched, holdingId?, trades, buys, sells, firstTradeDate, lastTradeDate, rebuild {status ok|quantity_mismatch|no_holding|unchanged, lotsBefore, lotsAfter, handDatedLotsReplaced, syncLotsReplaced, realisedPnl, adjustedFor, q, net, openCostAfter, brokerCost, costDelta, costNote}}], notHeld{securities, trades}, securitiesTotal, unmatchedListed}`; the list holds every held or master-listed security and at most 200 unmatched ones; read-only; 400 for unreadable files; ≤ 10 s for 20,000 rows.
12a. The preview also returns, per security, `openingQty {before, after}` and a per-holding lot-shape digest, plus one overall `digest`; statuses include `ambiguous`.
13. `POST /api/portfolio/trades/import` (same body + `rebuild`: bool, default true) stores the batch and, with `rebuild`, rebuilds every holding whose result is `ok`, in one unit of work per holding with the lots replaced atomically; mismatches and unheld securities are stored but not applied and reported; idempotent; with `rebuild: true` the request must carry the preview's `digest` — a mismatch (holdings, lots, corporate actions or trades changed meanwhile) is `409 portfolio.preview_stale` with the fresh report and nothing applied (13a); audit events (batch imported, lots rebuilt: counts only); `409 portfolio.sync_running` while that account's holdings sync runs.
14. `GET /api/portfolio/trade-batches` (newest first, paged) and `POST /api/portfolio/trade-batches/{id}/undo`: voids the batch's transactions (never deletes) and rebuilds the affected holdings from the remaining trades; undo twice is a no-op; audited. **Limitation:** an undo cannot bring back hand-dated or sync-added lots that an earlier rebuild replaced (a holding returns to one default lot, or to the shape of the remaining trades); `?dryRun=true` (default false) returns the same result without changing anything, with `handDatedLotsReplaced`, `syncLotsReplaced` and `notRebuilt[{symbol, holdingId, reason}]` (holdings that could not be rebuilt keep their lots).
15. `GET /api/holdings/{id}/trades` (non-voided and voided flagged, newest first, paged); `POST /api/portfolio/trades` (manual: account, holding or security, side, date, qty, price, charges?; validation as for CSV rows; dedupe identical manual trades is NOT applied) and `POST /api/portfolio/trades/{id}/void`; `POST /api/holdings/{id}/rebuild-lots?dryRun=true|false` returns the same `rebuild` block and, when not a dry run, applies it.
16. Lots: a rebuild deletes the holding's old lots and inserts the new ones in one unit of work (the portfolio schema grants DELETE on `holding_lots`; the holding's `xmin` token guards a concurrent sync, retried once); `holding_lots.source` gains `trade` (check constraint widened); new lots carry `source = trade` (not default-flagged, date read-only through `PUT /api/holdings/{id}/lots/{lotId}` → `409 portfolio.lot_from_trade`), the opening lot stays editable and default-flagged but a hand edit of its date must be before the earliest trade date (`400 portfolio.invalid`, field `acquiredOn`); the P2-01 first-import-date re-dating skips holdings that have trade lots.
16a. Transactions: `qty bigint > 0`; unique `(account_id, external_id)` only among non-voided rows (partial index), so a file can be imported again after Undo; identical lines inside one file get an occurrence index so genuine identical trades are both kept and a re-import adds nothing; the UI requires an explicit confirmation (checkbox) when the preview says hand-dated lots will be replaced.
17. History/benchmark/groups/actions caches are invalidated after import, undo, void, add and rebuild (the existing unit-of-work invalidation); the Action center and notices reconcile on the next tick/read as usual.

**Screen**
18. Portfolio screen: a **Trades** button (header or sync bar) opens a drawer: **Import CSV** (account select, file picker, drag-and-drop, template link) → preview with counts, skipped/bad rows (collapsible, with line numbers), the securities table (symbol, trades, lots before → after, realised P&L, status chip with icon+word: ok / quantity mismatch / not held), a summary of replaced lots ("3 hand-dated lots and 2 lots from syncs will be replaced"), the "adjusted for N corporate actions" note, and **Import and rebuild lots** / **Import only**; loading, error with Retry, empty states.
19. Import history list (file, date, counts, status) with **Undo** (confirmation dialog stating what will be rebuilt) and a toast with the outcome; **Add trade** dialog (account → holding or security search, side chips, date picker (not future), quantity, price, charges, validation messages) offering **Rebuild lots** afterwards.
20. Holding drawer: a **Trades** section (list with source/date/side/qty/price, voided shown struck-through with the reason text, **Void**, **Rebuild lots** showing the dry-run result first); lots from trades are labelled *from trades* with the date not editable (tooltip why); the opening lot is labelled *before the first trade* and stays editable.
21. Responsive 360 px → 4K, a11y (file input labelled, status not by colour alone, focus management in drawer/dialogs, accessible table), tests by role/label/text; initial bundle ±1 KB (drawer and dialogs lazy).

## Contract
- **API:** as in 12–15; DTOs in `ApiJsonContext`; `scripts/gen-api.sh`; problem codes `portfolio.invalid`, `portfolio.not_found`, `portfolio.sync_running`, `portfolio.lot_from_trade`; the multipart endpoint follows the existing CSV upload idiom (`DataCsvEndpoints`).
- **Data:** migration `AddTradeTransactions`: `portfolio.transactions(id uuid pk, account_id, instrument_id null, isin null, trading_symbol, exchange, side check, trade_date date, trade_time time null, qty bigint > 0, price numeric(18,4) > 0, charges numeric(18,4) >= 0, source check csv|manual, batch_id null, external_id text, created_by, created_at, voided_at null, voided_by null)` unique `(account_id, external_id)` among non-voided; `portfolio.trade_batches(...)`; `holding_lots.source` check widened with `trade`; grants SELECT/INSERT/UPDATE on transactions and trade_batches, no DELETE; audit event types.
- **UI:** `features/portfolio` (TradesDrawer, ImportWizard, PreviewTable, ImportHistory, AddTradeDialog, HoldingTrades, hooks, lib, api), lazy chunks; file upload via the shared API client (multipart).

## Non-functional
- Parsing is streaming and bounded; reconstruction is O(trades) per holding and runs in memory; one read of holdings+lots, one of applied corporate actions per request; a 20,000-row file previews in a few seconds.
- Security: session + CSRF; the file is parsed, never executed or stored (only its hash and name); symbols/notes are escaped in the UI; no row content or amounts in logs (counts only).
- Observability: log batch sizes, counts and durations only.

## Test plan
- Domain/application unit: reader (aliases, formats, bad rows, skips, limits, template), reconstructor (every rule in 6–10 incl. same-day order, P0 cases, FIFO partials, mismatch, split/bonus conversion, no trades, determinism), identity/dedupe, batch/undo semantics, handlers with in-memory ports.
- Integration (TimescaleDB): import → rebuild → returns/XIRR/history reflect real dates; undo restores the one-lot state; re-import is a no-op; mismatch holding untouched; sync running → 409; grants (no DELETE on transactions/batches); preview digest stale → 409; concurrent sync during rebuild retried; query-count guard; caches invalidated.
- Frontend (Vitest + MSW): wizard states and preview table, skipped/bad lists, import history + Undo, Add trade validation, holding Trades section, read-only lot marking, loading/error/empty, phone layout; no hung handlers (pendingResponse/releasePending).
- Architecture: reader and reconstructor pure; handlers sealed/internal; ports implemented in Infrastructure.
- Visual check (light, dark, phone), then the owner with a real tradebook.

## Open questions (owner) — answered 2026-10-13: all as written
1. **Transactions stored + an explicit, previewed "Rebuild lots"** (nothing replaced without being shown) — OK?
2. **Rebuild replaces all lots of a holding** (including hand-dated and sync-added ones, counted in the preview) rather than keeping them — OK?
3. **Trades for securities you do not hold now** are stored but not applied (no closed positions created) — OK?
4. **Generic CSV** with auto-detected headers + template now; broker-specific profiles when you give me a real export — OK? (Please tell me which broker's tradebook you will import first.)
5. **Charges stored only** (not in cost or XIRR yet); **splits/bonuses** converted with your **applied** corporate actions — OK?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddTradeTransactions`
- [ ] Design docs updated (12 §1–§3, §9, 13), ADR 0023 accepted, sample CSVs committed
- [ ] Deployed; the owner imports a real tradebook for one account, reviews the preview, rebuilds, and compares XIRR with their own records
