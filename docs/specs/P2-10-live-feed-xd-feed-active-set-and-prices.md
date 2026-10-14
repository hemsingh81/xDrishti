# P2-10 — Live feed (`xd-feed`): active set, live prices and live alerts

- **Status:** Approved (owner, 2026-10-14: "approved as written", split into P2-10 and P2-10a) · decision [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md) (Accepted) · the screen follows in [P2-10a](P2-10a-live-hub-and-live-portfolio-screen.md)
- **Phase / roadmap row:** P2 — "`xd-feed` live feed for the active set + quote polling for holdings" · after P2-06 (holdings quotes), P2-09 (price alerts), P1-13 (services framework)
- **Design refs:** docs/design/05-data-and-brokers.md §5 (real-time, active set), 03-target-architecture.md §5 (live active set, `xd-feed`), 04 §3 (services: *Live feed*, window 09:00–15:35), 12-portfolio-monitoring.md §6, 15 §4 (price alerts), 17 (`xd-feed` container), [ADR 0019](../adr/0019-holdings-quotes-day-change-and-the-first-interval-service.md), [ADR 0022](../adr/0022-action-center-and-price-alerts.md), [ADR 0007](../adr/0007-accounts-credential-references-and-step-up.md) / [0008](../adr/0008-broker-credentials-vault-and-connection.md)

## Goal
During market hours a separate `xd-feed` container keeps the prices of the owner's open holdings (and a few indices) within seconds of the exchange in the database, fires price alerts within seconds of a level being reached, and shows on the Services screen whether it is connected, how many instruments it carries and when the last tick arrived. Every existing figure (value, P&L, XIRR, day change, Action center) is already correct because it reads the same price columns; the live *screen* is P2-10a.

## Scope
**In:**
- New host `XDrishti.Feed` (container `xd-feed`): Dhan market-data WebSocket client (connect, batched subscribe/unsubscribe, reconnect with back-off, token handling), binary packet parser (little-endian), tick coalescer and price sink.
- The **active set** as it can exist in P2: open holdings that map to an NSE instrument, instruments of active price alerts, the benchmark index, and the context indices NIFTY 50, NIFTY BANK, INDIA VIX. Built from small providers (open positions and armed tickets plug in later); recomputed every minute and on a change signal; the feed applies the difference. Capped by `Live:MaxWsInstruments` (default 200).
- Price sink: latest price per instrument flushed every 2 s in one statement into the holdings (new source `live`) and into `market.live_quotes` for instruments that are not holdings.
- Price alerts evaluated on every flush with the P2-09 evaluator; the 5-minute check stays as the safety net.
- `NOTIFY live_prices` after a flush (at most once a second) — nobody listens until P2-10a.
- Services row **Live feed** (window 09:00–15:35 IST, trading days), heartbeat status, Pause/Resume, Run now.
- Compose service `xd-feed`, Dockerfile line, migration, config `Live:*`.

**Out (not in this slice):** SignalR, the hub, any frontend change except the generic Services row (P2-10a); live 1-minute bars and reconciliation with the EOD bars (first consumer is P3/P7); 20/200-level depth and tick recording (P13); open positions and armed tickets as members of the set (P7/P12); a pinned basket; BSE; changing the 5-minute `holdings-quotes` service (it stays and still owns `previous_close`); any trading action; a keep-awake assertion for the Mac (the owner keeps it awake, doc 17).

## Acceptance criteria
**Endpoint and credentials**
1. The allow-list accepts exactly `wss://api-feed.dhan.co` with the query keys `version`, `token`, `clientId`, `authType` only; any other host, scheme or query key is refused before a connection is opened (extends the existing guard tests). Token and client id come from the **primary Data account** through the same token port the quote and bar clients use, are never logged, never in an exception message, never stored. A refused token (401/403 on the upgrade, or a disconnect packet for a bad token) is reported as "the Data account's token was refused" and stops reconnect attempts until the token changes.
2. With no usable Data-account token the feed does not connect; the Services row shows `skipped — no usable token`; holdings quotes keep working as today.

**Parser and connection (unit tests on constructed packets)**
3. The parser decodes the 8-byte header (response code, message length, exchange segment, security id) and the packets used here — **ticker** (last price, last trade time), **quote** (last price, volume, open/high/low/close), **previous close** and **disconnect** (reason code). Unknown codes, a length that does not match, a zero or negative price and a trade time more than a day away are dropped and counted, never thrown; one malformed packet does not end the connection. Fixtures in `docs/research/dhan-samples/` are built from Dhan's documentation and marked UNVERIFIED until the owner's first real session.
4. Subscriptions are sent in messages of at most 100 instruments (ticker mode for indices and alert-only instruments, quote mode for holdings) and removed with the matching unsubscribe; a throttle of its own spaces the requests; a set over `Live:MaxWsInstruments` is cut in the order alerts, indices, holdings by value, with one warning.
5. Connection lifecycle (fake socket and clock): connects at the window start; reconnects after a drop with back-off (1, 2, 4 … 30 s, jittered) and resubscribes the whole set from the database; answers the server's ping and does not close on silence shorter than the server's timeout; no packet for 30 s in market hours counts as dead; never more than one connection; stops cleanly at the window end and on cancellation; more than 10 reconnects in 5 minutes pauses attempts for 5 minutes and raises the service failure notice.
6. Prices received between 09:00 and 09:15 are written (indicative) but never replace a quote of the same India day.

**Active set**
7. The builder is pure over providers: `HoldingsProvider`, `AlertsProvider`, `IndicesProvider`, each returning `(instrumentId, segment, securityId, mode, reason)`; the union is de-duplicated (reasons merged), ordered and cut at the cap. Tests: each provider, de-duplication, the cap, a retired or unmapped instrument, an index with no master row (skipped, one log line), an empty set (connects, subscribes nothing, state *Idle*).
8. A holding added, closed or synced, or an alert created, triggered or cancelled, changes the subscription within 60 s (`NOTIFY active_set_changed` makes it immediate; the one-minute recompute is the safety net); the difference is applied as subscribe/unsubscribe, not a reconnect.

**Price sink**
9. Ticks are coalesced in memory (latest per instrument; **Dhan's trade times are whole seconds, so a tick with the same time and a different price replaces the pending one and is written; same time and same price is a no-op**) and flushed every `Live:FlushSeconds` (default 2) in **one statement** per batch: every open holding of that instrument across accounts gets `last_price`, `last_price_at = tick time`, `last_price_source = live`; closed holdings and holdings with a newer price are untouched; `previous_close` is never written by the sink; nothing is written when nothing changed.
10. Source rules (extends ADR 0019 §3): a holdings sync keeps a quote or live price of the same India day; a quote never overwrites a live price newer than the quote's fetch time; a live price never overwrites a newer quote; `last_price_source` accepts `live`. The row version still protects against a sync and the sink overwriting each other; a lost write is retried once, then dropped for that flush with a log line.
11. Instruments that are not holdings are upserted into `market.live_quotes` (`UNLOGGED`, one row per instrument), emptied **once per window, when the supervisor opens the run** (not at each reconnect). No price history is stored.
12. A regression test shows a live tick moving value, P&L, day change and the summary consistently; **review amendment: for a holding priced by `live`, the previous close counts only when `previous_close_at` falls on the same India day as the price time — otherwise the holding is left out of day change and counted in `dayChangeExcluded` (a live price on top of a two-session-old close is never shown as the day's change, in the first minutes or when the quote poll fails)**; quote- and sync-priced rows behave as before; `GET /api/holdings` rows report `priceSource = live`.

**Alerts**
13. After each flush the active alerts of the flushed instruments are evaluated with the P2-09 evaluator (**ticks before 09:15 IST are written but trigger no alert; the Action-center reconcile after a flush is throttled to once a minute**): a level reached becomes `triggered` with the live price and time in the same transaction as its audit event, once (conditional update on `status = 'active'`), even if the 5-minute check runs at the same moment. Tests: hit, not hit, price past the level at the flush (documented: a spike between two flushes is not seen), double trigger, alert on an instrument with no tick.

**Notify**
14. After a flush that wrote something the feed sends `NOTIFY live_prices` (no payload), at most once a second; with no listener it is harmless.

**Service, health, operations**
15. The Services screen shows a **Live feed** row: `window` 09:00–15:35 IST on trading days (editable within bounds; the checks warn when it does not cover 09:15–15:30), runs as `xd-feed`, status healthy / running / failed / paused from the feed's heartbeat (`platform.service_heartbeats`, row `xd-feed`, every 15 s and also outside the window, in a `finally` so a failing look still beats: state, connected, subscribed count, last tick time, ticks per second, reconnects today; a last beat with state `stopped` on shutdown — the System status page treats it as benign for 10 minutes). Pause/Resume and Run now go through the existing desired-state path (the feed reads the schedule row each minute; no command channel to a process). History has one run per window with its end reason (window end, token refused, paused, flapping).
16. The container has a health check (a minimal probe, not the whole host: heartbeat within 60 s; timeout 10 s), `stop_grace_period: 30s`, `restart: unless-stopped`, starts with `deploy/xd-up.sh`, stops with `xd-down.sh`, ≤ 256 MB, hardened like the others, on `back` and `egress`, no published port; it gets the vault key and the app DB password as secret files only (no secret in an environment variable); the project has its `COPY` line in the Dockerfile.
17. Logging: `feed.connected|disconnected|subscribed|unsubscribed|flush|alert_triggered|token_refused|flapping|packet_dropped` with counts and instrument ids only — never a token, client id, URL query or raw packet; no line per tick. Heartbeat details carry ticks/s, flush time, dropped packets, reconnects.

## Contract
- **API:** none new (the Services list gains the row; `priceSource = live` is an existing field's new value); `scripts/gen-api.sh` if the services contract changes.
- **Data:** migration `AddLiveFeed`: `portfolio.holdings.last_price_source` CHECK widened to `sync|quote|live`; `market.live_quotes` (`instrument_id` pk, FK to the instruments, `last_price numeric(18,4)`, `tick_at timestamptz`, `UNLOGGED`); the **existing** `platform.service_heartbeats` (key `service_name`, `last_seen_at`) gains nullable `state` and `details jsonb` (no new table; the feed's row is `xd-feed`). Migration `AddPreviousCloseTime` (review amendment): `portfolio.holdings.previous_close_at timestamptz null`, set by the quote poll whenever it writes `previous_close`. Grants: `xd_app` may upsert `live_quotes` and `service_heartbeats`, update holdings as today; clearing `live_quotes` is a function owned by the owner role. `NOTIFY` channels `active_set_changed`, `live_prices`.
- **Services:** registers `live-feed` (type `window`, 09:00–15:35, tradingDaysOnly, runs as `xd-feed`, retries 0 — the connection manager is its own retry, no timeout, catch-up off, notify on failure on). `xd-api` shows and edits it but never starts it or closes its runs (a marker, `IExternallyRunService`, keeps it out of the scheduler, the runner and the interrupted-run recovery); `xd-feed` owns the run (a supervisor looks at the stored row every 15 s, opens its run row at the window start, closes it at the end). **Run now** writes the key `live-feed.manual_until` in `platform.app_settings` (now + `Live:ManualRunMinutes`, default 30, 1–240) and answers with a run of status `queued` and an empty id; the feed opens the run within 15 s and honours the marker even outside the window. **Pause** (or a save with the service off) clears the marker. The status on the Services row (healthy / running / failed / paused) comes from the stored row, the latest run and the `xd-feed` heartbeat (`ServiceView.live`).
- **Config:** `Live:MaxWsInstruments` (200), `Live:FlushSeconds` (2) — bounds-checked options, documented in doc 16.
- **UI:** none beyond the generic Services row.

## Non-functional
- **Latency:** tick received → price in the database ≤ 3 s at p95 for 100 instruments. **Load:** one connection, one UPDATE and one NOTIFY per flush whatever the tick rate; memory bounded by the set size.
- **Security:** token/client id leave the feed process only in the connection request to the allow-listed host; the vault key is a read-only secret file; the token port reads the primary Data account only; egress only for `xd-feed` and `xd-api`.
- **Resilience:** fail closed — on any doubt the feed stops writing prices and the 5-minute quotes carry on; a bad packet never changes a price.

## Test plan
- **Unit:** parser (every packet kind, truncated, wrong length, unknown code, bad price/time), subscription batcher, active-set providers and builder, cap ordering, coalescer, source precedence (live vs quote vs sync, same/earlier day), alert evaluation on flush, connection state machine (connect, drop, back-off, resubscribe, silence, flapping, token refused, window end, cancellation), heartbeat state.
- **Infrastructure:** WebSocket client against a local test server speaking binary frames (headers, query keys, no secrets in logs, allow-list refusals); the batched price statement; `live_quotes` upsert; listen/notify round trip.
- **Integration (TimescaleDB):** migration; two accounts holding one security get one live price; closed holding untouched; sync-after-live and quote-after-live; an alert triggers once under a concurrent 5-minute check; the services list shows `live-feed` with heartbeat state.
- **Architecture:** `XDrishti.Feed` depends on Application/Infrastructure through ports only, no reference to `Api`; the Portfolio module owns every price write; the allow-list guard covers the new URL.
- **Manual (owner), once, in market hours:** run with real holdings, compare 5 prices and the day change with the Dhan app, check the packet shapes and the previous-close meaning, cut the network for 30 s and watch the reconnect, then record sanitised packets into `docs/research/dhan-samples/` and flip UNVERIFIED.

## Open questions (owner) — answered 2026-10-14: all as written
1. **Split:** P2-10 (this) and P2-10a (hub and screen) — done.
2. **Streamed holdings:** all open holdings, cap 200; the 5-minute poll stays as safety net.
3. **Data plan and packets:** the first real session is the check (packets then replace the constructed fixtures).
4. **Pre-open:** record from 09:00, never replace a same-day quote; the screen labels it in P2-10a.
5. **Indices:** NIFTY BANK and INDIA VIX are streamed now and stored; no strip on the screen.
6. **Alerts:** checked on every flush.
7. **Mac sleep:** left to the owner (doc 17); `xd-up.sh` does not hold a keep-awake.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Migration `AddLiveFeed` added; `compose.yaml` and `Dockerfile` updated; contract regenerated if it changed
- [ ] Design docs updated (05 §5, 12 §6/§9, 03, 04 §3, 17, 16 `Live:*`, 15 §4 alert latency), ADR 0024 accepted, `docs/research/dhan-samples/README.md` updated
- [ ] Deployed; the owner runs one real market session and checks prices and a test alert against the Dhan app
