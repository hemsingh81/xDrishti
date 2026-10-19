# 08 — Exits & Risk

## 1. R-multiples

- **1R** = amount risked = `|entry − initial_stop| × qty`. All results are in R, so trades are comparable.
- **Expectancy** `E[R] = P(win)·avg_win_R − P(loss)·avg_loss_R − cost_R`.
- Break-even win rate before costs: 33.3% at 1:2, 25% at 1:3.

## 2. Exit policy library

| Policy | Description | Key parameters |
|--------|-------------|----------------|
| `FIXED_RR` | Full exit at k×R or stop | `rr`: a list of candidate k, each in (0, 10], e.g. {1.5, 2, 2.5, 3} |
| `PARTIAL_BE` | Book x% at T1, stop → breakeven, rest at T2 | `book_pct` in (0, 100), `t1_rr` in (0, 10], `t2_rr`: a list of candidate T2, each in (0, 10] and above `t1_rr` |
| `PARTIAL_TRAIL` | Book x% at T1, trail remainder | `book_pct` in (0, 100), `t1_rr` in (0, 10], `trail_atr_mult` in (0, 20], optional whole `atr_period` (14) — the trail is the ATR in schema 1 |
| `ATR_TRAIL` | Chandelier/ATR trail after activation | `mult`: a list in (0, 20], `activate_at_rr` in [0, 10] (0: trailing from the entry), optional whole `atr_period` (14) |
| `STRUCTURE_TRAIL` | Trail beyond prior swing / previous candle | `lookback_bars` (whole, the catalog's range): the N-bar extreme (`lowest` for BUY, `highest` for SELL); "previous candle" is 1 |
| `EMA_TRAIL` | Exit on close beyond EMA | `length` (whole, the catalog's range) |
| `PYRAMID` | Add at progress levels, trail combined position | `initial_pct`, `add_at_rr`, `add_pct`, `max_adds`, `trail_atr_mult`, optional `atr_period`: see §4 |
| `TIME_STOP` | Exit if not ≥ +x R after N bars/sessions — an **overlay**, not a candidate | `min_progress_rr` in [0, 10]; an intraday strategy gives `intraday_bars`, a swing strategy `swing_sessions` (below `max_hold_sessions`) |
| `SESSION_EXIT` | Intraday square-off | time |
| `MAX_HOLD` | Swing exit at end of session N | N ≤ 5 |
| `EVENT_EXIT` | Exit before results — an **overlay**, not a candidate | `days_before_results`, whole, 1 to 10 |

`SESSION_EXIT` (intraday) and `MAX_HOLD` (swing) are always on: they come from the time rules of §5, not from the list of candidates.

**As built (P3-02b, [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md)).** The strategy model (`XDrishti.Strategies.Model`) lists the **candidate** policies in `exits` (the seven above the line, each once; the Lab
compares them on the same entries, §3) and the **overlays** `time_stop` and `event_exit` in `overlays` (at most one of each, applied to every candidate, the doc 16 `exits.overlays`). The compile checks the
parameters of every candidate and of both overlays (names, required or optional, scalar or list, whole numbers, ranges, the relations between values: `t2_rr` above `t1_rr`; `add_at_rr` strictly increasing; `add_pct` as long as
`add_at_rr` and `initial_pct` plus its sum at most 100; `max_adds` at most the number of levels; `swing_sessions` below `max_hold_sessions`; `intraday_bars` at most the bars of a session). The tables of P3-02b2
([ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) Amendment 1) are **accepted but provisional until P4's spec**: the break-even offset, whether a trail may move down, what activation means and
a pyramid trail other than the ATR are the walker's rules, not parameters, and P4 may rename or add an optional name (by Amendment 2) as long as no stored strategy uses it. The features a trail reads (the ATR, the N-bar extreme, the
EMA) are plain features on the evaluation timeframe, **as of the closed bar**: a value computed at the close of bar *k* may move a stop or signal an exit only from the first minute after that close, never inside bar *k* (P4's spec carries this
as a property: truncating the data after bar *k* changes no decision at or before *k*). `FIXED_RR` is the one policy the P3 simulator runs; the others are accepted by the model, given the warning `exit_not_evaluated` and refused by name by the simulator.

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

| Parameter | Name in the model | Default |
|-----------|-------------------|---------|
| Initial position | `initial_pct` | 50% of full size |
| Add triggers | `add_at_rr` (strictly increasing) | +1R, +2R (on trigger-TF close) |
| Add sizes | `add_pct` (as many as `add_at_rr`; with `initial_pct` at most 100) | 30%, 20% |
| Max adds | `max_adds` (1 to the number of levels) | 2 |
| Trail of the combined position | `trail_atr_mult`, optional `atr_period` | the ATR (the only trail in schema 1) |
| Risk rule | not a parameter: **always on** | Before each add, move stops so **total open risk ≤ initial 1R** |

The defaults are the example the model accepts (they sum to exactly 100). The names are provisional until P4's spec (§2).

## 5. Time rules

| Rule | Intraday | Swing |
|------|----------|-------|
| Earliest and latest entry | `first_entry` (e.g. 09:30) and `last_entry` (e.g. 14:30); both inclusive: the bar that closes at those times is still allowed | Signal valid N sessions (default 1) — not in the model of P3-02b |
| Forced exit | `square_off` (e.g. 15:15), required | `max_hold_sessions`, 1 to 5, required: the close of session N, counting the entry session as the first and sessions by the trading calendar |
| Time stop | e.g. < +0.5R after 6 bars (the `time_stop` overlay) | e.g. < +0.5R after 2 sessions (the `time_stop` overlay) |
| Overnight gap | — | Gap through stop ⇒ filled at the open (simulated, and counted); a target is never filled better than the target ([ADR 0040](../adr/0040-swing-positions-held-across-sessions-the-maximum-hold-the-position-gate-and-simtrade-schema-3.md)) |

**Swing positions in the simulator (P3-03d, [ADR 0040](../adr/0040-swing-positions-held-across-sessions-the-maximum-hold-the-position-gate-and-simtrade-schema-3.md)).** A swing position stays open across sessions with its stop and target unchanged; the rows of ADR 0038 apply to every minute that holds a trade on every date, and there is no square-off. `MAX_HOLD` N leaves at the **close of the last 1-minute bar of session N** (the entry session is session 1; sessions are counted with the trading calendar, a special session counts and a holiday does not; with no calendar every weekday counts), with the base slippage and no extra ticks; a stop or target reached in that minute comes first. When the due minute holds no trade the position leaves at the close of the last minute that does (counted as `MaxHoldAtLastClose`), and when the data ends first it is reported open (`EndOfData`, never in a statistic). A session on which the walker read no trade is counted (`SessionsUnseen`), not excluded. There is at most one open position per instrument and side: a candidate decided while one is open is not walked and is counted (`PositionOpen`). A swing position that is open when a flagged day (an unadjusted jump above 20 %) starts a new segment of the series is excluded and counted. Dividends are neither adjusted nor received, and the closing auction and the circuit limit of a stock held overnight are not modelled (ISS-18, ISS-27).

**An entry that rests overnight (P3-03e, [ADR 0043](../adr/0043-the-daily-evaluation-and-the-overnight-entry-order-sim-5-and-run-record-schema-5.md)).** In a swing walk a decision taken at or after the end of its session (every decision of a 1D evaluation, and the decision of the last 30m or 1h bar) places its order for the next trading session: it fills at the first minute that holds a trade at most five minutes after that session's open, at the open when that is within the limit (the daily close plus 0.1 %), at the limit when the session opens beyond it and trades back to it, and is otherwise a counted miss; an open at or through the stop is no trade. A list that ends before the fill session is in the data is a pending order (`OrderPendingAtEnd`), not a miss. The trade is dated on the fill date; its key and the window cut follow the day of the decision. The fill is in the minute whose open may be the auction price (ISS-18 (a)): a daily strategy fills there by design.

How the simulator fills a stop that was gapped through, a target, a minute that holds both, the square-off minute and an entry limit order is the rule table of [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md) (P3-03b): a gapped stop fills at that minute's open, a target at the target and never better, stop first unless the minute's open is already beyond the target, and a square-off waits for the next minute that holds a trade.

The four time rules of a strategy are `first_entry`, `last_entry`, `square_off` and `max_hold_sessions` (the `time_rules` of [doc 11](11-strategy-lab.md) §2); [doc 16](16-configuration.md) spells the same four
`first_entry_time`, `last_entry_time`, `square_off_time` and `max_holding_sessions` in its `styles` block. A stored strategy carries its own: the numbers of the configuration are not defaults for a missing
time rule, so that the same stored text (and hash) always means the same behaviour.

## 6. Position sizing (per account)

```
risk_amount = account_capital × risk_per_trade_pct
qty         = min(risk_amount / |entry − stop|, max_capital_per_trade / entry, liquidity_cap)
qty         = round_down_to_lot(qty)        # futures
skip if qty < 1 lot, or cost_R > max_cost_R
```
The simulator's version of this block (built in P3-03c, [ADR 0042](../adr/0042-costs-and-sizing-the-charge-sheet-the-quantity-net-r-and-simtrade-schema-4.md)): `qty = floor(min(risk_amount / |entry − stop|, max_capital_per_trade / entry))` from the **exact** entry, whole shares, on a fixed simulated capital with the `standard` profile; below 1 share, or a charge above `max_cost_R` of the risk taken, the trade is skipped and **counted** (`SkippedQuantity`, `SkippedCost`); the lot rounding and the liquidity cap are not applied (named limits).

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

The simulator charges every simulated order from an effective-dated **charge sheet** (ten rates per segment, intraday and delivery) on the rates in force on the date of that order, on the filled price, each line rounded to the paisa once; it reports `RFilled` (R from the filled prices) and `RNet = RFilled − CostR` side by side with `RExact`, with the 16 lines of the charge visible per trade. A run with no sheet is gross and says `costs: none`; the choice of a sheet by a request, the stored sheet and the check against the owner's bills are P3-03f. The model, the rounding and what is not modelled are in [ADR 0042](../adr/0042-costs-and-sizing-the-charge-sheet-the-quantity-net-r-and-simtrade-schema-4.md).

The simulator's slippage is `SlippageConfig` with these defaults (`costs.slippage` in [doc 16](16-configuration.md)): market-type fills pay `(ticks + extra) × tick + bar_range_frac × (high − low)` against the trade, rounded to the instrument's tick, with the extra ticks on stop exits; a resting limit (a target, an entry at the limit) does not slip. The rules and what is not modelled are in [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md).

The exit rules of §§1, 5 and 8 as the simulator walks them (the order inside a minute, gaps, the square-off, the maximum hold, slippage on the filled price, `MAE`, `MFE` and `R`) are checked against an independent second implementation written from the written rule tables only ([ADR 0047](../adr/0047-the-oracle-and-the-rule-table-gaps.md), P3-08): over 18,500 seeded series and 100 hand rows the two agree on every field of every trade; the disagreement log holds three class A reading decisions of the dry read and one open class B entry for the owner (Q3-MFE: in one corner, an entry at the limit filled in the last minute of the data or of the hold, MFE can be below R). That is agreement with the rules as a second reader understands them, not a comparison with charts (P3-09) and not evidence that any strategy has an edge.
