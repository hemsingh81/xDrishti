# 0008 — Broker credentials in an encrypted vault; broker-first accounts; connecting Dhan and Upstox

- **Status:** Accepted (owner, 2026-10-11); narrowed to Dhan + consent flow only by [ADR 0009](0009-dhan-consent-flow-only.md) — supersedes decision 1 of [ADR 0007](0007-accounts-credential-references-and-step-up.md) (credential references); decisions 2–4 of 0007 stay.
- **Date:** 2026-10-11

## Context
ADR 0007 kept broker secrets out of the application: the owner stored client id / API key / API secret in the Keychain with a script and the API only saw
read-only files. Reading the brokers' own authentication documentation changed the picture:

- **Dhan** ([docs](https://docs.dhanhq.co/api/v2/guides/authentication)) — individuals get an access token either from Dhan Web (24 h, renewable with
  `GET /v2/RenewToken` while active) or with **API key & secret** (valid 12 months, created on web.dhan.co with a *Redirect URL*): `POST auth.dhan.co/app/generate-consent`
  (headers `app_id`, `app_secret`, query `client_id`) → browser login `auth.dhan.co/login/consentApp-login?consentAppId=…` (2FA) → redirect `?tokenId=…` →
  `POST auth.dhan.co/app/consumeApp-consent?tokenId=…` → `accessToken`, `expiryTime`, `dhanClientId`, `dhanClientName`, `dhanClientUcc`. 25 consents/day; static IP only for orders.
- **Upstox** ([docs](https://upstox.com/developer/api-documentation/authentication)) — app created at account.upstox.com/developer/apps with API key, API secret and an exact
  *Redirect URI*: browser `api.upstox.com/v2/login/authorization/dialog?client_id&redirect_uri&response_type=code&state` → redirect `?code=&state=` (single use) →
  `POST /v2/login/authorization/token` → `access_token` (valid until **03:30 IST the next day**), `extended_token`, `user_id` (UCC), `user_name`, `exchanges`, `products`, `is_active`.
  A token can also be generated manually in the app console.

Both flows need the **server** to hold the API secret and to **store a daily access token** — and containers cannot write to the macOS Keychain. The owner also asked that the
Accounts page start with the broker and follow each broker's documented flow, with the secrets entered in the app.

## Decision
1. **Encrypted broker vault.** Broker secrets (API key, API secret, access token, Upstox extended token) are entered in the app, **write-only**, and stored in
   `accounts.broker_secrets` encrypted with **AES-256-GCM**; the associated data binds each ciphertext to its account and secret name, and a key version allows rotation. The key comes
   from the Keychain (`xdrishti/broker/vault-key`, created by `deploy/xd-up.sh` on first run) as the secret file `BrokerVault__Key`, mounted into `xd-api` only. The API never returns,
   logs or audits a secret value — responses say only *set / not set* and when it was last changed. The client id / UCC is not a secret (stored plain, shown masked).
   `scripts/broker-secrets.sh`, `brokers.index` and the read-only `brokers/` mount are removed.
2. **Broker profiles.** Each broker is a profile in code (Dhan, Upstox) that declares its fields, setup steps, redirect path and token rules. The add-account wizard starts with
   **choose broker**, then shows only that broker's fields and instructions. A new broker = one profile + one connector, nothing else changes.
3. **Connecting** follows each broker's documented individual-user flow:
   - *Primary:* **browser login** — Dhan API-key consent flow; Upstox authorization-code flow. `POST /api/accounts/{id}/connect` (step-up) prepares the login and returns the broker URL;
     the broker redirects the owner's own browser to `http://localhost:8080/api/brokers/{broker}/callback`, which (signed-in session + a one-time pending-connect record, 10 minutes,
     `state` for Upstox) exchanges the code/tokenId server-side, stores the token encrypted and redirects back to the account.
   - *Alternative:* **paste an access token** generated on Dhan Web / the Upstox console; it is verified with the broker's profile endpoint before it is stored. Dhan Web tokens can be
     renewed with `RenewToken` while active ("Renew" action; unattended renewal belongs to the services work, P1-12).
   - *Session cookie `SameSite=Lax`* (amends ADR 0006, which had `Strict`): the broker's redirect to the callback is a top-level cross-site
     navigation, and a `Strict` cookie is not sent with it, so the callback could not find the signed-in session. `Lax` still keeps the cookie off
     cross-site POST/PUT/DELETE and sub-requests; every state-changing request also needs the `X-CSRF-Token` header, and no GET endpoint changes
     anything except the one-time, session-bound callbacks.
   - *Login-CSRF:* Upstox's callback must carry the session-bound `state`. Dhan's redirect carries no state, so for Dhan the defence is that a `tokenId`
     only works with the owner's own app key/secret and the returned `dhanClientId` must equal the account's client id; a forged callback can at most use up
     the open sign-in (the owner presses *Connect* again), never store a token.
   - Not now: TOTP + PIN (would store the trading PIN and TOTP seed — separate decision), Upstox notifier webhook (needs a public URL; the stack is localhost-only).
4. **Outbound network.** `xd-api` joins a new `egress` network (outbound only, no published ports) so it can reach `auth.dhan.co`, `api.dhan.co` and `api.upstox.com`. Only the auth and
   profile endpoints are called in this slice; no order, position or funds endpoint.
5. **Upstox enabled for accounts** (connect + profile). Upstox market data and portfolio sync remain later work (doc 05).

## Consequences
- The owner configures everything in the browser; one redirect URL per broker has to be registered once in the broker's developer console (shown by the wizard).
- Compromising the database alone yields no usable secret; compromising the database **and** the vault key does. The key lives only in the Keychain and the private secrets folder.
- A daily login (Dhan 24 h, Upstox until 03:30) is still needed until unattended renewal is decided; the account shows token status and expiry and offers *Connect again*.
- The API now has internet egress; this is limited to the API container and recorded in doc 17. In the API, the broker HTTP clients can reach
  only the auth, profile, renew and logout endpoints (allow-list), with a 15-second timeout, no retries, no redirects and no request logging.
- Implemented in [P1-16c](../specs/P1-16c-connect-dhan-and-upstox.md): `accounts.broker_connections` (status, expiry, holder) and
  `accounts.pending_connects` (one-time sign-ins, also Dhan's daily consent count); token flows per broker in doc 05 §8.
