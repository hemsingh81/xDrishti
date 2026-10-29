# P12-01b - The execution record, the `xd_exec` role and the `xd-exec` container

- **Status:** Draft (split from P12-01 at build, ADR 0155, 2026-10-29; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P12 - Execution service, slice P12-01b of [P12-00](P12-00-execution-service-plan.md)
- **Needs:** P12-01 (built). Gate: none to build (still no broker call).
- **Safety:** yes. New database role, new container (profile `live`, not started by `xd-up.sh`), new network; everything OFF.

## Goal
Everything of [P12-01](P12-01-xd-exec-skeleton-and-order-state-machine.md) that needs a database, a role or a container: the parts moved out by its "Amendments at build".

## Scope
- Schema `execution` and migration `AddExecutionRecord` (tables, insert-only rules, legal-pairs check trigger, chain keys), role `xd_exec`, `ExecGrantsTests` (criteria 7, 8, 10 in database form).
- Database event store and projection replacing the in-memory one; command poller and `command_results` (criterion 13); heartbeat `exec` and `--healthcheck`; `GET /api/execution/status` with `scripts/gen-api.sh` (criterion 12).
- Dockerfile target, compose `xd-exec` in profile `live`, network `exec`, secret `db_exec_password`, `xd-up.sh --exec` (criterion 16); design docs 03, 17, 20; boundary test (g) changed from "absent" to "only in the `xd-exec` block".
- Out: any broker call (P12-02).

## Acceptance criteria
As the criteria cited above in P12-01; plus `scripts/check.sh` green and a reviewer pass.
