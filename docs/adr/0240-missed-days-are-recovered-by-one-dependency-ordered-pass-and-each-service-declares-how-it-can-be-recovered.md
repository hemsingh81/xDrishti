# 0240 — Missed days are recovered by one dependency-ordered pass, and each service declares how it can be recovered

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Pure planner built in AU-01 (see Amendment 1); the runner is AU-02.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Supersedes §4 of [0018](0018-services-framework-and-editable-schedules.md) (the reach and the once-per-start rule; the paused rule stays). Tightens [0097](0097-the-p7-phase-check-ten-consecutive-scheduled-sessions-from-stored-plan-runs-and-never-met-on-fewer.md). Keeps [0086](0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md).

## Context
Catch-up runs once per start and, for daily jobs, only for the same India day (`CatchUpReach.SameDay`). A machine that sleeps for days is left with holes. Replaying everything naively would be wrong: a plan for a past day is meaningless and a decision-time capture cannot be made after the fact.

## Decision
1. Each service declares a recovery class. `Convergent`: one run closes all missed days (the end-of-day run). `LatestOnly`: one run for the latest closed session. `Dated`: one run per session, oldest first. `TimeBound`: never made up, reported as missed. Unclassified services fail a test.
2. One pass, started by start-up, wake or Data-account reconnect, runs steps one at a time in the topological order of `MustFollow` and `after`. A failure blocks its dependents only.
3. An item is unique per service and occurrence, so a pass after a crash is idempotent.
4. `nightly` is `LatestOnly` and runs only before its review cutoff, with all gates. Runs it publishes are flagged recovered and do not count towards the unattended streak of ADR 0097.
5. Reach is `Recovery:MaxSessions` (default 30, 1 to 250). Paused services are not missed.
6. Trigger `catch_up` is reused, so the `service_runs` check is unchanged.

## Consequences
The scheduler's own catch-up branch is replaced. The phase check of P7 gets stricter. Sessions beyond the reach need the existing Data backfill.

## Amendment 1 (AU-01 build, 2026-11-09; decided by delegation, owner rule "unsettled decisions: research, choose the best, record"; revised after the independent review)
Choices the spec left open, taken for the pure planner. The class of each service is pinned in `RecoveryClassTableTests`.
1. **Classes, with the facts read in the code.**
   - `Convergent`: `eod`, `history-eod`, `options-backfill`, `options-quality`, `shadow-grade` (each reads stored data and fills what is missing; UNVERIFIED for the last four beyond their descriptions).
   - `TimeBound`: `preopen`, `review-cutoff`, `shadow-write`, `paper-capture`, `live-estimates`, `live-capture`, `live-settle`, `live-feed`, `holdings-quotes`, the three recorders, and, after review, `readiness-snapshot`, `decay-monitor` and `ml-monitor`. The snapshot handler stamps its row with today's India date (a recovered run would write a misdated evidence row); the decay and model monitors evaluate the state as of today, are keyed "already checked today", and can propose or apply a kill or rollback, so a run on a stale reading must never happen.
   - `LatestOnly`: everything else, including `nightly`. `post-trade-prompts` looks back `CatchUpTradingDays` (3) trading sessions itself, so one run covers the last three; a missed session older than that is listed superseded although the run does not reach it (known limit, the Data side has no equivalent). Other `LatestOnly` rows are the AU-00 default, UNVERIFIED.
   - No service is `Dated`: with no baseline for "registered since" (item 4) a first run of a `Dated` service would replay the whole reach, so none may be added before AU-02 settles it (a test pins it).
2. **Order.** Kahn's topological order over `MustFollow` and `after`; of the ready services the lowest stage first, then by id. Stage 0 data, 1 learning (follows `eod`), 2 `nightly` and what follows it, derived from the chains. A cycle (a self-follow included) is `recovery.cycle`, naming the services on it.
3. **Occurrences.** One per India date: the first occurrence of the schedule that day, due when at or before now and no run of the service started at or after it. A service on an `after` schedule is due when the service it follows is, and only when every service on the chain is enabled with catch-up on; one that follows an unregistered service has none. Paused, disabled or catch-up-off services are skipped.
4. **Reach.** The `Recovery:MaxSessions`-th trading session back from the latest closed one. Older missed occurrences are `out_of_reach` back to the last run (at most 400 days). A service that never ran has no baseline, so it is missed for the whole reach; AU-02 decides whether a first start needs a "registered since" baseline.
5. **Dependencies.** A service that is not eligible, or was due and made no step (not recoverable, out of reach, or blocked itself), blocks what follows it by `MustFollow` or `after`, transitively: those occurrences are `not_recoverable: <service> did not run` and have no step. A `TimeBound` service keeps its own reason.
6. **Nightly.** One step for the latest session only when that session is the latest closed one and the review cutoff of the next session is strictly ahead; otherwise (or with no next session in the calendar) every occurrence is `not_recoverable: cutoff passed`.
7. **Weekly services.** A run on a weekly schedule (or one that follows it) with the `SameDay` reach is made up only within 24 hours of its time (the same rule as the services' own catch-up); later it is `not_recoverable`, since the run on a weekday fails or would describe a past week.
8. **Setting.** `Recovery:MaxSessions`, 1 to 250, default 30, validated at start-up; not in the visible settings. No reader until AU-02.
9. **Unclassified.** `ServiceConstraints.RecoveryClass` is nullable on purpose: a service that declares none fails the registry test and the planner refuses the plan (`recovery.unclassified`).
