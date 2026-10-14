# P2-11 — My account: profile, change password, sign-in activity

- **Status:** Done (owner, 2026-10-14: "option to change password, and other details" before the next story)
- **Phase / roadmap row:** P2 — follows [P1-01](P1-01-password-login-session-lockout-idle-timeout-audit.md) (auth) and [P1-21](P1-21-top-bar-search-notifications-and-avatar.md) (user menu)
- **Design refs:** docs/design/04-accounts-access-and-operations.md §2; P1-01 "Owner account (fixed)" (password so far changed only by `xd-api owner-reset`)

## Goal
The owner can change their password, display name and sign-in e-mail inside the app, and see recent sign-in activity, without touching the Keychain or the CLI.

## Scope
**In:** API (`/api/auth/account` read, profile update, change password), audit events, session revocation on password change, a `/account` page in the React app (reached from the user menu: "My account"), tests, docs, contract regeneration.
**Out:** second user, e-mail-based reset, 2FA, device list / per-session revoke, Keychain write-back (the Keychain items used at first provisioning go stale after a change; `owner-reset` still overwrites from them — documented in the page help text and doc 04).

## Acceptance criteria
1. `GET /api/auth/account` → `200 {email, displayName, createdAt, passwordChangedAt}` for the signed-in owner; 401 otherwise.
2. `POST /api/auth/change-password {currentPassword, newPassword}` (cookie + CSRF): wrong current password → `401 auth.invalid_credentials` and counts toward the same lockout counter as login/step-up (5 failures → `423 auth.locked`); correct → `204`, new hash stored (PBKDF2), `passwordChangedAt` updated, **all other sessions revoked, the current one stays signed in**, audit `PasswordChanged`. The new password must be 10–1024 chars and different from the current one → `400 auth.password_invalid` / `auth.password_unchanged` (problem+json, field-level message). Password never logged or stored in plaintext.
3. `PUT /api/auth/profile {displayName, email}` (cookie + CSRF + **step-up**): validates (non-empty name ≤ 128, e-mail with `@` ≤ 256, lower-cased); updates the owner; audit `ProfileUpdated` (detail: which fields changed, never values of passwords); other sessions stay valid; returns `200 {email, displayName, createdAt, passwordChangedAt}`. The session cache/`GET /api/auth/session` reflects the new name/e-mail on the next call.
4. `GET /api/auth/audit` (existing) is used by the page for "Recent sign-in activity" (last 10); event labels added for the new events.
5. UI `/account` (route under the guard, nav-less, opened from the user menu item "My account" and the palette): page header; three cards — **Profile** (display name, e-mail; Save → step-up dialog if needed → toast), **Change password** (current, new, confirm; show/hide; strength hint "at least 10 characters"; mismatch/unchanged errors inline; success toast "Password changed — other devices were signed out"; fields cleared; locked → countdown message as on login), **Recent sign-in activity** (time, event, IP; loading/empty/error states). Matches the prototype look (cards, spacing, type) per [P2-12](P2-12-match-prototype-spacing-fonts-colours.md); labelled fields, keyboard-only, 360 px phone without horizontal scroll.
6. The user menu shows name + e-mail, a **My account** item and **Sign out**; the top-bar avatar initials update after a profile change.
7. Tests: domain (password change, profile replace), application handlers with fakes (wrong current password → failure counter + lockout; success revokes other sessions but not the current; unchanged/short password; profile validation; audit events), API integration against real DB (401/403 step-up/CSRF, change-password happy path then old password rejected and new one accepted, other session 401, profile update reflected in `GET /api/auth/session`, lockout), frontend (forms, validation, step-up flow, success/error states, menu item). `scripts/gen-api.sh` run; `scripts/check.sh` green; reviewer: no blockers/majors.

## Contract
| Method & path | Auth | Request | Responses |
|---|---|---|---|
| `GET /api/auth/account` | cookie | — | `200 AccountResponse` · 401 |
| `POST /api/auth/change-password` | cookie + CSRF | `{currentPassword, newPassword}` | `204` · `400 auth.password_invalid`/`auth.password_unchanged` · `401 auth.invalid_credentials` · `423 auth.locked` |
| `PUT /api/auth/profile` | cookie + CSRF + step-up | `{displayName, email}` | `200 AccountResponse` · `400 auth.owner_email_invalid`/`auth.owner_name_invalid` · `403 auth.step_up_required` |
- **Data:** no new tables/columns (`owner_accounts` already has `password_changed_at`); new audit event names `PasswordChanged`, `ProfileUpdated` (stored by name; column is text → no migration expected — verify).
- **UI:** `features/auth/` gets `AccountPage`, `ProfileCard`, `ChangePasswordCard`, `SignInActivityCard`; lazy route `/account` in `app/router.tsx`; `UserMenu` item.

## Non-functional
Security: generic errors, same lockout/rate rules as login, no secrets in logs (`[LoggerMessage]`, e-mail only), password fields `autocomplete="current-password"`/`"new-password"`, no password in URL/state beyond the form. Performance: one slow hash for verification + one for hashing the new password (≈ 200 ms).

## Decisions taken by default (change on request)
Minimum 10 characters (no composition rules); password change signs out other devices; e-mail/name change needs step-up (re-entering the password), the password change itself asks for the current password in the form.

## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer: no blockers/majors · [ ] Contract regenerated · [ ] Docs updated (04 §2) · [ ] Deployed and checked in the browser
