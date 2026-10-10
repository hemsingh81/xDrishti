# P1-01 — Password login, session, lockout, idle timeout, audit

- **Status:** Approved (owner instruction 2026-10-10: "build authentication … only for private user … fixed username and password"; design defaults below are mine — object to any)
- **Phase / roadmap row:** P1 — Data platform & instruments (first story; unblocks every other screen and API)
- **Design refs:** docs/design/04-accounts-access-and-operations.md §2; 02 FR-3b, NFR-9; 03 §8; 17 §4; ADR 0002/0003

## Goal
Nobody but the owner can open any screen or call any API of xDrishti. One fixed owner account, signed in with email + password; sessions that
expire when idle; brute-force protection; every sign-in event recorded. No registration, no password-reset-by-email, no other users.

## Owner account (fixed)
- One owner: e-mail, display name and password exactly as given by the owner in chat on 2026-10-10 (deliberately not repeated here). The **values live in the macOS Keychain**
  (`xdrishti/auth/owner-email`, `owner-name`, `owner-password`) and reach the containers as file secrets (ADR 0003) — **never in the repository, images, logs or tests**.
- The owner row is **provisioned by `xd-migrate`** (after migrations) from those secrets if no owner exists; the password is stored only as a salted PBKDF2 hash.
  `xd-api owner-reset` re-reads the secrets, replaces the hash and revokes all sessions (how the password is changed later).
- No sign-up endpoint and no way to create a second account exists.

## Scope
**In:** backend (domain, handlers, persistence, endpoints, auth middleware), login page and route guard in the React app, proxy protection of the static docs,
secrets plumbing in `xd-up.sh`/compose, tests at every level, docs.
**Out (later):** password re-entry for sensitive actions (P1-02), TLS/LAN exposure (OPS-04), second factor (out of scope by design), Seq/PostgreSQL ports (stay 127.0.0.1-only).

## Acceptance criteria
1. **Everything is closed by default.** Every `/api/*` route except `POST /api/auth/login` and `GET /api/auth/session` answers `401` (problem+json) without a valid session —
   including the OpenAPI document and the API docs. `/health/live` and `/health/ready` stay public (container probes). New endpoints are protected automatically (fallback policy).
2. **Login.** `POST /api/auth/login {email, password}` with the owner's credentials returns `200 {user, csrfToken, idleTimeoutSeconds, expiresAt}` and sets an opaque session cookie
   (`xd.sid`: HttpOnly, SameSite=Strict, Path=/, `Secure` whenever the request is HTTPS). Wrong email **or** password returns the same `401 auth.invalid_credentials` (no user enumeration;
   a dummy hash is computed for unknown emails so timing does not reveal them).
3. **Session.** Sessions are server-side (revocable), stored hashed in `auth.sessions`; **idle timeout 30 min** (sliding, `last_seen` written at most once a minute) and **absolute lifetime 12 h**;
   `GET /api/auth/session` returns the current user (or 401); `POST /api/auth/logout` revokes the session and clears the cookie. A new login does not revoke other sessions (one owner, several devices).
4. **CSRF.** State-changing requests (POST/PUT/PATCH/DELETE) other than login need the `X-CSRF-Token` header equal to the session's token; otherwise `403`.
5. **Lockout and rate limit.** 5 consecutive failures lock the account for 15 minutes (`423 auth.locked` with `retryAfterSeconds`); a correct password during the lock is still refused; a success resets the counter.
   The login route is additionally rate-limited per client address (10 attempts/minute → `429`). All time rules use `TimeProvider`.
6. **Audit.** Login success, failure, lockout, logout, session expiry (idle/absolute) and owner provisioning/reset are stored in `auth.audit_events` (time, event, email attempted, client IP, user agent) —
   never passwords or tokens. The owner can read the latest 50 via `GET /api/auth/audit` (authenticated).
7. **Passwords.** PBKDF2-HMAC-SHA512, ≥ 210,000 iterations, random salt, via the framework hasher behind a port; hash upgrade on login when parameters change; the plaintext password is never logged or stored;
   the secrets files are read only at provisioning.
8. **Provisioning.** `xd-migrate` creates the owner when none exists and does nothing otherwise (idempotent); `xd-api owner-reset` updates the hash and revokes sessions; the stack still starts with one command
   (`deploy/xd-up.sh` reads/creates the Keychain items; with no TTY and no items it fails with a clear message pointing to `scripts/auth-secrets.sh`).
9. **Proxy.** The static docs served by the proxy (`/prototype/`, `/docs/`) require a valid session too (Caddy `forward_auth` → `GET /api/auth/verify`); the React app shell and its public assets stay reachable so the login page can load.
10. **Frontend.** `/login` page (centered card, UX of the prototype's login, labelled fields, show/hide password, loading and error states, locked-out message with a countdown, "Session expired" message);
    every other route is behind a guard that redirects to `/login?next=…`; the top bar shows the owner's name with a **Sign out** action; a `401` anywhere sends the user to the login page; the CSRF token is sent on mutations.
    Works on a 360 px phone and with the keyboard only; no horizontal scroll.
11. **Quality.** Unit tests (domain rules incl. lockout/idle/absolute expiry, handlers with fakes and `FakeTimeProvider`), integration tests against real PostgreSQL/TimescaleDB (all of AC 1–6, 9's endpoint), architecture tests still green,
    frontend tests (form, guard redirect, 401 handling, sign out, locked message). `scripts/gen-api.sh` run; `scripts/check.sh` green; reviewer agent: no blockers/majors.
12. **Deployed and verified.** The stack is rebuilt with `deploy/xd-up.sh`; unauthenticated requests are refused (API, docs, static docs); the owner can sign in through the browser, see the app, and sign out; System status stays green; no secret appears in logs, images or the repo.

## Contract
| Method & path | Auth | Request | Responses |
|---|---|---|---|
| `POST /api/auth/login` | anonymous (rate-limited) | `{ "email": string, "password": string }` | `200 {user:{email,displayName}, csrfToken, idleTimeoutSeconds, expiresAt}` + cookie · `401 auth.invalid_credentials` · `423 auth.locked {retryAfterSeconds}` · `429` |
| `GET /api/auth/session` | cookie (anonymous allowed) | — | `200 {user, csrfToken, idleTimeoutSeconds, expiresAt}` · `401` |
| `POST /api/auth/logout` | cookie + `X-CSRF-Token` | — | `204` (cookie cleared) |
| `GET /api/auth/verify` | cookie | — | `204` · `401` (used by the proxy) |
| `GET /api/auth/audit` | cookie | `?take=50` | `200 [{at, event, email, ip, userAgent}]` |
- **Data (schema `auth`):** `owner_accounts(id, email unique, display_name, password_hash, failed_attempts, locked_until, created_at, password_changed_at)`,
  `sessions(id, owner_id, token_hash unique, csrf_token_hash, created_at, last_seen_at, expires_at, revoked_at, ip, user_agent)`, `audit_events(id, at, event, email, ip, user_agent, detail)`;
  `xd_app` gets DML on these tables only (migration calls `GrantSchemaUsageToAppRole`). Index on `sessions(token_hash)`, `audit_events(at)`.
- **Secrets:** compose secrets `Auth__Owner__Email`, `Auth__Owner__Name`, `Auth__Owner__Password` → `xd-migrate` (and `xd-api` for `owner-reset`); written by `deploy/xd-up.sh` from the Keychain.
- **UI:** routes `/login` (public) and the existing routes behind the guard; the System status page and Today page keep working unchanged.

## Non-functional
Security: generic errors, constant-ish timing, no secrets in logs (`[LoggerMessage]` with email only), cookie flags, CSRF, rate limit, hashed session tokens at rest, least-privilege DB role.
Performance: one indexed lookup per request; `last_seen` write throttled; login hashing ~100 ms by design.
Observability: auth events are also structured log events (no secrets). Availability: the nightly workers do not use sessions (they run under the service identity).

## Test plan
- **Domain/Application unit:** lockout counter and window; success resets; locked refuses even with the right password; unknown user same result; session idle and absolute expiry; sliding window update throttle;
  provisioning idempotent; reset revokes sessions; audit events recorded; password hash upgrade.
- **API integration (Testcontainers, real DB):** 401 on protected routes (incl. OpenAPI/docs); login → cookie flags; session; CSRF 403 without header; logout invalidates; lockout after 5 failures with 423; rate limit 429; verify endpoint; audit list; health public.
- **Architecture:** layer rules unchanged. **Frontend (Vitest + MSW):** login form states, guard redirect with `next`, 401 handling, sign-out, locked countdown, accessibility roles.
- **Live (agent-run):** `deploy/xd-up.sh`; curl proves 401s; a browser sign-in/out smoke with the owner account read from the Keychain (never typed into logs).

## Decisions taken by default (change on request)
Idle 30 min / absolute 12 h; 5 failures → 15 min lock; 10 attempts/min/IP; PBKDF2-SHA512 210k (no new package); server-side cookie sessions instead of JWT; docs behind `forward_auth`;
Seq (5341) and PostgreSQL (5433) remain unauthenticated but 127.0.0.1-only; `Secure` cookie only on HTTPS (the local stack is HTTP until OPS-04). The supplied password is short for a login that will later be reachable on the LAN — consider changing it with `owner-reset` before then.

## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors · [ ] Contract regenerated · [ ] Stack redeployed and verified · [ ] Docs updated (04 §2, 17 §4; ADR if needed)
