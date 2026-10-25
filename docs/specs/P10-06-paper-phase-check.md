# P10-06 — The paper phase check

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P10 — Paper trading, slice P10-06 of [P10-00](P10-00-paper-trading-plan.md)
- **Design refs:** docs/design/18 (P10 exit: "≥ 60 graded trades; positive expectancy after costs within backtest band; drawdown within Monte Carlo 90th pct"; deliverable "≥ 12w"), 19 §3, §6; ADR 0097 and P7-18 (the pattern: three outcomes, caveat in the label, limits always printed), ADR 0080 (never MET on thin data), P6-18
- **Needs:** P10-01 to P10-05 (their endpoints/ports); P7-18 handler (read). ADR: "the paper phase check" (next free ADR number at build time)
- **Size:** 2 agent-days plus wall-clock time (12 weeks)
- **Safety:** no. Read only; a verb and one GET; writes nothing, runs no replay, changes no setting.

## Goal
Say, from stored records only, whether the current measurement epoch meets the P10 exit, naming every fact that stands against it, every caveat that sits on a pass, and what is still missing, and never print MET on fewer than 60 counted trades, fewer than 12 weeks, an unconfirmed charge sheet or a configuration that changed under the count.

## Scope
**In:** pure `Domain/Paper` `PaperPhaseCheck`, `PaperPhaseFacts` (plain records), `PaperPhaseReport`, `PhaseLimits`, `WeekLine`; `Application/Paper` `EvaluatePaperPhaseCheckHandler` (read only: composes the P10-01 to P10-05 reads and the P7 phase check) and its view; verb `xd paper phase-check [--through DATE] [--epoch ID]`; endpoint `GET /api/paper/phase-check`; frontend tab **Phase check** (read only); contract regenerated; ADR; docs and roadmap row.
**Out:** a stored record of the check (recomputed), running any test from the verb, deciding the exit (the owner confirms it), starting P11, any setting change, judging look-ahead or the single code path (tests prove those, see 9).

## Acceptance criteria
1. **Four exit criteria and one integrity criterion**, each `MET`, `NOT MET` or `NOT EVALUABLE`:
   - **C1 duration and coverage.** Elapsed days of the current epoch, counted from its `started_on` to `through` inclusive, below 84: `NOT EVALUABLE` (`week k of 12`). At 84 or more: coverage = sessions of the epoch with a published plan **and** a finished capture run (no pending outcome) over the trading sessions of the epoch; below 0.80 is `NOT MET` (the fact, with the missing sessions listed), else `MET`.
   - **C2 count.** Counted trades (closed, primary group, `measured`, one per ticket, current epoch) below 60: `NOT EVALUABLE` (`n of 60`, pace, ETA when P10-03's rule allows); 60 or more: `MET`. Never `NOT MET` for being short.
   - **C3 expectancy within the band, after costs** and **C4 drawdown within the 90th percentile**: the verdict and label of P10-04 (`not_evaluable` below 60 counted, or costs not confirmed, or a basis difference, or a thin reference; `not_met` for a mean <= 0, below the band, or a drawdown above P90).
   - **I capture integrity.** `NOT MET` if any capture run of the epoch recorded an `integrity` mismatch (a replay that disagreed with a stored entry) or a ticket-allocation is armed and has had no outcome for more than 3 sessions and its bars exist; `NOT EVALUABLE` with no capture run; else `MET`.
2. **Overall.** `MET` only when C1 to C4 and I are all `MET`; `NOT MET` when any is `NOT MET`; otherwise `NOT EVALUABLE`. Before printing MET the code re-checks: counted tickets distinct, n >= 60, elapsed >= 84, one epoch id throughout, `through` a trading session or the session before; anything else prints `NOT EVALUABLE (malformed window)`.
3. **The caveats are part of the headline label**, not a footnote: `MET (point estimate; 90% interval [lo, hi] includes 0)`, `MET (k risk-down changes: the population was censored)`, `MET (k of n counted trades were auto-approved)`, `MET (m manual runs of the nightly pipeline in the epoch)`, `MET (P7 phase check not met)`; several may stack. The log, JSON `exitCriterion` and `exitVerdict` (bare word) carry them.
4. **Resets are printed.** The report lists every epoch of the history with start, kind, counted n and its unexplained changes; the current epoch is the only one judged; earlier epochs are never pooled into any criterion (a test).
5. **Week lines.** For each ISO week of the epoch: sessions, plans published, failed-closed, manual runs, capture finished, trades counted that week, cumulative counted, cumulative R (metric), config changes (frozen/risk-down). Deterministic text.
6. **Printed, not judged:** the discipline lines (P10-03) in one line each, the sensitivity mean over all primary proposals (P10-04), the auto-approval split, plans with zero primary tickets, the P7 phase check outcome, setup state (services `paper-capture`, `paper-review`, `nightly`, `preopen`, `plan-expiry`, `weekly`; settings `Planning:OpenBook:UsePaperLedger`, `autonomy.auto_approve_tickets`; charge sheet confirmed or not) and **blockers** (what stops a criterion and whose act it is).
7. **Never judged here** (`NOT EVALUABLE` with the test names that prove them): no look-ahead (P3-08 leak properties, `TicketReplayer` equivalence tests), paper equals grading for the same ticket (the P10-01 equivalence test), determinism, the no-order rule, real-fill behaviour (P11).
8. **Limits always printed**, in the object and the text: simulated fills on official 1-minute bars with no real execution; about 2.1 years of history and no hold-out behind the cells; the band and drawdown are a composition-matched bootstrap with the selection and clustering limits of P10-04; 60 trades give an interval of about +-0.3R, so a pass is weak evidence; the owner's charge sheet confirmation is the owner's act; config read from the `xd-api` process; risk-down changes censor the population; stored records only; nothing written.
9. **Deterministic text**; exit code 0 whatever the verdicts say; 1 refused (a bad `--through`, an unknown epoch), 2 usage, 3 failure, 130 cancelled.
10. **Read endpoint** `GET /api/paper/phase-check?through=&epoch=` returns the same report and text (`PaperPhaseCheckView`); GET only; 422 `paper.invalid` for a date after the newest closed session or before the epoch start, 404 `paper.not_found` for an unknown epoch.
11. **Reads only**: every call on every store is a read (a spy); no replay is started.
12. The run on the owner's data is reported whichever way it falls (at build time: no epoch is expected, so every criterion `NOT EVALUABLE`, said as such).

## Contract
- **API:** `GET /api/paper/phase-check?through=YYYY-MM-DD&epoch=ID` -> `PaperPhaseCheckView` { `report` { `epoch`, `through`, `criteria[]` { `id`, `title`, `verdict`, `label`, `facts[]`, `blockers[]` }, `exitVerdict`, `exitCriterion`, `weeks[]`, `epochs[]`, `printed` {...}, `setup`, `limits[]` }, `text` }.
- **CLI:** `xd paper phase-check [--through DATE] [--epoch ID]`, `scripts/xd.sh paper phase-check`.
- **Data:** none, no migration. Reads through ports: `paper.*`, `trading.plan_runs`, `plans`, `grades`, the P7 phase check handler, the service schedules, settings.
- **Events/jobs:** none. Log ids 5250 to 5254.

## Non-functional
One epoch read (entries, exits, skips, runs, about 100 rows), the P10-04 evaluation (under 4 s including the reference runs), 12 weekly lines; not a polling target.

## Test plan
Unit: `PaperPhaseCheckTests` with the hand table below; label stacking; malformed window; epoch history not pooled; week lines. Application with fakes: reads only (spy), default `through`, bad dates, no epoch, charge sheet unconfirmed. Integration: endpoint on an empty database (every criterion `NOT EVALUABLE`), on a seeded 12-week epoch (all met), with a straddle and an integrity case. CLI options parsing. Frontend (MSW): tab, the three verdict words, blockers, no write control.
**Hand table** (the seeded 12-week epoch needs a stub `ICostConfirmationFact` returning true; with the production default the real MET is unreachable until the confirmation-writer slice exists)  (epoch `started_on` 2026-11-02, a Monday; `through` is rounded down to the last closed session, so a Sunday `through` counts as the Friday before; day 85 is 2027-01-25, a Monday, and is the first day a 12-week epoch can read MET, because Sunday 2027-01-24 (day 84) rounds down to Friday day 82; the C1 boundary is 84 elapsed days): (1) through 2027-01-25 (day 85), coverage 0.83, n 61, E3 met (interval includes 0), E4 met, no integrity issue, 2 risk-down changes -> `MET (point estimate; 90% interval [-0.12, +0.51] includes 0; 2 risk-down changes: the population was censored)`. (2) same but n 58 -> C2 `NOT EVALUABLE`, overall `NOT EVALUABLE`. (3) through 2027-01-11 (day 71), n 66 -> C1 `NOT EVALUABLE`, overall `NOT EVALUABLE`. (4) day 90, coverage 0.72 -> C1 `NOT MET`, overall `NOT MET`. (5) n 61, mean -0.10 -> C3 `NOT MET`. (6) sheet unconfirmed -> C3 and C4 `NOT EVALUABLE: costs not confirmed`. (7) one integrity mismatch -> I `NOT MET`. (8) the owner restarted the epoch yesterday -> C1 `NOT EVALUABLE (day 1 of 84)`, earlier epoch's 61 trades printed in the history, not counted.

## Decisions (researched)
1. **Q: What is "graded trade"?** A: a closed paper trade of a primary ticket, measured (not straddle), one per ticket, whose R is the replay's `r_sim`. Why: "graded" in the repo means a trade with an R from the stored rules (P7-13); count-once is the population rule of ADR 0099; reserve trades are the owner's selection, so counting them lets hindsight-free but owner-chosen trades into a sample meant to describe the system's proposals. Rejected: counting per account (duplicates one signal), counting open trades (survivor-free but unfinished R), counting reserve (selection by the owner).
2. **Q: "≥ 12 weeks" and "≥ 60 trades" together?** A: both, whichever is later; 84 elapsed days of the current epoch plus 80 % session coverage so a broken pipeline cannot be waited out. No automatic end: if the count lags (the pace line says so), widening the universe or rules is a frozen change and a new epoch, the owner's decision. Why: ~12 weeks x 5 sessions x about 2 entries a day x a plausible trigger rate is near 60, so the count, not the weeks, may bind (doc 10: `max_new_entries_per_day` 2 to 3).
3. **Q: Why MET can carry a point-estimate label?** A: 60 trades cannot show positivity unless the true mean is above about +0.3R (P10-04); refusing every MET would make the criterion unreachable, printing an unlabelled MET would over-claim. The roadmap row also says "within band", which a point estimate can satisfy. The label keeps the claim exactly as strong as the data. Same device as ADR 0097 (MET with the empty-plan caveat) and ADR 0080.
4. **Q: Does the check decide to go live?** A: no. It reports; the owner confirms the exit and the P11-00 plan starts from the epoch's configuration unchanged. Scaling too early is a named behavioural risk (doc 19 §3).
5. **Q: Endpoint as well as a verb?** A: yes (unlike P6-18): a screen tab needs it and the cost is bounded (one epoch). A P8 phase-check page can reuse the view.

## Risks and honest limits
- A MET means the pipeline and the owner's process worked for 12 weeks and the simulated paper results agreed with the research within a wide band; it says nothing about real fills, slippage, rejections, latency or discipline under real money.
- The check cannot see a configuration change outside the fingerprint, nor a change and its reversal between captures.
- Coverage counts days the pipeline ran, not the quality of the plan; ten empty plans cover ten sessions (the zero-primary share is printed).
- "Unattended" is not required here; manual runs are printed and label a MET.

## Open questions
None for the build. For the owner (wall-clock): complete the pre-flight of P10-00 and start the epoch; confirm the charge sheet; review weekly; then twelve weeks must pass. Defaults until then: every criterion reads `NOT EVALUABLE` with the reason.

## Done when
- [ ] Acceptance criteria 1 to 12 have evidence
- [ ] `scripts/check.sh` green; contract regenerated
- [ ] ADR written, roadmap row and P10 status line, `scripts/build-docs.sh` run

## Review fixes 2026-10-25
- Costs: C3/C4 stay `not_evaluable: costs not confirmed` until the unwritten slice that writes the charge-sheet confirmation exists; P10-06 reads the fact only through P10-04's `ICostConfirmationFact`.
- Coverage reads `capture_runs` (`outcome`, `pending`) as written by P10-01 criterion 11b.

## Review fixes 2026-10-25 (2)
- Dates: 2027-01-24 and 2027-01-10 are Sundays; 'through is rounded down to the last closed session' defined; hand table uses 2027-01-25 (day 85) and 2027-01-11 (day 71); the 84-day boundary noted.
- The seeded 12-week epoch needs a stub `ICostConfirmationFact`; real MET is unreachable until the confirmation-writer slice exists. Label 'P10-07 or P8-06b' replaced.
