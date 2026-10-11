# P1-16c — Connect an account: Dhan consent login, Upstox authorization code, pasted token

- **Status:** Approved (owner, 2026-10-11 — "configure + connect", "token methods as per the documentation")
- **Status note:** narrowed by [P1-16d](P1-16d-dhan-consent-only-and-header-accounts.md) (ADR 0009): Dhan only, consent flow only; Upstox, pasted/renewed/tested tokens and the broker catalogue are removed.
- **Phase / roadmap row:** P1 — pulls the authentication part of P1-04 (broker adapter) forward, for Dhan and Upstox · decision [ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md) · after [P1-16b](P1-16b-broker-first-accounts-and-credential-vault.md)
- **Design refs:** docs/design/04 §1; 05 §7/§8; 17; Dhan: [auth guide](https://docs.dhanhq.co/api/v2/guides/authentication), generate-consent, consentApp-login, consumeApp-consent,
  RenewToken, `/v2/profile`; Upstox: [authentication](https://upstox.com/developer/api-documentation/authentication), authorize, get-token, `/v2/user/profile`, logout

## Goal
From an account, the owner presses **Connect**, logs in on the broker's own page (with the broker's 2FA), and comes back to xDrishti with the account connected: token status,
expiry and the broker's name/UCC for the account are shown, and the token is stored encrypted for later services.

## Flows (as documented by the brokers)
**Dhan — API key & secret (primary)**
1. `POST /api/accounts/{id}/connect` (step-up) → server calls `POST https://auth.dhan.co/app/generate-consent?client_id={clientId}` with headers `app_id`, `app_secret` → `consentAppId`;
   stores a pending connect (account, session, broker, created, expires +10 min) → returns `{authorizeUrl: "https://auth.dhan.co/login/consentApp-login?consentAppId=…"}`.
2. Browser goes there (same tab); owner logs in with 2FA; Dhan redirects to `{PublicBaseUrl}/api/brokers/dhan/callback?tokenId=…`.
3. Callback (requires the signed-in session; finds this session's single open Dhan pending connect) → `POST https://auth.dhan.co/app/consumeApp-consent?tokenId=…` with `app_id`,
   `app_secret` → `accessToken`, `expiryTime`, `dhanClientId`, `dhanClientName`, `dhanClientUcc`, `givenPowerOfAttorney` → token stored encrypted; `dhanClientId` must equal the
   account's client id (else `accounts.connect_mismatch`, nothing stored) → 302 to `/accounts?account={id}&connected=1`.
4. Limit: Dhan allows 25 consents/day — the API counts per account per IST day and refuses the 26th with a clear message.

**Upstox — authorization code (primary)**
1. `POST /api/accounts/{id}/connect` (step-up) → server creates `state` (32 random bytes, base64url) in a pending connect → returns
   `{authorizeUrl: "https://api.upstox.com/v2/login/authorization/dialog?client_id=…&redirect_uri=…&response_type=code&state=…"}`.
2. Upstox redirects to `{PublicBaseUrl}/api/brokers/upstox/callback?code=…&state=…`.
3. Callback (signed-in session; `state` must match an open pending connect of this session, one-time) → `POST https://api.upstox.com/v2/login/authorization/token` (form: code,
   client_id, client_secret, redirect_uri, grant_type=authorization_code) → `access_token`, `extended_token`, `user_id`, `user_name`, `is_active` → stored encrypted; expiry =
   next 03:30 IST after issue; if the account has no client id yet, `user_id` becomes it; if it differs → `accounts.connect_mismatch` → 302 back.

**Pasted token (alternative, both brokers)** — `PUT /api/accounts/{id}/token` (step-up) `{accessToken}` → verified with the profile endpoint (Dhan `GET https://api.dhan.co/v2/profile`
header `access-token`: client id must match, expiry from `tokenValidity`; Upstox `GET https://api.upstox.com/v2/user/profile` `Authorization: Bearer`: user id must match, expiry next 03:30 IST)
→ stored with `source = pasted`.

**Renew (Dhan, pasted Dhan-Web tokens only)** — `POST /api/accounts/{id}/token/renew` (step-up) → `GET https://api.dhan.co/v2/RenewToken` (headers `access-token`, `dhanClientId`) → new token and expiry; refused for
consent-flow tokens and expired tokens with a clear message.

**Disconnect** — `DELETE /api/accounts/{id}/token` (step-up) → token deleted (Upstox: best-effort `DELETE https://api.upstox.com/v2/logout`).

**Test connection** — `POST /api/accounts/{id}/token/test` → calls the profile endpoint, updates status (`connected` / `expired` / `invalid`) and `lastCheckedAt`.

## Acceptance criteria
1. The flows above work against the real brokers (owner-run) and against fake broker servers in integration tests (stubbed `HttpMessageHandler`), including: wrong/expired code or tokenId,
   state mismatch/replay, pending connect expired, other session's callback, client-id mismatch, broker 4xx/5xx/timeouts (mapped to `accounts.broker_error` 502 with a safe message).
2. `GET /api/accounts/{id}` shows `connection {status: not_connected|connected|expired|invalid, source: login|pasted, expiresAt, brokerUserName, brokerUserId (masked), lastConnectedAt, lastCheckedAt, lastError}`;
   status flips to `expired` after `expiresAt` without any call. The list shows the token health dot from it.
3. Tokens and secrets are never in responses, logs (incl. HttpClient logging — disabled/redacted for broker clients), audit text, redirects or URLs we build except the documented
   query parameters (`consentAppId`, `client_id`, `redirect_uri`, `state`). Audit: `BrokerConnected`, `BrokerConnectFailed`, `TokenPasted`, `TokenRenewed`, `BrokerDisconnected`.
4. Callback endpoints: `GET /api/brokers/dhan/callback`, `GET /api/brokers/upstox/callback` — require the session cookie (SameSite=Lax works for the top-level redirect), are excluded from
   CSRF (GET, one-time pending record), never render broker data; always end in a 302 to the SPA with `connected=1` or `connectError=<code>`.
5. Only auth/profile/logout endpoints are reachable from the broker clients (allow-list test); no order/position/funds endpoint.
6. `xd-api` joins an `egress` network (not internal, no published ports); doc 17 updated; System status stays green.
7. **UI:** account header and Connection tab show status chip + expiry ("valid until 03:30", countdown when < 2 h); actions **Connect with Dhan/Upstox** (step-up → navigate),
   **Paste token** (dialog, masked input), **Renew** (Dhan pasted only), **Test**, **Disconnect** (confirm); after return, a toast "Connected as <name>" or the error; the wizard's last
   step offers **Create and connect**; the Overview "not connected yet" placeholders for token are replaced by the real status (funds/holdings stay "—" until P2).
8. Tests: unit (expiry rules incl. 03:30 IST edge cases, state generation/validation, mapping), integration (all flows with fakes, no-leak scan, allow-list), frontend (MSW: connect
   navigates to returned URL, return-with-connected toast, paste/renew/test/disconnect, status chips). `scripts/check.sh` green; reviewer no blockers/majors.

## Data
`accounts.broker_connections (account_id PK, status, source, expires_at, broker_user_id, broker_user_name, last_connected_at, last_checked_at, last_error)`;
`accounts.pending_connects (id, account_id, session_id, broker, state_hash, created_at, expires_at, consumed_at)`; tokens in `accounts.broker_secrets` (`access_token`, `extended_token`).

## Non-functional
Security as AC 3/5; HTTP timeouts 15 s, no retries on token exchange (codes are single-use); Polly-free (built-in resilience not needed). Observability: `[LoggerMessage]` with broker, account slug,
outcome and broker error code only.

## Done when
- [ ] All acceptance criteria have evidence (incl. one real login per broker by the owner — pending) · [x] `scripts/check.sh` green; reviewer: no blockers/majors (2026-10-11; minors fixed) · [x] Contract regenerated
- [ ] Deployed (✅ egress network, migration applied, callbacks answer) and verified in the browser (pending) · [x] Docs updated (04, 05, 17, ADR 0006/0008)
