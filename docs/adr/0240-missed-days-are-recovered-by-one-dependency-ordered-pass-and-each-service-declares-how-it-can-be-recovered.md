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

## Amendment 1 (AU-01 build, 2026-11-09; decided by delegation, owner rule "unsettled decisions: research, choose the best, record")
Choices the spec left open, taken for the pure planner. Class per service is pinned in `RecoveryClassTableTests`; every class below except the eod rows is UNVERIFIED against what the service really does and is the conservative default of AU-00 (`LatestOnly`).
1. **Classes.** `Convergent`: `eod`, `history-eod`, `options-backfill`, `options-quality`, `shadow-grade` (each reads stored data and fills what is missing). `TimeBound`: `preopen`, `review-cutoff`, `shadow-write`, `paper-capture`, `live-estimates`, `live-capture`, `live-settle`, `live-feed`, `holdings-quotes` and the three recorders. `LatestOnly`: everything else, including `nightly`. Best practice for backfills (serial, oldest first where a day depends on the previous, bounded reach) gives `Dated` for tests only; no registered service is `Dated` yet.
2. **Order.** Kahn's topological order over `MustFollow` and `after`; of the ready services the lowest stage first, then by id (deterministic). Stage 0 data, 1 learning (follows `eod`), 2 `nightly` and what follows it, derived from the chains (no table). A `Dated` service runs all its sessions, oldest first, before the next service. A cycle (a self-follow included) is `recovery.cycle`, naming the services on it.
3. **Occurrences.** One per India date: the first occurrence of the schedule that day (an interval or window counts once a day), due when at or before now and no run of the service started at or after it. A service on an `after` schedule is due when the service it follows is; one that follows an unregistered service has none. Paused, disabled or catch-up-off services are skipped.
4. **Reach.** The `Recovery:MaxSessions`-th trading session back from the latest closed one. Older missed occurrences are `out_of_reach` back to the last run (at most 400 days); a service that never ran has no baseline, so only the reach is listed.
5. **Nightly.** One step for the latest session only while the review cutoff of the next session after the latest closed one is strictly ahead; otherwise (or with no next session in the calendar) every occurrence is `not_recoverable: cutoff passed`.
6. **Setting.** `Recovery:MaxSessions`, 1 to 250, default 30, validated at start-up; not in the visible settings. No reader until AU-02.
7. **Unclassified.** `ServiceConstraints.RecoveryClass` is nullable on purpose: a service that declares none fails the registry test and the planner refuses the plan (`recovery.unclassified`), never a default class.
