# 07 — Strategies & Signals

## 1. Strategy anatomy

| Part | Description | Example (Opening Range Breakout) |
|------|-------------|----------------------------------|
| `id` | Unique name | `ORB` |
| `styles` | intraday / swing | intraday |
| `timeframes` | Allowed entry timeframes | 5m, 15m, 30m |
| `sides` | BUY / SELL | both |
| `setup` | Conditions known before the trigger (evaluated tonight) | High relative volume yesterday, NR7, trend filter |
| `trigger` | What must happen to enter (evaluated tomorrow) | Candle closes above OR-high (BUY) |
| `valid_window` | When the trigger may fire | 09:30–11:30 |
| `initial_stop` | Where the idea is wrong | OR opposite side / trigger-candle extreme / k × ATR |
| `filters` | Context gates | Higher-TF trend agrees; not results day |
| `invalidation` | Cancel if… | Gap beyond trigger; stop level hit before trigger |
| `params` | ≤ 4 tunable values | OR length, ATR multiple |

Built-in strategies are written in the same rules language users use in the Strategy Lab
([doc 11](11-strategy-lab.md)), so there is one way to define a strategy.

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

## 3. Starter library

Each mirrored for SELL where sensible. Most cells will fail qualification — expected and healthy.

**Intraday (5m–1h):** `ORB` (opening-range breakout) · `PDH_PDL_BREAK` / `PDH_PDL_REJECT` (previous-day
high/low) · `VWAP_PULLBACK` · `VWAP_RECLAIM` · `EMA_PULLBACK` (9/21 EMA with higher-TF trend) ·
`GAP_GO` / `GAP_FILL` · `INSIDE_BAR_BO` · `CPR` (central pivot range) · `MEAN_REV_BANDS`.

**Swing (1h/1D, ≤ 5 sessions):** `BASE_BREAKOUT` · `TREND_PULLBACK` (20 EMA / 50 DMA) · `NR7_ID_BO` ·
`RSI2_REVERSION` (above/below 200 DMA) · `BB_SQUEEZE` · `HIGH_52W_MOMENTUM` · `FAILED_BREAKOUT` ·
`RS_ROTATION` (strongest stocks in strongest sectors).

**Per-session levels in the rules language.** The intraday cells are built on levels of the session (`ORB`: `or_high` / `or_low`; `PDH_PDL_*` and `GAP_*`: `prev_day.*`, `gap_pct`; `VWAP_*`: `vwap`). These features keep the history of the session only ([ADR 0035](../adr/0035-per-session-features-keep-the-history-of-the-session-only.md), [doc 11](11-strategy-lab.md) §2 *Lags*), so a crossing of one of them (`crosses_above(close, vwap)`) is unknown at the first evaluation of a session and can first be true at the second bar of the timeframe, and unknown never fires an entry; a break through yesterday's high or low is written in the level form (`close > prev_day.high and close[1] <= prev_day.high`), which is known from the first bar and also catches a gap through the level. A valid window such as the `ORB` 09:30–11:30 (a `time` comparison) is a trading rule, not a guard the language needs to stay correct. On 1D (the swing cells) nothing restarts and a lag is by position; on 1h the intraday rule holds.

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
