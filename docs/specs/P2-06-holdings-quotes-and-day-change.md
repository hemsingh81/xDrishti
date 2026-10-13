# P2-06 — Holdings quotes every 5 minutes and day change

- **Status:** Approved (owner, 2026-10-13: "approved as written" — all five open questions answered with the defaults) · decision [ADR 0019](../adr/0019-holdings-quotes-day-change-and-the-first-interval-service.md) (Accepted)
- **Phase / roadmap row:** P2 — "quote polling for holdings" (+ the day-change part of the Portfolio screen) · after P1-13 (services framework), P2-01/P2-05 (holdings, returns)
- **Design refs:** docs/design/05-data-and-brokers.md §5 (real-time: quote API polling for long-term holdings), 12-portfolio-monitoring.md §3 (day change), §6 (live updates), 04 §3 (services overview: *Holdings quote refresh, every 5 min 09:15–15:30*)

## Goal
During market hours the Portfolio screen shows holdings at prices that are at most one tick (5 minutes) old, with **day change** (₹ and %) per holding and in total; the poll is a service the owner can see, change, pause or run now on the Services screen.

## Scope
**In:**
- Dhan quote client (`POST /v2/marketfeed/quote`, primary Data account token + client id, throttled, ≤ 1,000 securities per request), added to the endpoint allow-list as the only new path.
- `holdings-quotes` service (first `interval` service) in the Portfolio module: every 5 min 09:15–15:30 IST on trading days, retries 0, timeout 2 min, catch-up off, notify on failure on; overlapping ticks dropped silently (log only); skipped run with a reason when the Data account has no usable token; no-op without open holdings.
- Holding price update: `previous_close` and `last_price_source` columns (migration), one statement per batch across accounts, rules for sync vs quote (ADR 0019 §3).
- Day change calculation (pure) and API fields (rows + summary).
- Portfolio screen: Day change tile and column, prices line, *Refresh prices*, one-minute re-read while the market is open and the page visible.
- Services screen: the new card renders and edits like the others (interval schedule words, Configure dialog, History).

**Out (not in this slice):** the live WebSocket feed and SignalR push (`xd-feed`), quotes for instruments that are not holdings (baskets, indices), intraday price history or charts, price alerts, day change history, dividends, corporate-action adjustment of the previous close, BSE-only holdings, a quote fallback to the stored 1D bars.

## Acceptance criteria
**Quote client and allow-list**
1. The allow-list accepts exactly `POST https://api.dhan.co/v2/marketfeed/quote` (https, default port, no query) in addition to today's paths; `…/marketfeed/ltp`, `…/ohlc`, any other marketfeed path, a query string, GET, or another host are refused before anything is sent (tests extend the existing guard tests).
2. Given a Data-account token and client id, when quoting N securities, then the request carries headers `access-token` and `client-id` (never logged), body `{"NSE_EQ":[ids]}` with ≤ 1,000 ids per request (more are split into several requests, ≥ 1 s apart through the throttle), and the answer is parsed into `{securityId, lastPrice, previousClose}`; a security missing from the answer, or with a zero/negative/non-numeric price, is reported as "no quote" for that security only.
3. 401/403 → `Unauthorized` (run fails with "the Data account's token was refused"), 429 → `RateLimited` (run fails; next tick tries again), other 4xx/5xx, timeouts, malformed JSON or an unexpected shape → `Failed` with a safe fixed message; no secret, token or body is logged or stored in a message. Unit tests with a fake handler for each case (recorded sample shapes committed under `docs/research/dhan-samples/` as sanitised fixtures once the owner has run it for real; until then fixtures are written from the Dhan documentation and marked unverified).

**Price update**
4. Given open holdings of the same security in two accounts, when a quote arrives, then both rows get `last_price`, `previous_close`, `last_price_at = fetch time` and `last_price_source = quote` in one statement; closed holdings are untouched; a security with no quote keeps its price.
5. A holdings sync after a quote **of the same India day** keeps the quote's `last_price`/`last_price_at`/`source` and never changes `previous_close`; a sync after a quote of an earlier day, or on a holding that never had one, sets the price from Dhan's list with source `sync` (as today).
6. Holdings that cannot be quoted (instrument not mapped to an NSE master instrument, retired) are skipped and counted; the run message says "Quoted 41 of 43 holdings; 2 not on NSE".
7. P&L, XIRR and totals read the new `last_price` without other changes (regression: existing returns tests unchanged; one test that a quote moves value, P&L and XIRR consistently).

**Day change (pure, tested)**
8. Per holding: ₹ = qty × (last − previousClose), % = (last − previousClose) / previousClose; unavailable when previousClose is null/≤ 0. Per group and total: sum of ₹; % = Σ₹ / Σ(qty × previousClose) over included holdings; `dayChangeExcluded` = number of open holdings left out. Tests: gain, loss, zero, one excluded, all excluded (null totals), group total vs sum of holdings, rounding.
9. `GET /api/holdings` rows carry `previousClose`, `dayChange`, `dayChangePct`, `priceAt`, `priceSource`; `GET /api/holdings/summary` carries `dayChange`, `dayChangePct`, `dayChangeExcluded`, `pricesAsOf {oldest, newest}`; sums of rows equal the summary; read-only, projections, < 200 ms for 500 holdings.

**Service and scheduler**
10. `holdings-quotes` appears in `GET /api/services` (runs as xd-api, status, next run, history) with the defaults above; only `interval` is allowed; the schedule checks accept it and warn for a window that does not cover 09:15–15:30 when its constraint says so.
11. At 09:15, 09:20 … 15:30 IST on trading days a run starts (fake-clock tests incl. holiday, weekend, special session, 15:30 inclusive, nothing at 15:31); nothing runs outside the window or on a non-trading day; a Run now works outside the window.
12. A tick that falls while the previous run is still running is dropped: no run row, one `service.tick_dropped` log line; the unique running index still guarantees one at a time. A missed tick (machine asleep) is not made up; the next aligned tick runs. Interval services never catch up.
13. A failed run (token refused, Dhan down) ends `failed` with the safe message and raises the service failure notice (cleared by the next success); a run with no usable token is recorded `skipped` with the reason; neither changes any price.
14. No open holdings → the run succeeds with message "No open holdings"; no request is made.

**Screen**
15. Portfolio summary shows a **Day change** tile (₹, % with sign and icon, not colour alone; "—" with an explanation when nothing is included) and a note "Day change excludes N holdings without a previous close".
16. The holdings grid has a **Day change** column (₹ over %, sortable via the API sort fields `dayChange` and `dayChangePct`), hidden below `lg` like other secondary columns; the holding drawer shows previous close, price time and source.
17. A prices line under the tiles: "Prices as of 10:35 IST · quotes every 5 min" while quotes are fresh (price ≤ 2 intervals old during market hours), "Prices delayed — last quote 10:05 IST" when older during market hours, "As of 10 Oct · from the last sync" for synced prices or outside market hours, "Quotes off — {reason}" when the service is paused or the last run failed, with a link to Services; the text and chip come from the services list and the summary, not from the browser clock alone (IST).
18. **Refresh prices** (in the sync bar) runs the service now (`POST /api/services/holdings-quotes/run`), shows progress, re-reads the holdings and toasts the outcome; disabled while the service runs; a 409 `services.already_running` is explained.
19. While the market is open (IST clock + trading day from the timeline) and the page is visible, holdings and summary are re-read once a minute; paused in background tabs; stops after the close; loading/error/empty states unchanged; a11y (no live-region spam: the prices line updates at most once a minute), 360 px → 4K.
20. Services screen: the `holdings-quotes` card shows "Every 5 min 09:15–15:30 IST on trading days"; Configure supports interval fields (from/to/every N min, trading-days switch), preview shows the next three runs; History lists runs with the quoted/not-on-NSE message.

## Contract
- **API:** additive fields as in criterion 9; no new route for the screen (Refresh prices uses the services run endpoint); `scripts/gen-api.sh`; sort fields `dayChange`, `dayChangePct` on `GET /api/holdings`.
- **Data:** migration `AddHoldingQuotes`: `portfolio.holdings.previous_close numeric(18,4) null`, `last_price_source text not null default 'sync'` with CHECK `sync|quote`; grants unchanged (`xd_app` already UPDATEs holdings).
- **Services:** registers `holdings-quotes` (interval 5 min, window 09:15–15:30, tradingDaysOnly, retries 0, timeout 2 min, catchUp false, notify true; constraint `MustCover` 09:15–15:30 as a warning rule) in the Portfolio module; scheduler change: a due tick while the service runs is dropped (log, no row) for `interval`/`window` types.
- **UI:** `features/portfolio` (tiles, column, prices line, Refresh prices, polling) and a small port in `shared/api` for the services run call (feature boundaries respected); prototype `docs/prototype` Portfolio screen for the day-change look.
- **Events/jobs:** interval service as above; idempotent (a repeated run writes the same latest quote); failures raise the service notice.

## Non-functional
- One request per tick for typical portfolios (< 1,000 securities); the run takes seconds; the price update is one statement per batch, not per holding.
- Throttle: ≤ 1 request/second to the quote endpoint, shared with nothing else; the bar fetch's throttle is separate.
- Security: token and client id only from the Data account's vault entry, never logged or returned; allow-list enforced for the new path; no user input reaches the request.
- Observability: log events `holdings_quotes.quoted|failed|skipped|dropped` with counts and slugs only; the service run message carries "Quoted n of m".
- Bundle: Portfolio chunk growth ≤ 3 KB gzip; initial bundle unchanged (≤ 200 KB).

## Test plan
- Domain/application unit: day change calculator, price-update rules (quote vs sync same/earlier day), service handler with fake quote source/clock/stores (no holdings, unmapped, token missing, 401/429/5xx, partial answer, > 1,000 split), scheduler interval ticks (holiday, weekend, special session, 15:30 inclusive, dropped overlap, no catch-up, Run now outside window), sort fields.
- Infrastructure: quote client with a fake `HttpMessageHandler` (headers, body, parse, errors, allow-list), no secrets in logs.
- Integration (TimescaleDB): migration, batch update across two accounts, sync-after-quote rule, API fields and sort, services list entry, run now end to end with a fake source.
- Frontend (Vitest + MSW): tile, column, sort, drawer, prices line variants, Refresh prices, one-minute polling only while open + visible (fake timers), Services card.
- Architecture: the service depends on abstractions; Portfolio module owns the update; allow-list guard tests.
- Visual check in the browser (light, phone) once the owner has run it with real holdings.

## Open questions (owner) — answered 2026-10-13: all as written
1. **Interval:** every 5 minutes from 09:15 to 15:30 (editable on Services) — OK?
2. **Data account:** quotes use the primary Data account's token and client id (the ₹499 Data API plan); if the plan or token does not allow the quote API the service fails visibly and prices fall back to the synced ones — OK?
3. **Previous close:** taken from Dhan's quote (`ohlc.close`), verified once by you against the Dhan app; no fallback to our stored daily bars in this slice — OK?
4. **Unmapped holdings** (BSE only) keep their synced price and are left out of day change, with a note — OK?
5. **Polling the screen:** once a minute while the market is open and the page visible (no SignalR until the live feed) — OK?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddHoldingQuotes` added
- [ ] Design docs updated (05 §5, 12 §3/§6/§9/§10, 04 §3 services table, 16 configuration if any key), ADR 0019 accepted
- [ ] Deployed; the owner runs *Refresh prices* with real holdings during market hours and compares price and day change with the Dhan app
