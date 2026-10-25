# P11-06 — The small-live phase check

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P11 — Small live capital, slice P11-06 of [P11-00](P11-00-small-live-plan.md). Exit: "Live within expectations => scale gradually".
- **Design refs:** docs/design/18-roadmap.md (P11 row) · 19 §5 · 09 §9, §12 · ADR 0097 and P7-18 (the pattern: three outcomes, caveat in the label, limits always printed, the allow-list pin), ADR 0080 (never MET on thin data), P10-06 (the sibling for the paper phase: criteria C1 to C4 and I, caveat labels, resets printed), P11-02, P11-03, P11-04, P11-05.
- **Needs:** P11-01 to P11-05 (their stores and handlers, read through ports), the P7-18 allow-list facts, P10-06 (the paper report, read). **Size:** 2 agent-days plus wall-clock time (at least 8 weeks of live sessions). **Safety:** no — read only; a verb, one GET and a read-only tab; writes nothing, runs no replay, changes no setting, moves no ramp step.

## Goal
Say, from the stored records only, whether the small-live phase has met its exit — eight weeks of real trading at 25 % to 50 % of the intended risk whose execution stayed within expectations of the paper phase — name every fact that says otherwise, and never print MET on thin evidence. The exit is the owner's call; this report is the evidence and the only thing that unlocks the owner's `exit_confirmed` act (P11-04). It does not change the ramp, raise a step or start P12.

## Scope
**In:** pure `Domain/LiveTrading/PhaseCheck`: `LivePhaseCheck`, `LivePhaseFacts` (plain records), `LivePhaseReport`, `LiveCriterion`, `LiveWeekLine`, `LivePhaseLimits`; `Application/LiveTrading/EvaluateLivePhaseCheckHandler` (reads through ports: live window and sessions, capture health, outcomes and grades, `LiveVsPaper` summary from the P11-03 handler, slippage estimates, capacity, ramp events and halts, compliance records, services and settings state, the allow-list facts of P7-18) and its view; verb `xd live phase-check [--through DATE] [--account ID]`; endpoint `GET /api/live-trading/phase-check`; the **Phase check** tab of `/live` (read only); contract regenerated; ADR; docs and roadmap row.
**Out:** a stored record of the check (it is recomputed); running any test from the verb; deciding the exit (the owner confirms in P11-04); writing `exit_confirmed`; starting P12; any setting change; judging look-ahead, determinism or the no-order tripwire from records beyond the allow-list comparison (tests prove those).

## Acceptance criteria
1. **Window.** Given the live account, then the window starts at the first live entry fill on or after the account's first `set_step` event (`live_fills` of an entry of a live ticket, or the first captured fill if no ticket) and ends at `--through` (default: the last closed session; a non-trading date takes the session on or before; a later date or one before the window is refused with 422 `live.invalid`). Elapsed calendar days and trading sessions are counted from the calendar.
2. **Eight criteria and integrity line I, each `MET`, `NOT MET` or `NOT EVALUABLE`:**
   - **C1 duration and coverage.** Below 56 elapsed days **or** below 40 trading sessions: `NOT EVALUABLE` ("week k of 8"). At or above both: coverage = sessions with an approved live ticket or a fill whose `CaptureHealth` is `complete`, over such sessions; below 0.90 is `NOT MET` (the missing sessions listed), else `MET`.
   - **C2 risk band.** `NOT MET` if any live allocation in the window has `risk_scale` above 0.5 (impossible by the database before `exit_confirmed`; the check reports it if it ever happens); `NOT EVALUABLE` when fewer than 20 sessions carry a live allocation; `MET` when every live allocation is within 0.125 and 0.5 (0.125 allowed: a weekly halved S1) and no session ran above the step factor in force that session (each session's `risk_scale` <= the factor of its ramp step from `ramp_events`).
   - **C3 volume.** Graded live round trips below 40 or paired tickets below 30: `NOT EVALUABLE` ("n of 40, m of 30", pace and ETA from the window's rate); both reached: `MET`. Never `NOT MET` for being short.
   - **C4 within expectations.** The P11-03 verdict on the window: `within` is `MET`; `not_within` is `NOT MET`; `inconclusive` or `insufficient` is `NOT EVALUABLE` (the interval and the pairs are printed).
   - **C5 slippage.** Overall live mean total entry slippage in R over one reference kind with n >= 20: one-sided 95 % upper bound <= 0.10 is `MET`; lower bound > 0.10 is `NOT MET`; else `NOT EVALUABLE`. The per-bucket table (n, mean, interval, state) is printed, not judged.
   - **C6 discipline and breaches.** `NOT MET` if a hold is active, or a `step_down` was written in the last 20 sessions, or any `after_halt` fill exists in the window without a later `clear_hold`; `NOT EVALUABLE` if protected share or order evidence is `unavailable` for more than 20 % of intraday entries; else `MET`.
   - **C7 execution feedback delivered.** The weekly estimates ran in each of the last 4 ISO weeks of the window and the capacity score exists in a state other than `empty` (a state of `insufficient` counts as delivered and is printed): `MET`; otherwise `NOT EVALUABLE`; `NOT MET` is not used.
   - **C8 compliance.** `static_ip`, `dhan_ip_read_policy` and `sebi_recheck` records (P11-05) each exist with `decidedOn` inside or after the window start and `nextReviewOn` after `through`: `MET`; a missing or overdue record is `NOT EVALUABLE` before the window has 56 days and `NOT MET` after (the fact: not done).
   - **I capture integrity.** `NOT MET` if the history-versus-trade-book comparison found any disagreement in the window not later explained, any ticket outcome still `unknown` after 5 sessions, or any fill journaled twice; `NOT EVALUABLE` with no capture run; else `MET`.
3. **Overall.** `MET` only when C1 to C8 and I are all `MET`; `NOT MET` when any is `NOT MET`; otherwise `NOT EVALUABLE`. Before printing MET the code re-checks: window >= 56 days and >= 40 sessions, pairs distinct, one live account, sessions strictly ascending, `through` a trading session; anything else prints `NOT EVALUABLE (malformed window)`.
4. **Caveats are part of the label,** stacked when several apply, in the headline, the JSON `exitCriterion` and the log (the bare word in `exitVerdict`): `MET (point estimate; one-sided bound 0.0xx R from n pairs)`, `MET (k tickets left out of the pairing)`, `MET (order evidence missing on m sessions)`, `MET (k step-downs or holds in the window, all cleared)`, `MET (paper shadow incomplete on j sessions)`, `MET (charges unconfirmed: gross comparison only)`, `MET (all live trades at S1)`.
5. **The recommendation is a sentence, not an action:** when overall is `MET` the report adds "Evidence supports proposing step S3 (100 %). Recording it is your act: Ramp tab, Exit confirmed." In every other outcome it names the criteria that block and whose act each is; it never writes, never moves a step.
6. **Printed, not judged:** realised live net R with n and its interval (never a criterion: see P11-04 D2), charges per round trip as a share of 1 R, discretionary share, missed-trigger cost in plan R by class (`missed_no_order`, `missed_unfilled`, `rejected`, `missed_unknown`), the ramp history, halts opened, the capacity score and its parts, the slippage per bucket, setup state (services `live-capture`, `live-settle`, `live-estimates`, `paper-capture`, `nightly`, `preopen`, `plan-expiry`; switches `Live:Capture:Enabled`, `Live:Capture:OrderBook:Enabled`, `live.auto_approve_tickets`, `live.auto_ramp_up`) and **blockers** (what stops a criterion, whose act).
7. **Week lines.** For each ISO week of the window: sessions, live tickets, outcomes by class, round trips graded, cumulative graded, step and scale, capture sessions complete, halts, holds, breaches. Deterministic text.
8. **Never judged here** (`NOT EVALUABLE`, with the test names that prove them): no order was placed by the system (the architecture tripwire and the allow-list pin tests; the allow-list equality and the refused probes are read through the P7-18 port and shown as one `MET` or `NOT MET` line inside I), no look-ahead in the grading replay (P3-08 properties), determinism, paper equals grading (P10-01 equivalence).
9. **Limits always printed**, in the object and the text: about 40 to 80 round trips in eight weeks give an interval of roughly +-0.05 R on a paired sd of 0.15; one market regime; simulated paper fills are a model; arrival prices come from 1-minute bars; the owner's discretionary trades are reported apart; Dhan field names unverified until the recorded fixture; the swing stop protection is unknown; the static IP and SEBI facts are the owner's notes (P11-05); the report reads stored records and the options of the process that ran it; nothing is written.
10. **Deterministic text; exit code 0** whatever the verdicts say; 1 refused (a bad `--through`, no live account), 2 usage, 3 failure, 130 cancelled.
11. **The report writes nothing:** every call on every store is a read (a fake store throws on a write); the handler reads at most the window's rows and the newest 200 ramp / halt rows.
12. **Read endpoint** returns the same report and text; session rules as the other GETs; 404 `live.not_found` without a live account; 422 `live.invalid` for a bad date.

## Contract
- **API:** `GET /api/live-trading/phase-check?accountId=&through=YYYY-MM-DD` → `LivePhaseCheckView { report{ through, windowStart, elapsedDays, sessions, exitVerdict, exitCriterion, criteria[{code,name,verdict,detail,caveat}], weeks[], printed{…}, blockers[], limits[] }, text }`. Read only. Routes without an `orders` segment.
- **CLI:** `xd live phase-check [--through YYYY-MM-DD] [--account ID]`, `scripts/xd.sh live phase-check`.
- **Data:** none; no migration. Reads through ports: `live_capture_runs`, `live_fills`, `live_ticket_outcomes`, `journal_entries`, `fill_grades`, `live_executions`, `slippage_estimates`, `capacity_scores`, `ramp_events`, `live_halts`, `live_settings`, `compliance_records`, `plan_item_allocations`, the P11-03 handler, `platform.service_schedules`, the P7-18 allow-list port.
- **UI:** `/live` tab **Phase check** (read only): the headline with its label, the criteria list (word + icon per verdict, detail, caveat), week lines, printed lines, blockers (each with "your act" or "time"), limits. States: loading, error, `empty` ("No live session yet: nothing to check"). A non-value never renders a digit.
- **Events/jobs:** none (an owner's read). Log id 5150: date, the exit word and counts only.

## Non-functional
Reads one window (about 40 sessions) with one query per store and the P11-03 summary; p95 < 1 s. No secret read or logged; no price, quantity, symbol or account number in logs. One lazy chunk shared with the other `/live` tabs.

## Test plan
- **Unit (Domain `LivePhaseCheckTests`, hand tables):** each criterion at its boundaries (C2 rows: scale 0.5 MET and 0.51 NOT MET; 19 of 19 sessions at or under the factor but only 19 sessions with a live allocation NOT EVALUABLE, 20 of 20 MET, and 19 of 20 sessions within the factor (one above) NOT MET; a halved-S1 scale of exactly 0.125 MET; 55 days / 56 days; 39 / 40 sessions; coverage 0.89 / 0.90; round trips 39 / 40 and pairs 29 / 30; C4 for each P11-03 verdict; C5 upper bound exactly 0.10; C6 with a hold, a step-down 19 and 21 sessions ago, an `after_halt` fill cleared and not cleared; C7 with 3 and 4 weeks; C8 with a record overdue before and after day 56); overall combinations; the malformed-window guard; caveat stacking order; the recommendation sentence appears only on MET.
- **Application (`EvaluateLivePhaseCheckHandlerTests`, fakes):** default end of the window, a weekend date, a later date refused, the write-throwing store, an unavailable paper source gives `NOT EVALUABLE`, services and switches read and named as blockers.
- **Integration (Testcontainers):** the endpoint on an empty database (`empty`), on a seeded 8-week fixture (all MET with the stacked caveat of an all-S1 run), with a hold (NOT MET), with a missing compliance record, 422 and 404 cases.
- **Frontend (MSW):** every verdict, the caveat in the label, blockers with the act, no digit for a non-value, no mutation import.
- **CLI:** `PhaseCheckOptionsTests` for the arguments and exit codes.

## Decisions (researched)
1. **Q: What does "within expectations" mean as a test?** A: The paired non-inferiority verdict of P11-03 (margin 0.10 R, one-sided 95 %, >= 30 pairs). Why: it is the only comparison with power at tens of trades (paired sd 0.1 to 0.2 R against an sd of R near 1); realised profit cannot be judged at n < 200. Rejected: "live net R positive", "within the backtest band" (independent samples).
2. **Q: Why 56 days and 40 sessions?** A: The roadmap says at least 8 weeks; 40 sessions guards against a calendar with many holidays. Why: both floors, whichever is later. Rejected: weeks only.
3. **Q: Why 40 round trips and 30 pairs?** A: 40 gives the slippage gate (n >= 20 at S1 and 40 at S2) room; 30 is the floor for a stable t interval of a skewed shortfall. Rejected: 60 (the paper figure) as unreachable in eight weeks at up to three entries a day with misses.
4. **Q: Is a missing compliance record NOT MET?** A: After day 56, yes. Why: doc 19 §5 and Q6 make the re-check an exit item; before day 56 it is simply not yet due. Rejected: never failing it (the item would be forgotten).
5. **Q: Does the report recommend S3?** A: It states the evidence supports proposing it; the owner records `exit_confirmed`. Why: the standing rule that raising real risk is the owner's act. Rejected: automatic S3, no recommendation.
6. **Q: Which criteria can be NOT MET?** A: Only those with a stored fact against them (a hold, a breach, a verdict `not_within`, a missing record after day 56); shortness of data is NOT EVALUABLE. Why: the ADR 0080 / P7-18 / P10-06 rule: never MET on thin data, never NOT MET for being early.

## Risks and honest limits
A MET here is weak evidence: it says the owner's executions over about eight weeks were not demonstrably worse than paper by more than 0.10 R and the discipline and capture held, not that the strategies have an edge or will in another regime. The check reads one paper account; if paper capture was incomplete, pairs are fewer and the label says so. The 0.10 R margin and the floors are options and are printed with every report; changing them after seeing a result is visible in the log of the options (a changed value is printed beside the old default). The `exit_confirmed` guard in P11-04 reads this handler at the moment of the act, so a later change of data can flip it.

## Open questions
None for the build. For the owner (wall-clock): run the eight weeks; keep `live-capture`, `live-settle`, `live-estimates` and the paper services running; write the three compliance records (P11-05); read this report at the end and decide.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); no migration
- [ ] ADR written (next free number at build time); roadmap P11 row and doc 19 §5 note updated; `scripts/build-docs.sh`
- [ ] The run on the owner's data is reported whichever way it falls

## Review fixes 2026-10-25 (2)
- C2: 'at least 50 % of sessions at S1 or S2' was always true under the band; replaced by 'no session above the step factor in force that session'.
- 'Eight criteria' now reads 'eight criteria and integrity line I'.
- Test plan: C2 rows added (0.5 / 0.51, 19 / 20 sessions, 0.125).
