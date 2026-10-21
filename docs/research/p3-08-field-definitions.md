# P3-08 — the dry-read table: the sentence that defines every compared field

Written 2026-10-19 for [P3-08](../specs/P3-08-independent-walker-oracle-and-leak-properties.md) D4 and D10, by the engine owner (role E), by reading the ADRs and specs the oracle author (role O) may read (D1) and, for the gaps only, the engine. Nothing was built or run.

**Sources and how they are cited.** `ADR 0038 #3` is the numbered decision 3 of that ADR (its line is 15 + the number: `#3` is line 18); `ADR 0040 #n` is line 15 + n; `ADR 0042 #n` is line 15 + n; `ADR 0043 #n` is line 16 + n; `ADR 0037 D3` is line 21, `D4` 23, `D5` 25, `D6` 27. `P3-03 row n` is row n of the field table of `docs/specs/P3-03-simulator-walking-skeleton-scan-walk-and-orb.md` (line 124 + n). `P3-03b X1..X6` are lines 58 to 63, `E4` line 46, `S1` 68, `S3` 70, the MAE sentence line 85, the counter sentence line 86 of `P3-03b-fill-realism-...md`. `P3-03d gate` is line 122 of `P3-03d-swing-positions-...md`; `P3-03e E8` is line 36 and `P3-03e gate` line 40 of `P3-03e-the-daily-evaluation-...md`.

**Status words.**
- `DEFINED`: an ADR sentence (or a spec table the oracle may read) says it, and nothing the oracle needs is left open.
- `DEFINED, ADR 0047 closes a corner`: defined, but a case is left open by the wording; ADR 0047 gives the rule in the words of the reading the engine implements (section number in the last column).
- `SPEC ONLY`: no ADR sentence; the allowed spec has it; ADR 0047 restates it so that the ADR set is complete.
- `NONE`: no sentence anywhere the oracle may read. ADR 0047 adds the rule **before O starts**.
- `PASS-THROUGH`: an echo of the input or a constant; compared for equality only.

The comparison unit for `SimTrade` is its 34 positional properties (the 34th, `Legs`, added by P4-01); for counters, the 32 positional properties (the last three added by P4-01) of `WalkCounters`; for the cost chain (Annex C, P3-08c) the 11 properties of `TradeCosting` and the 8 of `LegCharges`; plus the gate's hold end and blocked set. The `Key` is computed and not compared.

## A. `SimTrade` (34 positional fields, schema 5)

| # | Field | Defining text | Status | ADR 0047 |
|---|---|---|---|---|
| 1 | `SchemaVersion` | ADR 0042 #12 (4) | PASS-THROUGH | - |
| 2 | `StrategyId` | P3-03 row 2 | PASS-THROUGH | - |
| 3 | `InstrumentId` | P3-03 row 3 | PASS-THROUGH | - |
| 4 | `Symbol` | P3-03 row 4 | PASS-THROUGH | - |
| 5 | `Side` | P3-03 row 5 | PASS-THROUGH | - |
| 6 | `TradeDate` | P3-03 row 6 (India date of the entry); ADR 0043 #2 (E9): the fill date, `DecisionEpochSeconds` stays on the decision date | DEFINED | - |
| 7 | `DecisionEpochSeconds` | P3-03 row 7; ADR 0038 #1 (E1): the decision instant is the end of the candidate's own minute | DEFINED (a candidate input) | - |
| 8 | `TriggerTimeframe` | P3-03 row 8 | PASS-THROUGH | - |
| 9 | `TriggerBar` | P3-03 row 9 | PASS-THROUGH | - |
| 10 | `TriggerBarMinutes` | P3-03 row 10 | PASS-THROUGH | - |
| 11 | `TriggerBarComplete` | P3-03 row 11 | PASS-THROUGH | - |
| 12 | `EntryEpochSeconds` | P3-03 row 12 (start of the fill minute); ADR 0038 #1 (E1); ADR 0043 #2 (E9) | DEFINED | - |
| 13 | `EntryPrice` | ADR 0038 #1 (E2 to E5): the open of the fill minute, or the limit `tickDown(close x (1 + p))` (a sell `tickUp`, `1 - p`) | DEFINED | R1 (conversion) |
| 14 | `StopPrice` | ADR 0038 #7: "`SimTrade.StopPrice` is the candidate's stop as converted (8 places)" | DEFINED | R1 |
| 15 | `TargetPrice` | ADR 0038 #7 ("never rounds a stop or a target"; target `entry +- k x risk`); #3 says "every level in decimal from doubles rounded to 8 decimals" | DEFINED, ADR 0047 closes a corner: the two sentences pull apart for the target (8 places or not); ADR 0042 "the unit of R" shows the target can have a ninth decimal | R5 |
| 16 | `ExitEpochSeconds` | P3-03 row 16 and D4 ("the start of the exit minute in every case"); ADR 0040 #3 and #4 (the minute whose close is used) | DEFINED | - |
| 17 | `ExitPrice` | ADR 0038 #2 (X1 to X6), ADR 0040 #1, #3, #4, #5 (X7 to X9, the gap rule) | DEFINED | R1, R5 |
| 18 | `ExitReason` | ADR 0038 #2; ADR 0040 #3, #4; ADR 0037 D4 | DEFINED; the ties ("beyond", "touching counts") are the inequalities of the P3-03b X1 to X4 table | R3 states them in the ADR |
| 19 | `RExact` | ADR 0038 #3 (D5b): `d x (exit - entry) / risk`, decimal, `risk = abs(entry - stop)` of the exact entry; ADR 0038 #13 (+0 for decimal zero) | DEFINED | R1 |
| 20 | `MaeR` | ADR 0037 D5; ADR 0038 #3; ADR 0040 #7 | DEFINED, ADR 0047 closes a corner: the adverse extreme of an X5 fill minute (ADR 0038 says only that the high is left out of MFE; P3-03b line 85 and X5 say the low counts for MAE) | R6 |
| 21 | `MfeR` | ADR 0037 D5; ADR 0038 #3; ADR 0040 #7 ("after MaxHold and EndOfData the last minute counts in full") | DEFINED | R6 |
| 22 | `Fills` | ADR 0038 #4 and #5 (`Exact` when the slippage is `None`, else `Modelled`) | DEFINED | - |
| 23 | `Costs` | ADR 0042 #11, #12 (`Charged` iff `Costing` is not null) | DEFINED | - |
| 24 | `EntrySnapshotJson` | P3-03 row 24 (null) | PASS-THROUGH | - |
| 25 | `RFilled` | ADR 0038 #3 ("uses the filled prices and the same risk"); S1 to S5 | DEFINED | R1, R4 |
| 26 | `SlippageR` | ADR 0038 #3: "`SlippageR = RFilled - RExact <= 0` is exact"; P3-03b line 85 | NONE for the arithmetic: whether it is the difference of the two decimals converted once, or the difference of the two stored doubles, is not said anywhere | R2 |
| 27 | `EntryFilledPrice` | ADR 0038 #4 (S2, S3): buy `min(tickUp(entry + a), limit)`, sell `max(tickDown(entry - a), limit)`; `AtLimit` the limit; `None`: unchanged | DEFINED | - |
| 28 | `ExitFilledPrice` | ADR 0038 #4 (long `tickDown(exit - a)`, short `tickUp(exit + a)`; stop exits add the extra ticks); ADR 0040 #3 (S5: `MaxHold` pays the base amount, no extra); P3-03b S3 line 70: a target exit goes on the grid only (`tickDown` long, `tickUp` short) | DEFINED; the direction of the grid for a target is in the spec only | R4 restates |
| 29 | `EntryFill` | ADR 0038 #1 (E3, E4) | DEFINED | - |
| 30 | `TickFallback` | ADR 0038 #6 | PASS-THROUGH | - |
| 31 | `SessionsHeld` | ADR 0040 #2 (D3), #7 (D8) | DEFINED | - |
| 32 | `SessionsUnseen` | ADR 0040 #6 (D7) | DEFINED | - |
| 33 | `Costing` | ADR 0042 #11 to #12; see section C | DEFINED | - |
| 34 | `Legs` | P4-01 D7 to D12 (added 2026-10-21, schema 5): the fills of a position that left in several; the entry first, the last exit last; null for a trade of one entry and one exit. R of the trade: D10 | SPEC ONLY: the spec P4-01 and ADR 0049 are the text; compared leg by leg, every field | ADR 0049 |

Not a field but compared with them: **the no-trade reason** (`NoTradeReason`): ADR 0038 #1 (E1, E5, E6), ADR 0040 #10 (D11, `PositionOpen`), ADR 0043 #3 (D3, `OrderPendingAtEnd`): DEFINED.

## B. `WalkCounters` (32 positional properties) and `WalkOutcome`

| # | Counter | Defining text | Status | ADR 0047 |
|---|---|---|---|---|
| 1 | `Candidates` | ADR 0037 D6; ADR 0043 #9 (identity) | DEFINED | - |
| 2 | `Trades` | ADR 0037 D6 | DEFINED | - |
| 3 | `NoFillBar` | ADR 0038 #1 (E1); ADR 0043 #2, #3 | DEFINED | - |
| 4 | `AfterSquareOff` | ADR 0038 #1 (E1) | DEFINED | - |
| 5 | `EntryThroughStop` | ADR 0038 #1 (E6) | DEFINED | - |
| 6 | `ExitsStop` | ADR 0037 D6 | DEFINED | - |
| 7 | `ExitsTarget` | ADR 0037 D6 | DEFINED | - |
| 8 | `ExitsSquareOff` | ADR 0037 D6 | DEFINED | - |
| 9 | `ExitsEndOfData` | ADR 0037 D6 | DEFINED | - |
| 10 | `StopGapExits` | ADR 0038 #2 ("count exits whose minute opens strictly beyond the level"); P3-03 D6 (buy `open < stop`) | DEFINED | - |
| 11 | `TargetGapExits` | same (buy `open > target`) | DEFINED | - |
| 12 | `EntriesAfterMissingMinutes` | ADR 0037 D3 (line 21: "a fill minute that starts later than the decision instant ... counted"; a **superseded** decision, D3 of ADR 0037 is replaced by ADR 0038 #1); ADR 0043 #2 (E9: "a fill later than the session's open") | DEFINED for the overnight order; for the intraday sense the only sentence sits in a superseded decision | R7 |
| 13 | `BarsExamined` | ADR 0038 #9; ADR 0040 #9 | DEFINED | - |
| 14 | `LimitNotReached` | ADR 0038 #1 (E5) | DEFINED | - |
| 15 | `EntriesAtOpen` | ADR 0038 #1 (E3) | DEFINED | - |
| 16 | `EntriesAtLimit` | ADR 0038 #1 (E4) | DEFINED | - |
| 17 | `AmbiguousMinutes` | ADR 0038 #2 (X4): "ambiguous when both are reached and the open lies strictly between them" | DEFINED, ADR 0047 closes a corner: which minute can be ambiguous (only the exit minute; never an X5 fill minute, never an X1 minute) | R8 |
| 18 | `LateSquareOffs` | ADR 0038 #2 (X1: "`LateSquareOffs` when the minute starts after `Q`"); P3-03b line 86 ("a `SquareOff` at a minute after `Q`") | DEFINED | - |
| 19 | `ZeroVolumeMinutes` | ADR 0038 #9 ("empty minutes passed over between the decision instant and the exit; for a no-trade before the minute that decided it; none after the last trade minute at the end of the data"); ADR 0043 #2 (decision-date minutes after the candidate's own are ignored; a zero-volume minute is passed over and counted) | DEFINED, ADR 0047 closes the corners: the five-minute window, the minutes after the last trade minute in a `MaxHoldAtLastClose`, the minutes of the decision date, a no-trade that ended by a date change | R9 |
| 20 | `RiskUnderFourTicks` | ADR 0038 #1 (E6: "a risk under four ticks is counted"); P3-03b line 86: "trades with `risk < 4 x tick`" | SPEC ONLY for strictness and unit | R10 |
| 21 | `PositionOpen` | ADR 0040 #10 (D11) | DEFINED | - |
| 22 | `ExitsMaxHold` | ADR 0040 #3 (X8) | DEFINED | - |
| 23 | `MaxHoldAtLastClose` | ADR 0040 #3 (X8, second rule), #9 | DEFINED | - |
| 24 | `OvernightStopGaps` | ADR 0040 #5, #9 (first minute that holds a trade on a date later than the entry date) | DEFINED | - |
| 25 | `OvernightTargetGaps` | same | DEFINED | - |
| 26 | `SessionsHeld` | ADR 0040 #9 (the sum) | DEFINED | - |
| 27 | `SessionsUnseen` | ADR 0040 #9 (the sum) | DEFINED | - |
| 28 | `OvernightEntries` | ADR 0043 #2, #9 | DEFINED | - |
| 29 | `OrderPendingAtEnd` | ADR 0043 #3 (D3) | DEFINED | - |
| 30 | `LegsTotal` | P4-01 D15: the legs of the trades that have legs; 0 for a trade without | SPEC ONLY | ADR 0049 |
| 31 | `LegsMerged` | P4-01 D11: legs of no shares merged into the next one by the costing; 0 in the walker | SPEC ONLY | ADR 0049 |
| 32 | `StopLoosenIgnored` | P4-01 D6: requests of an exit rule to loosen a stop, ignored | SPEC ONLY | ADR 0049 |

`WalkOutcome.HoldEndEpochSeconds` (the gate's input, compared as a list, section D) and the booleans of `WalkOutcome` are the same facts as the counters above.

## C. `TradeCosting` (11 properties) and `LegCharges` (8 lines) - for P3-08c, listed here so that the dry read is one pass

| Field | Defining text | Status | ADR 0047 |
|---|---|---|---|
| `Segment` | ADR 0042 #10 (D12) | DEFINED | - |
| `SheetId` | ADR 0042 #4 | PASS-THROUGH | - |
| `Quantity` | ADR 0042 #9 (D11): `floor(min(capital x risk% / risk, capital x cap% / entry))`, entry the exact entry, below 1 a skip | DEFINED, ADR 0047 closes a corner: the entry and stop are the stored doubles converted by R1 | R11 |
| `RiskTaken` | ADR 0042 #9: `quantity x risk` | DEFINED | R11 |
| `EntryTurnover`, `ExitTurnover` | ADR 0042 #11 (D13): quantity times the **filled** price; D3: the price is the stored double converted by the 8-place rule | DEFINED | R11 (a target's 9th decimal is rounded in the cost chain and not in R) |
| `EntryLeg`, `ExitLeg` | ADR 0042 #7 (D7: schedule of the trade date, and of the date of the exit minute), #8 (D8, D9), #6 (D6) | DEFINED | - |
| `LegCharges.Brokerage` | ADR 0042 #8: `min(cap, max(min, T x pct / 100))`, 0 when the cap is 0 | DEFINED | - |
| `LegCharges.Stt` | #8: on the side the sheet says (`stt_buy_pct`, `stt_sell_pct`) | DEFINED | - |
| `LegCharges.ExchangeTxn`, `SebiFee`, `OtherCharges` | #8 (exchange and SEBI on both sides; "other" a share of turnover; SEBI per crore, one crore is 10,000,000) | DEFINED | - |
| `LegCharges.StampDuty` | #8: buy orders only | DEFINED | - |
| `LegCharges.DpCharge` | #8: once on a delivery sell, the sheet's figure, not rounded again | DEFINED | - |
| `LegCharges.Gst` | #8, #6: GST on the **rounded** brokerage, exchange and SEBI of the same leg, then rounded by the schedule's rule | DEFINED | - |
| `TotalCost` | #6, #11: the sum of the 16 rounded lines | DEFINED | - |
| `CostR` | #11: `TotalCost / RiskTaken` in decimal, converted once to double | DEFINED, ADR 0047 closes a corner: decimal zero is +0 | R12 |
| `RNet` | #11: `RFilled - CostR` | NONE for the arithmetic (the difference of the two stored doubles, in double; a zero result is +0) | R12 |

The rounding of a charge line (`half_away_from_zero`, `half_even`, `down`) is ADR 0042 #6 and #4; the 16 lines are therefore defined. The two skips and the equal-kept rule are ADR 0042 #11 and the *In short*.

## D. The position gate

| Item | Defining text | Status | ADR 0047 |
|---|---|---|---|
| Blocked set | ADR 0040 #10 (D11): blocked when `D <= E`, or after an accepted `EndOfData`; swing only; a segment restart empties the gate | DEFINED | - |
| Hold end `E` | ADR 0040 #10: "its exit instant" (for `MaxHoldAtLastClose` `ToMinute(T)`); P3-03d gate line 122: "`E` is `ExitEpochSeconds`"; P3-03e gate line 40: "the start of the due minute, 15:29" | SPEC ONLY: the ADR says "exit instant" and the instant is the **start** of the exit minute only by P3-03d and P3-03e | R13 |
| A no-trade and a pending order | ADR 0040 #10 ("a no-trade is not accepted"); ADR 0043 #3 ("a pending order ... does not set the gate") | DEFINED | - |

## E. Cross-cutting questions that no field row answers

| Question | Defining text | Status | ADR 0047 |
|---|---|---|---|
| How a double becomes a decimal | ADR 0038 #7: "`TryConvert` turns a double price into a decimal rounded to 8 places"; ADR 0042 #3: `Math.Round((decimal)x, 8)` | NONE for the midpoint rule of the rounding and for the 15-significant-digit step of the `(decimal)` cast, which is not named | R1 |
| How a decimal becomes a double | ADR 0038 #13 (decimal zero is +0.0) | NONE for the conversion itself (the platform cast, one conversion per field) | R1 |
| A well-formed bar | no sentence (the walker does not validate high >= open, close >= low) | NONE: the oracle's series must keep `low <= open, close <= high`; the engine's behaviour on a malformed bar is not a rule | R14 |

## Summary (at the dry read, before ADR 0047)

- **Properties read:** 33 `SimTrade` + 29 `WalkCounters` + 11 `TradeCosting` + 8 `LegCharges` = **81**, plus the no-trade reason, the gate (blocked set and hold end) and 3 cross-cutting questions: **86 items**.
- **Pass-through / constants:** 12 (11 `SimTrade` fields and `SheetId`).
- **No defining sentence anywhere (`NONE`): 5 items** - the arithmetic of `SlippageR` and of `RNet`, and the three cross-cutting questions (the midpoint and 15-digit step of the double-to-decimal conversion, the decimal-to-double conversion, what a well-formed bar is). All five get a rule in ADR 0047 (R2, R12, R1, R1, R14).
- **Spec only (`SPEC ONLY`): 2 items** - `RiskUnderFourTicks` (strictness and unit) and the hold end as the start of the exit minute; plus the grid direction of a target exit inside `ExitFilledPrice`. ADR 0047 restates them (R10, R13, R4).
- **Defined but a corner left open: 8 items** - `TargetPrice` (R5), `ExitReason` ties (R3), `MaeR` in an X5 minute (R6), `EntriesAfterMissingMinutes` (R7), `AmbiguousMinutes` (R8), `ZeroVolumeMinutes` (R9), `Quantity` (R11), `CostR` (R12). The three where the engine's reading is one of two possible readings of existing sentences are in [the disagreement log](p3-08-disagreements.md) (D-01 to D-03).
- After ADR 0047 **no row has the status `NONE`**; a field added later is caught by the reflection test of D4.
