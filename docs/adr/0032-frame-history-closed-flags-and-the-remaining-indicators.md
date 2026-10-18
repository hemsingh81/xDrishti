# 0032 — The frame exposes closed flags and value history; the remaining indicators and patterns

- **Status:** Accepted (owner, 2026-10-16: "approved" on [P3-01b](../specs/P3-01b-frame-api-for-the-dsl-and-more-indicators.md), with the recommendations below) — amends [ADR 0031](0031-streaming-indicators-and-the-feature-frame.md) ("Amendment, final review": the API the DSL and the simulator need)
- **Date:** 2026-10-16 (built the same day: see *Consequences from the build* and *Amendments*)

## Context
The parser (P3-02) must evaluate conditions such as `crosses_above(close, vwap)`, `rising(rsi(14), 3)` and `close[1]` on the closing of a bar of a given timeframe, and the simulator (P3-03) must know when a bar closed. ADR 0031's frame returned only the current value of each feature, with no closed flag, no history, no guard against a repeated bar and no raw bar values. Doc 11 §2 also lists `adx macd bbands supertrend` and the patterns `inside_bar nr(n) engulfing pivot_high/low(n)`, which P3-01 left out.

## Decisions
1. **Closed flags.** `BarClosed(tf)`, `ClosedBarsAt(tf)` and `TryLastClosed(tf)` say what the last 1-minute step closed on each timeframe the frame was asked for (always true for 1m). A timeframe not asked for throws (`HasTimeframe(tf)` tells which are available). The flags describe the last step: false / 0 after a bar the ordering guard skipped and before the first step.
2. **Value history.** Each feature keeps the values it had after each of the last *HistoryDepth* closed bars of **its own timeframe** (default 8, maximum 256, sized once at creation); `At(handle, lag)` and `IsReadyAt(handle, lag)` read it. History is written only when the feature's timeframe closes a bar. This is what `close[1]`, crossings and `rising/falling` read; the DSL evaluates a condition only when its timeframe has closed a bar.

   *Note (2026-10-17, P3-02 close-out): for the twelve per-session features on an intraday timeframe (see [ADR 0035](0035-per-session-features-keep-the-history-of-the-session-only.md)) the history is the history of the current session only. `At(handle, lag)` and `IsReadyAt` give NaN and false for a lag that reaches back before the timeframe's first bar of the India date of the last accepted 1-minute bar, and for every lag, lag 0 included, from the first 1-minute bar of a new date until the timeframe closes its first bar of it; the ring of these features is also cleared when the date changes, not only written at a close. Every other feature, and everything on 1D, is read by position as written here.*
3. **Raw bar features** `open high low close volume` are catalog features: the value of the last closed bar of the timeframe (`tf("1D").close` is yesterday's close until today's daily bar closes).
4. **Ordering guard.** A 1-minute bar at or before the previous one is counted (`IgnoredBars`) and skipped by every lane, so the 1-minute lane and the builders cannot diverge.
5. **`PriceSource.Default`** (the zero value) resolves at creation to the catalog's default source (High for `highest`, Low for `lowest`, otherwise Close); the catalog collections are read-only.
6. **New indicators** (each output is its own feature): `adx plus_di minus_di` (Wilder), `macd_line macd_signal macd_hist`, `bb_upper bb_middle bb_lower bb_width` (population σ), `supertrend supertrend_dir` (TradingView-style: final bands that carry over, the flip decided against the *current* final band; Wilder ATR), `roc`.
7. **New patterns and structure:** `inside_bar`, `nr(n)` (strictly the smallest range of the last n bars), `engulfing` (on bodies; +1/−1/0), `pivot_high(n)`/`pivot_low(n)` (the most recent *confirmed* swing point: strictly above/below the n bars before **and** after, confirmed when the n-th later bar has closed, so it is look-ahead-safe and arrives n bars late) with `pivot_high_age(n)`/`pivot_low_age(n)`.
8. **Conventions vs Skender** are asserted exactly in tests (never absorbed by tolerance) and listed in "Consequences from the build": only `supertrend` differs.
9. **Out of this slice (P3-01c):** regime and market-context features (index, VIX, breadth series) and the 1W/1M builders. *Built in P3-01c, [ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md).*

## Consequences
- The parser can compile every building block of doc 11 §2 except regime/context and weekly timeframes.
- A pivot is known only n bars after it formed; strategies that use swing stops see them late by design.
- History costs features × depth doubles (≈ 16 KB for 250 features at depth 8).
- The catalog grows from 19 to **44** names; the DSL parser (P3-02) maps its spellings (`bbands`, `crosses_above`, `close[1]`, `tf("1D").close`) onto them and onto `At(handle, lag)`.

## Consequences from the build
What was learnt while building [P3-01b](../specs/P3-01b-frame-api-for-the-dsl-and-more-indicators.md); the deviations from the spec are listed under *Amendments* below.

**The catalog has 44 names**, 25 of them new:
- trend and momentum: `adx`, `plus_di`, `minus_di` (default n = 14), `macd_line`, `macd_signal`, `macd_hist` (12/26/9, `fast` below `slow`), `bb_upper`, `bb_middle`, `bb_lower`, `bb_width` (n = 20, k = 2), `supertrend`, `supertrend_dir` (n = 10, mult = 3), `roc` (n = 10);
- patterns and structure: `inside_bar`, `nr` (n ≥ 2, default 7), `engulfing`, `pivot_high`, `pivot_low`, `pivot_high_age`, `pivot_low_age` (n ≥ 1, default 5);
- raw bar features: `open`, `high`, `low`, `close`, `volume`.

Every new entry is `Continuous` (runs across sessions) and has at most 3 parameters. Warm-up, in bars of the feature's own timeframe: `plus_di` / `minus_di` n+1, `adx` 2n, `macd_line` slow, `macd_signal` / `macd_hist` slow+signal−1,
`bb_*` n, `supertrend*` n, `roc` n+1, `inside_bar` and `engulfing` 2, `nr` n, raw bar features 1. The pivots declare `2n+1`, the earliest bar at which a pivot can be confirmed, but they are ready from the first
**confirmed** pivot on, which depends on the data (`IsReady` stays exactly "the value is not NaN", asserted separately for them). The spec's "23 new names (total 42)" was wrong: it left out `pivot_high_age` and
`pivot_low_age` and its arithmetic did not add up; the build has 25 and 44.

**Skender.Stock.Indicators 2.7.3 cross-check.** `adx`, `plus_di` and `minus_di`, `macd_*` (periods (3,6,4), (12,26,9), (5,35,5)), `bb_*` (with the population σ; Skender's `Width` is (upper − lower)/middle) and `roc`
are identical to 1e-9 on 24 seeded series, with the same seeds and the same readiness, so there is nothing to document for them. `supertrend` differs in **three** conventions, each asserted exactly:
- **First value.** Ours is at bar n; Skender's is at bar n+1. The cause is the ATR seed already described in ADR 0031 (ours is the mean of TR₁ … TRₙ, Skender's the mean of TR₂ … TRₙ₊₁). The relation is pinned: at the first
  bar both have a value (bar n+1), when both are up on the lower band, `ours − Skender = mult × (ATR_Skender − ATR_ours)`. When the two seeds are the same number the two lines coincide from bar n+1.
- **Start direction.** Skender starts up when `close ≥ (high + low)/2` and down otherwise; ours always starts up. When the two start differently they differ over an initial stretch and agree exactly from the first bar where one of them flips onto the other.
- **Tie-break.** A close exactly on the final lower band while up turns Skender's trend down (`<=`); ours and TradingView's stay up (strict `<`). Down to up is strict (`>`) in both.
The rest of the flip rule below is the same in both, so on series where the ATR seeds coincide, the start direction agrees and no close lands exactly on a final lower band, ours equals Skender's line and direction bar by bar.

**Supertrend flip rule.** The final bands are computed first (the final upper band is the basic upper band when that is below the previous final upper band or the previous close was above it, else the previous final
upper band; mirrored for the lower), then the direction: while down it turns up when the close is above the **current** final upper band, while up it turns down when the close is below the **current** final lower band.
The value is the final lower band while up and the final upper band while down. It starts at bar n with the direction up. This is the TradingView and Skender rule.

**Bollinger σ is exact and O(n).** The population standard deviation (÷ n) is a second pass over the window of n values at each update, with the mean from a compensated running sum. The O(1) identity
Σx² − n·mean² cancels catastrophically when a window is tight against a large price, and a flat window must give exactly 0. The price is speed: 53 M updates/s for
`bb_upper(20, 2)`, still five times the 10 M target.

**History and the closed flags.** The history of a lane is a ring of the values its features had after each of its own closed bars; it is written once per closed bar, so a minute that closes two buckets writes two entries (the late
one first), and nothing is written between closes. `At(handle, lag)` throws `ArgumentOutOfRangeException` for a lag outside `0 .. HistoryDepth − 1` (a programming error, never a silent NaN) and gives NaN, with
`IsReadyAt` false, when fewer than `lag + 1` bars have closed or the feature was not ready then. `BarClosed` and `ClosedBarsAt` reflect the **last** step: after a bar the ordering guard skipped they are false / 0 on every
timeframe, and before the first step too; `TryLastClosed` keeps the last bar the timeframe closed. `Steps` counts accepted bars only; a skipped bar goes to `IgnoredBars`. `Reset` clears the history, the flags and the guard. *(Close-out, 2026-10-17: for the per-session features on an intraday timeframe the ring is also cleared when the India date changes, and they are not ready between that change and the timeframe's first bar of the date, see the note under decision 2.)*

**`PriceSource`** is now `{ Default = 0, Close, Open, High, Low, Volume }`. `Default` is resolved at creation to the catalog's default and is the same feature as the explicit default source (two such specs share one
instance and one history); it is not an error to leave it. `Volume` is only read by the `volume` feature, a spec naming it as the source of another indicator is refused. `FeatureSpec.Source` defaults to `Default`,
so `new FeatureSpec("highest", P1: 20)` is `highest(20, high)`. The numeric value of `Close` changed (it was the zero value); nothing persists it or reads it yet (no project references `XDrishti.Indicators` except its tests; the `PriceSource` of the portfolio domain is another type).

**`FrameOptions` and the overloads.** `Create(specs, FrameOptions?)` takes `FrameOptions(Calendar, HistoryDepth = 8)`; a depth outside 1..256 is a validation error on `options.historyDepth`, reported together with any bad spec.
The earlier `Create(specs, TradingCalendar?)` stays. `Create(specs, null)` with a literal `null` is ambiguous between the two; pass a typed variable or a name. `IndicatorCatalog.All`, `SupportedTimeframes` and
`IndicatorInfo.Parameters` are read-only collections.

**Architecture tests.** `IndicatorsArchitectureTests` now also read the project file: the `XDrishti.Indicators.csproj` may contain only the `ProjectReference` to `XDrishti.Domain` and no `PackageReference`, which the
compiled references could hide (a reference the compiler never used). A self-test proves the reader sees the references of the test project.

**Test gaps of the P3-01 review, closed here:** the allocation test over a series with gaps and zero volumes (and with a calendar), the builder reference run with a calendar, a culture test (under a culture with a comma as the decimal separator and another minus sign, the frame's values are bit-identical and its validation messages and
`TestSpecs.Describe` are unchanged) and the name of an RSI test.

**Benchmarks.** Same harness and command as ADR 0031 (`--filter-class "*ThroughputBenchmarks*"`, explicit tests; the new ones are `New_indicator_updates_per_second` and `Twenty_feature_frame_with_history_step_in_microseconds`).
Best of 5 passes of 1,000,000 steps (after a warm-up pass), Apple-silicon Mac, Release, through `IIndicator`:

| Feature | Million updates/s | Feature | Million updates/s |
|---------|------------------:|---------|------------------:|
| `adx(14)` | 127 | `supertrend(10, 3)` | 87 |
| `plus_di(14)` | 101 | `supertrend_dir(10, 3)` | 77 |
| `minus_di(14)` | 72 | `roc(10)` | 194 |
| `macd_line(12,26,9)` | 155 | `inside_bar` | 114 |
| `macd_signal(12,26,9)` | 113 | `nr(7)` | 83 |
| `macd_hist(12,26,9)` | 157 | `engulfing` | 109 |
| `bb_upper(20, 2)` | 53 | `pivot_high(5)` | 86 |
| `bb_middle(20, 2)` | 90 | `pivot_low(5)` | 67 |
| `bb_lower(20, 2)` | 54 | `pivot_high_age(5)` | 72 |
| `bb_width(20, 2)` | 74 | `pivot_low_age(5)` | 66 |
| `close` | 201 | | |

A **20-feature frame** (1m, 5m, 15m, 1h and 1D, history depth 8) runs at 10.7 M steps/s, **0.094 µs per 1-minute step** (target: 1 µs); the allocation test also runs a 35-feature frame. The earlier **12-feature frame** went
from 0.045 to 0.060 µs per step; the difference, about 0.015 µs (roughly 30% of the old step time), is the cost of writing the history on every closed bar.

## Amendments (2026-10-16)
Deviations of the build from the sketch in [P3-01b](../specs/P3-01b-frame-api-for-the-dsl-and-more-indicators.md); the guarantees of the acceptance criteria are unchanged.
1. **25 new names, 44 in total** instead of the spec's 23 and 42 (see above). The spec's arithmetic was off and `pivot_high_age` / `pivot_low_age` were counted in neither number.
2. **The Supertrend flip rule is the current-band rule.** The spec's Definitions said the direction flips on the close beyond the *previous* final band. The owner had approved a "TradingView-style" Supertrend, and
   TradingView (and Skender) decide the direction against the *current* final band, after the bands of the bar are final. The build follows the approval, not the literal sentence; the spec's text is corrected and the
   Skender differences are three (first value at bar n versus n+1, the start direction, and the strict tie-break on the lower band).
3. **Throws instead of silent NaN/false.** A timeframe the frame was not asked for (`BarClosed`, `ClosedBarsAt`, `TryLastClosed`) and a lag outside `0 .. HistoryDepth − 1` (`At`, `IsReadyAt`) throw
   `ArgumentOutOfRangeException`: a wrong timeframe or lag is a bug in the caller (the parser will size both from the strategy) and must not read as a quiet "false". `HasTimeframe(tf)` was added so the parser can ask first.
4. **`Steps` counts accepted bars**; bars skipped by the ordering guard are in `IgnoredBars` only (the spec did not say).
5. **`PriceSource.Volume`** added, usable only through the `volume` feature; the order of the enum is `Default, Close, Open, High, Low, Volume` (the sketch listed Close last).
6. **Bollinger σ is O(n) per update, not O(1)**, for exactness (see above).
7. **Parameter bounds the spec left open:** `bb` `k` in 0..100 and `supertrend` `mult` in 0..1000, each also above 0 (a value of 0 is refused by the extra check on `specs[i].p2`); `macd` `fast ≥ slow` is refused on `specs[i].p1`;
   `nr` `n ≥ 2`; `pivot_*` `n ≥ 1`; every period at most 10,000.
8. **Pivot warm-up.** The catalog declares `2n+1` (the earliest possible), not a bar by which the feature is certainly ready. The measured-first-ready-bar test checks equality for the pivots on a series whose first pivot is
   the earliest possible one, and a second test checks on a random series that a pivot is never ready before the declared bar and is a number from its first value on; every other entry is checked for equality on every timeframe.

Documents updated: [doc 03 §4.1](../design/03-target-architecture.md), [doc 11 §2](../design/11-strategy-lab.md), the [roadmap](../design/18-roadmap.md), and "Amendment, final review" of [ADR 0031](0031-streaming-indicators-and-the-feature-frame.md).

## Amendment (2026-10-16, P3-01c)
[ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) builds what decision 9 left out: the 1W and 1M timeframes and the calendar and market-context features (12 new names, 56 in all); `FrameOptions` gains `Context` and `ExpiryDays` after `HistoryDepth`. `BarClosed(M1)` still throws for a frame that has no feature on 1m (follow-up 4 of the P3-01b spec), except that a frame with a date-driven feature always has a 1m lane.

## Amendment (2026-10-17, P3-02 close-out)
[ADR 0035](0035-per-session-features-keep-the-history-of-the-session-only.md) narrows decision 2 for the twelve per-session features on the intraday timeframes (1m, 5m, 10m, 15m, 30m, 1h), and criterion 2 of [P3-01b](../specs/P3-01b-frame-api-for-the-dsl-and-more-indicators.md) ("across session boundaries") with it: their value history is the current session's only and they are not ready between a change of the India date and the timeframe's first bar of it; see the note under decision 2. Nothing else here changes.
