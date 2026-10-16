# P3-01b — The frame API the DSL needs, and the remaining indicators and patterns

- **Status:** Approved (owner, 2026-10-16: "approved" with the six recommendations: strict pivots on both sides; population σ for Bollinger; TradingView-style Supertrend; history depth default 8; engulfing on bodies; 1W/1M builders and regime/market-context features wait for P3-01c) · decision [ADR 0032](../adr/0032-frame-history-closed-flags-and-the-remaining-indicators.md) (amends [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md))
- **Phase / roadmap row:** P3 — "Streaming indicators/features", second slice (P3-01 built the engine; the DSL parser P3-02 and the simulator P3-03 read from it)
- **Design refs:** docs/design/11-strategy-lab.md §2 (building blocks: indicators `adx macd bbands supertrend`, conditions `crosses_above/below rising/falling`, patterns `inside_bar nr(7) gap_pct engulfing pivot_high/low(n)`, price access `close[1] prev_day.high today.open`), 07-strategies.md (feature families: trend, momentum, volatility; swing structure), 08-exits-and-risk.md (swing-low/high stops, structure trail) · [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md) and its "Amendment, final review" (the API gaps listed there)

## Goal
Everything the parser and the simulator need to read from the engine without touching its internals: "a bar of this timeframe just closed", the raw bar values and a short history of every feature (`close[1]`, `crosses_above`, `rising(x, n)`), a frame that cannot be corrupted by a repeated or out-of-order bar — plus the indicators and candle patterns of the building-block list that P3-01 left out. Still a pure library: no database, API or screens.

## Scope
**In:**
1. **Frame API (ADR 0031 amendment items):**
   - `BarClosed(Timeframe)` — whether the timeframe closed at least one bar at the current step (always true for 1m), plus `ClosedBarsAt(Timeframe)` and `TryLastClosed(Timeframe, out ClosedBar)` (the last `ClosedBar`, `Complete` flag included). Only timeframes the frame was asked for (`HasTimeframe(tf)` tells which); any other timeframe throws `ArgumentOutOfRangeException`. The flags describe the **last** `Step`: false / 0 after a bar the ordering guard skipped and before the first step.
   - **Value history:** `Create(specs, options)` with `FrameOptions { TradingCalendar? Calendar; int HistoryDepth = 8 (1..256) }`; for each feature the frame keeps the values it had after each of the last `HistoryDepth` closed bars **of the feature's own timeframe**: `At(handle, lag)` (`lag = 0` is the current value) and `IsReadyAt(handle, lag)`; a lag outside `0..HistoryDepth−1` throws `ArgumentOutOfRangeException`. This is what `close[1]`, `crosses_above(a, b)` (needs `a[0] a[1] b[0] b[1]`) and `rising(x, n)` read.
   - **Raw bar features** in the catalog: `open`, `high`, `low`, `close`, `volume` — the value of the last closed bar of the timeframe (so `tf("1D").close` is yesterday's close until today's daily bar closes, as the closed-bar rule demands).
   - **Ordering guard:** a 1-minute bar at or before the previous one is counted (`IgnoredBars`) and skipped for **all** lanes, so the 1m lane and the builders can no longer diverge. `Steps` counts accepted bars only.
   - `FeatureSpec` default source: a new `PriceSource.Default` sentinel (the zero value) resolved at `Create` to the catalog's default (`High` for `highest`, `Low` for `lowest`, `Close` otherwise), so `new FeatureSpec("highest", P1: 20)` means what `IndicatorCatalog.DefaultSpec` means. `PriceSource.Volume` exists for the `volume` feature only.
   - `IndicatorCatalog.All` / `SupportedTimeframes` and `IndicatorInfo.Parameters` are read-only collections.
2. **New indicators** (all streaming, closed-bar, warm-up declared; names are catalog names, outputs are separate features so each has one number): `adx(n)`, `plus_di(n)`, `minus_di(n)` (Wilder DMI); `macd_line(fast, slow, signal)`, `macd_signal(…)`, `macd_hist(…)`; `bb_upper(n, k)`, `bb_middle(n, k)`, `bb_lower(n, k)`, `bb_width(n, k)` (source-aware); `supertrend(n, mult)` (the band value) and `supertrend_dir(n, mult)` (+1 up, −1 down); `roc(n)` (percent rate of change, source-aware).
3. **New candle patterns / structure** (stateless on the closed bars of the timeframe, value `1`/`0`, or `+1`/`−1`/`0`): `inside_bar` (high below the previous high and low above the previous low), `nr(n)` (the bar's range is the smallest of the last n bars, current included), `engulfing` (+1 bullish, −1 bearish: the body fully covers the previous body, opposite colours), `pivot_high(n)` / `pivot_low(n)` — the price of the most recent **confirmed** swing point: a bar whose high (low) is strictly above (below) the highs (lows) of the n bars before **and** the n bars after it; it is confirmed only when the n-th bar after has closed, so the value is look-ahead-safe by construction and arrives n bars late (companion `pivot_high_age(n)` / `pivot_low_age(n)`: bars since that pivot, for structure stops).
4. **Catalog**: 25 new names (total 44: the 23 of items 1–3 — `adx plus_di minus_di macd_line macd_signal macd_hist bb_upper bb_middle bb_lower bb_width supertrend supertrend_dir roc inside_bar nr engulfing pivot_high pivot_low` and the five raw bar features `open high low close volume` — plus `pivot_high_age` and `pivot_low_age`) with parameter ranges, warm-up, session behaviour (every new entry is `Continuous`), and the same catalog-consistency test as before; at most 4 parameters per feature (the new ones have at most 3).
5. Tests and documentation as in P3-01 (independent references, hand examples, Skender cross-checks where the indicator exists there, prefix property, frame semantics, determinism, reset, zero allocation, benchmarks extended).

**Out (not in this slice):** the DSL parser and the look-ahead lint (P3-02); the simulator (P3-03); regime and market-context features (`regime()`, `index.trend`, `vix_pct`, `sector_rs(n)`, breadth — they need index and VIX series fed alongside the instrument: P3-01c); weekly and monthly timeframes (1W/1M builders: needed by swing strategies on 1D with a weekly higher timeframe, P3-01c); `adx` smoothed variants, Ichimoku, Keltner, Donchian, OBV and other indicators not on the doc 11 list; plug-in indicators.

## Definitions (numbers the tests pin)
- **`plus_di/minus_di/adx(n)`** Wilder: `+DM = up move if up > down and up > 0`, `−DM = down move if down > up and down > 0` (up = high − prev high, down = prev low − low), smoothed with Wilder's sum recursion (seed = sum of the first n); `+DI = 100·smoothed(+DM)/smoothed(TR)`; `DX = 100·|+DI − −DI|/(+DI + −DI)` (0 when both 0); `ADX` seeded with the mean of the first n DX then Wilder-smoothed. Ready: DI at bar n+1, ADX at bar 2n.
- **`macd_line(f, s, g)`** `ema(f) − ema(s)` of the source; ready at bar `s`; `macd_signal` = `ema(g)` of the line seeded with the SMA of its first g values, ready at bar `s + g − 1`; `macd_hist = line − signal`. Requires `f < s`.
- **`bb_*`**: `bb_middle = sma(n)`, `bb_upper/lower = middle ± k·σ` with **population** standard deviation of the last n values (computed exactly, a second pass over the window: O(n) per update); `bb_width = (upper − lower)/middle`; ready at bar n.
- **`supertrend(n, m)`**: `hl2 = (high + low)/2`; basic bands `hl2 ± m·atr(n)` (Wilder); final upper band = basic upper if it is below the previous final upper or the previous close was above the previous final upper, else the previous final upper (mirrored for the lower); the direction is decided **after** the bands of the bar are final: while down it flips to up when the close is above the **current** final upper band, while up it flips to down when the close is below the **current** final lower band (the TradingView and Skender rule; the first draft of this definition said the *previous* final band, changed in the build, see Amendments); the value is the final lower band while up and the final upper band while down. Ready at bar n (first direction: up).
- **`roc(n)`** `100·(x − x[n])/x[n]`; ready at bar n+1; NaN when `x[n] = 0`.
- **`nr(n)`** `1` when `high − low` is strictly smaller than the ranges of the previous n−1 bars (ties are not narrower), else `0`; ready at bar n.
- **`engulfing`** current body = |close − open| > 0 and previous body > 0; bullish (+1) when the previous bar is down (close < open), the current is up and `open ≤ prev close` and `close ≥ prev open`; bearish (−1) mirrored; otherwise 0; ready at bar 2.
- **`pivot_high(n)`** see above; `NaN` until the first confirmed pivot; equal highs are not pivots. The declared warm-up is `2n + 1`, the earliest bar at which a pivot can be confirmed; the feature is ready from the first confirmed pivot on, which depends on the data (`IsReady` is still exactly "not NaN").
- **History:** `At(h, 0)` is `this[h]`; `At(h, k)` is the value after the k-th previous closed bar of that feature's timeframe; `NaN`/not ready when fewer than k+1 values exist or the value was not ready then. The history is recorded when the feature's timeframe closes a bar, never between closes.

## Acceptance criteria
**Frame API**
1. `BarClosed/ClosedBarsAt/LastClosed` are correct at every step for 1m, 5m, 15m, 1h and 1D over the P3-01 timeframe fixtures (a bucket-closing minute, a late-closed incomplete bucket, `Push` returning two, a special session with and without a calendar); a timeframe the frame was not asked for throws, it is not a silent false; the flags reflect the last step only (false / 0 after a skipped bar and before the first step).
2. History: for random seeded series every `At(h, k)` equals the value the feature had `k` closed bars of its timeframe ago (compared with an independent record kept by the test), for all `HistoryDepth` values 1…256 edges (1, 2, 8, 256), including not-ready lags and across session boundaries; the history is only written on closes of the feature's timeframe.
3. Raw bar features: `close` on 15m equals the close of the last closed 15m bar at every minute; `tf("1D").close` before the first daily close of the stream is not ready; `volume` is the sum of the bucket.
4. Ordering guard: feeding a duplicate, an older bar, or the same series with repeated bars in the middle gives exactly the output of the clean series, with `IgnoredBars` equal to the number of repeats; no lane sees the repeat (test on frames with 1m, 5m and 1D lanes).
5. `PriceSource.Default` resolves per the catalog; two specs that differ only by `Default` versus the explicit default source share one instance; the catalog collections cannot be mutated through a cast.

**Indicators and patterns**
6. Every new indicator equals an independent batch reference (plain code in the test project) on ≥ 24 seeded random series within 1e-9, with the stated warm-up; hand-calculated examples (≤ 12 bars) pin `adx` seeding, `macd_signal` seeding, `bb` population σ, `supertrend` band carry-over and a flip in each direction, `roc` with a zero base, `nr` ties, `engulfing` both directions and the zero-body cases, `pivot_high/low` confirmation delay, equal highs, and `*_age`.
7. Skender cross-check where it exists: `adx`/`plus_di`/`minus_di` (`GetAdx`), `macd_*` (`GetMacd`), `bb_*` (`GetBollingerBands`, population σ), `supertrend` (`GetSuperTrend`), `roc` (`GetRoc`); each difference in seeding/convention is asserted exactly and documented in ADR 0032, never absorbed by a loose tolerance (adx, macd, bb and roc turned out identical; supertrend differs in three stated conventions).
8. Prefix property (≥ 200 seeds) and frame-semantics tests extended to every new feature, including `pivot_*` (the value at step t never depends on bars after t), `At(h, k)` and `BarClosed`; determinism and `Reset` for every new indicator and for the history.
9. Catalog: the 25 new names exist with metadata; the consistency test (declared warm-up = measured first-ready index, on every supported timeframe) covers them; `FeatureFrame.Create` rejects `macd` with `fast ≥ slow`, `bb` with `k ≤ 0`, `supertrend` with `mult ≤ 0`, `nr(n < 2)`, `pivot(n < 1)`.

**Performance and structure**
10. Steady-state `Step` and every `Update` still allocate 0 bytes (extended allocation test over a frame with ≥ 20 features including history depth 8, on a gap-free and on a gap/zero-volume series); the benchmark harness reports updates/s per new indicator and a 20-feature frame step; targets: each new indicator ≥ 10 M updates/s, the 20-feature frame ≤ 1 µs per 1-minute step.
11. Architecture tests unchanged and green; no new project references; no contract or migration change.
12. Docs: ADR 0032 (definitions, conventions vs Skender, history semantics, ordering guard, `Default` source), doc 11 §2 ("what exists now": 44 names; regime/context and 1W/1M follow in P3-01c), doc 03 §4.1, the roadmap.

## Contract (public surface, sketch)
```csharp
public sealed record FrameOptions(TradingCalendar? Calendar = null, int HistoryDepth = 8);
public sealed class FeatureFrame {
  public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs, FrameOptions? options = null);
  public bool BarClosed(Timeframe tf); public int ClosedBarsAt(Timeframe tf); public bool TryLastClosed(Timeframe tf, out ClosedBar bar); public bool HasTimeframe(Timeframe tf);
  public double At(int handle, int lag); public bool IsReadyAt(int handle, int lag);
  public long IgnoredBars { get; }
}
public enum PriceSource { Default = 0, Close, Open, High, Low, Volume /* only for the raw bar feature `volume` */ }
```
(`Create(specs, TradingCalendar?)` of P3-01 stays as an overload; `Create(specs, null)` with a literal `null` is ambiguous, so pass `options:` or `calendar:` by name.) Exact names are settled in the implementation; the guarantees above are what the tests pin.

## Non-functional
Same as P3-01: pure functions of the bars fed, thread-confined instances, no logging, no exceptions for expected conditions. History memory = features × `HistoryDepth` doubles (≈ 16 KB for 250 features at depth 8), allocated once at `Create`.

## Test plan
Unit (Indicators.Tests): references (6), hand examples (6), Skender (7), frame API (1–5), prefix/frame semantics (8), catalog (9), allocation and benchmark (10). Architecture (11). No integration tests, no UI.

## Open questions
1. **Pivot definition:** proposal — strict on both sides (a bar whose high is *strictly above* the n bars before **and** n bars after; equal highs are not pivots). The alternative (≥ on one side) finds more pivots in flat markets. Which?
2. **Bollinger σ:** population (÷ n, as TradingView and Skender) rather than sample (÷ n−1). OK?
3. **Supertrend convention:** the TradingView-style rule in Definitions (final bands carry over; flip on the close beyond the previous final band; Wilder ATR). Skender differs in details; the test pins the difference. OK?
4. **History depth default 8** (max 256): enough for `rising/falling(x, n)` with n ≤ 7 and crossings; the parser will size it from the strategy. OK?
5. **Engulfing on bodies** (open/close) rather than the full high–low range. OK?
6. **Scope:** regime/market-context features and the 1W/1M builders wait for **P3-01c** (they need index/VIX series and the weekly bucket rules). OK, or do you want the weekly builder in this slice?

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (2026-10-16, minors below)
- [x] No contract or migration change
- [x] Architecture tests green (extended: they now also read the csproj `ProjectReference`/`PackageReference` items)
- [x] Design docs/ADR 0032 updated (doc 03, 11, roadmap)

## Amendments (2026-10-16, from the build)
Deviations of the build from the text above; the guarantees of the acceptance criteria hold. Details and reasons are in [ADR 0032](../adr/0032-frame-history-closed-flags-and-the-remaining-indicators.md) ("Consequences from the build" and "Amendments").
1. **Counts.** The catalog gained **25** names (total **44**), not 23 (total 42): the spec's own arithmetic was off, and `pivot_high_age` / `pivot_low_age` were counted in neither number. The 25: `adx plus_di minus_di macd_line macd_signal macd_hist bb_upper bb_middle bb_lower bb_width supertrend supertrend_dir roc inside_bar engulfing nr pivot_high pivot_low pivot_high_age pivot_low_age open high low close volume`.
2. **Supertrend flip rule.** The Definitions first said the direction flips on the close beyond the *previous* final band. The build uses the *current* final band (the bands are made final first, then the close is compared), because the owner approved a "TradingView-style" Supertrend and that is what TradingView and Skender do. Three Skender differences remain: the first value is at bar n for us and at n+1 for Skender (ATR seed); Skender starts up when `close ≥ hl2` where ours always starts up; and a close exactly on the final lower band while up flips Skender down (`<=`) but not ours (strict `<`, as TradingView).
3. **Throws instead of silent NaN/false** for a timeframe the frame was not asked for and for a lag outside `0..HistoryDepth−1`; `HasTimeframe(tf)` was added so a caller can ask first.
4. **`Steps`** counts accepted bars; bars skipped by the ordering guard go to `IgnoredBars` only. `BarClosed` / `ClosedBarsAt` describe the last step (false / 0 after a skipped bar and before the first step).
5. **`PriceSource`** is `{ Default = 0, Close, Open, High, Low, Volume }`; `Volume` is only usable through the `volume` feature (a spec with `Source = Volume` on another indicator is refused).
6. **Bollinger σ** is computed exactly with a second pass over the window (O(n) per update, about 53 M updates/s for `bb_upper(20, 2)`), because the O(1) sum-of-squares identity cancels badly when a window is tight against a large price.
7. **Parameter bounds.** `bb` accepts `k` in 0..100 and `supertrend` `mult` in 0..1000, each value must also be above 0 (errors are reported on `specs[i].p2`); `nr` needs `n ≥ 2` (default 7), `pivot_*` needs `n ≥ 1` (default 5); defaults: `adx plus_di minus_di` n = 14, `macd` 12/26/9, `bb` n = 20 k = 2, `supertrend` n = 10 mult = 3, `roc` n = 10. A history depth outside 1..256 gives a validation error on `options.historyDepth`.
8. **Skender cross-check result.** `adx`, `plus_di`, `minus_di`, `macd_*` (3,6,4), (12,26,9), (5,35,5), `bb_*` (population σ; Skender's `Width` is (upper − lower)/middle) and `roc` agree with Skender 2.7.3 within 1e-9, with the same seeds and readiness, so there is no convention difference to document for them. Only `supertrend` differs (item 2).
9. **Performance (met).** Each new indicator is far above 10 M updates/s (lowest: `bb_upper` 53, `pivot_low_age` 66); a 20-feature frame on 1m, 5m, 15m, 1h and 1D with history depth 8 runs at 10.7 M steps/s, 0.094 µs per 1-minute step (target 1 µs); the allocation test also covers a 35-feature frame and gap/zero-volume series.
10. **Also closed** (gaps found in the P3-01 review): the allocation test over a gappy series, the timeframe-builder reference with a calendar, a culture test, and a clearer name for an RSI test.
11. **Closed later:** the 1W / 1M builders and the regime / market-context features that this slice left to P3-01c are built ([P3-01c](P3-01c-weekly-monthly-timeframes-and-market-context.md), [ADR 0033](../adr/0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md)).

## Follow-ups from the final review (minors, none blocks)
1. `bb_*` and `pivot_*` cost O(n) per update; the 10 M/s target holds for typical n but not for n near `MaxPeriod` (10,000). Consider a lower cap for these families or a monotone window for pivots.
2. Indicators of one family (`bb_*`, `macd_*`, `adx`/`plus_di`/`minus_di`, `pivot_*` and ages) each keep their own core; sharing one core per (family, parameters, source, timeframe) inside the frame lane would save work. The `k > 0` and MACD parameter checks are repeated and could share a helper.
3. `PriceSources.Pick(Default)` reads close, so a directly constructed `new Highest(5, PriceSource.Default)` reads close while the catalog resolves it to high. Normalise or throw in the constructors, or document it.
4. "Always true for 1m" for `BarClosed(M1)` holds only when the frame has a 1m feature (it throws otherwise); reword, or add a `LastStepAccepted` flag for the simulator.
5. The architecture test reads the project's own csproj only; items added through `Directory.Build.props` would not be seen.
6. The 25 new catalog entries sit before the 9 session fields, so `IndicatorCatalog.All` order changed; append new entries at the end if the DSL/API will list the catalog by index.
7. `bb_width` and `roc` can become not-ready again after warm-up (zero middle / zero base); say so in the catalog summary text.
