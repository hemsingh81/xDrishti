# 0155 - The execution service starts as a pure skeleton: the order state machine, closed ports and an in-memory fake broker, with no network code, and the database, role and container come in a second part

- **Status:** Accepted (decided in P12-01 under the owner's instruction; spec [P12-01](../specs/P12-01-xd-exec-skeleton-and-order-state-machine.md)).
- **Date:** 2026-10-29.
- **Relation to other ADRs:** builds on ADR 0083 (insert-only trails, no order by name) and ADR 0097 (the Dhan data allow-list, unchanged); the tripwire of P7-03 is unchanged.

## Context
P12 puts the first order-capable code into the repository. The owner asked for the skeleton and the pure state machine first, with no real order call written, wired or reachable.

## Decision
1. New project `XDrishti.Execution` (Worker SDK). It references only `XDrishti.Domain` and the hosting packages. No Infrastructure, no ASP.NET Core, no HTTP client, no Npgsql or EF Core, no broker library. Nothing references it except `XDrishti.Execution.Tests` and `XDrishti.Architecture.Tests`.
2. Pure core: the 14-state machine with exactly 23 legal moves, the intent fold (`Apply` returns a new intent or a refusal), leg status rank, the idempotent client order id, the account gate (`AccountGate.MayAct`; no answer, switch off, a non-executing mode or environment `none` all refuse), the position book (a short is flagged, never clamped), the refused actions (stop loosen, add to position, re-entry), the settings registry and reader, and the halt reading (`Unreadable` means halted).
3. Ports with closed defaults: `IModeSource` answers nothing, `IHaltSource` answers `Unreadable`. `IBrokerOrders` is the only interface that may name an order action; its only implementation is `FakeBroker`, in memory, and it is not registered in the host.
4. The host defaults to environment `none`, enters `recovering`, builds no broker client and registers no HTTP client.
5. Split: schema `execution`, role `xd_exec`, migration, grants tests, command poller, heartbeat, status endpoint, container, network and secret are P12-01b. Nothing in `deploy/` or `xd-api` changes in this slice, so the allow-list still refuses every order write.
6. `ExecutionBoundaryArchitectureTests` is the one place the exemption is written: the old tripwire file is not edited, order verbs only in the `Dhan` namespace or `IBrokerOrders`, only two test projects reference the project, the four order-write literals appear in source only in the planned adapter file (plus the P11-01 order-book read files for the GET on `/v2/orders`), no HTTP surface, no HTTP client, no clock or file or network or database in Domain and Application, and no execution container, role or secret in compose yet.
7. The settings registry stays in `XDrishti.Execution.Domain` until `xd-api` needs it (P12-07).

## Consequences
The state machine and its rules are proved by property tests before any storage or broker exists. A later slice can add the adapter only inside `XDrishti.Execution.Dhan` and the tripwire still protects every other project. The cost of the split is a second, small slice before the service can run.
