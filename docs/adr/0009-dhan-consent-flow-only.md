# 0009 — Accounts: Dhan only, API key & secret consent flow only

- **Status:** Accepted (owner, 2026-10-11) — narrows [ADR 0008](0008-broker-credentials-vault-and-connection.md)
- **Date:** 2026-10-11

## Context
ADR 0008 built broker-first accounts for Dhan **and** Upstox with several ways to get a token (consent/authorization login, pasted token, renew, test). The owner wants
only Dhan, only the documented *API Key & Secret* flow (generate consent → browser login → consume consent), and a simpler page.

## Decision
1. **One broker, one flow.** Dhan only; the connection is made exclusively with the consent flow: `POST auth.dhan.co/app/generate-consent` → browser login
   `consentApp-login` → redirect with `tokenId` → `POST /app/consumeApp-consent`. Removed: Upstox (connector, profile, enum value, callback, expiry rule), pasting an access token,
   renewing a token, "test connection", the Upstox extended token, the token `source` (always the login), the broker catalogue endpoint and the choose-broker wizard step.
   **Kept:** connect/reconnect, disconnect (deletes the stored token), the encrypted vault for API key/secret and the access token, status and expiry (Dhan `expiryTime`), the 25-consent/day guard,
   client-id check, allow-list for the three Dhan auth endpoints.
2. **Where status lives.** The app header lists every account with its connection status and a **Connect / Reconnect** action for any account that is not connected; the Accounts page keeps
   the same action on the Connection tab (the separate banner is removed — the header replaces it).
3. **Adding a broker later** (Upstox, …) means a new decision and slice; the code keeps the `broker` column and the connector port so that is possible, but nothing unused ships now.

## Consequences
- Less code, fewer endpoints and screens; a daily consent login (Dhan token ≈ 24 h) remains the owner's routine until unattended renewal is decided (TOTP route is a separate decision).
- Existing data is empty (owner reset on 2026-10-11), so tightening constraints in a new migration is safe.
