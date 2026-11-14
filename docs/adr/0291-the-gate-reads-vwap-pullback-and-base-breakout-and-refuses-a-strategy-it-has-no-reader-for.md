# 0291 — The gate reads VWAP_PULLBACK and BASE_BREAKOUT, and refuses a strategy it has no reader for (P3-09e)

- **Status:** Accepted (owner delegated, 2026-11-14). Amends [ADR 0048](0048-the-automated-gate-replaces-the-owners-chart-check-as-the-p3-exit.md) (the exit-code table and the "cannot read" consequence).
- **Date:** 2026-11-14.
- **Relation to other ADRs:** ADR 0037, 0038 (fills), 0040 (swing hold, X8), 0043 (the resting order, E8, E9), 0047 (the oracle); specs P3-05 and P3-06 (the rule tables).

## Context
The first Round B gate (`p3-roundb-1`, 25 rows) was `BLOCKED_DEFECT` on every row with "reader: no trade". `XDrishti.SimReader` read only ORB; the machine claimed a defect where it simply could not read. That is a tooling gap, and the 0048 consequence "a gate on a strategy the reader cannot read is BLOCKED_DEFECT" was wrong for a strategy the reader will never know.

## Decision
1. **Two readers** in `XDrishti.SimReader`, written from the rule tables of the specs only, with no engine, strategy or indicator code (project references and the architecture tests unchanged). The fill and exit walk of ORB (ADR 0038 E2 to E6, X1 to X5) moved into shared internal helpers; the ORB reading is unchanged (its 35 tests are green).
   - `VwapPullbackReader`: 15-minute bars anchored at 09:15; VWAP of the bars' typical prices, volume-weighted, per session; ATR(14) Wilder over all sessions in a row, seeded with the mean of the first 14 true ranges; setup `tf("1h").ema(20) > ema(50)` (hourly buckets 09:15, 10:15, ... and the 15:15 stub, so 7 a session; a bucket closed at the very step of the decision counts) and `tf("1D").close > ema(20)` on the previous session; trigger items per P3-05 D3; window 10:00 to 14:30; stop = lowest low of the dip and trigger bars, used as given; limit +0.1 %, 5 minutes, square-off 15:15; the first candidate of the day is the only one.
   - `BaseBreakoutReader`: daily bars from the 1-minute bars; setup and trigger per P3-06 D1 (weekly gate, 50-day average, base span, relative volume, closing high with a tie); stop = lowest low of the last `base_len` sessions; the order rests overnight and fills in the first five minutes of the next session (09:20 fills, a later minute does not); a hold of at most 5 sessions, the fill session being the first, leaving at the close of the due session's last minute with a trade, after the stop and the target of that minute.
2. **History.** The readers need sessions before the trade date (and, for the swing trade, after it). The assisted read loads them from the stored bars (130 calendar days back; BASE_BREAKOUT also 14 days forward) with one range query per row. The second source still gives only the row's own date: it replaces that session, and the history stays stored. A difference in a history day therefore cannot show as `data_sensitive`; the bar comparison cites only the signal date for BASE_BREAKOUT, and the minutes from the open to the trigger for VWAP_PULLBACK.
3. **Unsupported strategy.** `AssistRunner` (so `--assist` and `--gate`) refuses a pack with a strategy outside ORB, VWAP_PULLBACK and BASE_BREAKOUT **before any read or write**: exit **2** (unusable input), the text "unsupported strategy: the gate has no independent reader for X", no gate file. A future strategy gets a reader first; it is never `BLOCKED_DEFECT` (12).
4. **Exit codes of `verify-pack --gate`:** 0 PASS or PASS_WITH_DATA_FINDINGS; 10 RERUN_NEEDED; 11 DATA_REPAIR_NEEDED; 12 BLOCKED_DEFECT; **2 unusable input, now also an unsupported strategy**.

## Interpretations taken where the documents are ambiguous (not resolved from the engine code)
- **Daily bar:** open of the first minute, high and low over the session, close of the last minute that holds a trade (P3-06 D7), volume the sum.
- **Weekly bar (P3-06 D6):** the week of the signal is closed, and counts as the last closed week, only when the signal session is the last of its week, taken as "the next session in the data is in a later ISO week" (a holiday on the Friday makes the Thursday the last). Fewer than 10 closed weeks, 50 sessions, or 21 sessions for the volume mean: no trade.
- **`rel_volume`:** today's volume against the mean of the 20 sessions before; a mean of 0 is no trade.
- **Calendar:** a session is a date with bars. A weekday with no stored bars is not counted in the five-session hold (the engine counts the calendar's sessions; a stored hole could differ, and would show as a difference to be judged).
- **Hourly buckets and the VWAP bars:** built from the minutes present, closing at the first minute present at or after the bucket's last minute (the ORB rule), the 15:15 stub closing at the 15:29 step.
- **Stale daily bar:** a signal session without its 15:29 minute loses the signal (P3-06 D5).
- **Position gate** (a candidate while a position is open): not modelled; the rows are trades the engine made.
- **EMA seed:** SMA-seeded, started at the first session loaded (about 90 sessions), so the averages have converged for the sign tests used; the engine starts at the first stored day.

## The honest limit
As ADR 0048: the readers, the oracle and the engine come from the same rule tables and the same model family. A shared misreading stays invisible; a history day wrong in the stored bars is invisible to the second source (point 2).

## Consequences
- The Round B pack can be gated; a difference between a reader and the engine is now a real finding to be judged against the specs.
- `IStoredDays` gains `GetRangeAsync` (the host and the test fakes); `ReaderExitReason.MaxHold` is new.
- No API, table or migration.

## Tested by
`VwapPullbackReaderTests` and `BaseBreakoutReaderTests` in `XDrishti.SimReader.Tests`; `GateRunnerTests` (unsupported strategy, exit 2, nothing written); `SimReaderArchitectureTests`.
