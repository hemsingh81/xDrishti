# 07 — Strategies & Signals

## 1. Strategy anatomy

| Part | Description | Example (Opening Range Breakout) |
|------|-------------|----------------------------------|
| `id` | Unique name | `ORB` |
| `styles` | intraday / swing | intraday |
| `timeframes` | Allowed entry timeframes | 5m, 15m, 30m |
| `sides` | BUY / SELL | both |
| `setup` | Conditions that must hold when the trigger fires. Evaluated **together with the trigger** at every closed bar of the evaluation timeframe and ANDed with it (the nightly shortlist of P7 classifies which parts of a setup are knowable at the previous close; the model does not) | High relative volume yesterday, NR7, trend filter, `time >= "09:45"` |
| `trigger` | What must happen to enter, evaluated at every closed bar | Candle closes above OR-high (BUY) |
| `time_rules` | The one place for windows: `first_entry` and `last_entry` (the bar that closes at those times is still allowed, both ends inclusive), `square_off` (intraday, required) or `max_hold_sessions` (swing, 1 to 5, required). Replaces the former `valid_window`; a `time >= "09:45"` inside a condition stays legal | 09:30–11:30, square-off 15:15 |
| `initial_stop` | Where the idea is wrong: `fixed_pct`, `atr_mult`, `swing_low` / `swing_high` (the lowest low / highest high of the last N closed bars of the stop timeframe, trigger bar included), `bar_low` / `bar_high`, `or_opposite`; read on the evaluation timeframe or on an optional timeframe of its own | OR opposite side / trigger-candle extreme / k × ATR |
| `filters` | Context gates, by name: `not_event_day`, `min_turnover` (accepted and not evaluated until the results calendar and the liquidity data exist) | Higher-TF trend agrees; not results day |
| `invalidation` | The `invalidate` list: cancel if any of it is true. Covers "gap beyond trigger" (a condition on `today.open` against a level); "stop level hit before trigger" cannot be written as a condition and is not covered | Gap beyond trigger |
| `params` | ≤ 4 tunable values | OR length, ATR multiple |

Built-in strategies are written in the same rules language users use in the Strategy Lab
([doc 11](11-strategy-lab.md)), so there is one way to define a strategy.

**As built (P3-02b, [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md)).** The anatomy is a typed model (`XDrishti.Strategies.Model`, schema 1) that `StrategyCompiler.Compile` checks and compiles side by side. **How the
sections combine:** at every closed bar of the evaluation timeframe the sections are evaluated together, and an entry is possible when `setup` is true, `trigger` is true and `invalidate` is not true (unknown counts as
not true, and an unknown invalidation does not fire either; `SideEvaluation.Fires` is the one definition). A stored strategy is **self-contained**: a missing time rule is not filled from the `styles` block of
[doc 16](16-configuration.md), so the same stored text always means the same behaviour. Each condition of a list is compiled and evaluated as its own condition; the lists are never joined into one text. An intraday
strategy lists 5m to 1h, a swing strategy 30m, 1h and 1D, and swing SELL is refused (futures only, §7). The simulator runs a swing strategy evaluated on 30m or 1h (P3-03d, [ADR 0040](../adr/0040-swing-positions-held-across-sessions-the-maximum-hold-the-position-gate-and-simtrade-schema-3.md)): the position is held up to `max_hold_sessions` sessions with at most one open position per instrument and side, so a signal that fires while a position is open is counted and not entered, and it still spends that day's one candidate; the daily (1D) evaluation is P3-03e.

## 2. Conditional trade plans

Intraday entries can't be fixed the night before, so each plan item is a **conditional ticket** — and the
simulator tests exactly this ticket on history:

```
BUY RELIANCE | INTRADAY | 15m | ORB         basket: Best stocks   account: dhan_main
  Trigger : 15m candle CLOSE above 2,948.0 between 09:30 and 11:30
  Entry   : at trigger-candle close (limit +0.1% max)
  Stop    : 2,921.5 (OR low) → risk/share 26.5
  Exit    : 50% at 1R, stop → breakeven, trail rest ATR(15m)×2; square-off 15:15
  Size    : 37 shares (risk ₹980 = 0.49% of capital)
  P(win)  : 0.57 (n=214, regime UP_LOW_VOL)   E[R]: +0.38 after costs
  Cancel  : open > 2,960 (gap > 0.5 ATR) or 2,921.5 touched before trigger
  Why     : NR7 yesterday, rel-volume 1.8×, 1h & 1D uptrend, sector strongest
```

Swing tickets from daily bars: signal known at close; trigger typically "buy-stop above today's high
tomorrow" or "enter at open if gap within limits".

In the simulator (P3-03e, [ADR 0043](../adr/0043-the-daily-evaluation-and-the-overnight-entry-order-sim-5-and-run-record-schema-5.md)) the second form is built and the first is not: a 1D strategy is evaluated at the step of the 15:29 minute, its order rests overnight and fills at the first minute that holds a trade of the next session, within five minutes of its open and within the 0.1 % limit of the daily close (a gap above the limit is a counted miss, a gap through the stop is no trade). A buy-stop above the day's high, which lives across bars, is not run yet.

The ticket is what the planner (P7) shows; the model above evaluates the same conditions at every closed bar. What an `at_close` entry fills at ("at trigger-candle close, limit +0.1 %") is the simulator's
decision (P3-03), not the model's.

**As the simulator realises the ticket (P3-03b, [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md); the first stage of P3-03, [ADR 0037](../adr/0037-simulation-conventions-and-the-two-stage-seam.md) decisions D3, D4, D5 and D7, filled the entry at the open with exact prices and is superseded by it).** The simulator walks the entry and the exit of an intraday ticket on 1-minute bars with **modelled fills**, and says so on every trade and in the header of a run (`fills: modelled`, or `fills: exact` when the slippage is switched off; `entry limit: +0.1 %`; `costs: none`). Costs are P3-03c.
- *Entry.* "At trigger-candle close, limit +0.1 % max" cannot be filled at the close: the bar has closed. The entry is a **limit order** placed when the trigger bar has closed, at the trigger close +0.1 % rounded down to the instrument's tick (a sell: −0.1 %, up). It lives for the first minute that holds a trade at or after the decision instant (at most five minutes after it, on the same day, before the square-off) and fills at that minute's **open** when the open is within the limit, at the **limit** when the minute trades back to it, and otherwise is a counted miss (`LimitNotReached`), never filled later; a miss spends the day's ticket. No such minute, or one at or after the square-off, is no trade (`NoFillBar`, `AfterSquareOff`). A fill at or through the stop is no trade (`EntryThroughStop`). A minute with volume 0 holds no trade. All are counted.
- *Stop and target.* Risk per unit is `|entry − stop|` of the exact entry; with `FIXED_RR` the target is the entry plus `rr` times that risk (minus for a sell). Touching a level counts. **Stop first:** a minute that holds both is a stop, unless its open is already beyond the target (the first print decides for the target). A stop that the market gapped through fills at that minute's **open**; a target fills at the target and never better. After an entry at the limit only the stop is checked in the fill minute.
- *Slippage and the tick.* Market-type fills (the entry at the open, stop, square-off and end-of-data exits) pay `(1 + extra) ticks + 5 % of the minute's range` against the trade (2 extra ticks on stops: doc 16), rounded on the instrument's tick grid; a resting limit (the entry at the limit, the target) does not slip. Every trade carries the exact result (`RExact`) beside the modelled one (`RFilled`) and their difference (`SlippageR`).
- *Square-off.* The always-on `SESSION_EXIT`: the first minute that holds a trade and starts at or after the square-off time (15:15) leaves at its open; if that open is already beyond the stop or the target, that level is what fills. A position still open when the day's data ends leaves at the last close and is
  reported apart (`EndOfData`), never among the trades that statistics count.
- *Result.* R, MAE and MFE in units of the risk; the minute in which the position leaves counts as ADR 0038 decision D5b says (stop first is assumed because the path inside a minute is unknown). The partial exit, the break-even
  move and the trail of the ticket above are P4 (the entry and the exit are separate stages so that exit policies can be compared on the same entries).

**As the scanner realises the trigger (P3-03a, [ADR 0037](../adr/0037-simulation-conventions-and-the-two-stage-seam.md) Amendment 1).** The entry scanner is the first stage: it asks the sections only at a step where a bar of the evaluation timeframe closed, so "the trigger is true at 09:45" means the 09:30 bar closed at 09:45
and the order can only be placed in the minute that follows. It makes **at most one candidate per instrument, side and day: the first**; a stop-out does not arm the pattern again, and BUY and SELL are independent (both can have
a candidate on one day). An `invalidate` that turns true cancels the day's entries not yet taken, and one that turns true after the entry changes nothing. A bar closed late by the first minute of the next date is never traded.
The scanner makes a candidate with the stop level and the reason (the conditions and the values they read), nothing about the exit; the counters say why a rule did not fire (the day was outside the window, the setup was false,
the trigger came twice, the invalidation was unknown, the stop had no value yet). The simulator runs `at_close` entries and the stop at the opposite end of the opening range only; the other entry kinds and stops are refused by name (doc 11 §3).

## 3. Starter library

Each mirrored for SELL where sensible. Most cells will fail qualification — expected and healthy.

**Intraday (5m–1h):** `ORB` (opening-range breakout) · `PDH_PDL_BREAK` / `PDH_PDL_REJECT` (previous-day
high/low) · `VWAP_PULLBACK` · `VWAP_RECLAIM` · `EMA_PULLBACK` (9/21 EMA with higher-TF trend) ·
`GAP_GO` / `GAP_FILL` · `INSIDE_BAR_BO` · `CPR` (central pivot range) · `MEAN_REV_BANDS`.

**Swing (1h/1D, ≤ 5 sessions):** `BASE_BREAKOUT` · `TREND_PULLBACK` (20 EMA / 50 DMA) · `NR7_ID_BO` ·
`RSI2_REVERSION` (above/below 200 DMA) · `BB_SQUEEZE` · `HIGH_52W_MOMENTUM` · `FAILED_BREAKOUT` ·
`RS_ROTATION` (strongest stocks in strongest sectors).

**Per-session levels in the rules language.** The intraday cells are built on levels of the session (`ORB`: `or_high` / `or_low`; `PDH_PDL_*` and `GAP_*`: `prev_day.*`, `gap_pct`; `VWAP_*`: `vwap`). These features keep the history of the session only ([ADR 0035](../adr/0035-per-session-features-keep-the-history-of-the-session-only.md), [doc 11](11-strategy-lab.md) §2 *Lags*), so a crossing of one of them (`crosses_above(close, vwap)`) is unknown at the first evaluation of a session and can first be true at the second bar of the timeframe, and unknown never fires an entry; a break through yesterday's high or low is written in the level form (`close > prev_day.high and close[1] <= prev_day.high`), which is known from the first bar and also catches a gap through the level. A valid window such as the `ORB` 09:30–11:30 (a `time` comparison) is a trading rule, not a guard the language needs to stay correct. On 1D (the swing cells) nothing restarts and a lag is by position; on 1h the intraday rule holds.

**`ORB` v1 as built (P3-03a, `OrbV1.Create(orMinutes = 15, rewardRisk = 2.0)` in `XDrishti.Strategies.BuiltIn`).** The first rule set the simulator runs, approved by the owner on 2026-10-18 as a **bare breakout**: the filters of the ticket above (NR7 yesterday, relative volume, trend) are added one at a time after the first hand check of its trades. Id `ORB`, intraday, evaluated on 5m or 15m (the 10m and 30m cells do not compile with a 15-minute opening range: not applicable), BUY and SELL, one parameter `or_min = 15`, entry at the close of the trigger bar (realised as above: a limit order at +0.1 %, filled in the next minute), no setup, no invalidate, no filters, the stop on the opposite end of the opening range (`or_opposite`, no buffer), exactly one `FIXED_RR` exit with `rr = 2.0` (the square-off at 15:15 is always on), entries accepted from 09:30 to 11:30.

| Side | Trigger (one condition) | Stop |
|------|-------------------------|------|
| BUY | `close > or_high(or_min) and close[1] <= or_high(or_min)` | `or_low(or_min)` |
| SELL (written out by hand; without a `sell:` block the mirror of doc 11 P3-02e would give the same text) | `close < or_low(or_min) and close[1] >= or_low(or_min)` | `or_high(or_min)` |

A 15-minute bar closes above the high of the first 15 minutes while the bar before it did not (SELL: below the low). The level form is used because `or_high` keeps the history of the session only (above): the level is read at lag 0 and
the lagged series is the close, so the condition is known from the first bar of the day, and a trigger that is false on the first bar is False, not unknown. On 15m the opening range is the first bar; `or_min = 30` is two bars, and
`or_min = 20` is refused on 15m (not a whole number of bars). The target is two times the risk by default and the walker fills the entry in the minute after the decision (at the open or at the +0.1 % limit), so a breakout that gaps through the opposite
end of the range at that open is no trade (`EntryThroughStop`, counted) and one that opens above the limit and does not trade back to it is a miss (`LimitNotReached`, counted). A figure from the simulator for this strategy is a signal-level result per instrument, in-sample, with modelled fills (the entry limit of +0.1 %, slippage on the tick grid; ADR 0038) and no costs.
The builder is the interim source of the built-in `ORB` until P3-02d seeds it as stored text (ADR 0002); the stored text and its hash then replace it as the version.

**`VWAP_PULLBACK` as built (P3-05, `VwapPullback.Create(pullAtr = 0.5, rewardRisk = 2.0)` in `XDrishti.Strategies.BuiltIn`).** The second rule set the simulator runs. **It is the owner's proposal**, approved as written for the first run on 2026-10-19 and open to change after the first look at its trades; nothing about it is evidence of an edge (passing the hand check says the mechanics are right). Id `VWAP_PULLBACK`, intraday, **15m only**, BUY and SELL both written out by hand, two parameters: `pull_atr` (default 0.5, declared range `0 < x <= 3`, [ADR 0041](../adr/0041-the-simulate-verb-the-export-library-and-the-pipeline-in-the-host.md) Amendment 2) and `rr` (default 2, as for ORB). Entries are accepted from 10:00 (the first bar at which the two lags of a session-restarting feature can be known) to 14:30; the position leaves at 15:15 at the latest.

| Part | BUY (SELL is the mirror: comparison reversed, `high` for `low`, `-` for `+`) |
|------|------|
| Setup 0, hourly trend | `tf("1h").ema(20) > tf("1h").ema(50)` |
| Setup 1, daily trend | `tf("1D").close > tf("1D").ema(20)` (the last closed day) |
| Trigger 0, the trigger bar | `close > vwap and close > open` |
| Trigger 1, two closes on the right side | `close[1] > vwap[1] and close[2] > vwap[2]` |
| Trigger 2, the dip | `low[1] < low[2] and low[1] <= vwap[1] + pull_atr * atr(14)` |
| Stop | `swing_low` over 2 bars (the lower of the dip bar's and the trigger bar's lows), no buffer |
| Exit | one `FIXED_RR`, `rr` times the risk |

Reading: bars *t*-2 and *t*-1 closed above the session VWAP; bar *t*-1 is the dip bar, which made a lower low and came back to within `pull_atr` ATRs of VWAP (it may go below it and close above); bar *t* closed above VWAP and above its own open. The lower low is the draft's addition to the roadmap's one-line sketch (without it a steady climb counts as a pullback); the stop at the pullback low replaces the sketch's `bar_low` (the trigger bar's own low alone is too tight and sits above the level the pattern says must hold). There is no lower bound on the dip: a long wick far below VWAP that closes above it gives a wide stop and a distant target. Only the level form is used (`vwap` keeps the history of the session only, ADR 0035), so the lagged items are unknown on the first two bars and the first bar that can be known is bar 3, decided at 10:00. On 15m **`vwap` is the volume-weighted mean of the 15-minute bars' typical prices**, not the minute-by-minute VWAP of a charting platform, and the engine's ATR may be seeded differently from a chart's: compare a trade with a chart by recomputing the engine's numbers, not by reading the chart. The hourly buckets start at 09:15 and a partial bucket from 15:15 is a bar of its own (seven hourly bars a session), so `tf("1h").ema(50)` is ready after about seven sessions and `tf("1D").ema(20)` after twenty: the first possible trade of a replay is on session 21, and the scanner's counters show the cold start as `NotReady`. A lagged item that is combined with a known False (`low[1] < low[2]` on bar 1 or 2, where the `low` history is continuous across sessions) is False, not unknown, because a False conjunct decides an `and`. The ATR stop and stop buffer that the roadmap row listed for this slice are not built (this rule set needs neither; they would be a separate slice with its own ADR). The builder is the interim source of the built-in until P3-02d.

**`BASE_BREAKOUT` v1 as built (P3-06, `BaseBreakoutV1.Create(baseLen = 20, basePct = 8, volX = 1.5, rewardRisk = 2.0)` in `XDrishti.Strategies.BuiltIn`).** The third rule set the simulator runs, and the first **swing** one. **It is the owner's proposal**, approved as written for the first run on 2026-10-19 and open to change after the first look at its trades (about half a day to reopen); nothing about it is evidence of an edge. Id `BASE_BREAKOUT`, swing, **1D only**, BUY only (no `sell` block), evaluated once a day at the step that closes the 1D bar (the 15:29 minute, decision instant 15:30). Four parameters, each with a declared range ([ADR 0041](../adr/0041-the-simulate-verb-the-export-library-and-the-pipeline-in-the-host.md) Amendment 2): `base_len` (20, `10 <= x <= 60`), `base_pct` (8, `2 <= x <= 20`), `vol_x` (1.5, `1 <= x <= 5`) and `rr` (2, `0 < x <= 10`). The entry is the next session's open within the +0.1 % limit of the signal close (a gap above it is a counted miss, a gap to or through the stop is `EntryThroughStop`), the position is held at most five sessions (`max_hold_sessions = 5`, always on, not a parameter) and a second signal while a position is open is `PositionOpen`.

| Part | Text |
|------|------|
| Setup 0, weekly trend | `tf("1W").close > tf("1W").sma(10)` |
| Setup 1, daily trend | `close > sma(50)` |
| Setup 2, a tight base | `(highest(base_len, source=close) - lowest(base_len, source=close)) / lowest(base_len, source=close) * 100 <= base_pct` |
| Setup 3, volume | `rel_volume(20) >= vol_x` |
| Trigger, the breakout | `close >= highest(base_len, source=close)` |
| Stop | `swing_low`, `lookback = base_len`, no buffer: the lowest low of the last `base_len` sessions, the signal day included |
| Exit | one `FIXED_RR`, `rr` times the risk, plus the always-on `MAX_HOLD` of five sessions |

Reading: the closes of the last `base_len` sessions, today's included, span at most `base_pct` percent and today's close is the top of that window (a tie counts), on at least `vol_x` times the mean volume of the 20 sessions before. `highest(n)` includes the current bar and a lag cannot be written on a call (ISS-24), so there is no spelling of "above the previous N-day high": the base is measured on the window that ends today, which costs a breakout that itself lifts the close by more than `base_pct` percent above the window's lowest close. **Ties count**: a close equal to the window's high triggers if the other rules hold, so a flat series passes R3 and R5 (it is stopped by the two trend gates, which are strict; a short flat run after a rise is a candidate on a close that is above nothing, row P12 of the spec). With a stop at the base's own low the risk is as wide as the base and a target of 2R is far for five sessions: the exit reasons of the first run are what decides whether the stop should be `bar_low` instead (a new version). **The weekly gate is weekday-dependent (observed, row W1 to W3 of the spec).** With a trading calendar a weekly bar closes at the very step that closes the last session of the week, so on that day `tf("1W").close` is the close of the week of the decision and on every other day it is the previous week's; without a calendar the week is emitted at the first bar of the next week and a Friday decision sees the previous week. The same pattern is therefore a candidate on a Friday and not on a Thursday (when Friday is a holiday Thursday is the last session and is the Friday case). The replay supplies the calendar, so a verb run behaves as the calendar rows. **Warm-up:** the first fully ready decision is the fiftieth session of the stored data (a Friday when the data starts on a Monday), so about 11 weeks of daily history are needed and an instrument with fewer sessions is skipped `features_not_ready`; the daily bar is built from the 1-minute bars, so its close is the close of the last minute that holds a trade and its volume the sum of the minutes, which can differ from a vendor chart. A 15:29 minute that is missing makes the daily bar close late: it is stale, counted in `StaleBucketSteps`, and the signal is lost. Every real run is `--gross` (`costs: none`) until the owner's charge sheet passes its check; a swing trade is costed in the delivery segment. The check of its trades by the owner needs the signal day and the base on a daily chart and a hand recompute of the five rules: the card draws the fill day only (P3-04d is only proposed). The builder is the interim source of the built-in until P3-02d.

**The intraday batch as built (P6-17a, [ADR 0079](../adr/0079-eight-more-intraday-strategies-as-stored-text-only.md)).** Eight more intraday strategies exist as stored text only (seed files, no C# builder): `PDH_PDL_BREAK`, `PDH_PDL_REJECT`, `VWAP_RECLAIM`, `EMA_PULLBACK`, `GAP_GO`, `GAP_FILL`, `INSIDE_BAR_BO` and `MEAN_REV_BANDS`, all 15m, BUY and SELL written out, one `FIXED_RR` exit with `rr` a parameter, square-off 15:15, built from the existing features only. Their rule sets are the author's reading of the one-line names above and are proposals; the tests ([spec](../specs/P6-17a-remaining-strategies-intraday-batch.md)) show the mechanics on fabricated days, nothing about an edge. `GAP_FILL` is a gap fade that exits on `FIXED_RR`, not a fill-to-previous-close target, and `PDH_PDL_BREAK` takes entries from 10:00 on purpose (a gap through the level at the open never fires it). `CPR` is deferred: no document defines it. The swing batch is P6-17b.

**The swing batch as built (P6-17b, [ADR 0081](../adr/0081-four-swing-strategies-as-stored-text-only.md)).** Four more swing strategies exist as stored text only, all 1D, BUY only, entry at the close and fill at the next open, one `FIXED_RR` exit (`rr` a parameter), a hold of at most 5 sessions, stop at the lowest low of the last N sessions: `TREND_PULLBACK` (`ema(20) > sma(50)`; the low reaches `ema(20)` and the close is back above it and above the open), `NR7_ID_BO` (yesterday an inside day and the strictly narrowest range of seven; close above yesterday's high), `RSI2_REVERSION` (`close > sma(200)` and `rsi(2) < 10`) and `HIGH_52W_MOMENTUM` (close at or above the highest close of 252 sessions and above the previous close). The rule sets are the author's reading of the names and proposals; the tests ([spec](../specs/P6-17b-remaining-strategies-swing-batch.md)) show the mechanics on fabricated daily series, nothing about an edge. Deferred: `RS_ROTATION` (needs `sector_rs`), `BB_SQUEEZE` and `FAILED_BREAKOUT` (no definition in any document, and both need a lag on a catalog call, ISS-24).

## 4. Multi-timeframe mapping

| Entry TF | Bias TFs | Typical style |
|----------|----------|---------------|
| 5m | 15m, 1h | Intraday |
| 10m | 30m, 1h | Intraday |
| 15m | 1h, 1D | Intraday |
| 30m | 1h, 1D | Intraday / short swing |
| 1h | 1D | Intraday / swing |
| 1D | 1W | Swing |

Confluence (same symbol & side on several timeframes/strategies) is a feature and a selection bonus.

## 5. Feature catalogue (point-in-time)

| Group | Examples |
|-------|----------|
| Trend | EMA slopes (9/21/50/200), price vs EMAs, ADX, swing structure, supertrend |
| Momentum | RSI(2/14), ROC, MACD histogram, distance from N-day high/low |
| Volatility | ATR %, ATR percentile, Bollinger bandwidth, NR4/NR7, inside bars, gap in ATR |
| Volume | Relative volume (same time-of-day), delivery %, OI change |
| Levels | Distance to previous-day H/L/C, CPR width, VWAP distance, 52-week high/low, round numbers |
| Relative strength | Stock vs index, stock vs sector, sector vs index (5/20/60 days) |
| Market context | Index trend/ADX, VIX level/percentile/change, breadth — **built in P3-01c** as the features `index_trend`, `index_adx`, `index_vol_pct`, `vix`, `vix_pct`, `vix_chg`, `breadth`, `regime_trend`, `regime_vol`, `regime`: daily, as of the previous session ([doc 11](11-strategy-lab.md) §2, [ADR 0033](../adr/0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md)); the computation of breadth follows in P3-01e and the index and VIX series in P3-01d (both after the P3 exit check, see the [roadmap](18-roadmap.md)); `sector_rs` is not planned until a strategy needs it |
| Calendar | Weekday, time of day, expiry day/week, pre/post holiday, days to results — `day_of_week` and `is_expiry_day` (the dates are passed in by the caller) are built; the rest follow |
| Plan geometry | Stop distance in ATR, room to next obstacle in R, achievable R:R |

## 6. Market regime

Daily label from index data: **trend** (UP / DOWN / RANGE via DMA slope + ADX) × **volatility** (LOW /
NORMAL / HIGH via VIX and ATR percentiles) → up to 9 regimes. Used to condition statistics and as a feature.

**As built (P3-01c, [ADR 0033](../adr/0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md)).** The label of a day is computed from the regime index (Nifty 50 by default) and the VIX, from that day and
earlier days only, and it is read **as of the previous session**: every minute of a date *D* sees the label of the latest day strictly before *D*, so the regime never contains the close of the day being traded and a
back-test cannot look ahead through it. The numbers are `ContextOptions` (defaults below; later trading configuration in the database, [ADR 0002](../adr/0002-config-and-strategies-in-app.md)).

| Part | Rule |
|------|------|
| Trend | **UP (+1)** when the index close is above its 50-day average, the average is higher than 5 days ago and ADX(14) ≥ 20; **DOWN (−1)** for the mirror image (close below the average, the average lower, ADX ≥ 20); otherwise **RANGE (0)**. Ready from day 55 |
| Volatility | score = the mean of two percentile ranks over the last 252 days — the VIX close and the index's ATR(14) as a percent of its close (ties take the middle of their span: `100 × (#below + 0.5 × #equal) / 252`). **LOW (0)** below 100/3 (33.33…), **HIGH (2)** from 200/3 (66.67…), **NORMAL (1)** in between. When the VIX percentile is not ready (a day without a VIX, or fewer than 252 VIX days) the index percentile alone is the score. Ready from day 265. *Pending owner confirmation, ADR 0033 Amendments 1 and 2 (the VIX window and the fallback) and 13 (the exact thirds; the owner's wording was 33.3 and 66.7).* |
| Code | `regime = (1 − trend) × 3 + volatility + 1` |

| Trend ↓ / volatility → | LOW | NORMAL | HIGH |
|------------------------|----:|-------:|-----:|
| UP | 1 `UP_LOW_VOL` | 2 `UP_NORMAL_VOL` | 3 `UP_HIGH_VOL` |
| RANGE | 4 `RANGE_LOW_VOL` | 5 `RANGE_NORMAL_VOL` | 6 `RANGE_HIGH_VOL` |
| DOWN | 7 `DOWN_LOW_VOL` | 8 `DOWN_NORMAL_VOL` | 9 `DOWN_HIGH_VOL` |

Breadth is carried next to the regime (feature `breadth`) but is not part of it; it feeds the market gate ([doc 09](09-learning-engine.md), P7). One regime index and the VIX only: Bank Nifty and the sector indices are not
part of the regime. The configuration inputs are in [doc 16](16-configuration.md).

## 7. Side specifics

- BUY and SELL are learned separately (different speed, stops, regimes).
- Intraday SELL: cash (MIS) or futures; swing SELL: futures only.
- Circuit-locked stocks excluded on the locked side.
