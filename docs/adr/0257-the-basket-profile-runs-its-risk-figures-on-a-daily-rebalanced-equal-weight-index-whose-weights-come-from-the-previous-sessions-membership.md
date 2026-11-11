# 0257 — The basket profile runs its risk figures on a daily-rebalanced equal-weight index whose weights come from the previous session's membership

- **Status:** Accepted (spec BK-06, approved by the owner 2026-11-03).
- **Date:** 2026-11-10.
- **Relation to other ADRs:** Builds on ADR 0102 (basket benchmarks: exclude, count and name, never repair) and the P8-07 buy-and-hold figure, which stays the headline return.

## Decision
1. Volatility, drawdown, beta, alpha, tracking error, capture, relative strength and the regime split run on one series: the daily-rebalanced equal-weight index, I(d) = I(d-1) x (1 + mean of r_i(d)), rebased to 100. Buy-and-hold weights drift, so its daily returns mix a changing portfolio; the rebalanced series has one meaning at each date. The buy-and-hold return is still reported (reused from `BuyAndHold`, not re-implemented) and contribution shares tie to it.
2. The return of day d uses the members of the membership as of d-1 (decided at the previous close). A member that joins on d counts from d+1; a member with no bar up to the window end is ignored entirely, so truncating the data or appending a later member changes no figure up to the cut.
3. An instrument-day with a move above the discontinuity threshold (the P8-07 setting) is left out of that day's mean, named and counted, never repaired. A day with fewer than the minimum members has no index point; the index is a number only when at least 90 % of days and the last day have enough members.
4. Every figure carries its honest state (`Metric`); a non-value state has no number. The pair correlation reuses the Pearson rule of `CorrelationTable` (P7-06), clamped to -1..1.
5. All of it is a pure Domain function (`BasketAnalytics.Profile`); defaults for the minimum samples live in `BasketProfileOptions` and are bound from settings by the caller (BK-07).

## Limits
A basket with one membership snapshot is constant in time and survivorship biased (ISS-22); the result says so (`MembershipBasis`). Risk-free rate is ignored. The effective number of bets is an equal-correlation heuristic.

## Owner decision 2026-11-11

Given in chat. The default benchmark of the basket profile is **Nifty 500** (not the portfolio's Nifty 50). Built in [BK-06c](../specs/BK-06c-nifty-500-default-benchmark-for-the-basket-profile.md): a basket-only setting, the portfolio benchmark (ADR 0020 §2) unchanged, and an honest `unavailable` state when Nifty 500 has no stored bars (never a silent fall-back to Nifty 50).

## Build note BK-06c (2026-11-11)

The default index is the basket setting `reports.basket_benchmark_instrument` (an instrument id; unset means Nifty 500), read through `IBasketBenchmark`; the portfolio benchmark (`portfolio.benchmark_instrument`, Nifty 50) is not read by the profile or the ranking. The index is found by canonical symbol `NSE:NIFTY_500` (`CanonicalSymbol.ForIndex("NIFTY 500")`) or the display name "Nifty 500", case-insensitively. A missing index or one without bars in the window makes every figure against it `unavailable` with the reason; there is no fall-back to Nifty 50, and a stored choice that left the master is reported missing rather than replaced. The cache key already holds the index id, so a change of default is never served from an old answer. `PUT /api/reports/basket-benchmark` (session and CSRF token, no password) stores the choice and audits it as `EssentialSettingChanged`.
