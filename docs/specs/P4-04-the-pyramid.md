# P4-04 — `PYRAMID`

- **Status:** Built 2026-10-22, ahead of the P3 exit check at the owner's request ([ADR 0052](../adr/0052-the-pyramid.md); the number 0051 named below is P4-03's); the build notes are at the end. Approved (owner, 2026-10-19, "yes to all recommendations": questions 1 to 8 stand as recommended, including question 7: re-read D3, D4, D7 and D9 against the built P4-01 before the build starts; the build still waits for P3 to close). Written 2026-10-19 from `main` at a6b27af. The table was read in [P3-02b2](P3-02b2-exit-policy-tables-of-schema-1.md) (D7, D11, D12), doc 08 §1 and §4 and the Approved specs [P4-01](P4-01-the-exit-tape-and-trades-with-legs.md), [P4-02](P4-02-partial-exits-and-the-time-stop.md) and [P4-03](P4-03-the-trails-atr-structure-and-ema.md), **none of which is built**: every name of the tape, `IExitRule`, the legs, `StopLoosenIgnored` and `TimeStop` below is the one those specs give and is UNVERIFIED against code until they are built. Written ahead of the build on the owner's request; it should be re-read against the code of P4-01 before it is approved. **Nothing is built before P3 closes** ([P4-00](P4-00-exits-and-exit-learning-plan.md)).
- **Phase / roadmap row:** P4 — Exits & exit learning; slice 4 of the plan [P4-00](P4-00-exits-and-exit-learning-plan.md). Candidate for the cut line of P4-00 question 6 (the pyramid moves to P5 if time runs short).
- **Design refs:** docs/design/08-exits-and-risk.md §1 (R), §4 (pyramiding: "used only where the cell shows trend persistence"), §6 (sizing) · 16-configuration.md §2 (`exits`) · [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) Amendment 1 · [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md) · [ADR 0042](../adr/0042-costs-and-sizing-the-charge-sheet-the-quantity-net-r-and-simtrade-schema-4.md) · ADR 0048 to 0050 (P4-01 to P4-03, to be written).

## Goal
The simulator runs a strategy whose exit is `PYRAMID`: enter `initial_pct` of the full size, add the listed shares when a bar closes at the listed progress, never let the total open risk exceed 1R, trail the combined position by the ATR, and leave all of it together. This is the first policy with more than one entry leg. It runs one exit set at a time, as P4-02 and P4-03 do.

## Scope
**In:**
1. **`PYRAMID`** as an `IExitRule` (D1 to D8), intraday and swing, buy and sell, with the trail of D5 and the risk rule of D4.
2. **R, quantity and costs for a trade with several entry legs** (D9, D10), on top of P4-01 D10 and D11.
3. **What the simulator accepts** (D11): P4-03 D8 widened to `PYRAMID`.
4. **Counters** for adds taken, missed and skipped (D12).
5. **The oracle** extended from the rule table of D1 to D8 and ADR 0051 only, and the properties (D13).
6. **Tests, ADR 0051, the docs** (Done when).

**Out (not in this slice):** a pyramid trail other than the ATR (a new row in schema 1, P3-02b2 D7); adds that fill at a limit rather than at the open (D3); a partial exit inside a pyramid; scaling the add by the current ATR; the portfolio-level heat limit (doc 08 §7, not simulated in P3 or P4); more than one candidate (P4-05); `EVENT_EXIT`.

**Size.** About **2 agent-days** (P4-00 estimate; not calibrated). Itemised, UNVERIFIED: the rule, the add trigger and fill, the risk rule 0.7; R, quantity and costs with several entry legs 0.4; `Simulator.Create`, counters, export, store and cards 0.2; oracle and properties 0.5; ADR and docs 0.2. If it overruns, the whole slice moves to P5 (P4-00 question 6); inside the slice, cut the swing seeds of the oracle before the intraday ones, never the risk rule or the leak property.

## Definitions
Notation as P4-02 and P4-03: `R0 = |entry − initial stop|` of the exact first entry; "buy" is written, a sell mirrors every comparison and sign; a level is an exact decimal; "minute" is a minute that holds a trade; "bar *k*", "counted", the tape and `Close_k` are P4-01's and P4-03's. **Shares are fractions of the full size `Q`**, the quantity that sizing (ADR 0042) gives for a trade risking 1R at the initial stop: the initial leg is `initial_pct/100` of `Q`, an add is `add_pct[j]/100` of `Q`. **1R of a pyramid is `Q × R0`**, the risk of the full size (question 1).

**D1. `PYRAMID` (doc 08 §4).** Parameters as P3-02b2 D7: `initial_pct` in (0, 100], `add_at_rr` (a strictly increasing list of progress levels in (0, 10]), `add_pct` (a list as long as `add_at_rr`, each in (0, 100], with `initial_pct` plus the sum at most 100), `max_adds` (whole, 1 to the length of `add_at_rr`), `trail_atr_mult` in (0, 20], optional whole `atr_period` (14). **The ladders `add_at_rr` and `add_pct` are not candidate lists** (P4-02 D10 refuses a list of more than one value for the policies that have candidate lists; a ladder is the policy's own shape and is accepted in full). Reads `atr(atr_period)` and `Close` from the tape.

**D2. The ladder and the trigger.** The levels are used in order, at most `max_adds` of the first ones. Level *j* is armed from the moment level *j − 1* has been **taken, missed or skipped** (D3, D4); the first level is armed from the first counted bar. At a counted bar *k* with the next level *j* armed and `progress_k = (Close_k − entry₀) / R0 >= add_at_rr[j]` (entry₀ is the exact first entry; the close of the bar, not a touch inside it, as the trigger timeframe of doc 08 §4 says), the add is **attempted** (D3). At most one attempt per bar: a close beyond two levels attempts the first now and the second at the next bar that closes at or beyond it.

**D3. The add order.** An add is a market-type buy of `add_pct[j]/100 × Q` shares placed at the close of bar *k*, with the entry's limit (the close of bar *k* plus the entry limit percent, ADR 0038 E2, rounded down to the tick). It fills at the **open of the first minute after the close of bar *k* that holds a trade**, **only if that open is within the limit** (a sell mirrored); it pays the entry's slippage. If the open is beyond the limit the add is **missed** (counted, not retried, the level is consumed); the rule does not fill an add at the limit when the minute trades back to it, because the order inside the minute is unknown and the stop (D4) would then have no defined moment (recommended; question 3). An add whose quantity `floor(Q × add_pct[j]/100)` is 0 is **skipped** and counted (`AddsSkippedQuantity`). An add at or after the square-off minute, or at a bar that closes at or after it, is not attempted. A minute that is also the minute of a stop exit (the stop in force before the add) is a stop exit and the add is not made.

**D4. The risk rule (always on; doc 08 §4: "Before each add, move stops so total open risk ≤ initial 1R").** Before an add fills at the price `P` of D3, the new stop is `S = max(S_now, S_req)` where, for a buy with open legs of shares `s_i` at exact entries `e_i` (the add included at `P`),
`S_req = (Σ s_i·e_i − 1.0·R0) / Σ s_i`
so that `Σ s_i·(e_i − S) / R0 <= 1.0`: the total open risk, in R, is at most 1. If `S_req >= P` (the stop could not be below the add's own price) the add is **skipped** (`AddsSkippedRisk`, counted, the level consumed). Otherwise the stop becomes `S` (tighter than or equal to `S_now`, never looser) **from the minute of the add**, which is the only place where a stop takes effect before the first minute after a bar's close: it uses only the open `P` of that minute, which is known at the open; it is checked by the minute table together with the add (a stop touched in the add's own minute exits all the shares, adds included, at the stop or at the gap). Worked example (hand row 1, buy, exact fills, no slippage): `Q` shares, `entry₀ = 100.00`, stop 99.00 (`R0 = 1.00`), `initial_pct` 50, `add_at_rr` [1, 2], `add_pct` [30, 20]. A bar closes at 101.20 (progress 1.2 ≥ 1); the next open is 101.25, within the limit 101.30. `S_req = (0.5·100 + 0.3·101.25 − 1.0) / 0.8 = 79.375 / 0.8 = 99.21875`, tighter than 99.00, so the stop becomes 99.21875 from that minute. The price falls to 99.21875: all 0.8 leave there. P&L `= 0.5·(99.21875 − 100) + 0.3·(99.21875 − 101.25) = −0.390625 − 0.609375 = −1.0`, **`RExact` −1.00**: the cap holds the loss of a stopped pyramid to exactly 1R.

**D5. The trail of the combined position.** At every counted bar *k* with `ATR_k` a number: `level_k = H_k − trail_atr_mult × ATR_k`, with `H_k` the highest high from the first fill minute through the end of bar *k* (a sell: lowest low, plus); it takes effect from the first minute after the close of bar *k* only if tighter than the stop in force (a looser one is ignored and counted, `StopLoosenIgnored`). It is the chandelier of P4-03 D2 with `activate_at_rr = 0` (active from the first counted bar); the pyramid shares P4-03's rule and no code beyond the minute table, so the two can differ later. The stop of D4 and the trail of D5 are the same stop: it only ever tightens.

**D6. The exit.** There is no target. All open shares leave together, as **one exit leg**, at the first of: the stop in force (a stop level, or the open if the minute gapped through it; market-type, pays slippage), the square-off, `MAX_HOLD`, the `time_stop` overlay of P4-02 (progress measured against `entry₀`), or the end of the data; reasons `Stop`, `SquareOff`, `MaxHold`, `TimeStop`, `EndOfData`.

**D7. Legs.** One entry leg (`initial_pct`), one add leg per add taken, one exit leg. A pyramid with no add taken has one entry leg and one exit leg, so it is a trade **without legs** (P4-01 D9): `Legs` is null and every field is as for a one-fill trade of share `initial_pct` (its `Quantity` is the initial quantity). With at least one add, `Legs` is written (P4-01 D7, D8).

**D8. Quantity, the gate and the cap on capital.** `Q` is sized from the exact entry and the initial stop as ADR 0042 says, for the full size; the initial leg is `floor(Q × initial_pct/100)` and a quantity of 0 there makes the trade a `SkippedQuantity` as today. The quantity cap `max_capital_per_trade / entry` is applied to `Q` at the first entry's price; adds at higher prices can lift the notional above it, which is a **named limit**, printed in the run record's not-modelled lines (UNVERIFIED: whether ADR 0042 should be amended to cap at the add price; question 4). The position gate holds the instrument until the exit leg (P4-01 D12); an add is not a new trade for the gate.

**D9. R of a trade with several entry legs.** The unit is `Q × R0`. For a buy, `RExact = (Σ_exit s_x·x − Σ_in s_i·e_i) / R0` over the exit leg's shares `s_x` at the exact exit price `x` and the entry legs' shares `s_i` at their exact entries `e_i`; `RFilled` is the same on filled prices; `SlippageR = RFilled − RExact` and never positive (each leg slips against the trade). This is P4-01 D10 stated for several entry legs: the share-weighted average entry gives the same number. A pyramid that stops out at the cap (hand row 1) has `RExact = −1.00`, not `−R0 × shares`. **MAE and MFE** are the excursions of price against the share-weighted average entry of the legs open at that minute, in units of `R0`, as P4-01 D10 says (price units, not scaled by the shares; question 2).

**D10. Costs.** Each leg is one order charged on the schedule in force on its date (P4-01 D11): the entry, each add and the single exit. The exit leg's quantity is the sum of the quantities of the legs in, not `floor(Q × share)`; `RiskTaken = Q × R0`, `CostR = TotalCost / RiskTaken`, `RNet = RFilled − CostR`. A pyramid that adds pays more charges than the same entry with a single exit, which is part of what the comparison of P4-05 is for.

**D11. What `Simulator.Create` accepts.** P4-03 D8 widened: a side whose `exits` has exactly one entry among `FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL`, `ATR_TRAIL`, `STRUCTURE_TRAIL`, `EMA_TRAIL` and `PYRAMID`, with one value in each candidate list (the ladders of `PYRAMID` are not candidate lists, D1) and at most `time_stop`; `EVENT_EXIT`, two candidates or a candidate list of two values stay `simulation.unsupported_exit`. `ExitTables.RunBySimulator` gains `PYRAMID`; `exit_not_evaluated` is no longer given for it; the test that the model's and the simulator's lists agree is updated.

**D12. New counters** (run record; zero for every run without a pyramid): `AddsTaken`, `AddsMissed` (open beyond the limit), `AddsSkippedRisk` (D4), `AddsSkippedQuantity` (D3); `LegsTotal` and `StopLoosenIgnored` are reused.

**D13. The oracle and the properties.** The reference implementation of P3-08 gets the rule written from D1 to D10 and ADR 0051 only; agreement on every field of every trade over at least 18,500 seeded series, including: the hand rows; a close beyond two levels; an add missed by the limit; an add skipped by the risk rule (`S_req >= P`); `max_adds` below the ladder; an add and the stop in one minute; the stop gap through the combined stop; an add at the square-off bar (not attempted); a one-share quantity (the add's quantity 0); a trail that moves above the risk-rule stop; a swing pyramid across sessions; a sell. **Leak** (P4-00 D9) over 10,000 seeds with its negative control failing; **monotonic stop**; **risk cap** (property: after every add, the total open risk at the stop in force is at most 1.0 R to the printed precision); **legs** (P4-01 invariants, with the shares of the entry legs plus adds at most `initial_pct + Σ add_pct ≤ 100`).

## Acceptance criteria
1. **Nothing else moves.** Every run, golden and oracle case of P4-03 is unchanged byte for byte.
2. **Hand rows.** At least 24 hand-computed rows (buy and sell; intraday and swing; no add, one add, two adds, `max_adds` 1 of 2; an add missed; an add skipped by risk; the combined stop hit before and after an add; trail above the risk stop; square-off, `MAX_HOLD` and `TimeStop` with adds open; a gap through the stop with adds open), with the legs, `RExact`, `RFilled`, `SlippageR`, `MaeR`, `MfeR` to the printed precision; hand row 1 of D4 (−1.00) and hand row 2 are in it. **Hand row 2** (buy, exact fills): the same ladder; the first add fills at 101.25 as above, the second add (20 %) at 102.40 after a close at 102.30; then `S_req = (50 + 30.375 + 20.48 − 1.0)/1.0 = 99.855` (tighter than the 99.21875 in force); the position leaves at the square-off at 103.00: `RExact = 1.0·103.00 − 100.855 = +2.145`.
3. **The risk cap.** In every seeded series, after every add taken, the total open risk at the stop in force is at most 1.0 R (the property of D13), and the stop is never looser than before.
4. **Costs.** For the hand rows with a TEST sheet, the charges per leg (entry, adds, exit), the quantities (the exit leg equals the sum of the legs in), `TotalCost`, `CostR`, `RNet` equal the hand values to the paisa.
5. **Counters.** `AddsTaken + AddsMissed + AddsSkippedRisk + AddsSkippedQuantity` equals the number of add attempts of the oracle; each is zero for a run with no pyramid.
6. **Refusals (D11).** The seven candidates are accepted, with `time_stop`; `EVENT_EXIT`, two candidates and a candidate list of two values are refused by name at the right path; the lists agree.
7. **Properties and oracle (D13).** All hold; the negative control of the leak property fails.
8. **Stores and cards.** A pyramid with adds round-trips through the export and the store with its legs; a card shows the entry, add and exit legs and their charges; the blindness scans pass; a pyramid without an add is shown as a trade without legs.
9. **The verbs.** `simulate` and `verify-pack` run a seeded fixture with `PYRAMID` end to end; the banner and the not-modelled lines are updated for the capital cap of D8.
10. **Performance.** The `FIXED_RR` path stays within the 5 % of P4-01 AC 14; the pyramid allocates nothing per minute beyond its legs (explicit benchmark).
11. `scripts/check.sh` green; a `reviewer` pass with no blocker or major; ADR 0051 and the docs (08 §1, §4, §6; 16 `exits`; 18-roadmap; the oracle log; ADR 0036 Amendment 2: the `PYRAMID` table stops being provisional) done and `scripts/build-docs.sh` run.

## Contract
- **API:** none.
- **Data:** counters of D12; no new `ExitReason`; no migration (the legs column of P4-01 holds the adds). `SimTrade` of a pyramid: `EntryPrice` and the entry fields are the first leg's, `StopPrice` the initial stop, `TargetPrice` null, `ExitReason` the exit leg's.
- **UI:** none; cards as in P4-01 D16.
- **Events/jobs:** none.

## Non-functional
As P4-01 to P4-03 (allocation-free `FIXED_RR`, deterministic bytes). A pyramid needs `atr` and `Close` in the tape only. The state per position is the list of legs open (at most `1 + max_adds`, bounded by 11). No secret, no network, no new log event id.

## Test plan
Unit: the ladder, the trigger, the add's fill and misses, the risk rule and its skip, the combined trail, the single exit leg, R and costs with several entry legs, refusals. Property: leak, monotonic stop, risk cap, legs, byte-identity of the earlier policies. Oracle: D13. Integration: verbs, export, store, cards. Edge cases: D13 plus a pyramid whose first add is attempted on the very first counted bar; an `initial_pct` of 100 with a ladder that sums to nothing (no add possible: `initial_pct + Σ add_pct ≤ 100` forces `add_pct` to be empty, which the table refuses, so a pyramid always has room); a swing pyramid whose add is attempted across a session break on E = 1D (the add fills at the next session's open within the limit).

## Open questions
All eight answered "yes to all recommendations" by the owner on 2026-10-19: each stands as the recommendation written in it.

1. **The unit of R (D1, D9).** 1R of a pyramid is the risk of the **full size** `Q × R0`, so a pyramid that enters only `initial_pct` and stops out loses `initial_pct` R (hand row: −0.5R before any add), and results are comparable with the other exits on the same entries (P4-05). Recommended: yes. The alternative, 1R = the risk of what is actually open, makes a half-size loss −1R and breaks the comparison.
2. **MAE and MFE (D9):** price excursion against the share-weighted average entry in units of `R0`, not scaled by the shares. Recommended: yes (the same as P4-01 D10).
3. **Adds fill only at the open (D3).** An add whose open is beyond the limit is missed, never filled at the limit when the minute trades back. Recommended: yes (pessimistic; it keeps the stop's moment defined); the alternative reuses E5 and needs a rule for the stop inside that minute.
4. **The capital cap and adds (D8).** Apply `max_capital_per_trade / entry` to `Q` at the first entry's price and name the excess as a limit. Recommended: yes for P4; amend ADR 0042 only if a result needs it.
5. **One attempt per bar and a consumed level (D2, D3).** A missed or skipped add consumes its level; the next level is armed at once. Recommended: yes.
6. **The trail starts at the first counted bar (D5)** with no activation, the chandelier of P4-03 D2. Recommended: yes (an activation is not in the table).
7. **Re-read before approval.** This spec was drafted before P4-01 is built. Recommended: approve it now for the plan, and re-read D3, D4, D7 and D9 against the built legs and tape before the build starts.
8. **Cut line.** If P4 runs late, `PYRAMID` moves to P5 (P4-00 question 6). Recommended: yes, nothing else in P4 depends on it.

## Done when
- [x] The owner has answered questions 1 to 8 and the status is Approved (2026-10-19)
- [x] All acceptance criteria have evidence (see the build notes; the deviations are listed there)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors (the caller runs the reviewer)
- [x] No migration, no API change
- [x] ADR 0052 written (not 0051: P4-03's); `docs/design` (08, 16, 18-roadmap), the oracle log and ADR 0036 Amendment 2 updated; `scripts/build-docs.sh` run

## Build notes (2026-10-22)
What differs from the text above, and what the criteria rest on.

**Deviations (all in [ADR 0052](../adr/0052-the-pyramid.md)).**
1. The ADR is 0052, not 0051.
2. An add skipped by the risk rule cannot happen (ADR decision 4): the counter exists and is always 0, a property holds that, and there is no hand row and no oracle case for it (criterion 2 and D13 asked for both).
3. A pyramid is always written with legs, also with no add taken (D7 and criterion 8 say it has none): `SimTrade` has no field for the share and the costing needs it.
4. `AddsSkippedQuantity` needs the full size `Q`, known only to the costing: a gross run skips no add for its quantity; the costed replay hands its sizing to the walker.
5. Criterion 1 (nothing else moves) holds for every trade and every counter; the goldens of `run.json` and the banners gain the four counters and the eleventh named limit `pyramid_capital_cap`, as P4-03's gained its counter.
6. The store has no column for legs (ADR 0049), so `simulate --store` of a pyramid is refused as for the partials; the legs are in the export. Criterion 9 (the verbs) is met at the simulator, the replay and the exporter, as in P4-03: no built-in strategy carries a pyramid.
7. Criterion 10: the allocation is tested (`PyramidAllocationTests`); the 5 % throughput check of the `FIXED_RR` path is the explicit benchmark of ADR 0051, to run by hand on a quiet machine, and has not been run.

**Evidence by criterion.** 1: the whole suite with the goldens regenerated for the new counters only; 2: `PyramidHandTableTests` (25 rows, P1 and P2 are the two hand rows); 3 and 7: `PyramidPropertyTests`, `Oracle.PyramidComparisonTests`; 4: `PyramidCostTests`; 5: `PyramidComparisonTests` and `PyramidSimulatorTests`; 6: `ExitBoundaryTests`, `PyramidSimulatorTests`; 8: `PyramidExportTests`; 10: `PyramidAllocationTests`; 11: `scripts/check.sh` and the docs.
