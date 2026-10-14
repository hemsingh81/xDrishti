# P2-10a — SignalR hub and the live Portfolio screen

- **Status:** Approved (owner, 2026-10-14: "approved as written", split from P2-10) · decision [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md) (Accepted) · needs [P2-10](P2-10-live-feed-xd-feed-active-set-and-prices.md) built
- **Phase / roadmap row:** P2 — exit criterion "live P&L updates during market hours" · after P2-10
- **Design refs:** docs/design/03-target-architecture.md §6 (SignalR `/hubs/events`), 12-portfolio-monitoring.md §6, 13-reports-and-ui.md (Portfolio screen), [ADR 0019](../adr/0019-holdings-quotes-day-change-and-the-first-interval-service.md) §6

## Goal
The open Portfolio screen shows prices, value, P&L and day change moving within a few seconds of the exchange without the owner doing anything, says honestly whether it is *Live*, *Delayed* or on the 5-minute quotes, and keeps working through the once-a-minute re-read when the hub is not available.

## Scope
**In:**
- SignalR hub `/hubs/events` in `xd-api` (session cookie, owner only) and a listener on `NOTIFY live_prices` that pushes what changed to the connected screens that asked for it.
- `GET /api/live/status` (feed state, subscribed count, last tick) as the REST twin of the hub's status message and the fallback when the hub is down.
- Caddy: `/hubs` proxied with WebSocket upgrade.
- Frontend: a `shared/realtime` module (one connection per tab), the Portfolio hub hook, incremental recompute of rows, group rows and tiles, the prices-line chip, the row flash, the "not live" note, fallback to polling.

**Out (not in this slice):** anything in the feed itself (P2-10); live bars; notifications other than prices and status (the hub is built so alerts, job progress and nightly status can use it later); a market strip for the indices; positions.

## Acceptance criteria
**Hub**
1. `/hubs/events` requires the owner's session (no cookie → 401; same idle timeout; a session that ends is disconnected), same origin only, no anonymous methods; through Caddy a WebSocket upgrade works (proxy config check in the tests/`scripts/check.sh`).
2. The screen calls `Subscribe(scope)` with the visible account/bucket scope and the holding ids it shows (and `Unsubscribe` on change or leave); the server pushes `pricesChanged {asOf, instruments: [{instrumentId, lastPrice, priceAt, source}]}` only for what that connection subscribed to. On `NOTIFY live_prices` the API reads what changed since its last push and sends it; a burst is coalesced to at most one message per second; payload ≤ 500 instruments and ≤ 32 KB (larger scopes are split).
3. `feedStatus {state, subscribed, lastTickAt}` is pushed on connect, on change and every 15 s; `GET /api/live/status` returns the same fields from the heartbeat; both are added to `ApiJsonContext`; the hub message types are generated from a typed contract file by `scripts/gen-api.sh` (OpenAPI cannot describe them).

**Screen**
4. The Portfolio screen opens the hub while the page is visible and the market is open (IST clock + trading day from the timeline) and closes it otherwise.
5. Each `pricesChanged` is applied in the browser to rows, group rows and tiles with the same pure functions as the server (value, P&L, day change, weight); a test proves the client result equals `GET /api/holdings/summary` and `/groups` for the same prices (property-style over random prices); the screen re-reads from the API every 5 minutes and on reconnect to stay exact.
6. No flicker of the grid; sort, selected row, scroll position and the open drawer are kept; a changed price flashes its cell for one second with an arrow as well as colour, and not at all under `prefers-reduced-motion`.
7. Prices-line chip, from the hub status and the services list, not the browser clock alone (IST): **Live** (a tick for the shown holdings ≤ 15 s old in market hours: "Live · 10:35:42 IST"), **Pre-open** (09:00–09:15), **Delayed** (feed connected but the last tick for the shown holdings older than 60 s in market hours; shows the age), **Quotes** (feed off, paused or failed: the existing 5-minute text of ADR 0019), **Closed** ("As of 15:30 · close"). The text changes at most once a second and is never a live region.
8. A holding with no live tick (not subscribed — over the cap, unmapped, BSE) keeps its quote or synced price, shows its own price time and source in the drawer, and the summary note says "N holdings are not live".
9. If the hub cannot connect (feed off, proxy, network) the screen falls back to the existing once-a-minute re-read, the chip says so, reconnect uses back-off, and no error dialog appears; when the hub returns the screen re-reads once and switches back.
10. Loading, empty and error states unchanged; a11y (labels on the chip, flash not the only signal, keyboard unchanged); 360 px → 4K.

**Operations**
11. The hub adds no secret to logs; connection and subscription events log counts only; the listener reconnects to the database by itself after a restart; `xd-api` healthy without the feed.

## Contract
- **API:** SignalR `/hubs/events`: server methods `Subscribe(scope)`, `Unsubscribe`; client events `pricesChanged`, `feedStatus`. REST `GET /api/live/status`. `scripts/gen-api.sh`.
- **Data:** none (reads `portfolio.holdings`, `market.live_quotes`, `platform.service_heartbeats` from P2-10).
- **UI:** `features/portfolio` (hook, recompute, chip, flash), `shared/realtime` (connection), `@microsoft/signalr` pinned exactly and loaded lazily with the Portfolio chunk; prototype `docs/prototype` Portfolio screen for the live look.
- **Proxy:** `deploy/caddy/Caddyfile` `/hubs/*` with upgrade.

## Non-functional
- Tick → browser ≤ 3 s at p95 end to end (P2-10's 3 s to the database is not additive: the push waits for the next flush at most 1 s after it); incremental recompute for 500 holdings < 16 ms; Portfolio chunk growth ≤ 20 KB gzip; initial bundle ≤ 200 KB.
- Security: owner session only, same origin, bounded payloads, no per-user data other than prices of what the screen shows.

## Test plan
- **Unit/frontend (Vitest + a fake hub):** recompute equals the server for random prices; chip states by clock/status; flash and reduced motion; drawer, selection, sort kept; fallback to polling and back; scope subscribe/unsubscribe on filter change; 5-minute exact re-read.
- **Integration:** hub 401 without session, `Subscribe` scoping, coalescing, payload bounds and splitting, listener restart, status endpoint; proxy upgrade check.
- **Architecture:** the hub depends on abstractions; no Api → Feed reference.
- **Manual (owner), in a market session with P2-10:** watch prices, chip states and the fallback with the feed stopped (`podman stop xd-feed`).

## Open questions (owner) — answered 2026-10-14: all as written
None open; the questions of P2-10 (pre-open labelled, indices stored only, no market strip) apply.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); Caddyfile updated
- [ ] Design docs updated (03 §6, 12 §6/§10, 13 Portfolio screen), ADR 0024 followed
- [ ] Deployed; the owner watches a real session and compares the Live chip, prices and day change with the Dhan app
