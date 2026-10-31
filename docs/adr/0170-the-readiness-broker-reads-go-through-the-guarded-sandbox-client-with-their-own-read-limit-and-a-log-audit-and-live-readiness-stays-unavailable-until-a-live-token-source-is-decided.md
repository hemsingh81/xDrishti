# 0170 - The readiness broker reads go through the guarded sandbox client with their own read limit and a log audit, and live readiness stays unavailable until a live token source is decided

- **Status:** Accepted (decided under the owner's standing run instruction; specs [P12-11](../specs/P12-11-daily-login-token-and-static-ip-operations.md) criterion 13 and [P12-02](../specs/P12-02-dhan-order-adapter-and-sandbox.md) criteria 1, 9, 10, 11).
- **Date:** 2026-10-31.
- **Relation to other ADRs:** closes the "Not built, owed" adapter of ADR 0164; builds on ADR 0157 (one guard table, sandbox only) and ADR 0158 (two switches, no vault access for `xd_exec`, the send audit). No order path, no migration, no grant, no API shape and no deploy file changes.

## Context
`IBrokerFacts` had only the closed default, so every readiness verdict was `unavailable`. The parsers for the profile and `getIP` existed; the guard rows and the HTTP calls did not.

## Decision
1. **Two guard rows, GET only.** `GET /v2/profile` and `GET /v2/ip/getIP` join the table in `ExecutionEndpoints` (sandbox host only; no query, no id). `setIP`, `modifyIP`, `RenewToken` and every other verb on those paths stay refused.
2. **One client, one thin port.** `DhanOrderClient` gains two internal reads on its existing GET path (read timeout, one retry after 250 ms, guard handler, token from `IExecCredentials` in the `access-token` header only). `DhanBrokerFacts : IBrokerFacts` holds the client privately, so the port cannot be cast to an order, trade, position or funds call.
3. **Fail closed, short codes.** Missing credentials: `execution.token_unavailable`, no request. HTTP 401 or a 4xx with DH-901: `token_invalid` at once, no retry (readiness reads it as `token_rejected`, not ready). 5xx or a fault twice: `read_failed`. A body that is not the expected JSON object: `profile_unreadable` or `ip_unreadable`. A profile without a readable `tokenValidity` is read but has no validity (readiness: unavailable). The 401/DH-901 rule applies to the four existing reads too, which now agree with the `FakeBroker` (`token_invalid`) instead of retrying a refused token.
4. **Own read limit.** The facts adapter admits at most 5 requests a second (each retry counts) through its own `ExecRateLimiter`, apart from the send limiter; a refused read is `read_rate_limited` with no request.
5. **Audit in the log, not in `broker_calls`.** Each facts request and each local refusal writes log event 6616 with the call, `GET`, the path template from the guard table, the HTTP status (0 when there was no answer) and the reason code; never a host, query, id, body, header, token, client id or address. `execution.broker_calls` is not used: its constraints admit only `orders_post` rows of a send (`begin`, `dropped`, `outcome`) for one account, and the send limiter counts every `begin` row, so read rows there would need a migration and would eat the send budget. P12-02 criterion 9's database audit of reads remains unbuilt.
6. **Composed only where a token can exist.** `DhanBrokerFacts` is registered inside the sandbox composition (environment `sandbox` and `Execution:SendEnabled` true, both default off), reading the owner's sandbox secret files. Everywhere else `ClosedBrokerFacts` stays (`no_broker`). Live has no token source for `xd_exec` (ADR 0158 section 3: no vault access, no key); none is invented here.

## Known limits
- Live readiness stays `unavailable` until the owner decides how a live token reaches `xd-exec` (a live ADR). This is the gate on any live readiness.
- `IBrokerFacts` has no account parameter; the sandbox reads are those of the one sandbox credential and the loop applies them to every account in assisted or above. A sandbox process should therefore not run against accounts whose readiness rows are evidence for live (the rows carry no environment column).
- The profile and `getIP` field names and the sandbox serving those paths are UNVERIFIED until the owner's sandbox smoke.
