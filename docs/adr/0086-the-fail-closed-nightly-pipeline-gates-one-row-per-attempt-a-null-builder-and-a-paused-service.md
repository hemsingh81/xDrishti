# 0086 — The fail-closed nightly pipeline: ordered gates, one row per attempt, a null builder and a paused service

- **Status:** Proposed (spec [P7-08](../specs/P7-08-fail-closed-nightly-pipeline-skeleton.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0018](0018-services-framework-and-editable-schedules.md) (the framework it registers in), [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (the plan and `plan_runs` it writes).

## Context
Doc 19 says bad data must not become a wrong plan: fail closed. P7-09 will wire the real stages; before that the gates, the run record and the idempotency should exist and be proved with a stub builder.

## Decision
1. `nightly` is a registered service (the framework, not Hangfire: docs 03/04/16/17 said Hangfire, the repo never used it), daily 18:30 trading days, after `eod` and `portfolio-broker-history`, **paused by default**, no first-start seed.
2. Six gates in a fixed order (calendar, eod, portfolio-sync, fresh-data, data-quality, selection-inputs), each pass, fail or unavailable; only pass continues; a throwing gate is unavailable. Pure decisions in `NightlyChecks`, thresholds fixed in `NightlyRules` (5 % of instruments, 5 sessions of statistics age) until the settings slice.
3. A real run writes one `plan_runs` row (`published` or `failed_closed` with gate and reason) and nothing else on failure; a dry run writes nothing; a published session is never run again (the first result is returned). A run after a failure is a new attempt. No run row is written for a repeat; a stored plan with no row also counts as published. The plan and its `published` row are written in one transaction (`PublishWithRunAsync`), and a unique partial index on `plan_runs(for_date) WHERE outcome = 'published'` (migration `PlanRunsOnePublishedPerSession`) makes the database refuse a second one.
4. The plan comes from a port, `IPlanBuilder`; the only implementation is `NullPlanBuilder` (empty, provisional, basis "skeleton"). The pipeline stores the plan only after the builder returned a whole one.
5. A fail-closed result ends the service run as failed so the existing failure notification shows it. `xd plan run [--date] [--dry-run]` and `GET /api/plan-runs` read and drive the same handler; the verb is always a manual run.
6. No approval, arming, order, credential or depth path is added; the lapse default of ADR 0083 stands.

7. Stated limits: `data-quality` accepts any full quality run that finished after the close (not tied to the `eod` job); `fresh-data` accepts one early bar for the reach test and can fail chronically if the coverage table holds many inactive instruments; a builder's own error text is never stored.

## Consequences
- Switched on today it would publish an empty plan every trading evening, which is why it starts paused.
- `selection-inputs` will fail on any night more than 5 sessions after the last cell statistics snapshot; weekly statistics (P7-00 Q16) are needed before the 10-session exit.
- A failed night is not retried unless the owner sets retries or runs the verb.

## Alternatives considered
- **A repeat after a failure is a no-op too.** Rejected: it would make a fixed input unusable until the next session.
- **Hangfire.** Rejected: not in the repo; the framework already has timeouts, retries, catch-up and history.
- **Enabled by default.** Rejected: an empty plan every night teaches nothing and pollutes the phase-check count that starts at P7-09.
