# AU-02 — Recovery runner and its three triggers

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-02 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** AU-01
- **Decisions:** [ADR 0240](../adr/0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md)
- **Design refs:** docs/design/04-accounts-access-and-operations.md §3; ADR 0018; ADR 0027

## Goal
After start-up, wake from sleep, or a broker reconnect, the system makes up what it missed once, in order, and stops the dependents of anything that fails.

## Scope
**In:** `RecoveryRunner`, the `platform.recovery_passes` and `platform.recovery_items` ledger, the three triggers, and replacing the scheduler's own `CatchUpAsync` branch so one path decides.

**Out (not in this slice):** The screen (AU-04); historical repair beyond the reach (the existing Data backfill).

## Acceptance criteria
1. Given a start-up with missed steps, then one pass runs them in plan order, one at a time, each through the normal service runner with trigger `catch_up`, linked by `run_id`.
2. Given a scheduled time found late by the existing wake detection, then a pass starts; given a successful Data-account connect (`CompleteConnect`), then a pass starts.
3. Triggers inside the debounce (`Recovery:DebounceMinutes`, 1 to 120, default 10), or while a pass runs, record no second pass and start nothing.
4. Re-running after a crash at any step starts no step that already succeeded (unique key on service and occurrence); a `running` item left by the old process becomes `failed: interrupted` and is eligible again.
5. When a step fails after its normal retries, its transitive dependents become `blocked` with the failed step named, and independent branches continue.
6. A step of a service with `MustNotOverlap` market hours waits, shown as `waiting: market hours`, and resumes after the close.
7. `eod` recovered during or after a session never treats the open session as closed; `nightly` plans only from the last closed session.
8. A failed or blocked item is retried at the next trigger, at most `Recovery:MaxAttempts` (1 to 10, default 3), then stays `failed` and raises one notice per service per day.
9. The old one-catch-up-per-start behaviour is replaced with no double run: a test with the scheduler and the runner together counts exactly one run per occurrence.
10. A run recovered for `nightly` is written to `plan_runs` as recovered; the P7 phase check reports it as `recovered_run`, it does not count as unattended, and it breaks the streak (ADR 0097 is tightened, not loosened).

## Contract
- **API:** none in this slice (the read API is AU-04).
- **Data:** Migration `AddRecoveryLedger`: `platform.recovery_passes(id, trigger, started_at, finished_at, status)`; `platform.recovery_items(id, pass_id, service_id, occurrence, session_from, session_to, state, reason, attempts, run_id)`, unique on `(service_id, occurrence)`; runtime role SELECT, INSERT, UPDATE, no DELETE. `plan_runs` gains `recovered bool not null default false`.
- **UI:** none.
- **Events/jobs:** Logs `recovery.started|step|blocked|finished`. Idempotent per service and occurrence.

## Non-functional
The scheduler tick stays a few indexed reads. A pass adds no market-table scans of its own.

## Safety
A step that cannot prove its inputs does not pass: `nightly` keeps all six fail-closed gates. Paused services stay paused. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit (fake clock, in-memory store): ordering, debounce, blocked dependents, crash resume, market-hours wait. Integration (TimescaleDB): migration, grants, unique key. A golden test of the phase check with a recovered run.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
