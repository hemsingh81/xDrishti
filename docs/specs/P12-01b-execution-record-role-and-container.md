# P12-01b - The execution record, the `xd_exec` role and the `xd-exec` container

- **Status:** Approved (2026-10-29, the owner's instruction to build P12; amended at build, ADR 0156) - built 2026-10-29, not committed
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

## Amendments at build (2026-10-29, ADR 0156)
- **Tables:** only the nine of Scope are created; the other tables named in P12-01 criterion 10 (`broker_calls`, `check_runs`, `alarms`, `halts`, `drills`, recon, owner-act tables) belong to their slices. `ExecGrantsTests` lists the nine and fails on any other.
- **No EF mapping:** the tables are raw SQL in the migration and plain Npgsql in the service; `xd-api` reads with one query. `XDrishti.Execution` references Npgsql (Persistence only); the boundary test allows exactly that.
- **Grants decided against the spec:** `accounts.broker_secrets` and `accounts.broker_connections` are not granted to `xd_exec` now (no reader before P12-02); P12-02 adds them.
- **Projection:** `intent_state` (+ `filled_qty`) is moved by trigger from the event; `legs`, `fills`, `positions` have no writer until P12-02, so criterion 8 is proved for `intent_state` only (200 random intents, in C# and in SQL).
- **Command plane:** 500 ms poll, no `LISTEN`; the only known kind is `ping`; no endpoint writes a command here.
- **Status:** adds `entriesBlocked` (always true until P12-05/06); gates all closed (`no_mode_answer`); `down` also for an unknown state word.
- **Deploy:** `xd-migrate` also mounts `db_exec_password` (to set the login), so boundary test (g) is "xd-migrate and xd-exec" for the file, "xd-exec" for the role name, "xd-db and xd-exec" for the network; `xd-down.sh` includes the profile; the next `xd-up.sh` recreates `xd-db` (new network): a brief whole-stack outage, the named volume persists.
- **Review fixes:** a trigger lets only the event triggers write `intent_state`; a row guard limits `xd_exec` to its own heartbeat row; an absent/empty password file sets the role NOLOGIN (fail closed); the health probe rejects a future-dated heartbeat; `Down` leaves the role by intent (ADR 0156).
- **Not done by the agent (owner acts):** `xd-up.sh --exec` (creates the Keychain item and starts the container), setting `XD_EXEC_ENVIRONMENT`, committing.
