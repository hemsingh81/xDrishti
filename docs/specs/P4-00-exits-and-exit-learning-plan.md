# P4-00 — Exits and exit learning: the plan, the split and the seam

- **Status:** Approved (owner, 2026-10-19, "yes to all recommendations": questions 1 to 8 stand as recommended; P4 build still waits for P3 to close). Written 2026-10-19 from `main` at b4835af. The facts about the code and the docs were read that day; what could not be verified is marked UNVERIFIED. This is the plan spec of the phase, like the P3 plan in the roadmap: it fixes the slices, the one design decision they share (the seam) and the questions, and each slice then gets a spec of its own before it is built.
- **Phase / roadmap row:** P4 — Exits & exit learning (3 weeks, after P3). Row: "All exit policies incl. pyramiding; same-entries comparison; per-cell exit choice (walk-forward); MAE/MFE diagnostics". Exit: "Chosen exits stable across folds".
- **Design refs:** docs/design/08-exits-and-risk.md §2 to §5 · 09-learning-engine.md §1 (layer 2, exit selection) · 11-strategy-lab.md §2 and *Why a condition cannot look ahead* · 16-configuration.md §2 (`exits`, `selection_objective`) and walk-forward block · [P3-02b2](P3-02b2-exit-policy-tables-of-schema-1.md) (the provisional tables, D3 to D17) · [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) Amendment 1 · [ADR 0038](../adr/0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md) · [ADR 0040](../adr/0040-swing-positions-held-across-sessions-the-maximum-hold-the-position-gate-and-simtrade-schema-3.md) · [ADR 0047](../adr/0047-the-oracle-and-the-rule-table-gaps.md).

## Goal
The simulator walks every exit policy of doc 08 (partial, trails, pyramid, the two overlays), the same entries can be replayed under each candidate and compared, MAE/MFE diagnostics say whether the stop, target and trail activation are sound, and a walk-forward picks one exit per cell and shows whether the choice is stable across folds. Like P3, this says the mechanics and the selection procedure are right; it does not say that any strategy has an edge.

## Why P4 waits
P4 starts after the owner has said that P3 passes (the 20-trade check, [P3-09](P3-09-verification-pack-and-the-20-trade-check.md)): a walker extended on top of an unchecked walker would have two sets of defects to separate. The slices below are specs to approve while the check is done; **nothing is built before P3 closes** except what the owner explicitly pulls forward.

## The seam (the one decision the slices share)
**Fact.** `TradeWalker.Walk(candidate, bars, rules)` is a pure function of an `EntryCandidate`, the 1-minute bars and `WalkRules` (reward-to-risk, square-off, fills, tick, optional `Hold`). It has no feature frame. The scanner (`EntryScanner`) computes features on the evaluation timeframe E and emits candidates; the walker reads only minutes. Today's exit is a fixed stop and one `FIXED_RR` target.

**Problem.** An ATR, structure or EMA trail, a pyramid trail and a `time_stop` need values that are computed at the close of each E-bar *after* the entry. The walker cannot compute them.

**Recommended design (D1).** The scanner emits, with each candidate, a **per-bar exit series**: for each closed E-bar after the decision, up to the latest possible exit (square-off, or `max_hold_sessions`), the values the chosen candidate policies read (`atr`, the N-bar extreme, the `ema`, the E-bar close and its end instant). The walker stays a pure function of `(candidate, series, bars, rules)`. A value belonging to the E-bar *k* may move a stop or signal an exit **only from the first minute after the close of bar *k*** (doc 08 §2, as built), never inside bar *k*. This is the look-ahead property of D9.

Alternatives considered: (a) hand the walker the whole feature frame — breaks "no frame", couples walker to compiler, much harder for the oracle; (b) compute trails from the minutes inside the walker — duplicates indicator code, risks disagreeing with the frame; (c) chosen — one source of truth for features, the walker stays small and the oracle (P3-08) can be extended with an independent reading of the same series.

Consequences: `EntryCandidate` (or a sibling record) grows the series; the run record and `SimTrade` get a new schema version (exit-policy id and parameters per trade, partial fills as legs, pyramid adds as legs); the engine version goes to `sim-6` or later (the Q3-MFE decision of the oracle log may be taken in the same bump). UNVERIFIED: the exact record shapes; the slice specs fix them.

## Slices (proposed; sizes are agent-days, an estimate, not calibrated)
| Slice | Content | Size | Needs |
|---|---|---|---|
| P4-01 | **The seam and the legs.** The per-bar exit series (D1), the look-ahead property (D9), `SimTrade` with legs (a trade of several fills: partial, adds, the final exit), `R` of a multi-leg trade (D4), engine version bump, the oracle extended for legs on `FIXED_RR` only. No new policy yet: the old trades must be byte-identical in every field except the schema. | 2.5 to 3 | P3 closed |
| P4-02 | **`PARTIAL_BE` and `PARTIAL_TRAIL`** (book x% at T1; stop to break-even or ATR trail for the rest) and the **`time_stop` overlay**. | 2 | P4-01 |
| P4-03 | **Trails: `ATR_TRAIL`, `STRUCTURE_TRAIL`, `EMA_TRAIL`** with activation and the "never moves against the position" rule (D5). | 2 | P4-01 |
| P4-04 | **`PYRAMID`** with the always-on risk rule (total open risk at most the initial 1R before each add, D6). | 2 | P4-02, P4-03 |
| P4-05 | **Same-entries comparison.** One scan, N candidate exits over the same entries and the same bars, one result per policy, run-record changes; MAE/MFE diagnostics from doc 08 §3 (winners' adverse excursion, MFE tail, giveback) as a table in the run record and the CSV. | 2 to 2.5 | P4-02 to P4-04 (a subset is enough to start) |
| P4-06 | **Per-cell exit choice, walk-forward.** Folds (D7), the objective `mean_R − λ·downside_dev_R` with a minimum sample (D8), the plateau preference and the stability check across folds; the exit is a result of the run and stored with `--store`. | 2.5 to 3 | P4-05, the result store (P3-10) |
| P4-07 (optional) | **`EVENT_EXIT`.** Needs a results calendar. UNVERIFIED that any source is in the allow-list or stored (not found in the code read for this draft); see question 5. | 1 to 1.5 or later | question 5 |

Sum: about 13.5 to 16 agent-days for P4-01 to P4-06, which is more than the "3 weeks" of the row can hold with the owner's checks included; see question 6.

## Definitions
**D2. Candidate and cell.** A *candidate exit* is one entry of the strategy's `exits` list with one value of each list parameter (a `rr` of `[1.5, 2]` is two candidates). A *cell* is `(strategy, timeframe, side, style, regime)`; until regimes exist (P6, P3-01d) the regime is `*`. The compared unit is `(cell, candidate exit)`.

**D3. Same entries.** All candidates are walked from the **same** `EntryCandidate` list, the same fill rules and the same bars, so a difference between two candidates is a difference of exit only. A candidate with a different stop (not a different exit) is a different strategy, not a candidate.

**D4. R of a trade with legs.** `R` stays in units of the **initial** risk `|entry − initial stop|` of the first leg, summed over legs weighted by the share of the quantity each leg held; a pyramid add changes the weights but not the unit. Costs follow ADR 0042 per leg (each order is charged). Rounding and the gross/net rows are fixed in the P4-01 spec; the oracle must reproduce them (UNVERIFIED: no row of ADR 0042 covers a leg that is not a full exit).

**D5. Trail rules (recommended).** A trail may only tighten (BUY: the stop never goes down; SELL: never up). A new level is computed at the close of the E-bar and takes effect from the first minute after it; a level beyond the last price is not a fill but a stop that is hit at its row in ADR 0038. `ATR_TRAIL` is a chandelier: highest high since the entry minus `mult × atr` once `activate_at_rr` has been touched on a close. `STRUCTURE_TRAIL` is the N-bar extreme as of the last closed bar. `EMA_TRAIL` exits at the **next open** after a close beyond the EMA (a market-type fill with slippage), not at the EMA.

**D6. Pyramid.** Add triggers are checked at the close of the E-bar (doc 08 §4, "on trigger-TF close"), adds fill like a market-type entry at the next minute's open within the 0.1 % limit; before an add the stop moves so that total open risk is at most the initial 1R, and an add that cannot satisfy that is skipped and counted. A pyramid add is not a new trade for position gates; it is a leg.

**D7. Folds (recommended, because of ISS-19).** The data is about 2.1 years (214 files, 2024-04 to 2026-04), so doc 16's `walk_forward.train_years: 3` cannot be run. Proposal for the exit choice only: **expanding train from the first stored month, test windows of 3 months, embargo of 5 sessions**, so the first test starts 9 to 12 months in and gives about 4 to 6 folds. With so few folds "stable across folds" can only mean "the same candidate or a neighbouring parameter wins in at least `min_positive_folds_pct` (60) of the folds"; the owner's reading decides whether that is enough (question 3).

**D8. The objective.** `mean_R − λ·downside_dev_R` with λ = 0.5 (doc 16 `selection_objective`), on net R when a sheet exists and on gross R labelled so otherwise; minimum 30 out-of-sample trades per cell (doc 09 §6.2); a cell below the minimum has **no choice**, is shown as such and falls back to the strategy's declared default exit. `prefer_plateau`: the winner is the candidate whose neighbours in the parameter grid are within one standard error of it; a lone peak is flagged and not chosen over a plateau (the precise rule is in the P4-06 spec).

**D9. The look-ahead property (carried over from ADR 0036 Amendment 1).** Truncating the 1-minute data after the end of E-bar *k* changes no decision (no stop move, no exit signal, no add) at or before the first minute after bar *k*. It is a property test over seeded series in the oracle, and a leak property in the style of `p3-08-leak-properties.md`.

**D10. Per-trade tuning is not in P4.** `per_trade_fit` (scale by ATR, snap to structure, obstacle check, late-entry target reduction) and the learned `min_rr` check are P7 (doc 16 puts them with the live learning); P4 chooses an exit per cell, not per trade.

## Scope
**In:** the seam and the legs (P4-01); `PARTIAL_BE`, `PARTIAL_TRAIL`, `time_stop` (P4-02); the three trails (P4-03); `PYRAMID` (P4-04); the same-entries comparison and the MAE/MFE diagnostics (P4-05); the per-cell walk-forward choice (P4-06); the oracle and the leak properties extended for each; the tables of P3-02b2 confirmed or renamed by Amendment 2 of ADR 0036 where a name proves wrong, as long as no stored strategy uses it.
**Out (not in P4):** per-trade tuning (D10, P7); the Lab screens and the strategy file (P5, P3-02c to P3-02e); regimes in the cell (P6); live use of the chosen exit by the ticket engine (later phase); `EVENT_EXIT` unless question 5 is answered; a change to any entry rule; the Dhan backfill toward 5 years (ISS-19, question 2) except as a precondition for more folds.

## Acceptance criteria (of the phase; each slice spec carries its own numbered list)
1. Every policy of doc 08 §2 that is a candidate (`FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL`, `ATR_TRAIL`, `STRUCTURE_TRAIL`, `EMA_TRAIL`, `PYRAMID`) and both overlays that are built (`time_stop`; `event_exit` only if P4-07) run in the simulator; `Simulator.Create` refuses nothing it can run and still names what it cannot (`simulation.unsupported_exit`, per policy and per index).
2. Given the same entries and fill rules, two candidates produce trades whose entries are byte-identical and differ only in exit fields (D3), tested on a seeded series.
3. Given a series truncated after any E-bar *k*, no decision at or before the first minute after *k* changes (D9), property test, 10,000 seeded cases at least.
4. The oracle (P3-08) is extended, written from the rule tables only, and agrees with the engine on every field of every trade of every new policy over seeded series; its disagreement log is updated (class A, B, C as in ADR 0047).
5. A trade with legs: `R` in the unit of the initial risk (D4); the quantities of the legs add up to the maximum quantity; no leg is filled outside its minute's range; a gate or cost refusal skips the whole trade and is counted, not half-filled.
6. The comparison of candidates over a seeded fixture reproduces a hand-computed table (trades, win rate, mean R, downside deviation) to the printed precision; for one real-data run, `--gross`, the owner can open the table and the per-policy trade cards.
7. MAE/MFE diagnostics: for a seeded fixture, the share of winners with adverse excursion beyond 0.8R and 1.2R, the median MFE and the share of trades reaching 2R and 3R are printed per cell and equal the hand-computed values.
8. The walk-forward picks, per cell, a candidate or "no choice" (D8); the folds, the embargo and the minimum sample are in the run record; changing the seed or a fold boundary changes only what it should; a deliberately planted always-best exit in a synthetic series is chosen in every fold and the stability report says so; a planted noise-only series is reported as unstable.
9. Run records carry the new engine version, the schema versions and the same banner lines as P3, plus a line that the choice is in-sample on the data shown and is not evidence of an edge; no number from a real-data run is quoted outside its run folder before an equivalent of the 20-trade check for the new policies (question 4).
10. Every slice ends with `scripts/check.sh` green and a `reviewer` pass; the docs (08, 11, 16, the roadmap) and an ADR per decision (ADR 0048 and following) are updated with `scripts/build-docs.sh`.

## Contract
- **API:** none; P4 stays in the simulation library, the `simulate` and `verify-pack` verbs and the export (as P3). The Lab screens are P5.
- **Data:** `SimTrade` schema 5 or 6 with legs and the exit-policy id and parameters; run-record schema 6; `research.sim_trades` and `research.sim_runs` follow in a migration only if the slice stores new columns (UNVERIFIED which; P3-10's tables may need a `legs` column or a child table). A walk-forward result is a new record (P4-06 spec).
- **UI:** none (cards and CSV, as in P3). The trade cards show the legs and the trail path as marks on the chart.
- **Events/jobs:** none.

## Non-functional
Replay stays cheap: the per-bar series is bounded by the bars until the latest exit and is built once per candidate, not once per candidate exit; the comparison of N candidates costs one scan plus N walks (target: N = 8 candidates over the 214 instruments within about 3 times the time of one P3 run; UNVERIFIED, the P3 timed run over 214 instruments is still open). Walkers allocate no more per minute than today's (benchmark kept explicit). No secrets, no network; deterministic by seed.

## Test plan
Unit: each policy's rows (hand tables, as ADR 0038 does), legs arithmetic, trail never loosens, a pyramid add skipped for risk. Property: look-ahead (D9), same entries (criterion 2), candidates independent of order. Oracle: extended per slice, with the dry-read habit of P3-08. Integration: the verbs end to end on a Testcontainers database as in P3-04b2; the comparison and the walk-forward on seeded fixtures. Planted-signal tests for the selection (criterion 8). No frontend.

## Open questions
All eight answered "yes to all recommendations" by the owner on 2026-10-19: each stands as the recommendation written in it (question 2 stays conditional on the allow-list change being the owner's security decision, taken when the backfill is scheduled).

1. **Approve the split and the seam (D1)?** Recommended: yes; the alternatives are in the seam section.
2. **Data (Q7).** Does the Dhan backfill run toward about 5 years (ISS-19) before P4-06? Recommended: yes if the daily-history allow-list change is acceptable to you; with 2.1 years the walk-forward has 4 to 6 folds (D7) and "stable across folds" is a weak statement.
3. **Folds and stability (D7, D8).** Accept expanding train, 3-month tests, 5-session embargo, minimum 30 out-of-sample trades, λ = 0.5, and "the same candidate or a neighbour wins in at least 60 % of folds"? Or wait for more data? Recommended: accept, and say in every report that it is weak evidence.
4. **A hand check for the new policies.** Recommended: a second, smaller hand check (about 10 trades per new policy family, on the P3-09 protocol, TradingView for at least 3) before any real-data number is quoted; it is your time again (UNVERIFIED hours, about 3 to 5 by the scale of P3-09).
5. **`EVENT_EXIT` (results calendar).** Is there a source of results dates you accept (a file you supply, a Dhan endpoint on the allow-list, or none)? Recommended: leave it out of P4 until a source exists (P4-07 stays unscheduled).
6. **Size against the 3 weeks.** The slices sum to about 13.5 to 16 agent-days of coding plus your reviews; 3 calendar weeks can hold that only if the checks do not queue behind each other. Recommended: build in the order P4-01, P4-02, P4-03, P4-05 (comparison on the policies built so far), P4-04, P4-06; cut order if time runs short: the pyramid (P4-04) moves to P5, then the plateau rule of D8 is simplified to the best candidate with stability.
7. **Names.** The provisional parameter names of P3-02b2 (`mult`, `lookback_bars`, `trail_atr_mult`, `book_pct`, …) stand unless a slice shows a reason to rename; renames go through Amendment 2 of ADR 0036 and need no stored strategy using the old name. Recommended: no renames now.
8. **The open oracle entry Q3-MFE (ISS-28)** (MFE below R in one corner): decide `sim-6` or amend the rule, in P4-01 with the engine bump, so there is one version change and not two. Recommended: decide it with P4-01.

## Done when
- [x] The owner has answered questions 1 to 8 (2026-10-19)
- [ ] The slice specs P4-01 to P4-06 are written and Approved
- [ ] Each slice: all acceptance criteria have evidence; `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] ADRs written (the seam; legs and R; trail rules; the selection objective and folds), `docs/design` and the roadmap updated, `scripts/build-docs.sh` run
- [ ] The oracle agrees on every new policy; the leak properties pass
- [ ] The hand check of question 4 done and recorded, if the owner takes it
