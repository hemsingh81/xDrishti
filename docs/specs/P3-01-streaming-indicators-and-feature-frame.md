# P3-01 — Streaming indicators and the multi-timeframe feature frame

- **Status:** Approved (owner, 2026-10-15: "approved, go with your recommendations" — the five open questions are settled as proposed: incomplete higher-timeframe bars are emitted and consumed; rolling indicators run continuously across sessions; VWAP on the typical price; Skender.Stock.Indicators as a test-only pinned dependency; ADX/MACD/Bollinger/Supertrend/patterns wait for P3-01b) · decision [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md)
- **Phase / roadmap row:** P3 — "Streaming indicators/features" (first slice; the DSL parser, the simulator and the strategies build on it)
- **Design refs:** docs/design/03-target-architecture.md (§3 tech choices "Indicators: own streaming implementations (cross-checked against Skender.Stock.Indicators)", §4 `XDrishti.Indicators`, "engine modules have no I/O"), 11-strategy-lab.md §2 (building blocks, "only closed bars; higher timeframes = last closed bar"), 05-data-and-brokers.md §4 (aggregation rules) and §11 (biases avoided), 19-risks-and-compliance.md (look-ahead bias: "closed-bar-only, point-in-time features, single code path, automated tests") · [ADR 0016](../adr/0016-market-bars-csv-load-dhan-fetch-and-app-managed-aggregates.md) · [ADR 0017](../adr/0017-trading-calendar-eod-schedule-quality-adjustments-and-csv-undo.md)

## Goal
A pure, I/O-free library that turns a stream of **closed 1-minute bars** into indicator values, closed higher-timeframe bars and session context — one code path for back-tests and (later) live trading — and that is *provably* unable to see the future. The DSL parser (P3-02) and the simulator (P3-03) read the values from it; nothing here touches the database, the API or the screens.

## Scope
**In:**
- **Project `XDrishti.Indicators`** (engine module, no I/O): references only `XDrishti.Domain` (for `Bar`, `Timeframe`, `SessionClock`, `IndiaTime`); added to the solution, the `Dockerfile` COPY lines and the architecture tests (no reference to Application/Infrastructure/EF/HTTP/file APIs; no `DateTime.Now`, no static mutable state).
- **Streaming indicator contract and the first set:** `sma`, `ema`, `rsi` (Wilder), `atr` (Wilder), `highest`/`lowest` (rolling, over high/low/close/open), `vwap` (session-anchored), `rel_volume`, the **opening range** (`or_high`, `or_low` of the first N minutes) and the **session context** (`prev_day` open/high/low/close/volume, `today` open/high-so-far/low-so-far, `gap_pct`).
- **`TimeframeBuilder`:** builds *closed* 5m/10m/15m/30m/1h/1D bars from the 1-minute stream with exactly the rules of the stored aggregates (doc 05 §4: 09:15 anchoring, the partial 15:15 hour, last buckets clipped at 15:30, a `bars` count and a `complete` flag).
- **`FeatureFrame`:** the DSL's view of the market at one 1-minute step — for each requested feature (`FeatureSpec` = name + parameters + timeframe + price source) the value as of the **last closed bar of that timeframe**, never the forming one; only the requested features run.
- **`IndicatorCatalog`:** metadata per indicator (name, parameter names, ranges, warm-up bars, factory, session behaviour) for the future DSL validator/lint (at most 4 parameters per strategy).
- **Reference and look-ahead tests** (below), an allocation test, and a benchmark harness that is documented and *not* part of `check.sh`.

**Out (not in this slice):** `adx`, `macd`, `bbands`, `supertrend`, candle patterns (`inside_bar`, `nr(n)`, `engulfing`, `pivot_high/low`), regime and market-context features (`regime()`, `index.trend`, `vix_pct`, `sector_rs`) — follow-up P3-01b; the DSL parser and look-ahead lint (P3-02); the simulator (P3-03…); reading bars from the database or the live feed (a bar-reader adapter belongs to the simulator story); weekly/monthly timeframes; corporate-action adjustment (the caller feeds the bars it wants — ADR 0017 §5 adjusted bars); plug-in indicators.

## Definitions (the numbers the tests pin)
All prices are `double` as in `Bar`; "bar index" counts bars fed to that indicator instance; an output before it is *ready* is `NaN` and `IsReady = false` (the DSL treats a not-ready value as a false condition).
- **`sma(n)`** mean of the last `n` values of the source (default close); ready at bar `n`. Running sum with Neumaier compensation (no drift; agrees with the batch mean within 1e-9 relative).
- **`ema(n)`** `α = 2/(n+1)`; seeded with the SMA of the first `n` values; ready at bar `n`; then `ema = α·x + (1−α)·ema`.
- **`rsi(n)`** Wilder: average gain/loss seeded with the simple mean of the first `n` changes, then `avg = (avg·(n−1) + x)/n`; `rsi = 100 − 100/(1+RS)`; `100` when the average loss is 0 and the average gain > 0, `50` when both are 0; ready at bar `n+1`.
- **`atr(n)`** true range `TR₁ = high−low`, `TRₜ = max(high−low, |high−prevClose|, |low−prevClose|)`; seeded with the mean of the first `n` TRs, then Wilder smoothing; ready at bar `n`.
- **`highest(n, src)` / `lowest(n, src)`** extreme of the last `n` values *including the current bar*; ready at bar `n`; monotone-deque O(1) amortised.
- **`vwap`** cumulative `Σ(typical·volume)/Σvolume` since the session open, `typical = (high+low+close)/3`; resets at the first bar of each India trading date; `NaN` until the session has volume > 0.
- **`rel_volume(n)`** `volume / mean(volume of the previous n bars)` (the current bar is **not** in the denominator); ready at bar `n+1`; `NaN` when the mean is 0.
- **`or_high(m)` / `or_low(m)`** extreme of the first `m` minutes of the session (bars 09:15 … 09:15+m−1); not ready until the bar at 09:15+m−1 has closed, then constant until the session ends; resets each session.
- **Session context** `prev_day.*` from the previous session that had bars (not "yesterday": weekends and holidays are skipped by the data, not guessed); `today.open/high/low` so far; `gap_pct = (today.open − prev_day.close)/prev_day.close·100`.
- **Session behaviour of the rolling indicators:** `sma/ema/rsi/atr/highest/lowest/rel_volume` run **continuously** across sessions on the series they are fed (an intraday EMA(20) at 09:16 uses the previous day's bars); `vwap`, `or_*` and the session context reset per India trading date. (Open question 2.)
- **`TimeframeBuilder`** a bucket is *closed* when the 1-minute bar that ends its window has been fed (for a bucket clipped at 15:30, the 15:29 bar; for `1D`, the 15:29 bar of a regular session or the last minute of a special session window if given); a bucket whose last minute never arrives is closed by the first bar of a later bucket/session and flagged `complete = false`; open = first, high = max, low = min, close = last, volume = sum, `bars` = number of 1-minute bars used.

## Acceptance criteria
**Contract and structure**
1. `XDrishti.Indicators` exists with the references and rules above; `Architecture.Tests` fails if it references Application/Infrastructure/EF/HTTP, uses `DateTime.Now/UtcNow`, `File`/`Directory`/`HttpClient`, or has non-readonly static state; every public type is `sealed` unless designed for inheritance and documented.
2. Every indicator implements one small interface (`Update(in Bar)`, `Value`, `IsReady`, `WarmUp`, `Reset()`), has constant-time or amortised-constant-time updates, and holds no reference to future data; `Reset()` returns it to the state after construction (tested for every indicator).

**Correctness against references**
3. Each indicator equals an **independent batch reference** (written plainly in the test project, not sharing code with the implementation) over ≥ 20 random seeded series of 1,000–5,000 1-minute bars (with gaps, zero-volume bars, flat bars, gap-ups across sessions) within 1e-9 relative (absolute 1e-9 near 0), and the first ready index equals the stated warm-up.
4. `sma`, `ema`, `rsi`, `atr` also agree with **Skender.Stock.Indicators** on the same series (test-only dependency; differences in seeding conventions, if any, are documented in the test and in ADR 0031, never hidden by a loose tolerance).
5. Small hand-calculated examples (≤ 12 bars, numbers written in the test) pin each definition above, including `rsi` = 100 and 50 cases, `atr`'s first bar, `rel_volume` excluding the current bar, `vwap` reset at a session change, `or_high/low` readiness, `prev_day` across a weekend/holiday gap and `gap_pct`.

**Timeframes**
6. `TimeframeBuilder` produces, for 5m/10m/15m/30m/1h/1D, exactly the buckets of an independent grouping reference (the same one the stored-aggregate tests use, or an equivalent plain `GroupBy` in the test) over fixtures: a normal day, a day with missing minutes, the partial 15:15 hour, the 10m bucket at 15:25 (5 bars) and 30m at 15:15 (15 bars), a Muhurat evening, a Saturday session, and three consecutive days; fields `open/high/low/close/volume/bars/complete` equal.
7. A higher-timeframe bar is emitted **only** when closed (never while forming) and exactly once; feeding the same series in two chunks or one gives identical output.

**No look-ahead (the core guarantee)**
8. **Prefix property:** for random series and random cut points `k`, every output (indicator values, closed bars, frame values) produced for steps `≤ k` when the series is fed *only up to k* equals the output for the same steps when the *full* series is fed. Applied to every indicator and the frame (≥ 200 seeds).
9. **Frame semantics:** for every step `t` and timeframe `T`, the frame's value for a feature on `T` equals the indicator value computed over the closed `T` bars whose window ended at or before `t` (never the bar containing `t` unless `t` is its last minute); a test that constructs the *forming* bar's value and asserts the frame does not use it.
10. **Determinism:** two runs over the same input give bit-identical outputs; no parallel reductions, no dependence on culture or the system clock.

**Frame and catalog**
11. `FeatureFrame` takes a list of `FeatureSpec`s once, builds only the needed `TimeframeBuilder`s and indicators (shared where specs coincide: two specs on the same indicator/params/timeframe/source share one instance), and exposes `TryGet(spec)` and a numeric handle per spec for hot-path reads; a spec with an unknown name, bad parameter or a timeframe not in {1m, 5m, 10m, 15m, 30m, 1h, 1D} is rejected with a clear error before any bar is fed.
12. `IndicatorCatalog` lists each indicator with parameters (name, type, min, max, default), warm-up bars as a function of the parameters, session behaviour (`continuous`/`per_session`) and a factory; a test fails if an indicator is registered without catalog metadata or the metadata disagrees with the measured first ready index.

**Performance and memory**
13. After a warm-up the steady-state `Update` of every indicator and `FeatureFrame.Step` allocates **0 bytes** (`GC.GetAllocatedBytesForCurrentThread` delta over 100,000 steps); a documented benchmark (not in `check.sh`) reports updates/second, target ≥ 10 M simple-indicator updates per second per core and a frame of 12 features in ≤ 1 µs per 1-minute step.

**Docs**
14. ADR 0031 records the definitions, the seeding conventions, the continuous-vs-per-session rule, the closed-bar rule, the incomplete-bar policy and the deviations from Skender; doc 03 (module), doc 11 §2 (indicator list: what exists, what follows) and the roadmap are updated.

## Contract (public surface, sketch)
```csharp
public interface IIndicator { void Update(in Bar bar); double Value { get; } bool IsReady { get; } int WarmUp { get; } void Reset(); }
public sealed class TimeframeBuilder { public TimeframeBuilder(Timeframe timeframe); public bool TryPush(in Bar minuteBar, out ClosedBar closed); }
public readonly record struct ClosedBar(long StartEpochSeconds, Bar Bar, int Bars, bool Complete, Timeframe Timeframe);
public readonly record struct FeatureSpec(string Name, Timeframe Timeframe, PriceSource Source, double P1, double P2, double P3, double P4);
public sealed class FeatureFrame { public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs); public void Step(in Bar minuteBar); public double this[int handle] { get; } public bool IsReady(int handle); }
public static class IndicatorCatalog { public static IReadOnlyList<IndicatorInfo> All { get; } }
```
Exact names are settled in the implementation; the guarantees above are what the tests pin.

## Non-functional
Pure functions of the bars fed; thread-confined instances (no locking; the simulator runs one frame per instrument per worker); no logging inside the hot path; no exceptions for expected conditions (a not-ready value is `NaN`/`IsReady = false`).

## Test plan
Unit (Indicators.Tests): hand examples (5), independent references with seeded random series (3), Skender cross-check (4), timeframe fixtures (6, 7), prefix property (8), frame semantics (9), determinism (10), reset (2), catalog consistency (12), allocation (13). Architecture: project rules (1). No integration tests, no UI.

## Open questions
1. **Incomplete higher-timeframe bars** (a bucket whose last minute never arrived): proposal — they are emitted with `complete = false` and **indicators on that timeframe still consume them** (a missing minute must not silence a strategy for an hour), while the simulator later reports how many incomplete bars it used. The alternative (skip incomplete bars) makes indicators wait and can shift every later value. Which do you prefer?
2. **Rolling indicators across sessions on intraday timeframes**: proposal — continuous (an EMA(20) on 15m at 09:30 uses yesterday's bars), which is what most charting tools and the examples in doc 11 assume; per-session reset (cleaner for opening-range style rules) would be a per-spec option added later. OK?
3. **VWAP price basis**: typical price `(H+L+C)/3` (standard) rather than the close. OK?
4. **Skender.Stock.Indicators** (MIT) as a **test-only** dependency, pinned to an exact version, as doc 03 intends. OK?
5. **Scope**: ADX, MACD, Bollinger, Supertrend and the candle patterns wait for P3-01b so this slice stays about two to three days. OK, or do you want the four indicators in now?

## Amendments (2026-10-15, from the build)
Where the build differs from the text above; the acceptance criteria hold as written. The reasons and the measured numbers are in [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md) (*Consequences from the build*, *Amendments*).
- **`Push`, not `TryPush`:** `TimeframeBuilder.Push(in Bar, out ClosedBar first, out ClosedBar second)` returns 0, 1 or 2, because one minute can close the late previous bucket and its own.
- **Optional calendar:** `TimeframeBuilder(Timeframe, TradingCalendar? = null)` and `FeatureFrame.Create(specs, TradingCalendar? = null)`; with a calendar a special session closes its last bucket and its day at its own last minute.
- **Counters:** `TimeframeBuilder.Ignored` and `FeatureFrame.IncompleteBarsConsumed`. A bar at or before the previous one (or for a bucket already emitted) is ignored and counted. The 1D bar of a regular date closes on its 15:29 bar and a later bar of that date is ignored; a 1D bar whose 15:29 minute is missing closes at the next session's first bar with `complete = false`.
- **Extra validation rules** in `FeatureFrame.Create`: an empty list, an unknown price source, a parameter that is not finite or not whole, a parameter the indicator does not have that is not 0, an intraday-only indicator (`vwap`, `or_*`) on 1D, an opening range that is not a whole number of bars (`or_high(20)` on 15m).
- **Warm-up depends on the timeframe** (definition of "ready at bar n" holds on 1m): `prev_day_*` and `gap_pct` are ready after bars per session + 1 (376 on 1m, 26 on 15m, 2 on 1D), `today_*` after 1 bar, the opening range after minutes ÷ bar length.
- **Flat session names:** `prev_day_open/high/low/close/volume`, `today_open/high/low`, `gap_pct` (19 catalog names in all), not nested `prev_day.*` / `today.*`.
- **Equal features share an instance** even when they differ only in a source the indicator ignores (canonicalised to close).
- **`PriceSource`** is a new enum (close, open, high, low) in the `XDrishti.Indicators` namespace; **`IsReady` is true exactly when `Value` is not NaN**.
- **Skender.Stock.Indicators 2.7.3** is the exact test-only pin. Differences found: the ATR seed (Skender has no TR for bar 1 and seeds with the mean of TR₂ … TRₙ₊₁, first value at bar n+1; ours seeds with TR₁ … TRₙ, ready at bar n; the gap shrinks by (n−1)/n per bar) and RSI when both averages are 0 (Skender 100, ours 50).
- **Allocation and benchmark tests** run in a non-parallel collection (`ExclusiveRun`) because the thread allocation counter showed spurious deltas under parallel tests. The benchmarks are explicit tests (listed as skipped in a solution run); the command and the numbers are in ADR 0031 and doc 03 §4.1.
- **Architecture tests are stricter than criterion 1:** also no `System.IO`, `System.Net`, `System.Data`, `System.Diagnostics`, `Environment`, `Random`, `TimeProvider`; an IL scan for `DateTime.Now/UtcNow/Today`; public classes sealed or static; self-tests prove the scanners work. Known limit: a mutable collection in a `static readonly` field is not detected.

## Follow-ups from the review (minor, not blocking)
Carried into P3-01b / P3-02 (see ADR 0031 'Amendment, final review'): the frame API gaps (closed-bar flag, raw OHLCV features and history, last `ClosedBar`, ordering guard), the `FeatureSpec` default source for `highest`/`lowest`, read-only catalog arrays, the 1D/no-flush differences from the stored aggregates (documented in doc 05 §4), calendar-path test gaps (random/reference comparison with a calendar, Muhurat with a calendar, 1h, `FeatureFrame.Create(specs, calendar)`) and an allocation test over gap/zero-volume/incomplete-bucket series, the culture test (use a comma-decimal culture), the architecture test reading the csproj, a mis-named RSI test, and the duplicated minute-of-day helper.

## Done when
- [ ] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (2026-10-15)
- [x] No contract or migration change (none expected); architecture tests cover the new project
- [x] Design docs/ADR 0031 updated (doc 03, 11, roadmap; also doc 05 §4 and §11)
