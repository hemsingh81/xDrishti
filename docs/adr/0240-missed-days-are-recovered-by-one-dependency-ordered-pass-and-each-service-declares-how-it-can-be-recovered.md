# 0240 — Missed days are recovered by one dependency-ordered pass, and each service declares how it can be recovered

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Not yet built.
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
