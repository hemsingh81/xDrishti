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
| Market context | Index trend/ADX, VIX level/percentile/change, breadth |
| Calendar | Weekday, time of day, expiry day/week, pre/post holiday, days to results |
| Plan geometry | Stop distance in ATR, room to next obstacle in R, achievable R:R |

## 6. Market regime

Daily label from index data: **trend** (UP / DOWN / RANGE via DMA slope + ADX) × **volatility** (LOW /
NORMAL / HIGH via VIX and ATR percentiles) → up to 9 regimes. Used to condition statistics and as a feature.

## 7. Side specifics

- BUY and SELL are learned separately (different speed, stops, regimes).
- Intraday SELL: cash (MIS) or futures; swing SELL: futures only.
- Circuit-locked stocks excluded on the locked side.
