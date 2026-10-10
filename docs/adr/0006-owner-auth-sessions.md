# 0006 — Owner authentication: server-side cookie sessions, PBKDF2, derived CSRF token

- **Status:** Accepted
- **Date:** 2026-10-10

## Context
xDrishti is a personal system with exactly one user. Doc 04 §2 described login "with ASP.NET Core Identity password hashing, a secure cookie and recovery codes".
Implementing P1-01 (spec [P1-01](../specs/P1-01-password-login-session-lockout-idle-timeout-audit.md)) showed that a smaller, stricter design fits better.

## Decision
- **One fixed owner account**, provisioned by `xd-migrate` from file secrets that come from the macOS Keychain (ADR 0003). No sign-up, no second account, no recovery codes or e-mail reset
  (the owner re-reads the Keychain with `xd-api owner-reset` / `deploy/xd-up.sh --owner-reset`, which also clears a lock and revokes all sessions).
- **Server-side sessions, not JWT:** an opaque 256-bit random token in an `HttpOnly`, `SameSite=Strict` cookie (`Secure` over HTTPS); only its SHA-256 is stored. Sessions are revocable, expire after
  30 minutes idle (sliding, `last_seen` written at most once a minute) or 12 hours absolute, and a new token is minted on every login (no fixation).
- **Password hashing:** PBKDF2-HMAC-SHA512, 210,000 iterations, 16-byte random salt, fixed-time compare, parameters stored with the hash so cost can be raised and hashes upgraded on login.
  It is a thin wrapper over `Rfc2898DeriveBytes` behind an `IPasswordHasher` port rather than ASP.NET Core Identity's hasher: the Infrastructure layer may not reference `Microsoft.AspNetCore.*`
  (architecture test), and no extra package is needed. Unknown e-mails run an equal-cost dummy hash.
- **CSRF:** mutations need the `X-CSRF-Token` header; the token is `HMAC-SHA256(session token, "xd-csrf-v1")`, so it can be returned again after a page reload without being stored in clear. SameSite=Strict and the absence of CORS are the first line of defence.
- **Closed by default:** a fallback authorization policy requires a session for every endpoint, including OpenAPI and the API docs and unknown `/api/*` paths (401, not 404). Only `/health/*`, `POST /api/auth/login` and `GET /api/auth/session` are anonymous.
  The proxy protects the static docs (`/prototype/`, `/docs/`) with Caddy `forward_auth` → `GET /api/auth/verify`, keeps them outside the SPA root, refuses lookalike paths, and sends a Content-Security-Policy.
- **Brute-force protection:** 5 consecutive failures lock the account for 15 minutes (the failure counter is concurrency-safe), plus a rate limit of 10 attempts/minute per client address.
  Client addresses come from `X-Forwarded-For` only when the peer is the trusted proxy.
- **Audit:** every sign-in event is appended to `auth.audit_events` (the runtime role cannot update or delete rows); logs carry sanitised e-mails only, never passwords or tokens.

## Consequences
- **Accepted trade-offs:** the lock is also a denial-of-service lever — anyone who knows the owner's e-mail can keep the owner locked out with 5 requests per 15 minutes — and the 5th failure reveals that the e-mail exists.
  Both follow from the required `423 auth.locked` response; the owner always has `owner-reset` from the Keychain, and a per-(e-mail, address) counter can replace the global one if this ever matters.
- The supplied password is short; before the system is reachable beyond localhost (OPS-04), change it with `owner-reset`.
- The owner secret files exist in the private secrets folder only while `xd-up.sh` runs (emptied afterwards).
- Password re-entry for sensitive actions is P1-02; TLS and LAN exposure are OPS-04; a second factor stays out of scope (doc 01).
