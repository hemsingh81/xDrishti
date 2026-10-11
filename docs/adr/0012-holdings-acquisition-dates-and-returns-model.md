# 0012 — Holdings: owner-set acquisition dates, lot model, auto refresh and returns

- **Status:** Accepted (owner, 2026-10-11) — replaces the "dates come from Dhan automatically" intent of [ADR 0011](0011-default-portfolio-bucket-per-account.md) §History date
- **Date:** 2026-10-11

## Context
Returns (profit/loss in ₹ and %, XIRR) need the date and size of every cash flow. Dhan's `GET /v2/holdings` gives quantity, **average cost** and last traded price but **no purchase date**; trade history and the ledger exist but do not reliably cover holdings that were bought elsewhere or long ago. The owner decided to supply the dates at import, with sensible defaults, and to keep holdings fresh with an automatic refresh.

## Decision
1. **Import with dates.** The owner previews the holdings fetched from Dhan and sets an **acquisition date** per holding (or one date for all); the default is **1 Jan 2025** when the account has no holdings in the system yet. Dates can be edited later per lot. Dates cannot be in the future.
2. **Lot model.** Each holding has lots `(acquired_on, qty, unit_cost)`. The first import creates one lot per holding from Dhan's total quantity and average cost. Later refreshes: a **new holding** gets one lot dated **today (IST)**; a **quantity increase** adds a lot dated today (unit cost = the implied price of the added quantity from the change in average cost, falling back to the last price); a **decrease** closes lots oldest-first (FIFO) at the last price and marks that realised amount **estimated**; a holding that disappears is closed the same way. Average cost and quantity always match Dhan after a refresh.
3. **Auto refresh** is a per-account setting: *Off*, *Every N minutes during market hours* (N ∈ 5/15/30/60; Mon–Fri 09:15–15:30 IST), or *Daily at HH:MM IST*. A hosted service in `xd-api` runs the same command as the manual **Sync now**; it needs a connected account (otherwise it records one "not connected" run per day, no spam); one run per account at a time.
4. **Returns.** Per holding and for any group (account, bucket, total): invested = Σ lot qty × unit cost; value = qty × last price; unrealised P&L ₹ and %; **XIRR** on the lots' dated outflows and a terminal inflow of today's value (closed lots add their dated sale inflow); realised P&L (estimated where marked). XIRR is shown as "—" when the span is under 30 days or undefined and is annualised otherwise; the solver (Newton with bisection fallback) is verified against spreadsheet XIRR. Dividends and charges are **not** included in v1 and the UI says so.
5. **Not guessed:** holdings that cannot be mapped to the instrument master (e.g. BSE-only) are kept and flagged "not in master list"; they still get returns from Dhan's price.
6. **Later:** an optional "derive dates from my Dhan trade history" helper may pre-fill dates where trades exist (separate story); live prices from the feed (P2-07) replace snapshot prices.

## Consequences
- XIRR/returns are only as accurate as the dates the owner enters; the screen shows which lots carry the default date so they can be corrected.
- Sells between two refreshes are estimated at the last price of the refresh that noticed them.
- Safeguard: a sync whose answer would close all or most open holdings (empty list, or more than half and at least 3) is not applied without the owner's confirmation (manual: `409 portfolio.confirmation_required` with the counts, then `{confirm: true}` echoing them back, `409 portfolio.confirmation_stale` if Dhan's answer changed meanwhile; automatic: recorded as skipped, once per India day), because a false "empty" would close and reopen holdings dated today and destroy the acquisition dates.
- The previous P1-16e note ("dates come from Dhan automatically") and doc 12 §3 are updated.
