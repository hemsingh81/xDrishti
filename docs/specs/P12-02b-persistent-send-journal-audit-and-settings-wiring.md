# P12-02b - The persistent send journal, the broker-call audit, the settings wiring and the read calls

- **Status:** Approved (2026-10-29, the owner's instruction to build P12; the pieces P12-02 moved out, ADR 0157 sections 9, 11, 12) - built 2026-10-29, not committed
- **Phase / roadmap row:** P12 - Execution service, slice P12-02b of [P12-00](P12-00-execution-service-plan.md)
- **Needs:** P12-01b, P12-02 (built). Gate: none to build. **Hard limits of the build:** no request to Dhan (sandbox or live) is made by any test or by the agent, no credential or Keychain entry exists, no container is started.
- **Safety:** yes. The send path stays unreachable by default; live stays refused everywhere.

## Goal
Make the send protocol of P12-02 honest end to end without enabling it: a committed write-ahead and an insert-only audit in the database, day and minute counts that survive a restart, the settings of P12-02 actually read on every send, the stored-token expiry check, the raw adapter hidden behind `LegSender`, the four read calls, and a stateful `FakeBroker` with a contract table.

## Scope
- Migration `AddExecutionBrokerLog`: table `execution.broker_calls` (insert-only by trigger for every role, codes only), grants for `xd_exec` (INSERT, SELECT on it; SELECT on three columns of `accounts.broker_connections`), revokes for `xd_app` (SELECT only). `accounts.broker_secrets` is **not** granted.
- `PgSendJournal` (`ISendJournal` + `ISendHistory`): write-ahead of the leg row and the `begin` audit row in one committed transaction, outcome and drop rows, `BeginsSinceAsync` for the counts.
- `PgTokenGate` + `TokenGate` (pure rule): connection must be `Connected`, with an expiry further than `exec.token_min_remaining_min`.
- `ExecSettingsCache` / `ISendSettings`: fed by the heartbeat service; `LegSender` reads caps, `exec.order_valid_for_s`, `exec.exit_collar_pct`, token margin; `DhanOrderClient` reads `exec.send_timeout_s` per write.
- Composition: only `LegSender` (+ `LegLookup`, + a read-only `IBrokerReads`) is registered, never `IBrokerOrders`; and only when `Execution:Environment` is exactly `sandbox` **and** `Execution:SendEnabled` (default OFF) is true.
- Reads: `GET /v2/orders`, `/v2/trades`, `/v2/positions`, `/v2/fundlimit` (guard rows, tolerant parsers, typed views; an unreadable answer is never an empty list).
- Stateful `FakeBroker` (statuses, price path, partial fills, modification cap 25, expired token, 10/s broker limit, end of day) and `BrokerContractTests`.
- Doc 17 section 3 egress table for `xd-exec`.
- Out: any call to Dhan; the explicit sandbox smoke (it would call Dhan: the owner runs it); the other reads (profile, static IP, kill switch, margin), super orders, the order-update socket; leg and intent state events from the send (P12-04 owns the state moves); any xd-api endpoint or frontend change.

## Acceptance criteria
1. **Audit table.** `execution.broker_calls` accepts only rows of phase `begin` (no outcome, no reason), `dropped` (a reason) and `outcome` (an outcome); environment only `sandbox`; reason only `^[a-z0-9_.:]{1,64}$`; broker order id only a plain token; UPDATE, DELETE and TRUNCATE are refused for the owner (trigger) and for `xd_exec` and `xd_app` (no privilege). *`ExecBrokerLogTests`, `ExecGrantsTests`.*
2. **No payload.** The journal stores a reason only when it already is a short code, else the fixed code `reason_invalid`; a broker order id only when it is a plain token. A scan of the table finds no token, header or client id. *`ExecBrokerLogTests`.*
3. **Write-ahead.** `BeginAsync` commits the leg row and the `begin` row together before it returns; the broker is called only after that (a spy reads the table at the moment of the call); a second begin of the same client id throws and stores nothing; a begin for a wrong account throws. *same.*
4. **Restart-proof counts.** After a "restart" (new journal, new sender, empty limiter) the day cap still holds from the stored `begin` rows and nothing reaches the broker; a history that cannot be read refuses the send (`send_history_unavailable`) and is tried again. *`ExecBrokerLogTests`, `GuardedSenderTests`.*
5. **Settings read.** The per-second, minute and day caps, the validity window, the collar and the token margin are those of the settings in force; with none in force (never read, or last read older than 60 s) every send is refused `settings_unavailable`. An exit or flatten needs a reference price within `exec.exit_collar_pct`. *`GuardedSenderTests`.*
6. **Token.** `token_unavailable` (no row, not Connected, no expiry, read fails), `token_expired`, `token_expiring`; refusals come before any record. *`GuardedSenderTests`, `ExecBrokerLogTests`.*
7. **Grants.** `xd_exec` reads exactly `account_id, status, expires_at` of the connections and nothing of the vault (every other column, every write: 42501). *`ExecGrantsTests`.*
8. **Composition.** Without the second switch, or outside sandbox, nothing is registered; with both, `IBrokerOrders` and `DhanOrderClient` are not services and the sender cannot be built without the persistent journal; the sender is never built by the host itself. *`DhanHostWiringTests`, `ExecutionBoundaryArchitectureTests`.*
9. **Reads.** The four GETs pass the guard and nothing else new does; parsers ignore unknown fields and the case of names, handle `availabelBalance`, and make one bad row an unreadable view. *`ExecutionEndpointsTests`, `DhanReadsTests`.*
10. **Fake broker and contract.** `FakeBrokerStatefulTests` incl. a 500-seed property (no client id accepted twice, fills never above the quantity); `BrokerContractTests` run against the fake.
11. `scripts/check.sh` green.

## Amendments at build (ADR 0158)
- Decided and built as above. `accounts.broker_secrets` is deliberately **not** granted (ADR 0158 section 3).
- Not built: the sandbox smoke (hard limit), the reads other than the four, leg events from the sender.
- Never verified against real Dhan: see ADR 0158 section 8.
