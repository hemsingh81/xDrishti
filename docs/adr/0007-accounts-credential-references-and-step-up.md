# 0007 — Accounts: credential references, step-up, one primary Data account, archive

- **Status:** Accepted — decision 1 (credential references) superseded by [ADR 0008](0008-broker-credentials-vault-and-connection.md) on 2026-10-11
- **Date:** 2026-10-10

## Context
The Accounts screen (spec [P1-16](../specs/P1-16-accounts-list-detail-roles-risk-profiles-wizard.md)) lets the owner register broker accounts, give them roles
(Data, Trading, Portfolio), set trading risk and manage shared risk profiles. Doc 04 §1 already says that secrets live in the Keychain and never in the
database, and that role changes need re-authentication. Implementing it needed four concrete decisions.

## Decision
1. **Credential references, not secrets.** An account stores only a *credential reference* (`[a-z0-9_-]{1,40}`, unique among active accounts). The owner stores the
   broker's client id, API key and API secret with `scripts/broker-secrets.sh <ref>` (hidden prompts) as Keychain items `xdrishti/broker/<ref>/client-id|api-key|api-secret`
   and the script registers `<ref>` in a small non-secret index file in the private secrets folder (`brokers.index`). `deploy/xd-up.sh` enumerates **only** the indexed
   references (no Keychain dump or listing), reads each item strictly (never creating or overwriting a Keychain item, never echoing a value) and writes
   `secrets/brokers/<ref>/{client-id,api-key,api-secret}`. `compose.yaml` bind-mounts that folder **read-only into `xd-api` only** at `/run/secrets/brokers` (no environment
   variables; later also `xd-worker`/`xd-feed` when they need to call the broker). The API only reports whether each file exists and is not empty (`GET /api/accounts/{id}/credentials`, or `GET /api/accounts/credentials/{ref}` while the add-account wizard runs);
   it never opens, returns or logs a value, and the reference pattern keeps it from being used as a path. The client id itself is stored in the database but always shown masked
   (first 4 + last 2 characters) and never written to audit text or logs.
2. **Step-up (P1-02).** `POST /api/auth/step-up {password}` verifies the password with the same hasher and the **same lockout counters and rules** as sign-in (wrong passwords count
   toward the 5-failure lock, a locked account is refused with `423`, the same row-version-safe retry as sign-in) and elevates *the current session* for **5 minutes**
   (`auth.sessions.elevated_until`). Endpoints marked `.RequireStepUp()` answer `403 auth.step_up_required` otherwise. Elevation is per session, ends with logout or session expiry,
   and is audited (`StepUpSucceeded`, `StepUpFailed`). Sensitive: creating/re-connecting/removing accounts, roles, make-primary, trading settings, risk profile writes.
3. **Exactly one primary Data account, enforced by the database.** Partial unique indexes allow at most one active (not archived) primary and one standby Data account, a check
   constraint forbids "primary and standby" and "primary without the Data role", and slugs and credential references are unique among active accounts. "Make primary" is an
   atomic swap inside one transaction serialised by a transaction-level advisory lock, saved in two steps (release flags, set new flags) so no statement violates an index half-way;
   the old primary becomes the standby when that slot is free (or the target was the standby), otherwise it loses the Data role. The primary cannot lose the Data role or be
   removed until another account is made primary. A Trading role needs a risk profile, capital (more than 0 for fixed capital), a style and instruments for each chosen style;
   a role that is switched off keeps its settings but is inactive.
4. **Removing an account archives it** (`archived_at`): history stays, it disappears from lists and services, its name and credential reference are free again, and its audit trail
   (`accounts.account_events`, insert-only for the runtime role like `auth.audit_events`) stays readable.

## Consequences
- No broker secret ever enters the database, the API responses, the logs, the audit trail or the repo; compromising the API database yields no credentials. The cost: the owner runs
  one script per account and `xd-up.sh` once more, and the API can only say "present/missing", not "valid" (that is the broker adapter's job in P1-04).
- The mounted folders must be readable by the container's non-root user, so `brokers/` and `<ref>/` are mode 755 and the files 644; privacy comes from the private (700) parent folder.
- Step-up adds one round trip for sensitive actions; the UI retries the original action after the password dialog. The 5-minute window is global per session (not per action).
- The primary Data account can never be removed directly, even when it is the only account: add or promote another account first.
- Seeded data (risk profiles Standard and Conservative, buckets Trading/MyLongTerm/MyChoice) is inserted by the migration and is idempotent.
- Later: automated trading modes, token refresh/test and failover execution reuse these tables (P1-04, P1-12, P12).
