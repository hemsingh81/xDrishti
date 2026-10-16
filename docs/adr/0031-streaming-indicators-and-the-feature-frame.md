# 0031 — Streaming indicators and the feature frame: closed bars only, one code path

- **Status:** Accepted (owner, 2026-10-15: "approved, go with your recommendations" on [P3-01](../specs/P3-01-streaming-indicators-and-feature-frame.md)) — implements design doc 03 §3 ("own streaming implementations, cross-checked against Skender.Stock.Indicators") and the look-ahead rules of doc 11 §2 and doc 19; builds on [ADR 0016](0016-market-bars-csv-load-dhan-fetch-and-app-managed-aggregates.md) (aggregation rules) and [ADR 0017](0017-trading-calendar-eod-schedule-quality-adjustments-and-csv-undo.md)
- **Date:** 2026-10-15 (built the same day: see *Consequences from the build* and *Amendments*)

## Context
Phase 3 needs indicators and multi-timeframe features that behave identically in back-tests and in live trading, are fast enough for tens of millions of 1-minute bars, and cannot see the future. The data layer already stores 1-minute bars and builds the other timeframes in the application with fixed rules (09:15 anchoring, a partial 15:15 hour, clipped last buckets, a `bars` count and a `complete` flag).

## Decisions
1. **A pure engine module `XDrishti.Indicators`** (no I/O; references only Domain for `Bar`, `Timeframe`, `SessionClock`, `IndiaTime`), enforced by architecture tests. Indicators are **streaming**: they take one closed 1-minute bar at a time, keep O(1) state (O(window) for rolling windows) and allocate nothing in steady state. A value that is not ready is `NaN` with `IsReady = false`; the DSL treats it as a false condition.
2. **Closed bars only, by construction.** Higher-timeframe bars are built by a `TimeframeBuilder` that emits a bucket only when its last minute has arrived (or when a later bucket/session proves it will not), with the stored aggregates' rules (differences on real data are listed in design doc 05 §4: a later same-date bar after the 1D close, a calendar that disagrees with the data, no flush at the end of a stream). A `FeatureFrame` exposes, for each requested feature, the value as of the **last closed bar of its timeframe** — never the bar still forming. The guarantee is tested from two sides: frame-semantics tests (the frame never shows the forming bar; the `CloseIndex` of every bucket equals the independent reference) and a *prefix property* (output for the first *k* bars equals the output for those bars when more are fed) over random series, which guards against hidden state and dependence on later input but cannot by itself see a forming-bar leak.
3. **Definitions** (pinned by hand-calculated examples and independent references): `sma` (running sum with Neumaier compensation), `ema` (α = 2/(n+1), seeded with the SMA of the first n), `rsi` and `atr` (Wilder, seeded with the simple mean of the first n changes / true ranges), `highest`/`lowest` (rolling, current bar included), `vwap` (typical price (H+L+C)/3 × volume, reset each India trading date), `rel_volume` (volume ÷ mean of the previous n bars, current bar excluded), opening range (first m minutes of the session) and session context (previous session that had bars, today's open/high/low so far, gap percentage).
4. **Rolling indicators run continuously across sessions** on the series they are fed (an intraday EMA(20) at 09:30 uses the previous day's bars), as charting tools and the doc 11 examples assume; `vwap`, the opening range and the session context reset per India trading date. A per-spec reset option is a later addition.
5. **Incomplete higher-timeframe bars are emitted with `complete = false` and are consumed by indicators.** A missing minute must not silence a strategy for an hour; the simulator reports how many incomplete bars it used.
6. **Cross-checks:** every indicator is compared with an independent batch reference written in the test project; `sma`, `ema`, `rsi`, `atr` are also compared with **Skender.Stock.Indicators** (MIT), a **test-only**, exactly pinned dependency. Convention differences (seeding) are documented, not hidden by loose tolerances.
7. **Scope of this slice:** ADX, MACD, Bollinger bands, Supertrend, candle patterns, pivots, regime and market-context features follow in P3-01b and later (built in P3-01b, [ADR 0032](0032-frame-history-closed-flags-and-the-remaining-indicators.md), except the regime and market-context features and the weekly and monthly builders, built in P3-01c, [ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md)); the DSL (P3-02) and the simulator (P3-03) read from the frame and the catalog.

## Consequences
- One code path for back-tests and live; the look-ahead property is a test, not a convention.
- Intraday indicators carry yesterday's state into today's open; strategies that need a clean session use the session-anchored features (`vwap`, opening range, `today.*`).
- Bars are fed as given: corporate-action adjustment (ADR 0017 §5) is the caller's decision, so a back-test that wants point-in-time prices must say which adjustment it feeds.

## Consequences from the build
What was learnt while building [P3-01](../specs/P3-01-streaming-indicators-and-feature-frame.md); the deviations from the spec are listed under *Amendments* below.

**The library.** `XDrishti.Indicators` references `XDrishti.Domain` only. The catalog has **19 names**: `sma`, `ema`, `rsi`, `atr`, `highest`, `lowest`, `vwap`, `rel_volume`, `or_high`, `or_low`, `prev_day_open`, `prev_day_high`,
`prev_day_low`, `prev_day_close`, `prev_day_volume`, `today_open`, `today_high`, `today_low` and `gap_pct`. The session context is a set of **flat catalog names** (one indicator instance per value), not a nested `prev_day.*`; the
DSL parser (P3-02) maps its spelling (`prev_day.high`) to them.

**Skender.Stock.Indicators** is pinned to the exact version **2.7.3** in `Directory.Packages.props` and referenced only by `XDrishti.Indicators.Tests`. The version was chosen at implementation time (the spec asked only for "an
exact version"). Over 24 seeded series and periods 2, 5, 14 and 20 the comparison is within 1e-9 for `sma`, `ema` and `rsi`, and for `atr` once the seed difference is accounted for. The two differences are conventions, pinned
by tests and not hidden by a tolerance:
- **ATR seed.** Skender has no true range for the first bar and seeds the average with the mean of TR₂ … TRₙ₊₁, so its first value is at bar n+1. We use TR₁ = high − low and seed with the mean of TR₁ … TRₙ, so ours is ready at
  bar n. Both then follow the same recursion `atr = (atr·(n−1) + TR)/n` on the same true ranges, so the difference between the two series shrinks by exactly (n−1)/n per bar; the test asserts that relation. When the two seeds
  happen to be equal numbers the series coincide from bar n+1.
- **RSI when both averages are 0** (nothing moved over the window). Skender reports 100; we report 50 (a series that never moves is neutral, not overbought). `sma`, `ema` and every other RSI value are identical.

**`TimeframeBuilder`.** `Push` returns how many bars the minute closed, 0, 1 or 2, with the bars in two `out` parameters: one minute can close the late previous bucket (a later bucket began and its last minute never arrived) and,
being alone in a one-minute window at the end of its own, also its own. An optional `TradingCalendar` (on the builder and on the frame) lets a special session close its last bucket and its day at **its own last minute**; without
it only the regular 15:30 close is known, so the last bucket and the day of a special session are closed only by the first bar of a later bucket or date. Two counters make data problems visible: `TimeframeBuilder.Ignored` (bars dropped because they were late, see below) and
`FeatureFrame.IncompleteBarsConsumed` (incomplete higher-timeframe bars the indicators took in, decision 5). The 1D bar of a regular date closes on its **15:29 bar**; a later bar of the same date is ignored and counted. A 1D
bar whose 15:29 minute is missing closes at the first bar of the next session with `complete = false`. The builder reproduces the stored aggregates' rules and is tested against an independent `GroupBy` reference over fixtures
(a normal day, missing minutes, the partial 15:15 hour, the 10m bucket at 15:25, the 30m bucket at 15:15, a Muhurat evening, a Saturday session, three consecutive days) and over random series with special sessions.

**Late bars.** A bar at or before the previous one, or for a bucket that has been emitted already, is **ignored and counted**, never merged: emitting a bucket twice, or changing one that a strategy has seen, would be look-ahead
in disguise. On valid data the counter stays 0.

**Warm-up depends on the timeframe.** The catalog's `WarmUp(spec)` counts bars *of the spec's own timeframe*; the fixed numbers of the spec apply to the 1-minute timeframe. `prev_day_*` and `gap_pct` need one full session and the
first bar of the next: *bars per session + 1*, that is **376 on 1m, 26 on 15m, 2 on 1D**. `today_open/high/low` are ready after **1** bar. The opening range needs `minutes ÷ bar length` bars, and its length must be a whole
number of bars of the timeframe: `or_high(15)` is ready after 15 bars on 1m and 1 bar on 15m, and `or_high(20)` on 15m is refused. `vwap` and `or_*` are **intraday-only** and are refused on 1D. A test measures the first ready
bar of every catalog entry on every supported timeframe and fails if it differs from the declared warm-up.

**Specs that mean the same number share one instance.** A source an indicator ignores (`atr`, `vwap`, `rel_volume`, `or_*`, the session fields) is canonicalised to `close`, so `atr(14, close)` and `atr(14, high)` are one
feature, one instance and two handles that read the same value.

**Memory and the allocation test.** Steady-state `Update`, `TimeframeBuilder.Push` and `FeatureFrame.Step` allocate 0 bytes over 100,000 steps. The allocation and benchmark tests run in a **non-parallel collection**
(`ExclusiveRun`): `GC.GetAllocatedBytesForCurrentThread` showed spurious deltas when other tests ran in parallel (a collection or tiered-JIT work landing in the measurement window). A self-test proves the measurement does see
an allocation. (P3-01c found that a collection started by *any* thread, also the runner's own, can still land in the window; the measurement is now protected and repeated when disturbed, see the *Allocation* bullet in the list of tests under *Consequences from the build* in [ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md).)

**Architecture tests are stricter than the spec.** `IndicatorsArchitectureTests` additionally forbid dependencies on `System.IO`, `System.Net`, `System.Data`, `System.Diagnostics`, `System.Environment`, `System.Random` and
`System.TimeProvider`; scan the compiled IL metadata for `DateTime.Now/UtcNow/Today` (and `DateTimeOffset`); forbid non-readonly static fields; require every public class to be `sealed` or `static`; and check that the library references
no `Microsoft.*`, `Npgsql`, `Serilog` or `System.Net.Http` assembly and, among the projects, only `XDrishti.Domain`. Self-tests against a sentinel class that reads the clock, touches a file and holds a mutable static prove the scanners do detect what they look for. **Limit:** a
mutable collection held in a `static readonly` field is not detected (the field is read-only, its contents are not).

**Benchmarks.** The benchmarks are *explicit* xUnit tests (`Fact(Explicit = true)`): `check.sh` and a plain solution run list them as skipped. Run them in Release from `backend/`:

```
dotnet test --project tests/XDrishti.Indicators.Tests -c Release -- --explicit only --filter-class "*ThroughputBenchmarks*" --report-trx --results-directory <dir>
```

The numbers are in the test output, so the TRX report is where to read them. Best of 5 passes of 1,000,000 steps (after a warm-up pass) on an Apple-silicon Mac, Release build:

| Feature | Million steps/s (through `IIndicator`) |
|---------|------------------------------------|
| `sma(20)` | 104 |
| `ema(20)` | 359 |
| `rsi(14)` | 109 |
| `atr(14)` | 209 |
| `highest(20)` | 64 |
| `lowest(20)` | 55 |
| `vwap` | 172 |
| `rel_volume(20)` | 199 |
| `or_high(15)` | 208 |
| `gap_pct` | 182 |

`TimeframeBuilder.Push` runs at 109 million 1-minute bars per second (5m) to 251 million (1D). A **12-feature frame** (1m, 5m, 15m and 1h) runs at 22 million steps per second, **0.045 µs per 1-minute step**. The targets of the
spec were 10 million updates per second and 1 µs per step, so there is a wide margin; the floors asserted in the benchmarks are those targets.

## Amendments (2026-10-15)
Deviations of the build from the sketch in [P3-01](../specs/P3-01-streaming-indicators-and-feature-frame.md); the guarantees of the acceptance criteria are unchanged.
1. **`Push` instead of `TryPush`.** A minute can close two buckets (see above), so `TimeframeBuilder.Push(in Bar, out ClosedBar first, out ClosedBar second)` returns the count 0, 1 or 2.
2. **Optional calendar.** `TimeframeBuilder(Timeframe, TradingCalendar? = null)` and `FeatureFrame.Create(specs, TradingCalendar? = null)`; without it a special session's last bucket and its day close only when a later bucket or date begins.
3. **Extra validation rules** in `FeatureFrame.Create` (every problem is reported at once, with its field, e.g. `specs[2].p1`, and nothing is thrown): an empty list; a price source that is not close, open, high or low; a parameter
   that is not finite or not a whole number where one is required; a parameter the indicator does not have that is not 0; an indicator that is intraday-only on 1D; an opening range that is not a whole number of bars.
4. **Timeframe-dependent warm-up** (see above) instead of the single number per parameter set.
5. **Flat session names** (`prev_day_high`, `today_open`, `gap_pct`) in place of nested `prev_day.*` / `today.*`.
6. **Ignored late bars** counted in `TimeframeBuilder.Ignored`, instead of an unspecified behaviour.
7. **`PriceSource` is a new enum in the `XDrishti.Indicators` namespace** (close, open, high, low); the spec's sketch named it without saying where it lives.
8. **`IsReady` is exactly "the value is not NaN"** for every catalog indicator (asserted bar by bar over 800 one-minute bars, two sessions), so the DSL can use either to decide "not ready is false".
9. **Skender.Stock.Indicators 2.7.3** is the exact version chosen (the spec left it to the implementation).

## Amendment (2026-10-15, final review)
- `vwap` on a higher timeframe is the VWAP of the closed bars' typical prices, not the 1-minute VWAP.
- `IncompleteBarsConsumed` counts bars per lane, so one missing minute counts once per timeframe that consumes it; the simulator's report must say so.
- Open for P3-01b (API needed by the DSL and the simulator, not built here): a per-timeframe "a bar closed at this step" flag (for `crosses_above` and on-close evaluation), raw `open/high/low/close/volume` features and history (`close[1]`, `tf("1D").close`), exposing the last `ClosedBar` per timeframe, an ordering guard in `FeatureFrame.Step` (a duplicate or out-of-order 1-minute bar is fed to the 1m lane but ignored by the builders), the default source of `FeatureSpec` for `highest`/`lowest` (the catalog's `DefaultSpec()` uses High/Low, a bare `FeatureSpec` uses Close), read-only copies of the catalog arrays, and a project-reference check in the architecture test that also reads the csproj.
  **All of these are closed by [ADR 0032](0032-frame-history-closed-flags-and-the-remaining-indicators.md)** (P3-01b: `BarClosed`/`ClosedBarsAt`/`TryLastClosed`, `At`/`IsReadyAt` history, the raw bar features, the ordering guard and `IgnoredBars`, `PriceSource.Default`, read-only collections, the csproj check). The weekly and monthly timeframe builders and the regime and market-context features, still open then, are **built in P3-01c** ([ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md)).

## Amendment (2026-10-16, P3-01b)
[ADR 0032](0032-frame-history-closed-flags-and-the-remaining-indicators.md) adds to this one: the catalog has 44 names (the 19 above plus 25); `PriceSource` gained `Default` (the zero value) and `Volume`; `FeatureFrame.Steps` counts accepted bars only
(a bar the new ordering guard skips is in `IgnoredBars`); `Create` takes `FrameOptions` (calendar and history depth) next to the earlier calendar overload; and the 12-feature frame of the benchmark now takes 0.060 µs per step (0.045 µs
before the value history). Nothing else here changes.

## Amendment (2026-10-16, P3-01c)
[ADR 0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) adds to this one: `TimeframeBuilder` also builds 1W and 1M (from its own 1D rules; with a calendar a week or month is emitted at the step that closes the last session of the period, without one at the first bar of the next period); the catalog has 56 names (the 44 above plus the calendar features and the market context, which are asked for on 1m only) and each indicator says which timeframes it supports (`IndicatorInfo.Supports`: `vwap` and `or_*` are refused on 1D, 1W and 1M, `prev_day_*`, `today_*` and `gap_pct` on 1W and 1M); `FrameOptions` also carries the `MarketContext` and the expiry dates. Nothing else here changes.
