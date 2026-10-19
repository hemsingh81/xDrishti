# P4-03 — The trails: `ATR_TRAIL`, `STRUCTURE_TRAIL` and `EMA_TRAIL`

- **Status:** Draft (written 2026-10-19 from `main` at b0ee2db; nothing here is Approved until the owner says so). The tables were read in [P3-02b2](P3-02b2-exit-policy-tables-of-schema-1.md) (D4 to D6, D12, D13), doc 08 §2 and [P4-01](P4-01-the-exit-tape-and-trades-with-legs.md) and [P4-02](P4-02-partial-exits-and-the-time-stop.md), which are Approved and **not built**: every name of the tape, `IExitRule`, the legs and `TimeStop` below is the one those specs give and is UNVERIFIED against code until they are built. **Nothing is built before P3 closes** ([P4-00](P4-00-exits-and-exit-learning-plan.md)).
- **Phase / roadmap row:** P4 — Exits & exit learning; slice 3 of the plan [P4-00](P4-00-exits-and-exit-learning-plan.md).
- **Design refs:** docs/design/08-exits-and-risk.md §2 (the as-built paragraph: a value of bar *k* acts only from the first minute after its close), §3 (trail activation from MAE/MFE giveback), §5 · 16-configuration.md §2 (`exits`) · [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) Amendment 1 · [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md) · [ADR 0040](../adr/0040-swing-positions-held-across-sessions-the-maximum-hold-the-position-gate-and-simtrade-schema-3.md) · ADR 0048 (P4-01) · ADR 0049 (P4-02), both to be written.

## Goal
The simulator runs a strategy whose exit is an ATR trail (chandelier, with an activation), a structure trail (under the N-bar extreme) or an EMA exit (leave when a bar closes beyond the EMA), with the initial stop live until the trail is tighter, the square-off or `MAX_HOLD` as always, and optionally the `time_stop` overlay of P4-02. These policies **leave in one fill**, so their trades have no legs; what is new is that the position's stop is moved by values of the tape and that one policy exits on a signal. One exit set per run, as in P4-02.

## Scope
**In:**
1. **`ATR_TRAIL`** (D1 to D3), **`STRUCTURE_TRAIL`** (D4, D5) and **`EMA_TRAIL`** (D6, D7) as `IExitRule`s, for intraday and swing walks, buy and sell.
2. **What the simulator accepts** (D8): P4-02's rule (one candidate, one value per list, at most `time_stop`) widened to the three policies; `ExitTables.RunBySimulator` and the warning `exit_not_evaluated` follow.
3. **One new exit reason, `EmaExit`** (D7).
4. **The oracle** extended for the three rules from the rule tables here and ADR 0050 only, and the leak property for each (D10).
5. **Tests, ADR 0050, the docs** (Done when).

**Out (not in this slice):** `PYRAMID` (P4-04: the trail of the combined position uses D2's anchor rules but is specified there); more than one candidate or a list with more than one value (P4-05); an activation for `STRUCTURE_TRAIL` or `EMA_TRAIL` (not in the tables; question 3); a trail that offsets the level by a tick, a percentage or a spread; any change to the stop kinds, entries, fills or charges; `EVENT_EXIT`.

**Size.** About **2 agent-days** (P4-00 estimate; not calibrated). Itemised, UNVERIFIED: the three rules and their tape features 0.7, the activation and the anchor `H` from the minutes 0.3, `Simulator.Create`, the reason, counters, export, store and cards 0.2, the oracle and the properties 0.6, ADR and docs 0.2. If it overruns, cut in this order: the swing forms of the oracle seeds (the unit tests stay), the cards' extra text; never the leak property or the oracle on the intraday form.

## Definitions
Notation as P4-02: `R0 = |entry − initial stop|` of the exact entry; "buy" is written, a sell mirrors every comparison and sign; a level is an exact decimal; "minute" is a minute that holds a trade; "bar *k*" and the tape are P4-01's. A bar is **counted** when it closes at or after the fill minute (every bar that contains the fill minute counts, as in P4-02 D8). `progress_k = (Close_k − entry) / R0` for a buy, with `Close_k` the tape's close of bar *k*.

**D1. `ATR_TRAIL` (doc 08: "Chandelier/ATR trail after activation").** Parameters as P3-02b2 D4: `mult` in (0, 20] (one value in this slice), `activate_at_rr` in [0, 10], optional whole `atr_period` (14). Reads `atr(atr_period)` on E, from the tape.

**D2. The chandelier level (recommended).** For a counted bar *k* with `ATR_k` a number: `level_k = H_k − mult × ATR_k`, where `H_k` is the highest high of the minutes from the fill minute through the end of bar *k* (a sell: the lowest low, and plus). `H_k` comes from the minutes the walker has already read; it is not a tape feature. The anchor is **the entry**, not the activation: a trail that has just activated already stands under the whole run since the entry, as a chandelier does. The level takes effect from the first minute after the close of bar *k* (P4-01 D2), only if it is **tighter** than the stop in force (a looser one is ignored and counted, `StopLoosenIgnored`, P4-01 D6), and the stop is then gap-aware (a stop level, or the open if the minute gapped through it; market-type, pays slippage; reason `Stop`). A bar with `ATR_k` not a number gives no level.

**D3. Activation.** `ATR_TRAIL` computes levels only once it is **active**. It is active from the first counted bar *j* with `progress_j >= activate_at_rr` (the close, not the high: a touch inside a bar that closes back below does not activate; question 2), from the level of bar *j* itself (its `level_j`, effective from the next minute), and stays active for the rest of the trade even if progress falls back. With `activate_at_rr = 0` the trail is active from the first counted bar whatever it closed at (doc 08: "0: trailing from the entry"). Worked example (hand row 1, buy, exact fills): entry 100.00, stop 99.00 (`R0 = 1.00`), `mult` 2.0, `activate_at_rr` 1.0, ATR 0.50 on every bar. Bar 1 closes at 100.40 (progress 0.4, not active). Bar 2 closes at 101.20 (progress 1.2, active) and `H_2 = 101.60`, so `level_2 = 101.60 − 1.00 = 100.60`, effective from the next minute. The price falls to 100.60: exit at **100.60, `RExact` +0.60**, reason `Stop`.

**D4. `STRUCTURE_TRAIL` (doc 08: "Trail beyond prior swing / previous candle").** Parameter as P3-02b2 D5: `lookback_bars` (whole, 1 to 10,000). Reads, from the tape, `lowest(lookback_bars, source=low)` on E for a buy and `highest(lookback_bars, source=high)` for a sell. At every counted bar *k* `level_k` is that value as of the close of bar *k*, which includes bar *k* itself (ADR 0031). There is no activation and no offset: the level is the extreme; it takes effect from the first minute after the close of bar *k* if tighter than the stop in force (a looser one is ignored and counted); the stop is gap-aware as in D2. A value that is not a number gives no level. "Previous candle" is `lookback_bars` 1.

**D5. What the structure level can be.** The N-bar extreme may include bars before the entry; it is never above the low of bar *k*, which for the first counted bar is not above the entry for a buy; a level at or above the price when it takes effect is a stop already passed, and fills at the open of its first minute (the gap rule), not at the level. Worked example (hand row 2, buy, `lookback_bars` 1, entry 100.00, stop 99.00): counted bars have lows 100.30 and then 100.90 (and 100.50 afterwards). `level_1 = 100.30` (tighter than 99.00, effective from the minute after bar 1), `level_2 = 100.90` (effective after bar 2), the next bar's 100.50 is looser and is ignored (counted). The price falls to 100.90: exit at **100.90, `RExact` +0.90**.

**D6. `EMA_TRAIL` (doc 08: "Exit on close beyond EMA").** Parameter as P3-02b2 D6: `length` (whole, the catalog's range). Reads `ema(length)` of the close on E, from the tape. At every counted bar *k* with `EMA_k` a number: if `Close_k < EMA_k` (a sell: `>`), the position is signalled out. The initial stop stays live throughout and the policy has no target.

**D7. The EMA exit.** On a signal at bar *k* the whole position leaves at the **open of the first minute after the close of bar *k* that holds a trade**, market-type (pays slippage), reason **`EmaExit`** (a new member of `ExitReason`, stored by name, additive, counter `ExitsEmaExit`). Until that minute the initial stop is live as always (a minute that touches the stop first is a stop exit: the stop is checked before the signal exit of the same open, because the stop is a resting order and the signal is an order placed after the close). A signal at a bar that closes at or after the square-off minute makes no exit (the square-off exits first). A signal needs the bar to be **counted**: an entry made while the close is already beyond the EMA signals out at the first counted bar by this rule (that is the policy's own meaning; question 3 asks whether to forbid an activation). Worked example (hand row 3, buy): entry 100.00, stop 99.00; bar 3 closes at 100.20 with `EMA` 100.40; the next minute opens at 100.15: exit at **100.15, `RExact` +0.15**, reason `EmaExit`.

**D8. What `Simulator.Create` accepts.** P4-02 D10 widened: a side whose `exits` has exactly one entry among `FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL`, `ATR_TRAIL`, `STRUCTURE_TRAIL` and `EMA_TRAIL`, one value in each list, and at most `time_stop`. `PYRAMID`, `EVENT_EXIT`, two candidates or a list of two values stay `simulation.unsupported_exit` with the policy and parameter named. `ExitTables.RunBySimulator` gains the three names; `exit_not_evaluated` is no longer given for them; the test that the two lists agree is updated.

**D9. Which trade is "without legs".** An `ATR_TRAIL`, `STRUCTURE_TRAIL` or `EMA_TRAIL` position enters once and leaves once, so its `SimTrade` has `Legs = null` and every field as P4-01 D8 says for a trade without legs; `TargetPrice` is null, `StopPrice` is the **initial** stop (so the card shows the plan; the trail is in the rule text and the exit price), `ExitReason` is `Stop` (initial or trailed), `EmaExit`, `SquareOff`, `MaxHold`, `TimeStop` (P4-02) or `EndOfData`. Costs are the ADR 0042 costs of one entry and one exit.

**D10. The oracle and the properties.** The reference implementation of P3-08 gets the three rules written from D1 to D7 and ADR 0050 only; agreement on every field of every trade over at least 18,500 seeded series per rule, including: activation on a close, a bar that touches the activation level and closes below, `activate_at_rr = 0` and a first bar that closes below the entry, an ATR that is not a number, a looser level, a level at or above the price (gap fill at the open), a lookback longer than the history, a signal on the square-off bar, a signal and the initial stop in one open, a trail across a session break in a swing walk (including E = 1D), a sell. The disagreement log gets a P4-03 section. **Leak property** (P4-00 D9) for each rule over 10,000 seeds: truncating after bar *k* changes nothing at or before the first minute after *k*; a negative control that uses the value of bar *k* inside bar *k* must fail it. **Monotonic stop**: for a buy the stop never decreases, for a sell never increases (property).

## Acceptance criteria
1. **Nothing else moves.** Every `FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL` and `time_stop` run, golden and oracle case of P4-02 is unchanged byte for byte; adding the three rules changes nothing for a strategy that does not use them.
2. **`ATR_TRAIL` hand rows.** At least 20 hand-computed rows (buy and sell; intraday and swing; `activate_at_rr` 0 and above 0; activation on a close and a touch that closes back; an ATR not a number; a looser level ignored; a level passed by a gap; the initial stop before any level; square-off, `MAX_HOLD` and `TimeStop` after a level; E = 1D), with `RExact`, `RFilled`, `SlippageR`, `MaeR`, `MfeR` to the printed precision; hand row 1 of D3 is in it.
3. **`STRUCTURE_TRAIL` hand rows.** At least 16 rows including `lookback_bars` 1 and longer than the history, a level beyond the first counted bar, the same gap rule; hand row 2 of D5 is in it.
4. **`EMA_TRAIL` hand rows.** At least 16 rows including the signal on the first counted bar, a signal at the square-off bar (no exit), a stop and a signal at the same open (the stop), a gap at the signal open, a sell; hand row 3 of D7 is in it.
5. **The tape.** With one strategy per policy, the tape holds exactly the features of `ExitPlan.Reads` (`atr(p)`; `lowest(n, low)` or `highest(n, high)`; `ema(n)`) plus `Close`; two strategies in one run sharing a feature (an `ATR_TRAIL` and a `PARTIAL_TRAIL` with the same `atr_period`) hold it once (P3-02b2 D12 dedupe); a strategy with none of the three builds no tape.
6. **Properties.** Leak (with the negative control failing), monotonic stop, legs invariants (none are produced: `LegsTotal` is 0 for these policies) over the seeds of D10.
7. **Refusals (D8).** `PYRAMID`, `EVENT_EXIT`, two candidates and a list of two values are refused by name at the right path; the six candidates and `time_stop` are not; the lists agree.
8. **The new reason.** `EmaExit` appears in the counters, the export (CSV, JSON), the store round trip, and the card, and nowhere in a file name or the index (the blindness scans pass).
9. **Costs.** For the hand rows with a TEST sheet, the charges, `CostR` and `RNet` equal the hand values to the paisa (one entry and one exit order).
10. **The oracle** agrees on every field over the seeds of D10; its log has the P4-03 section.
11. **The verbs.** `simulate` and `verify-pack` run a seeded fixture with each policy end to end; the banner is unchanged.
12. **Performance.** The `FIXED_RR` path stays within the 5 % of P4-01 AC 14; a trail run allocates nothing per minute beyond the trade (explicit benchmark).
13. `scripts/check.sh` green; a `reviewer` pass with no blocker or major; ADR 0050 and the docs (08 §2 and §3, 16 `exits`, 18-roadmap, the oracle log, ADR 0036 Amendment 2: the tables of the three policies stop being provisional) done and `scripts/build-docs.sh` run.

## Contract
- **API:** none.
- **Data:** `ExitReason.EmaExit` (new member, stored by name); counter `ExitsEmaExit`; `StopLoosenIgnored` reused. No migration if the store keeps the reason as text (UNVERIFIED as in P4-02; if a constraint exists, `AddEmaExitReason`, together with P4-02's, in one migration if both are built before a deploy).
- **UI:** none; cards as in P4-01 D16 (a trade without legs is shown as today, with the exit reason text).
- **Events/jobs:** none.

## Non-functional
As P4-01 and P4-02 (allocation-free `FIXED_RR`, deterministic bytes). Each distinct feature is in the tape once; `H_k` is a running extreme over minutes already read (O(1) per minute). No secret, no network, no new log event id.

## Test plan
Unit: each rule's rows as hand tables, the activation, the anchor `H`, the structure extreme and the signal order, refusals, the dedupe of features. Property: leak, monotonic stop, byte-identity of the earlier policies. Oracle: D10. Integration: verbs, export, store, cards. Edge cases: D10 plus a trail whose first counted bar is the fill bar with a gap-up open; a one-minute position; E = 1D with the order that rests overnight (the fill session is the first counted day); `atr_period` longer than the history; a NaN `Close` in the tape (refused by the tape build, as a data error).

## Open questions
1. **The chandelier anchor (D2):** highest high since the **entry**, not since activation. Recommended: yes (it is the chandelier of doc 08; P4-02's `PARTIAL_TRAIL` anchors at T1 for its own reason).
2. **Activation (D3):** by the **close** of a bar reaching `activate_at_rr`, and `0` meaning active from the first counted bar whatever it closed at? Recommended: yes. The alternatives are activation by a touch inside a bar (one more minute-level rule) and, for `0`, requiring a close at or above the entry.
3. **No activation for `STRUCTURE_TRAIL` and `EMA_TRAIL` (D4, D6, D7).** They act from the first counted bar. Recommended: yes (not in the tables); an activation is a later optional name by Amendment 2 of ADR 0036 if a result asks for it.
4. **`EmaExit` as its own reason (D7)** and a trailed stop reported as `Stop`. Recommended: yes; a `Trail` reason or a flag on the exit is a later change if the diagnostics of P4-05 need to tell a trailed stop from the initial one (the exit price against the initial stop tells it already).
5. **The signal exit fills at the next open (D7),** not at the EMA or the close. Recommended: yes (an order cannot be filled in a bar that has closed; the P3 rule).
6. **Names.** Keep `mult`, `activate_at_rr`, `atr_period`, `lookback_bars`, `length` and end the provisional status of these three tables. Recommended: yes.
7. **Engine version.** Keep `sim-6` as in P4-02 question 7. Recommended: yes.

## Done when
- [ ] The owner has answered questions 1 to 7 and the status is Approved
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Migration only if the store needs it; no API change
- [ ] ADR 0050 written; `docs/design` (08 §2 and §3, 16, 18-roadmap), the oracle log and ADR 0036 Amendment 2 updated; `scripts/build-docs.sh` run
