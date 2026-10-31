# 0171 - A live broker token reaches the execution service only as a per-account, same-day, sealed hand-over with its own key, for the readiness reads first

- **Status:** Accepted in part (owner approved option B on 2026-10-31, after due diligence by an independent review against the code, which corrected the first draft). The sealed hand-over (steps 1 to 4 below) may be built. Reading from the broker with a live token is blocked on a separate egress decision (step 5) that is still open.
- **Date:** 2026-10-31.
- **Relation to other ADRs:** closes the open point of ADR 0158 §3 ("a later live ADR decides how the live token reaches the process") and of ADR 0170 (live readiness stays unavailable). Builds on ADR 0008 and 0009 (the consent flow is the only login), ADR 0156 (`xd_exec` is the only writer of `execution`), ADR 0164 (readiness).

## Context
Live readiness is `unavailable` for every account, because `xd-exec` cannot get a live token: the vault (`accounts.broker_secrets`) is ciphertext, `xd_exec` holds no vault key and has no grant on the table (ADR 0158 §3). The Dhan token lasts 24 hours and is created only by the owner's daily login in the API. Four facts shape the choice:
1. The vault key opens every secret of every account (API key, secret, PIN-like values), not only the token.
2. `xd-exec` is the process that will, in time, hold the power to place orders. It should know as little as possible.
3. Dhan has one token per account; there is no read-only token. Whatever lets us call `profile` and `getIP` can also place an order if a sender exists. The guard table and the absence of a live sender are what stop that, not the token.
4. A hand-over that is wrong must fail closed: no token means unavailable, never a guess.

## Options
- **A. Give `xd-exec` the vault key and SELECT on `broker_secrets`.** Smallest code. Widest blast radius: a bug or a compromise of the exec process opens every secret of every account. Rejected.
- **B. A sealed same-day hand-over (recommended).** At login the API, which already holds the token, also seals it with a second key (`ExecHandover__Key`, a new secret file given to `xd-api` and `xd-exec` only, not to feed or worker) and inserts a row into a new table `execution.token_handover` (`account_id`, `sealed_token`, `key_version`, `expires_at`, `created_at`). `xd_app` gets INSERT only. `xd_exec` gets SELECT of the newest unexpired row per account and nothing on `broker_secrets`. A row is never updated; a disconnect or a new login adds a row or an expiry marker. `expires_at` is the connection's own expiry. A token past expiry, an undecryptable row or a missing row is `token_unavailable`.
- **C. Keep live readiness `unavailable` and make the owner attest the morning by hand.** No new secret path. Readiness stays honest but permanently manual, and the phase check (ADR 0169) can never count a session as proved. Acceptable as a fallback if B is declined.
- **D. The API proxies the two reads for `xd-exec`.** No token leaves the API. Adds a network path and a dependency of the execution service on the API being up at 08:00; weaker than B for the thing readiness must prove.

## Due diligence (2026-10-31, read-only review against the code)
Confirmed: the API holds the plaintext token only inside one consent request, where `CompleteConnect` stores the vault row and the connection in one unit of work (so a hand-over row can commit atomically with them); `Disconnect` and `ClearAsync` delete the vault token in the same unit of work; `BrokerSecretCipher` can be reused with another key; the vault key goes only to `xd-api` and `xd-feed` (ADR 0024), so `xd-feed` and `xd-worker` can be excluded from a new key; `xd_exec` has no access to `broker_secrets` and reads three columns of `broker_connections`.
Corrected from the first draft: (a) the table does not go in schema `execution` (a credential is not evidence, and the schema's default privileges would give `xd_app` UPDATE and DELETE); (b) it is one row per account, not append-only, because Disconnect deletes the vault token today and an append-only row would leave a valid token readable after a disconnect; (c) no copied `expires_at`; (d) the compose rules and grant tests must be rewritten; (e) `xd-exec` has no route to Dhan.

## Decision
1. **The table is `accounts.exec_token_handover`**, one row per account (`account_id` key, `sealed_token`, `key_version`, `created_at`). `xd_app` keeps its default rights on it; `xd_exec` gets `SELECT` on this table only; no other role. ADR 0156 §3 gains one line: `xd_exec` also reads this table.
2. **Written and removed with the connection.** `ConnectionWriter` (through a new Application port `IExecTokenHandover`, implemented in Infrastructure) upserts the row in the same unit of work as the vault row and the connection at consent; `Disconnect`, `ClearAsync` and a new login replace or delete it in the same unit of work. If the write fails the whole connect fails; there is no connect without a hand-over.
3. **Its own key.** `ExecHandover__Key` is a new secret file (Keychain `xdrishti/exec/handover-key`, created by `xd-up.sh` like the vault key), mounted into `xd-api` and `xd-exec` only. The cipher gets a purpose parameter (own salt and info) and the associated data binds account and name, so a row cannot be moved to another account. The decrypt half is a small public type shared with `XDrishti.Execution`, which does not reference Infrastructure. Only the access token is sealed.
4. **Freshness comes from the connection.** `xd_exec` uses the token only when `broker_connections.status` is Connected and `expires_at` is in the future, and the row decrypts; otherwise the answer is `token_unavailable`. A key rotation makes every row unreadable until the next login (acceptable for a 24-hour token). Tests: the grants table, `xd_exec` still refused on `broker_secrets`, an expired or undecryptable row is unavailable, a row cannot decrypt for another account, the feed and worker lack the key, and `ExecComposeRules` (`one-secret`, `secret-holders`) is rewritten for two secrets.
5. **Prerequisite, not decided here: egress.** `xd-exec` is on the `exec` network, which is `internal` with only `xd-db` and `xd-exec`, and `ExecComposeRules` pins "no route out". Even with a token, the live reads cannot run. Giving `xd-exec` a route to the live Dhan host (pinned by name) is a security change of its own and needs its own ADR and the owner's explicit decision. Until then the hand-over can be built and tested but the live readiness stays `unavailable`.
6. **Readiness only.** The token source feeds `IBrokerFacts` only, behind `Execution:ReadinessLiveEnabled` (default false) in `Environment=live`. It is not an `IExecCredentials` for the order client, which keeps refusing live (ADR 0157). `IBrokerFacts` becomes account-scoped, which removes the sandbox-applies-to-all-accounts caveat of ADR 0170 for live.
7. **Split out.** The `read` phase of `broker_calls` (it needs a new constraint, and the send limiter must ignore it) and the environment column on `readiness_checks` are separate slices, not part of this one.

## Consequences
- After the egress decision, live readiness becomes real for a connected account, and the checklist, halt auto-clear and phase check can reach READY or MET on real data.
- One secret file, one table, one migration; a compromise of `xd-exec` exposes the day's tokens of connected accounts (24 hours), not the long-lived credentials, and it still has no live sender.
- The vault key is already held by two services, so option A would have added one more holder; B still keeps the exec process away from every long-lived secret.

## Known limits (build review, 2026-10-31)
- A token that merely expires stays sealed at rest until the next login, disconnect or archive; the reader refuses it (status Connected and `expires_at` in the future), so it is not usable, only present.
- `SetAsync` is find-then-add like the vault's; two simultaneous logins of one account could collide on the key, and the second save fails whole (the connect fails, nothing half-written).
- Archive also deletes the hand-over. `xd-exec` starts without the key and fails every lookup closed with one warning.
- Minor cleanups left: `LiveBrokerFacts` has a dead `enabled` branch and an unreachable `no_live_route` arm until the live guard rows exist (resolved by ADR 0172: `no_live_route` now means no proxy configured).

## Owner decisions still open
1. Egress for `xd-exec` to the live Dhan host (step 5), as its own ADR.
2. Whether the hand-over may later feed the order client (a live-send ADR; not decided here).
