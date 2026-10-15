# 11 — Strategy Lab

Submit your own strategy rules at any time; the system validates, tests and reports a verdict — no code
changes, no restart.

## 1. Ways to submit

| Channel | How |
|---------|-----|
| **Rules editor** | React page with Monaco YAML editor, live JSON-Schema validation and autocomplete |
| **Form builder** | Pick indicators, conditions, stops, exits from menus → generates the same YAML |
| **Plain English** | Describe the idea to the assistant → it drafts YAML → **you confirm** |
| **Upload / CLI** | Upload a strategy file in the Lab, or run `xd strategy test file.yaml` — the file is imported into the database |
| **C# plugin** (advanced) | Implement `IStrategy` for logic the DSL can't express; compiled and run in `xd-sandbox` (no network, resource limits) |

All channels produce one validated strategy spec, run by the same engine as built-in strategies.

## 2. Rules language (DSL)

Declarative, safe (parsed and compiled to expression trees, never `eval`), look-ahead-proof (only closed bars;
higher timeframes = last closed bar).

> "Intraday: after 9:45 buy when a 15-min candle closes above VWAP with RSI(14) above 55 and 1-hour EMA20
> above EMA50. Stop below last swing low. Target 1:2, or trail after 1R. Exit by 15:15. Mirror for shorts.
> Test 2022 to today on my MyChoice and Best stocks baskets."

```yaml
strategy:
  id: VWAP_RSI_TREND
  version: 1
  style: intraday
  timeframes: [15m]
  sides: [BUY, SELL]              # SELL auto-mirrored unless a `sell:` block is given
  universe: { baskets: [MyChoice, "Best stocks"] }   # or instruments: [...] or a rule-based basket
  params: { rsi_len: 14, rsi_min: 55, swing_lookback: 5 }
  setup:
    all:
      - time >= "09:45"
      - tf("1h").ema(20) > tf("1h").ema(50)
      - rsi(rsi_len) > rsi_min
  trigger:
    all:
      - crosses_above(close, vwap)
  entry: { type: at_close }       # at_close | stop_above_high | limit_pullback
  stop:  { type: swing_low, lookback: swing_lookback, buffer_atr: 0.1 }
  exits:
    - { policy: FIXED_RR, rr: [2.0] }
    - { policy: PARTIAL_TRAIL, book_pct: 50, t1_rr: 1.0, trail: { type: ATR, mult: 2.0 } }
  time_rules: { last_entry: "14:30", square_off: "15:15" }
  filters: [not_event_day, min_turnover]

test:
  period: { from: 2022-01-01, to: today }
  method: walk_forward            # walk_forward | simple_split | full_period (exploration only)
  compare_with: [baseline_random, ORB]
  robustness: { param_sensitivity: true, monte_carlo: 1000, by_year: true, by_regime: true }
```

| Building blocks | Examples |
|-----------------|----------|
| Price & bars | `open high low close volume`, `close[1]`, `prev_day.high`, `today.open`, `or_high(15)` |
| Indicators | `sma ema rsi atr adx macd bbands vwap supertrend roc highest lowest rel_volume` — all exist now (`macd` and `bbands` are several features each): see *What exists now* below |
| Multi-timeframe | `tf("1h").ema(50)`, `tf("1D").close` |
| Conditions | `crosses_above/below`, comparisons, `between`, `rising/falling(x,n)`, `all/any/not` |
| Patterns | `inside_bar`, `nr(7)`, `gap_pct`, `engulfing`, `pivot_high/low(n)` (all exist now) |
| Context | `regime()`, `index.trend`, `vix_pct`, `sector_rs(n)`, `day_of_week`, `is_expiry_day` |
| Stops | `fixed_pct`, `atr_mult`, `swing_low/high`, `bar_low/high`, `or_opposite`, `level` |
| Exits | Any policy from [doc 08](08-exits-and-risk.md) |

### What exists now (P3-01 and P3-01b, [ADR 0031](../adr/0031-streaming-indicators-and-the-feature-frame.md), [ADR 0032](../adr/0032-frame-history-closed-flags-and-the-remaining-indicators.md))

The building blocks are implemented in `XDrishti.Indicators` as streaming code over closed bars; the DSL parser (P3-02) will read them through the `IndicatorCatalog` and the `FeatureFrame`. The **44 names** that exist (every output is a feature of its own, so `macd` is three names and `bbands` four):

| Group | Names | Notes |
|-------|-------|-------|
| Rolling (continuous across sessions) | `sma(n)`, `ema(n)`, `rsi(n)`, `atr(n)`, `highest(n)`, `lowest(n)`, `rel_volume(n)` | `sma ema rsi highest lowest` take a price source (close, open, high, low); `rsi` and `atr` are Wilder; `rel_volume` divides by the mean of the *previous* n bars |
| Trend, momentum, volatility (continuous) | `adx(n)`, `plus_di(n)`, `minus_di(n)`; `macd_line(fast, slow, signal)`, `macd_signal(…)`, `macd_hist(…)`; `bb_upper(n, k)`, `bb_middle(n, k)`, `bb_lower(n, k)`, `bb_width(n, k)`; `supertrend(n, mult)`, `supertrend_dir(n, mult)`; `roc(n)` | defaults: DMI n = 14, MACD 12/26/9, Bollinger n = 20 k = 2, Supertrend n = 10 mult = 3, `roc` n = 10. `adx`/DMI are Wilder (the DIs are ready at bar n+1, `adx` at bar 2n); `macd` and `bb` and `roc` take a price source; the Bollinger deviation is the *population* σ (÷ n, as TradingView) and `bb_width` is (upper − lower)/middle; `supertrend` is the TradingView-style line (final bands that carry over, the direction flips when the close passes the *current* final band, starts up at bar n) and `supertrend_dir` is +1 / −1; `roc` is the percent change over n bars |
| Patterns and structure (continuous, on the closed bars of the timeframe) | `inside_bar`, `nr(n)`, `engulfing`, `pivot_high(n)`, `pivot_low(n)`, `pivot_high_age(n)`, `pivot_low_age(n)` | `inside_bar` and `nr` are 1 / 0 (`nr(7)`: the range is strictly the smallest of the last n bars); `engulfing` is +1 / −1 / 0 judged on the bodies; a pivot is the price of the most recent *confirmed* swing point (strictly above / below the n bars before **and** after), known only n bars after it formed, so it is look-ahead-safe by construction (not ready until the first pivot is confirmed); `*_age` is the number of bars since that pivot (for structure stops) |
| Raw bar | `open`, `high`, `low`, `close`, `volume` | the value of the **last closed bar** of the timeframe (`tf("1D").close` is yesterday's close until today's daily bar closes); with the value history they make `close[1]` |
| Session-anchored (reset per India trading date) | `vwap`, `or_high(m)`, `or_low(m)` | intraday only (refused on 1D); `vwap` on the typical price (H+L+C)/3; the opening range is the first *m* minutes and must be a whole number of bars of the timeframe |
| Session context | `prev_day_open`, `prev_day_high`, `prev_day_low`, `prev_day_close`, `prev_day_volume`, `today_open`, `today_high`, `today_low`, `gap_pct` | the previous *session that had bars* (weekends and holidays skipped by the data); the DSL spellings `prev_day.high`, `today.open` map to these flat catalog names |

**What the frame gives the DSL.** For every feature the value as of the last closed bar of its timeframe, plus `BarClosed(tf)` (did a bar of that timeframe close at this step? the moment to evaluate a condition),
`At(handle, lag)` (the value `lag` closed bars of the feature's own timeframe ago: `close[1]`, the previous value for `crosses_above` and `rising(x, n)`; the history depth is 1 to 256, default 8, sized by the parser from the
strategy) and the last closed bar of each timeframe. A repeated or out-of-order 1-minute bar is skipped by every timeframe and counted.

**Not yet built — follow in P3-01c:** the regime / market-context features (`regime()`, `index.trend`, `vix_pct`, `sector_rs(n)`, breadth), which need the index and VIX series fed alongside the instrument, and the weekly and
monthly timeframes (1W / 1M builders): a feature reads 1m, 5m, 10m, 15m, 30m, 1h or 1D. The DSL parser and look-ahead lint are P3-02.

**How the closed-bar rule is kept.** A feature on a higher timeframe (`tf("1h").ema(50)`) is computed over bars that a `TimeframeBuilder` emits only once their last minute has arrived, so at 10:30 it reflects the 09:15 hour
(closed at 10:14) and never the 10:15 hour that is still forming. A bar with a missing minute is emitted late and flagged incomplete, and still used; the frame counts those so the simulator can report them. An indicator that is not
ready is `NaN`, and a condition on it is false. The warm-up is counted in bars of the feature's own timeframe (`ema(20)` on 15m needs 20 fifteen-minute bars; `prev_day.*` and `gap_pct` need 376 one-minute bars, 26 fifteen-minute
bars or 2 daily bars).

## 3. Pipeline

```mermaid
flowchart LR
    IN[Submit] --> V["1 · Validate<br/>schema · names · look-ahead lint · ≤4 params"]
    V -- errors --> FIX["Explain errors<br/>+ assistant fix suggestion"]
    V --> C["2 · Compile<br/>expression trees"]
    C --> Q["3 · Quick test<br/>1 year · 30 symbols · 1–2 min"]
    Q --> F["4 · Full test<br/>walk-forward · TF×side×exit · costs"]
    F --> R["5 · Robustness<br/>sensitivity · Monte Carlo · year/regime · random baseline"]
    R --> G["6 · Verdict<br/>gates, multiple-testing adjusted"]
    G --> REP[7 · Report]
    REP --> ACT{You decide}
    ACT --> D[Discard]
    ACT --> IT["Iterate: clone, change ONE variable"]
    ACT --> P["Promote → Candidate → Paper → Active"]
```

Runs as Hangfire jobs in `xd-worker`; progress streamed to React via SignalR; indicator results cached.
Full walk-forward: ~10–60 min depending on universe, timeframes and exits.

## 4. Report

| Section | Content |
|---------|---------|
| Verdict | PASS / MARGINAL / FAIL with exact reasons (each gate ✓/✗) |
| KPIs | Trades, win rate, avg win/loss R, expectancy, profit factor, max drawdown, net ₹ after costs |
| Best cells | TF × side × exit with OOS expectancy and sample size |
| By basket / instrument | Same metrics per basket and per instrument — where the idea works and where it doesn't |
| Charts | Equity & drawdown, R histogram, MAE/MFE, by year, by regime, parameter heatmap, Monte Carlo fan |
| Comparisons | Random-entry baseline, existing strategies, index buy-and-hold |
| Warnings | Low samples, cost sensitivity, concentration, unstable parameters |
| Trades | Full list with chart replay |
| Summary | Plain English from the assistant, using report numbers only |

## 5. Guarding against "test until it passes"

- Every variant recorded in a per-family **test ledger**; thresholds tighten as variants grow.
- Only walk-forward OOS can PASS; `full_period` is exploration-only.
- Promotion always passes through paper trading.
- The assistant can run tests but **cannot promote**.

## 6. Storage & reproducibility

`lab.strategies` / `lab.strategy_versions` (YAML + hash), `lab.lab_runs` (data snapshot, config version,
engine version), `lab.lab_results`, `lab.test_ledger`. The database is the only home of strategies (no repository
folder); any version can be downloaded as a file and re-imported ([ADR 0002](../adr/0002-config-and-strategies-in-app.md)).
