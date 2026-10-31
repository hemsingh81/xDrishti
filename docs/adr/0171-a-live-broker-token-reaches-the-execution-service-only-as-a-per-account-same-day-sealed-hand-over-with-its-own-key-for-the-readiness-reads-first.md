# 0171 - A live broker token reaches the execution service only as a per-account, same-day, sealed hand-over with its own key, for the readiness reads first

- **Status:** Proposed (owner decision needed; nothing is built from this ADR until it is Accepted).
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

## Decision (proposed): B, in two steps
1. **Readiness only first.** The new token source is registered for `IBrokerFacts` only and only in `Environment=live` when `Execution:ReadinessLiveEnabled` is true (default false). It is not an `IExecCredentials` for the order client. The order client keeps refusing live (ADR 0157). A later live-send ADR decides whether the same source may feed it.
2. **Account-scoped facts.** `IBrokerFacts` gains an account argument, so one account's token is never used for another, and the sandbox-applies-to-all-accounts caveat of ADR 0170 is removed for live. Sandbox readiness rows get an environment column so they cannot read as live evidence.
3. **Smallest secret exposure.** The exec key is separate from the vault key and rotates on its own; only the access token is sealed, never the API key, secret or any PIN. Rows older than two days are not read (the API may prune them; the app never deletes evidence tables otherwise, so the cleanup is a separate decision).
4. **Audit.** Reading a handed-over token writes no token, id or client id anywhere; one log line per read with the account slug and the reason code. The read-call audit gap of ADR 0170 (reads not in `execution.broker_calls`) is closed in the same slice by widening that table with a `read` phase.
5. **Grants and tests.** A new grant decision in `ExecGrantsTests` (INSERT-only for `xd_app`, SELECT for `xd_exec`, no UPDATE/DELETE for anyone, the table in the insert-only list), a test that `xd_exec` still cannot read `broker_secrets`, a test that an expired or undecryptable row is `token_unavailable`, a test that the feed and worker containers do not get the exec key.

## Consequences
- Live readiness becomes real for a connected account, so the morning checklist, the halt auto-clear and the phase check can reach READY / MET on real data.
- One new secret file, one new table and one migration; `xd-up.sh` creates the key in the Keychain flow like the vault key.
- A compromise of `xd-exec` exposes today's access tokens of connected accounts (24 h), not the long-lived API credentials. Because the exec process has no live sender, it still cannot place an order.
- The step that would let the exec process use this token to send is a separate decision and is not taken here.

## Owner decision needed
Accept B (two-step), choose C (manual attestation only), or ask for D. If B: say whether the exec key may live in the same Keychain folder as the vault key, and whether rows should be pruned after two days.
