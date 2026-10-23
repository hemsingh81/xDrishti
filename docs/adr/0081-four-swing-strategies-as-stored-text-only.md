# 0081 — Four swing strategies as stored text only

- **Status:** Proposed (spec [P6-17b](../specs/P6-17b-remaining-strategies-swing-batch.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0079](0079-eight-more-intraday-strategies-as-stored-text-only.md) (the same route for the intraday batch), [ADR 0055](0055-the-strategy-file-its-schema-and-the-content-hash.md) (file and hash), [ADR 0056](0056-stored-strategies-the-lab-tables-the-seed-and-loading-by-version.md) (the seed, loading by id), [ADR 0034](0034-the-rules-expression-language-lint-and-compiler.md) (language, no lag on a call).

## Context
Doc 07 names eight swing strategies besides `BASE_BREAKOUT`. The owner's answer to question 9 of the P6 plan was to build first what needs no new feature. Some of the names carry a definition in their parentheses or in the glossary; others are a name only.

## Decision
1. **Stored text is the only source** (as ADR 0079): `TREND_PULLBACK`, `NR7_ID_BO`, `RSI2_REVERSION` and `HIGH_52W_MOMENTUM` are embedded seed files, version 1 by hash, no builder, no registry row. All are 1D, BUY only, entry `at_close`, one `FIXED_RR` exit with `rr` a parameter, `max_hold_sessions` 5, stop `swing_low`.
2. **Only the existing language and features.** `NR7_ID_BO` reads "yesterday was an inside day and the narrowest range of seven" with `inside_bar[1]` and the ranges of days 3 to 7 back (`min` of raw lags), because a lag on `nr(7)` is not written (ISS-24); day 2 back is the mother bar of an inside day, so it needs no term. `HIGH_52W_MOMENTUM` is `close >= highest(252, source=close)` with `close > close[1]` (a tie counts, a flat series does not fire) and no momentum filter, because doc 07 defines none.
3. **Three are deferred, not invented.** `RS_ROTATION` needs `sector_rs(n)` (not built). `BB_SQUEEZE` and `FAILED_BREAKOUT` are names only: a squeeze threshold relative to the bandwidth's own past and the level of a failed breakout are undefined and both need a lag on a catalog call. They wait for an owner definition or for P3-02f.
4. **Tests** are the P6-17a pattern on daily series: compile with no warning, a hand-checked series per strategy with stop, entry and target (the oracle), a negative series, for every part that matters a series where only that part is false (and without it a candidate; for the `NR7` range, one series per older session), and the cut-off and replaced-future identity. The eleven older hashes are unchanged.
5. The numbers chosen by the author (`ema_len`, `trend_len`, `rsi_max`, `high_len`, `rr`, stop lookbacks, the hold) are tunable parameters or text, judged through the ledger.

## Consequences
- Fifteen built-in families are seeded; each new one is another trial for the ledger and the experiment margin (P6-05, P6-12).
- Nothing says any of the four has an edge; the mechanics test says the text does what it says.

## Honest limits
- The rules are proposals read from names; the fabricated series prove mechanics only. A 2 R target for a five-session hold with a stop at a 3 to 5 session low is far in places; the exit reasons of the first runs decide whether that should change (a new version).
- Warm-up is long (50, 200 and 252 sessions); an instrument with less daily history is skipped as not ready. A daily bar is built from 1-minute bars, so a vendor chart can differ.
- The below-200-day half of `RSI2_REVERSION` (the SELL, an overbought RSI(2) under the average) is not built: swing SELL is futures only (doc 07 §7) and a swing strategy cannot list SELL.
- The pinned hashes are change detectors taken from the code's own output, not independently computed values.
- `RSI2_REVERSION` classically exits on a close above a short average; only `FIXED_RR` runs, so it is held to the target, the stop or five sessions.
