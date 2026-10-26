# 0133 — A new broker endpoint enters the allow-list in the commit of the slice that uses it; index and VIX history need none

- **Status:** Proposed (the owner delegated these three allow-list decisions on 2026-10-26: "check yourself and do the best"). Becomes Accepted when the owner has read it. **SAFETY decision: the broker allow-list (ADR 0097).**
- **Date:** 2026-10-27.
- **Relation to other ADRs:** [0097](0097-the-p7-phase-check-ten-consecutive-scheduled-sessions-from-stored-plan-runs-and-never-met-on-fewer.md) (pin and refused probes), [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) (market context), [0016](0016-market-bars-csv-load-dhan-fetch-and-app-managed-aggregates.md) §7 (the chart endpoint), [0024](0024-live-feed-xd-feed-and-live-prices.md) (the one socket).

## Context
Three specs asked for a change of the pinned broker allow-list: index and VIX history (P3-01d/e, fallback C: `POST /v2/charts/historical`), 20-level depth recording (P7-16: `wss://depth-api-feed.dhan.co/twentydepth`) and the day's trade and order books (P11-01: `GET /v2/trades`, `GET /v2/orders`). An allow-list entry only permits; but a permission with no user is attack surface with no benefit, and the pin test (`PlanPhaseCheck.PinnedAllowList`) is how the owner sees what the application may call.

## Decision
1. **Index and VIX history: no entry.** P3-01d (decision D7) derives the daily NIFTY 50 and INDIA VIX rows from the 1-minute bars of the chart endpoint that is already allowed (`POST /v2/charts/intraday`, segment `IDX_I`). The list does not change. `POST /v2/charts/historical` stays refused and its refused-probe test stays. It is added only if the owner's probe (P3-01d criterion 1) says outcome O2 or O3, in a separate slice with its own ADR.
2. **Each other entry is added in the commit of the slice that uses it**, with the pinned list, `Describe()`, the allow and refuse tests and the phase check in that same commit (ADR 0097). Decided now, on the merits:
   - **Depth (P7-16): accepted in principle.** Read-only market data on the Data account's token, one exact host and path, query keys exactly `authType, clientId, token`; recording from today is the only way to have six months of depth by P13. The service stays paused and the switch OFF (an owner act with a fresh password); no order, no order-capable scope, no reader of the data. Conditions of the owner's earlier answer (Q9, "not in P7") are lifted by the owner's later delegation; the P7 plan stays unchanged otherwise.
   - **Trade and order books (P11-01): accepted in principle**, read-only GET, exact paths, no query. `GET /v2/orders/{id}`, query variants, any `POST`/`PUT`/`DELETE` on `/v2/orders`, positions and funds stay refused and become refused probes in the same commit.
3. **No entry is added now.** P7-16 is a large slice behind a disk guard; P11-01 needs P11-04A and live allocations first. Adding their entries today would widen what the application may call with nothing using it.
4. **Never allowed, whatever the slice:** any endpoint that places, modifies or cancels an order, super/forever orders, positions, funds, a wildcard host or path, a redirect.

## Consequences
- The pinned list is unchanged by this ADR. The decisions are made, so the owner's Approved on P7-16 and P11-01 is the only act left for those two entries, and the phase check keeps listing exactly what the guard allows.
- P3-01d and P3-01e can be built now. Their live probe needs the owner's connected Dhan account and tracked indices; until then the context says `unavailable`.
