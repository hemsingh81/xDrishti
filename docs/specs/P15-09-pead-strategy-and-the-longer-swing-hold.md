# P15-09 — PEAD strategy and the longer swing hold: event sides may hold up to 20 sessions, the `event_exit` overlay runs, surprise buckets with point-in-time breakpoints, and a built-in `PEAD` with its control

- **Status:** Built 2026-11-03 (ADR 0187). Approved (owner, 2026-11-03, "Build P15-09")
- **Phase / roadmap row:** P15 — slice P15-09 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("PEAD strategy and the longer swing hold"); on the cut line (after the LLM branch).
- **Design refs:** docs/design/07 §1, §7 (swing SELL only through futures) · 08 (`EVENT_EXIT`) · 11 §5 (`time_rules.max_hold_sessions` 1 to 5) · P15-00 protocol P, D7, D8 · ADR 0037 (engine version), 0040 (swing walk; amended here), 0043 (a decision at the close rests overnight and fills at the next open), 0181/0182 (the calendar and its fail-closed limit). New ADR: **next free number at build time**, plus amendments to ADR 0040 (hold up to 20 for event sides) and ADR 0037 (engine `sim-9`).
- **Needs:** P15-05 (frame `days_since_results`, `fu-1` `sue_yoy`, `ev-1` `results_car3`), P15-03 (EPS), P15-06 (built: snapshot). **Not needed to build:** the 5-year history (CF-04); it is needed for a verdict (P15-10). **Needed by:** P15-10 (protocol P).
- **Size:** 3 agent-days (estimate).
- **Safety:** no — a research strategy and simulator rules. It is not in any plan, selection or paper account; nothing here places an order. A strategy that is not an event side behaves exactly as on `sim-8` (differential test).

## Goal
The Lab can run a post-earnings-drift strategy exactly as protocol P pre-registers it: long the top surprise quintile, entered at the close of the reaction session (filled at the next open), held 5 or 20 sessions with a 2 × ATR(14) stop, against the same rule on the middle three quintiles as the control. Event sides may hold up to 20 sessions; every other side keeps 1 to 5.

## Verified facts (read 2026-11-02)
**Code:**
- The hold limit is enforced in `TimeRulesCheck` (`MinMaxHold` 1, `MaxMaxHold` 5, error `max_hold_out_of_range`) and `ExitTables.MaxSwingSessions` 5 (the `time_stop` overlay's `swing_sessions`); `StrategyFileSchema` has only `minimum: 1` (the plan named the schema); the simulator message says "(1 to 5)".
- `OverlayKind.EventExit` (`event_exit`, parameter `days_before_results` 1-10) is in the model and compiles with the warning `exit_not_evaluated` ("…the results calendar it needs is missing (P15)"); `Simulator.RefuseWhatItCannotRun` refuses every overlay but `time_stop` (`unsupported_exit`).
- Exit policies: `FIXED_RR` (`rr` list, 0 < rr ≤ 10) and trails; there is **no "hold only" policy**. `SESSION_EXIT` and `MAX_HOLD` are always on. A swing decision at or after 15:30 rests overnight and fills at the next session's open within five minutes (ADR 0043); `MAX_HOLD` leaves at the close of the due session. Swing SELL is refused (`swing_sell_unsupported`).
- Built-in strategies are rows of `BuiltInStrategies.Table` ("a later strategy is one more row"); stored strategies live in `lab.strategies` / `lab.strategy_versions`.
- `EventCalendarSnapshot` is handed to a run whose strategy names `not_event_day` (P15-06); under its fail-closed rule an instrument whose last meeting has passed and whose next is not intimated is `unavailable/no_upcoming_entry`.

## Scope
**In:**
- Model: **event side** = a side whose entry condition reads `days_since_results`; for it `max_hold_sessions` and `time_stop.swing_sessions` accept 1 to 20 (`MaxEventHold` 20); for every other side 1 to 5 as today. `StrategyFileSchema` gains `maximum: 20` with the model check giving the exact message.
- Simulator: `event_exit` runs (rule below); `sim-9`.
- Frame features `results_sue_q` and `results_car3_q` (quintile 1-5 by point-in-time breakpoints), supplied by the caller per India date like P15-05's.
- Domain: `SurpriseBreakpoints` (pure).
- `BuiltInStrategies` row `PEAD` with parameters `measure` (`sue`|`car3`), `hold` (5|20), `buckets` (`top` = {5} | `middle` = {2,3,4}); the four counted variants and the control.
- Docs 07, 08, 11 and the ADRs.

**Out:** the verdict (P15-10); the short side (research label only, P15-00: swing SELL needs futures; the simulator keeps refusing it); `not_event_day` on PEAD (see Rules); any plan, paper or live use; a new exit policy.

## The rules
- **Reaction session `R`** and `days_since_results` as in P15-05 (first session opening at or after `broadcast_ts + lag`). Entry rule of `PEAD`: timeframe `1D`, `days_since_results == 0 and results_<measure>_q in buckets`. The decision is the close of `R`; by ADR 0043 the order **fills at the open of `R + 1`** (the plan's "entry at the close" is the decision time). The announcement jump (the gap into `R`) is therefore never traded.
- **Breakpoints, point in time (corrects the plan).** The plan ranks "the cross-section in the season", but at the close of an early reporter's `R` most of that season has not reported: ranking against it is look-ahead. Breakpoints are the 20/40/60/80 % quantiles of the measure over **the previous complete season** (all tracked instruments whose results for the previous quarter end are visible at `t`, at least 100 values, else NaN for the season). Seasons are calendar quarters of `period_end`; a previous season is complete by construction (its last results, due 60 days after its quarter end, arrive before the next quarter's first). Quantiles by the nearest-rank rule; ties go to the higher bucket.
- **Stop:** `atr_mult` 2 on daily ATR(14). **Target:** `FIXED_RR` `rr: [10]` as a practical "no target" (a 20-ATR target in 20 sessions); exits by target are counted and printed, and if they exceed 2 % of trades the run is labelled "target not negligible".
- **`event_exit`** (`days_before_results d`): at the close of a session whose frame `days_to_results` (as known at the start of that session) is `≤ d`, the position leaves at that close (the same mechanics as `MAX_HOLD`, X8). **Unavailable never fires and is never "no event":** the trade is marked `event_exit: unavailable` for that session and counted (`event_exit_unavailable`). This is the only safe reading for an exit after results: the next meeting is usually not yet intimated (`no_upcoming_entry`), and exiting on that would close every PEAD trade on its first day.
- **`not_event_day` must not be combined with an event side** (model warning `event_side_with_not_event_day`): under the fail-closed rule it would block nearly every PEAD entry, because right after a results event the next meeting is rarely intimated.

## Acceptance criteria
1. **Hold limits.** A side reading `days_since_results` accepts `max_hold_sessions` 1-20 and `time_stop.swing_sessions` 1-20; any other side refuses 6 with `max_hold_out_of_range` as today (message names "1 to 5, or 1 to 20 for a side that reads days_since_results"); `StrategyFileSchema` validates the maximum 20; the hold-limit wording of the draft sheet is unchanged, but the sheet is generated from the indicator catalog, so it gains two feature lines (`results_sue_q`, `results_car3_q`) and **its hash changes**; it stays under 12 KB (`DraftSheetBuilderTests`). The assistant-eval baseline of ADR 0143 records that hash: the owner reruns the eval before the next claim that rests on it (it calls the model, so it was not run here). Amended 2026-11-03 after review.
2. **Swing walk to 20.** A fixture with a 20-session hold leaves by `MAX_HOLD` at the close of session 20 counted from the fill session, by stop, or by target; holidays and a special session counted by the calendar; hand-worked trade.
3. **`event_exit`.** Hand cases: position open, meeting intimated and visible with `days_to_results` 1 at the start of a session and `d = 1` → exits at that close; intimated during that session → not before the next session; no visible entry → stays, `event_exit_unavailable` +1 and the run counters `event_exit_unavailable` (session closes) and trades-with-one carry it (the trade record, schema 5, has no spare field, so there is no per-trade flag — amended 2026-11-03); a cancelled meeting → unavailable. The overlay is removed from the `unsupported_exit` refusals and from the `exit_not_evaluated` warning; a strategy naming it gets the snapshot (as `not_event_day`).
4. **Breakpoints.** `SurpriseBreakpoints` hand case: previous season values 1..200 → breakpoints 40, 80, 120, 160; a value of 160 is bucket 5 (tie to the higher); fewer than 100 previous values → NaN; property: adding a filing broadcast after `t` (any season) changes no bucket at `t`, control: a visible previous-season filing can.
5. **`PEAD` row.** `BuiltInStrategies` lists `PEAD` with the three parameters and refuses any other value; `buckets=top` and `buckets=middle` give the variant and the control with otherwise identical definitions; the four variants of `(measure, hold)` have four distinct definition hashes and the run key changes with each.
6. **Fill at the next open.** On a fixture with results broadcast Friday 16:12, `R` is Monday; the decision is Monday's close and the fill is Tuesday's first trade within five minutes of the open (ADR 0043), never Monday.
7. **Engine (ADR 0037).** `SimulationEngine.Version` becomes `sim-9`; goldens regenerated on purpose with the label diff only; strategies that are not event sides and do not name `event_exit` equal `sim-8` trades (differential test); `RunAssemblyTests` maps `sim-9`.
8. **No order, no plan.** `PEAD` is not offered to plan selection or paper accounts by this slice (a test); the no-order tripwire is green.

## Contract
- **Engine:** `sim-9`; frame features `results_sue_q`, `results_car3_q`; overlay `event_exit` runs.
- **Model:** the hold limit rule; warning `event_side_with_not_event_day`.
- **API/Data/UI:** none new (runs and their records are the existing Lab ones).

## Non-functional
The per-date bucket series for 214 instruments over 5 years ≤ 2 s; the walk's speed for other strategies unchanged (≤ 2 %).

## Test plan
- **Unit:** hold checks, breakpoints, the `event_exit` table, `PEAD` parameter table.
- **Simulation:** 20-session walk, next-open fill, `event_exit` hand cases, differential test, goldens.
- **Property:** criterion 4.
- **Architecture:** `PEAD` not referenced by planning or paper.

## Decisions (researched)
1. **Event side defined by reading `days_since_results`.** A hold over 5 is allowed only where the strategy states it trades after results (P15-00 D7); a name list or a flag would be a second, forgettable marker.
2. **Previous-season breakpoints.** Removes the look-ahead; the cost is a one-season lag in the distribution.
3. **`FIXED_RR 10` instead of a new "hold only" policy.** The smallest change; the count of target exits makes the approximation visible.
4. **`event_exit` holds on unavailable.** An exit that fires on missing data would turn the fail-closed calendar into a rule that closes every trade; the flag keeps the gap visible.

## Risks and honest limits
- **The `atr_mult` stop was not run before this slice** and is built here (not in the original scope): the close of the decision bar less the multiple of the ATR. It is covered by hand tables, not by the oracle.
- **Pre-open reporters are missed.** A filing broadcast before the open of its own reaction session is first visible the next date, where `days_since_results` is already 1, so `== 0` never enters it. The run record and label count these filings (`reaction session missed: k of n`).
- **Not evaluable today.** Protocol P needs 20 seasons of bars (8 choice + 8 test + 4 hold-out) with `sue_yoy` computable in each; the bars cover about 8 seasons. With CF-04's 5-year backfill and results filings from 12 quarters before the first bar season, it becomes evaluable (P15-10).
- Long-only PEAD on today's tracked set is flattered by survivorship (ISS-22); delivery costs are large against a 20-session edge (gross until ISS-25).
- A 1-season lag in breakpoints shifts buckets when the surprise distribution moves between seasons.

## Open questions (owner)
1. Build this slice before the 5-year history exists (it can only produce NOT_EVALUABLE until then)? **Default: build after P15-10's core (cut-line order); the verdict states the missing seasons.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any in-season ranking, any fill at the decision close, any exit fired by unavailable data, any change for non-event strategies)
- [ ] Goldens regenerated on purpose
- [ ] ADR, ADR 0040 and 0037 amendments; docs 07, 08, 11 updated
