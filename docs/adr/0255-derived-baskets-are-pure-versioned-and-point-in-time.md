# 0255 — Derived baskets are pure, versioned and point in time

- **Status:** Accepted (spec BK-02, approved by the owner 2026-11-03).
- **Date:** 2026-11-10.
- **Relation to other ADRs:** Builds on [0102](0102-basket-benchmarks-are-an-equal-weight-price-return-hold-over-members-as-of-one-stated-date-with-every-exclusion-named.md) ("every exclusion named" is applied to every instrument left out). Storage and scheduling are BK-03; the screen is BK-04.

## Decision
1. The fourteen derived baskets are computed by one pure engine, `Domain/Baskets/Derived`: arrays in, a `DerivedResult` out. No I/O, no clock, no network, no logging, no order-named member (an Architecture test pins it). An intraday basket without a formation time is refused with a `Result` failure, not computed from end-of-day data.
2. **Point in time.** The engine reads daily bars dated at or before `as_of`, and intraday bars strictly before the formation time (09:20 IST for gappers, 09:30 for the opening range). A later bar never changes the result (property test over all fourteen). Flags (ASM/GSM, ban, series) are read as known at `as_of`; a missing flag is not a pass.
3. **Named exclusions.** `corporate_action_unresolved`, `insufficient_history`, `bad_data` (non-finite, close of zero or less), `not_in_universe_filter` (basket 13 only), `small_cluster` (basket 14). Members + below-threshold + excluded = the universe.
4. **Parameters are versioned data.** `DerivedParams` holds every number with its default, rejects an unknown key or an out-of-range value at construction, and exposes a SHA-256 hash of the canonical JSON (sorted keys, version included). Defaults are never tuned on a strategy's result.
5. **Selection.** Ranking is by score with ties by symbol ascending. Top-k baskets keep incumbents while rank is within floor(1.5 x k) (best k of them), fill free slots in rank order, and slow (weekly, monthly) baskets keep a member added fewer than 5 sessions ago even outside the buffer. An instrument that leaves the universe, is excluded, or fails the basket's gate leaves at once; the dwell never blocks that.
6. **Median, not mean, for liquidity**; momentum skips the last 21 sessions (12-1, with 6-1 and volatility-adjusted variants as a parameter); mean-reverting needs three legs; clusters are market-neutral (stock return minus Nifty 50 return), Spearman on winsorised residuals, average linkage cut to k, named by majority industry, stability by adjusted Rand index against the previous result.

## Limits
- Sessions held are counted by the caller (one more at each evaluation); the engine has no calendar.
- Basket 14 needs every residual date of the window: an instrument with a missing day in it is `insufficient_history`.
- Basket 1 reports ATR% only (the secondary `rv_20` is not carried); basket 13 judges zero-volume minutes over the whole window, not per session.
- Ranking on the window a strategy is later tested on is in-sample; BK-04 shows the warning.
