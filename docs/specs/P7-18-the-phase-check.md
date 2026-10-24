# P7-18 — The phase check

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22 and the owner's binding decisions of 2026-10-23; every open question taken as recommended, P7-00 question 10 included). Built 2026-10-24.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-18 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 2 d plus wall-clock time (ten sessions)
- **Design refs:** docs/design/18-roadmap.md (P7 exit: "Plans published 10 consecutive sessions unattended") · docs/design/19 (what P7 may and may not do) · ADR 0097 · ADR 0080 (the P6 phase check, the pattern) · ADR 0086 (`plan_runs`), 0083/0095 (plans, review), 0092/0096 (wiring, pre-open)

## Assumptions (taken as recommended; the owner's decisions are binding)
1. **Binding decisions, unchanged.** P7 places no order; paper only; no auto-approval unless the owner's setting is ON (default OFF); this slice is **read only**: it writes nothing, runs no test and changes no setting.
2. **Q10, "published".** A session counts when the `nightly` service published a plan row for it by schedule or its catch-up (the stored `manual` flag is false), **including the skeleton or empty plan**, which is **reported separately** as "plans with zero primary tickets" (and "skeleton"): it never blocks a MET, it is printed beside it as a caveat in the label.
3. **Q10, "unattended".** Read from the stored records only: no attempt for the session has `manual = true` (a manual retry that published, or a manual failed attempt, makes the session not unattended) and no attempt failed closed. A code change or an owner action on the pipeline is not recorded and cannot be seen; this is a printed limit.
4. **Q10, a fail-closed day breaks the run**, even when a later scheduled attempt of the same session published (the owner's rule is strict: a bad-data day is not an unattended success). The check lists gate and reason so a correct refusal can be told from a bug.
5. **Q10, "consecutive"** means consecutive NSE trading sessions per the trading calendar (the planned dates `plan_runs.for_date`): the window is the last ten trading days ending at `--through`; weekends and confirmed holidays are not sessions and do not break the run; a trading day with no run row (the service did not run) is a gap and does.
6. **Three outcomes for the exit.** NOT MET when any session in the window has a fact against it (manual run, failed-closed attempt, no run on a trading day after the history began, a published row whose plan is not stored). NOT EVALUABLE when nothing is against it but fewer than ten sessions have any history (printed: how many more are needed). MET only for ten counted sessions in a row ending at `--through`. It is never MET on fewer; the code re-checks the ten counted sessions before it prints MET.
7. **The end of the window.** Default: the planned session of the last closed session; before the `nightly` time (the schedule's time, else 18:30 India) on the evening of that closed session, with no row yet, the newest session is **not yet due** and the window ends at the session before (so the check is not wrong at 17:00). `--through DATE` is taken as given (a non-trading date takes the trading session on or before it; a date after the newest planned session is refused, problem `plans.invalid`).
8. **All twelve acceptance criteria of P7-00 are listed**, numbered as there. Judged from stored records: 1 (ten sessions), 2 (fail closed), 4 (no order), 6 (honest evidence), 7 (human in the loop), 8 (accounts), 9 (grading), 11 (journal). **Never judged here (NOT EVALUABLE, with the test names that prove them):** 3 (no look-ahead), 5 (determinism), 10 (active set), 12 (process steps). A MET that rests on a test or a setting the check does not run or see carries a **caveat in its label**, as in ADR 0080.
9. **Criterion 2 (fail closed).** NOT MET if a stored failed-closed row lacks a gate or a reason or names a plan; NOT EVALUABLE with no such row, or while some gate has not been seen failing in a stored row (the unseen gates are listed); MET only when all six gates have been seen failing consistently (the per-gate proof stays the tests).
10. **Criterion 4 (no order).** The Dhan allow-list is read through a port from the code that holds it (`BrokerEndpoints`, not a config file) and compared with a **pinned list** in the Domain; ten probe requests that would be orders, positions or funds must all be refused. A difference is NOT MET and names the entry (a change of the list is an owner-approved item and must change the pin in the same commit). The architecture tripwire is a test and is only referenced.
11. **Criterion 7 (review).** From the newest 60 plans: counts of plans reviewed, lapsed, and tickets approved, modified, rejected by the owner and **auto-approved** (auto must be 0 while the switch was never stored ON; if it was ever ON the count is printed and allowed). NOT MET: an auto approval with the switch never ON; an approval dated at or after its cutoff; an approved, armed or triggered ticket without an approve row; a ticket still undecided more than two hours after its cutoff (the review-cutoff job did not lapse it). NOT EVALUABLE until a ticket has been decided or lapsed.
12. **Setup is printed.** The state (enabled, paused, not registered) of `nightly`, `eod`, `portfolio-broker-history` (needed for the exit) and `suggestions`, `review-cutoff`, `preopen`, `plan-expiry`, `weekly`; the switches `Planning:Builder:UseSelection`, `Planning:OpenBook:AssumedEmpty`, `autonomy.auto_approve_tickets`, `autonomy.promote_from_reserve` with ON or OFF; the **blockers**: which required services and which needed switches are still off and what that stops (the auto switches are the owner's choice, not part of the exit, and are not blockers). Counts: plans read, graded tickets, journal entries, weekly digests, armed tickets.
13. **Limits are always printed** (about 2.1 years of data and no hold-out; plans provisional because no cell is Active before P10; market gate, events, results, ban and surveillance data have no source; the empty-plan risk; the pre-open quote format unverified; "unattended" read from the manual flag only; the Dhan token lapse; stored records only; the switches are read from this process's configuration; nothing is written). They are in the report object and in the text.
14. **Deterministic text; exit code 0** whatever the criteria say (a report, not a gate); 1 refused (a bad `--through`), 2 usage, 3 failure, 130 cancelled.
15. **Read endpoint** `GET /api/plan-runs/phase-check?through=` returns the same report and text (`PlanPhaseCheckView`). It reads at most the newest 200 run rows, ten sessions of rows and the newest 60 plans with their tickets, approvals, grades and journal; session and CSRF rules as for the other plan reads (a GET).
16. **ADR 0097; log ids 4790 to 4791 (of 4790 to 4799); no table and no migration.** Docs: this spec, ADR, the roadmap row and a P7 status line, `scripts/build-docs.sh`.

## Goal
Say, from what is stored, whether the nightly pipeline has published ten consecutive sessions without anyone touching it, name every fact that says otherwise and every owner switch that is still off, and never print MET for the exit on fewer than ten real consecutive scheduled sessions.

## Scope
**In:** pure `Domain/Planning` (`PlanPhaseCheck`, `PlanPhaseFacts` and its records, `PlanPhaseReport`, the pinned allow-list and the probes); `Application/Planning/EvaluatePlanPhaseCheckHandler` (read only) and its view; port `IBrokerAllowList` with `BrokerAllowList` over `BrokerEndpoints.Describe()`; verb `xd plan phase-check [--through DATE]`; endpoint `GET /api/plan-runs/phase-check`; contract regenerated; ADR; docs and roadmap.
**Out:** running any test from the verb; a stored record of the check (it is recomputed); any setting change; a UI screen (P8); judging look-ahead, determinism or the live active set from records; the 10-session wall-clock itself (it needs the owner to switch the services and builder ON and ten sessions to pass).

## Acceptance criteria
1. Ten consecutive scheduled published sessions are MET; a manual run, a failed-closed attempt, a gap day or a published row without its plan breaks the run (NOT MET); fewer than ten sessions of history is NOT EVALUABLE; a window shorter than ten is never MET. *`PlanPhaseCheckTests`.*
2. A holiday or a weekend between two sessions does not break the run (the window is the calendar's trading sessions) and the holiday is not read. *`EvaluatePlanPhaseCheckHandlerTests`.*
3. Zero-primary and skeleton plans are counted and shown as a caveat on a MET; each session line shows the planned date, the scheduled and manual run counts, the failed attempts, the plan and its primary, reserve and rejected counts, the gate result and the finish time. *`PlanPhaseCheckTests`.*
4. Criteria 2, 4, 6, 7, 8, 9 and 11 each have their outcomes by a hand table; criteria 3, 5, 10 and 12 are never judged. *`PlanPhaseCheckTests`, `EvaluatePlanPhaseCheckHandlerTests`.*
5. Auto approvals are counted by actor kind and are a fact against the workflow while the switch was never ON. *`EvaluatePlanPhaseCheckHandlerTests`.*
6. Services and switches are read and the ones still off are named as blockers; the limits are printed in every outcome; the text is deterministic and keeps a reason on one line. *`PlanPhaseCheckTests`, `EvaluatePlanPhaseCheckHandlerTests`.*
7. The default end of the window waits for the nightly time; an explicit date is honoured, a weekend date takes the session before, a later date is refused. *`EvaluatePlanPhaseCheckHandlerTests`.*
8. The check writes nothing (every call on every store is a read). *`EvaluatePlanPhaseCheckHandlerTests`, `PlanPhaseCheckApiTests`.*
9. The allow-list in the code equals the pinned one and every order, position and fund probe is refused. *`BrokerAllowListTests`.*
10. The command line parses `--through` and refuses bad lines; the endpoint answers on an empty database, on ten real stored sessions, with a manual run, and refuses a later date with 422. *`PlanPhaseCheckOptionsTests`, `PlanPhaseCheckApiTests`.*
11. The run on the owner's data is reported whichever way it falls. *Reported 2026-10-24: see ADR 0097.*

## Contract
- **API:** `GET /api/plan-runs/phase-check?through=YYYY-MM-DD` → `PlanPhaseCheckView { report, text }`; 422 `plans.invalid` for a date after the newest planned session or before any session. Read only.
- **Data:** none; no migration. Reads `trading.plan_runs`, `plans`, `plan_items`, `workflow.approvals`, `trading.grades`, journal tables, `research.digests`, `workflow.autonomy_settings`, `platform.service_schedules` through their ports.
- **CLI:** `xd plan phase-check [--through YYYY-MM-DD]`, `scripts/xd.sh plan phase-check`.
- **Events/jobs:** none (not a service; an owner's read).

## Non-functional
Reads 10 session queries, one run query, the newest 60 plans and about four reads per plan; a verb or an occasional page, not a polling target. No secret is read or logged; log events carry the date, the exit word and counts only.

## Test plan
Unit: `PlanPhaseCheckTests` (domain, hand tables), `EvaluatePlanPhaseCheckHandlerTests` (application, fakes), `BrokerAllowListTests` (infrastructure). Integration (real TimescaleDB): `PlanPhaseCheckApiTests`; CLI parsing in `PlanPhaseCheckOptionsTests`. Architecture: unchanged and green (the no-order rule, the layer rules and the log-id rule scan the new code). The real-data read is by `scripts/xd.sh plan phase-check` and is reported, not asserted.

## Open questions
None for the build. For the owner (wall-clock): switch the `nightly`, `suggestions`, `review-cutoff`, `preopen` and `plan-expiry` services and the builder switch ON when ready (the check names what is still off); keep `eod` and the portfolio sync running; reconnect the Dhan Data account daily (a lapsed token fails the EOD gate and breaks the run); then ten trading sessions must pass with no manual run. Whether ten empty skeleton plans should count (P7-00 risk 1) stays the owner's call: the check counts them and says so beside the MET.

## Done when
- [x] Acceptance criteria 1 to 11 have evidence
- [x] `scripts/check.sh` green; contract regenerated
- [x] ADR 0097, roadmap row and P7 status line, `scripts/build-docs.sh` run
