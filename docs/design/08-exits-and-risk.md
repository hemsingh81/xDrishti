# 08 — Exits & Risk

## 1. R-multiples

- **1R** = amount risked = `|entry − initial_stop| × qty`. All results are in R, so trades are comparable.
- **Expectancy** `E[R] = P(win)·avg_win_R − P(loss)·avg_loss_R − cost_R`.
- Break-even win rate before costs: 33.3% at 1:2, 25% at 1:3.

## 2. Exit policy library

| Policy | Description | Key parameters |
|--------|-------------|----------------|
| `FIXED_RR` | Full exit at k×R or stop | k ∈ {1.5, 2, 2.5, 3} |
| `PARTIAL_BE` | Book x% at T1, stop → breakeven, rest at T2 | x, T1, T2 |
| `PARTIAL_TRAIL` | Book x% at T1, trail remainder | x, T1, trail |
| `ATR_TRAIL` | Chandelier/ATR trail after activation | period, multiple, activate_at |
| `STRUCTURE_TRAIL` | Trail beyond prior swing / previous candle | lookback |
| `EMA_TRAIL` | Exit on close beyond EMA | length |
| `PYRAMID` | Add at progress levels, trail combined position | see §4 |
| `TIME_STOP` | Exit if not ≥ +x R after N bars/sessions | x, N |
| `SESSION_EXIT` | Intraday square-off | time |
| `MAX_HOLD` | Swing exit at end of session N | N ≤ 5 |
| `EVENT_EXIT` | Exit before results | days before |

`SESSION_EXIT` (intraday) and `MAX_HOLD` (swing) are always on.

## 3. How the exit is chosen

**Per cell (weekly):** replay the same historical entries under every candidate policy; choose by
out-of-sample risk-adjusted expectancy (`mean_R − λ·downside_dev_R`), requiring minimum samples and stability
across folds — prefer the middle of a plateau over a sharp peak.

**From MAE/MFE (trade "nature"):**
- Stop: if 90% of winners never went beyond 0.8R adverse, 1R is sound; if winners routinely dip 1.2R, the stop is too tight.
- Target: if median MFE is 2.4R but only 30% reach 3R, prefer 2–2.5R or partial booking.
- Trail activation: where MFE giveback becomes common.

**Per trade (tonight):** scale by current ATR; snap to structure (swing points, previous-day levels, round
numbers); **obstacle check** — cap target before a strong level or skip if achievable R:R < `min_rr`; reduce
target for late intraday triggers (learned).

## 4. Pyramiding

Used only where the cell shows trend persistence (fat MFE tail), mostly 30m+ and swing.

| Parameter | Default |
|-----------|---------|
| Initial position | 50% of full size |
| Add triggers | +1R, +2R (on trigger-TF close) |
| Add sizes | 30%, 20% |
| Max adds | 2 |
| Risk rule | Before each add, move stops so **total open risk ≤ initial 1R** |

## 5. Time rules

| Rule | Intraday | Swing |
|------|----------|-------|
| Latest entry | e.g. 14:30 | Signal valid N sessions (default 1) |
| Forced exit | Square-off (e.g. 15:15) | Close of session ≤ 5 |
| Time stop | e.g. < +0.5R after 6 bars | e.g. < +0.5R after 2 sessions |
| Overnight gap | — | Gap through stop ⇒ filled at open (simulated realistically) |

## 6. Position sizing (per account)

```
risk_amount = account_capital × risk_per_trade_pct
qty         = min(risk_amount / |entry − stop|, max_capital_per_trade / entry, liquidity_cap)
qty         = round_down_to_lot(qty)        # futures
skip if qty < 1 lot, or cost_R > max_cost_R
```
Optional confidence scaling (capped fractional Kelly, 0.5×–1.25×) — off until calibration is proven in paper trading.

Learned multipliers on top ([doc 09](09-learning-engine.md) §14): **meta-labelling** size × 0.5–1.5,
**equity-curve throttle** × 0.5 while the system's equity is below its 20-trade average, **reduced market-gate
day** × 0.75. The multipliers never raise risk above the profile's maximum per trade.

## 7. Portfolio limits (per account, from its risk profile)

| Limit | Default |
|-------|---------|
| Max new entries per day | 3 |
| Max open positions | 5 |
| Max total open risk (heat) | 3% |
| Max per sector | 2 |
| Max pair correlation (60-day returns) | 0.60 — above it the lower-ranked ticket goes to reserve |
| Daily loss limit | −2R or −1.5% → no new entries |
| Weekly loss limit | −5R → half size next week |
| Max drawdown (goal) | 8% → halt new entries, review |
| Consecutive losses in a cell | 4 → pause cell |

## 8. Costs & slippage

Brokerage, exchange charges, STT/CTT, GST, stamp duty, SEBI fees — **from your broker's current charge
sheet in config** (rates change with budgets). Slippage: ticks + fraction of bar range; extra for stop exits
and gaps. Every simulation includes costs.
