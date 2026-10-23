# P7-08 — Fail-closed nightly pipeline skeleton

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every non-SAFETY open question of P7-00 taken as recommended; the slice places no order, reads no credential, approves nothing and does not touch the depth feed, so no SAFETY stop applies). Built 2026-10-23.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-08 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 3.5 d
- **Design refs:** docs/design/04-accounts-access-and-operations.md §3, §4 · 10-trade-selection.md · 12 · 15 §5 · 16 (`nightly`, `plan_review_cutoff`) · 17 · 19-risks-and-compliance.md §4, §5 · ADR 0086 · ADR 0018 (services), ADR 0083 (plan record)

## Assumptions (taken as recommended)
1. **Safe by construction.** No order, no credential, no real account, no approval (the code never touches `workflow.approvals` or a ticket move), no depth recording. The only writer is the plan store of P7-03 (`PublishAsync`, `RecordRunAsync`).
2. **The framework, not Hangfire** (P7-00 Q15): `nightly` is an `IScheduledService` run by the existing `ServiceScheduler` in `xd-api`; the timeout, retries (default 0), catch-up (same day) and history are the framework's.
3. **It starts paused.** Until the real builder (P7-04/P7-09) exists the only plan it can publish is an empty plan marked skeleton, so the owner switches it on from the Services screen; there is no first-start seed. Default schedule: daily 18:30 India time on trading days, never before the session ended, must follow `eod` and `portfolio-broker-history`.
4. **Session and planned session.** The session is the last closed session (or `--date`); the plan is for the next trading day after it (Friday's run plans Monday). `plan_runs.for_date` and `plans.for_date` are the planned session. The review cutoff is 09:05 India time of the planned session (doc 16), stored as UTC.
5. **Gates, in order** (id, reads): `calendar` (the session is a trading day and has closed, a next session is within 10 days, the cutoff of the planned session has not passed), `eod` (a succeeded `eod` service run began after the close and before the planned day), `portfolio-sync` (the same for `portfolio-broker-history`; a run skipped with "no Portfolio account connected" counts, there is nothing to sync), `fresh-data` (newest stored bar on or after the session and at most 5 % of instruments behind or empty), `data-quality` (the newest finished full quality run finished after the close, succeeded, at most 5 % of instruments failed), `selection-inputs` (a cell statistics snapshot exists, is not dated after the session, and is at most 5 sessions older; P7-00 D7 said "as of D", the statistics are refreshed weekly (Q16), so a tolerance is needed). The 5 % and 5 sessions are fixed constants until the settings slice (Q14).
6. **Fail closed.** A gate returns pass, fail or unavailable. Only pass lets the run go on. A gate that throws is unavailable (the row names the exception type only). A real run stops at the first blocking gate; a dry run reads them all.
7. **One row per attempt.** Every non-repeat real run writes exactly one `plan_runs` row: `published` (with the plan) or `failed_closed` (with the gate or stage id and the reason). The attempt number is the row's position among the rows of the planned session. A dry run writes nothing.
8. **Idempotent.** A planned session that already has a `published` row, or just a stored plan (a plan counts as published even if its row is missing), is not touched: the result is the first published row (lowest id) or that plan (`repeated`), no gate is read, nothing is written, also for a manual run and a dry run. After a failed run a new run is a new attempt (the point of re-running is that the data may have been fixed); the plan key `nightly:{date}:{builder}` makes a concurrent double publish store one plan. **The plan and its `published` run row are written in one transaction** (`IPlanStore.PublishWithRunAsync`); if the row cannot be written the plan is rolled back with it and the failure is recorded as `failed_closed` at `publish`. If the key is already stored and a concurrent run owns the row, no second row is written (the result says "already published by another run"). A migration (`PlanRunsOnePublishedPerSession`) adds a unique index on `plan_runs(for_date)` where `outcome = 'published'`, so the database also refuses a second published run for a session.
9. **The builder is a port** (`IPlanBuilder`), one implementation: `NullPlanBuilder`, an empty, provisional plan with basis "skeleton: no tickets ...". It only builds; the pipeline stores. A builder that refuses or throws fails closed under the id `plan-builder` (the row holds a fixed sentence and the error code, never the builder's own text); a store error under `publish`. Nothing is published partially (the store is one transaction).
10. **The service outcome.** Published or repeated: succeeded. Any fail-closed result: the run is *failed* with "Fail closed at {gate}: {reason}", so the Services screen and the bell show it (the existing notify-on-failure).
11. **Gate limits stated.** `fresh-data` passes "reach" on the newest bar of ANY instrument (one early bar passes that test; the share test catches a thin day) and counts instruments in the coverage table that are inactive or delisted as behind or empty, so a large dead universe can fail it chronically. `data-quality` is not tied to the `eod` run's data job: any full quality run that finished after the close and succeeded counts, a manual one too (tying by "started after eod finished" cannot hold, the quality step runs inside eod's job). `selection-inputs` reads one aggregate (`ICellStatsStore.NewestAsOfAsync`).
12. **Names.** Service `nightly`; verb `xd plan run [--date YYYY-MM-DD] [--dry-run]` (manual flag set; exit 1 when it failed closed); `GET /api/plan-runs?date=&limit=` (read only); log ids 4680 to 4684; ADR 0086. One small migration (`PlanRunsOnePublishedPerSession`, the unique index above); `ListRunsAsync` is a select whose attempt number is a `row_number()` over the whole (one row per night) table, a shortcut.

## Goal
Every evening the system either publishes a plan record for the next session or says, in a stored row, which input stopped it and why, and in the second case publishes nothing; a run can be previewed without writing.

## Scope
**In:** Domain `Planning` (`GateStatus`, `GateResult`, `NightlyGateIds`, `NightlyRules`); Application port `IPlanBuilder`, `PlanRunRow`, `IPlanStore.ListRunsAsync`, the gates and their pure checks, `RunNightlyPlanHandler`, `NullPlanBuilder`, `NightlyService`, `ListPlanRunsHandler`; Infrastructure `ListRunsAsync`; the endpoint; the verb; log ids; ADR 0086; docs; contract regenerated.
**Out:** the real builder, the candidate source, selection wiring, allocation, checklist (P7-04 to P7-07, P7-09); notifications beyond the existing service-failure one; the SignalR `nightlyStatus` event; the `suggestions` service; the phase check (P7-18); any approval, arming, pre-open check (P7-10, P7-11); autonomy and depth recording (SAFETY, owner).

## Acceptance criteria
1. Each of the six gates failing, and three of them unavailable, publishes no plan and writes exactly one `failed_closed` row naming that gate; later gates are not read. *`NightlyPipelineTests`, `NightlyPlanApiTests`.*
2. All gates green: one empty skeleton plan (Published state, no items, no approval row) and one `published` row naming it; the cutoff is 09:05 India time of the planned session. *`NightlyPipelineTests`, `NightlyPlanApiTests`, `NightlyRulesTests`.*
3. A second run for a planned session that has a published plan is a no-op returning the first result: no gate read, no row, no plan; a run after a failed one is attempt 2. *`NightlyPipelineTests`, `NightlyPlanApiTests`.*
4. A dry run reads every gate and writes nothing (no row, no plan), whatever it finds. *both.*
5. A gate that throws is unavailable and blocks; a builder that refuses or throws, and a store that fails, fail closed with their own ids and publish nothing; error text is never stored. *`NightlyPipelineTests`.*
6. Each gate's decision is pure and tested both ways with its edge (before the close, holiday, cutoff passed, no next session, a service run before the close or after the planned day, a skipped run, share of instruments, no quality run, look-ahead statistics, stale statistics). *`NightlyChecksTests`.*
7. The service is registered, starts paused, 18:30 trading days, follows `eod` and the portfolio sync with no schedule issue, and maps pass to succeeded and fail-closed to failed. *`NightlyServiceTests`, `ServicesApiTests`.*
8. `xd plan run` parses `--date`, `--dry-run`, refuses everything else; `GET /api/plan-runs` lists attempts (422 `plans.invalid` for a bad limit). *`PlanRunOptionsTests`, `NightlyPlanApiTests`.*
9. The no-order tripwire, grants and layer rules stay green with no rule relaxed; log ids unique. *Architecture and grants tests.*

## Contract
- **API:** `GET /api/plan-runs?date=&limit=` -> `PlanRunView[]` (attempt, planned date, plan key, outcome, failed gate, reason, plan id, manual, times). 422 `plans.invalid`.
- **Data:** migration `PlanRunsOnePublishedPerSession` (unique partial index `uq_plan_runs_published_for_date`).
- **Events/jobs:** service `nightly`, daily 18:30, paused by default, timeout 30 min.

## Non-functional
Gate reads are one indexed query each (`ListAsync` of 20 runs, bar totals from the coverage table, last quality run, latest statistics snapshot). Logs carry gate ids, status codes, dates and counts only (4680 to 4684). Time through `TimeProvider`; the pure rules take the instant as an argument.

## Test plan
Unit (rules, every gate's check, pipeline over fake gates and store, service), integration on a real database (publish, repeat, failure, dry run, real gates on an empty database, endpoint), CLI parsing, architecture unchanged.

## Honest limits
This is the skeleton: the plan it publishes has no ticket. A green night proves the inputs were present and fresh by the gates' definitions (stored runs, coverage, the last quality run, a statistics snapshot), not that the data is right. `fresh-data` counts instruments behind or empty from the coverage table and treats an unknown universe as a fail. The service retry is off by default, so a night that fails closed stays failed until the owner runs `xd plan run` (manual, attempt 2). Thresholds are constants. The quality and fresh-data limits are in assumption 11.

## Open questions
None blocking.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract regenerated; ADR 0086; docs and roadmap updated
