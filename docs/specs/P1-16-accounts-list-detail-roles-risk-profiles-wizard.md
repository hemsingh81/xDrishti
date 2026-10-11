# P1-16 — Accounts: list/detail, roles, risk profiles, wizard (includes P1-02 step-up)

- **Status:** Approved (owner instruction 2026-10-10: "build the Accounts screen where the user can configure the trading account … analyse the story and epic")
- **Phase / roadmap row:** P1 — Data platform & instruments · stories **P1-16** (5 pts) and **P1-02** (2 pts, password re-entry — role changes need it)
- **Design refs:** docs/design/04 §1 and §1.1 (accounts, roles, Accounts screen), §2 (step-up); 16 (accounts, risk_profiles); 08 §6–7; 05 §8; 12 (portfolio buckets); prototype `docs/prototype/` → Accounts

## Analysis — where this sits in the epic
**Epic P1 "Data platform & instruments"** (5 weeks) builds the data core plus the control plane around it. Accounts is the control-plane entry point: every later story asks *which account?* —
market-data jobs use the **primary Data account** (P1-12 EOD fetch, P2-07 live feed), the nightly plan allocates to **Trading** accounts with a risk profile (P7-02, P4-04 sizing), the portfolio syncs
**Portfolio** accounts (P2-01). So the Accounts screen must exist first, but several things it displays only become real later:

| Needed by Accounts | Provided by | Consequence now |
|---|---|---|
| Sign-in, sessions, CSRF, audit | P1-01 ✅ | build on it |
| Password re-entry for sensitive changes | **P1-02** | built here (small, and role changes need it) |
| Token status / refresh, connection test, subscription & renewal | P1-04 (Dhan adapter), P0-13 findings | shown as **"Not connected yet"**; actions disabled with a clear reason |
| Holdings value, P&L, funds, last sync | P2-01 / P2-05 | tiles show "—" with an explanation |
| Failover test | P1-12 | disabled |
| Baskets, instruments | P1-05 / P1-15 | not part of this screen |

So this slice delivers everything that is *configuration*: accounts, roles, the data-primary/standby rule, trading settings, portfolio settings, shared risk profiles, the add-account wizard, the role matrix, a per-account activity log — and the plumbing for **credential references** so the later broker adapter finds its secrets.

## Goal
The owner can register broker accounts, say what each is used for, set trading risk per account, manage shared risk profiles, and see who changed what — with every sensitive change protected by password re-entry, and **no broker secret ever stored in the database or shown in the UI**.

## Scope
**In:** step-up authentication; `accounts` module (domain, handlers, persistence, API); credential-reference plumbing (script + secret files + "present/missing" check); Accounts screen (list+detail with tabs, role matrix, risk profiles, wizard); activity log; tests; docs/ADR.
**Out (later stories):** talking to Dhan (token refresh/test/subscription — P1-04), funds/holdings/P&L (P2), data failover execution (P1-12), automated trading modes (P12), baskets (P1-15).

## Design decisions (defaults — object to any)
1. **Secrets stay out of the database and the UI.** An account stores only a **credential reference** (a short name such as `dhan_main`). The owner stores the broker's client id / API key / API secret in the Keychain with a new
   `scripts/broker-secrets.sh <ref>` (hidden prompts, items `xdrishti/broker/<ref>/client-id|api-key|api-secret`); `deploy/xd-up.sh` writes them as files under `~/Library/Application Support/xDrishti/secrets/brokers/<ref>/`, mounted read-only
   into `xd-api` (and later `xd-worker`) at `/run/secrets/brokers`. The API can only report whether each file **exists** (`present`/`missing`) — it never reads or returns the contents.
2. **Step-up (P1-02):** `POST /api/auth/step-up {password}` verifies the password and elevates the session for **5 minutes**; wrong passwords count toward the same lockout as sign-in. Endpoints that change roles, credentials references, trading
   risk, risk profiles or remove an account require an elevated session, else `403 auth.step_up_required`. The UI shows a password dialog and retries the action automatically. Elevation ends on logout/expiry.
3. **Exactly one primary Data account** (database-enforced by a partial unique index; at most one standby). The primary cannot lose the Data role or be removed until another account is made primary (atomic "make primary" swaps).
   A Trading role needs a risk profile, capital > 0 and at least one style; modes offered: `paper`, `manual_live` (automated modes shown disabled, "later phase").
4. **Removing an account archives it** (history kept, disappears from lists and services); archived accounts keep their audit trail.
5. **Risk profiles are shared** with the documented fields (doc 16): seeded **Standard** and **Conservative** (values from doc 16); accounts may override risk per trade, max new entries, max open, daily loss. Validation: risk per trade in (0, 2] %, daily loss > 0, counts ≥ 1.
   A profile in use cannot be deleted; the two seeded ones cannot be deleted.
6. **Portfolio buckets** seeded: `Trading`, `MyLongTerm`, `MyChoice` (owner can add more; P2-04 builds on them).
7. **Audit:** every change is written to `accounts.account_events` (who/when/what, no secrets) and shown on the Activity tab and in the global audit view later.
8. No accounts are pre-created; the owner adds them with the wizard. Client ids are shown masked (first 4 + last 2).

## Acceptance criteria
1. **Step-up:** `POST /api/auth/step-up` with the correct password returns `{elevatedUntil}` (5 min); wrong password → `401 auth.invalid_credentials` and counts toward the lockout (`423` when locked); each outcome is audited
   (`StepUpSucceeded/StepUpFailed`); requiring endpoints answer `403 auth.step_up_required` without elevation and succeed within the window; elevation is lost on logout and after 5 minutes. Needs a migration adding `elevated_until` to `auth.sessions`.
2. **Accounts API (all behind login; sensitive writes behind step-up)** per the Contract below: list, detail, create, update connection, update roles, make primary Data, update trading / portfolio / data settings, archive, activity, credential status.
   Every request/response type is in the OpenAPI contract; invalid input returns problem+json with stable codes.
3. **Invariants** (domain + database): one primary Data account; at most one standby; the primary cannot be demoted/archived while it is the only primary; Trading role validity (profile, capital > 0, ≥ 1 style, instruments chosen for each enabled style);
   mode `paper|manual_live`; unique slug/credential reference; overrides within ranges; a role switched off keeps its settings but is inactive.
4. **Risk profiles API:** list, create, update, delete (blocked when in use or seeded); seeded Standard/Conservative present after migration; sensible validation; changes audited and step-up protected.
5. **Credential references:** `scripts/broker-secrets.sh <ref>` stores the three items in the Keychain; `xd-up.sh` materialises `secrets/brokers/<ref>/` files (never echoing values) for every reference known to the Keychain index the script maintains;
   `GET /api/accounts/{id}/credentials` (and `GET /api/accounts/credentials/{ref}` for the wizard, before the account exists) return presence flags only; compose mounts the folder read-only into `xd-api` only; nothing secret in the repo, images, logs or API responses.
6. **Screen (React):** `/accounts` replaces the "coming soon" page and matches the prototype's UX with the theme tokens:
   - **Accounts** view: list on the left (colour, name, broker, masked client id, role chips, token health dot, "—" for value) and detail on the right with tabs **Overview · Roles · Trading · Portfolio · Data · Connection · Activity**; empty state with "Add account"; a banner summarising where data comes from and where allocations go.
   - **Role matrix**: accounts × roles table with checkboxes and primary-Data picker (step-up on save).
   - **Risk profiles**: cards/table with edit, create from existing, usage count, delete rules.
   - **Add-account wizard**: 1 Broker & identity → 2 Credential reference (shows the exact `scripts/broker-secrets.sh <ref>` command to copy, live "present/missing" check) → 3 Roles → 4 Role settings → 5 Review & confirm with password.
   - Token / funds / holdings / subscription / failover widgets show honest "not connected yet — available after the broker adapter" states; actions that cannot work are disabled with a reason.
   - Loading/empty/error states, validation messages next to fields, step-up dialog on 403, toasts; keyboard accessible, labelled controls, usable at 360 px and 4K without horizontal page scroll; initial bundle still ≤ 200 KB gzip (feature is lazy).
7. **Tests:** domain (invariants, trading validation), application handlers (fakes + `FakeTimeProvider`), integration against real PostgreSQL/TimescaleDB (all API rules, partial unique index, step-up flow, archive, seeded profiles, presence-only credential check, audit rows),
   architecture tests green, frontend tests (list/detail, tabs, wizard flow, step-up retry, role-matrix primary rule, risk-profile rules, empty/error states). `scripts/gen-api.sh` run; `scripts/check.sh` green; reviewer agent: no blockers/majors.
8. **Deployed and verified:** stack rebuilt with `deploy/xd-up.sh`; the owner adds an account through the wizard in the browser, changes roles/trading settings (password prompt appears and works), sees the Activity entries, and System status stays green.

## Contract (all under `/api`, session required; "S" = also needs step-up)
| Method & path | Purpose |
|---|---|
| `POST /auth/step-up` `{password}` | elevate the session → `{elevatedUntil}` |
| `GET /accounts` | list: id, slug, name, broker, maskedClientId, color, roles (data{enabled,primary,standby}, trading{enabled,mode,profile}, portfolio{enabled}), credentialRef, credentialsPresent, tokenStatus (`unknown` for now) |
| `POST /accounts` **S** | create `{name, broker, clientId, credentialRef, color, notes, roles{…}, trading?, portfolio?, data?}` |
| `GET /accounts/{id}` | full detail with trading/portfolio/data settings |
| `PUT /accounts/{id}/connection` **S** | name, clientId, credentialRef, color, notes, broker |
| `PUT /accounts/{id}/roles` **S** | enable/disable roles, data kind `primary|standby` |
| `POST /accounts/{id}/make-primary-data` **S** | atomic swap of the primary Data account |
| `PUT /accounts/{id}/trading` **S** | mode, riskProfileId, capitalSource `fixed|broker_funds`, capitalAmount, styles, instruments per style, overrides |
| `PUT /accounts/{id}/portfolio` | defaultBucket, historyFrom, includeInTotals |
| `PUT /accounts/{id}/data` | liveFeedBudget |
| `DELETE /accounts/{id}` **S** | archive |
| `GET /accounts/{id}/activity?take=` | account events |
| `GET /accounts/{id}/credentials` | `{ref, items:[{name, present}]}` — presence only |
| `GET /accounts/credentials/{ref}` | same, for a reference no account uses yet (wizard step 2); invalid reference → 400 |
| `GET /risk-profiles` · `POST` **S** · `PUT /{id}` **S** · `DELETE /{id}` **S** | shared profiles (+ `accountCount`) |
| `GET /portfolio-buckets` · `POST /portfolio-buckets` | bucket names |
- **Data (schema `accounts`):** `broker_accounts`, `trading_settings`, `portfolio_settings`, `risk_profiles`, `portfolio_buckets`, `account_events`; partial unique indexes for primary/standby Data and for active slugs; `xd_app` gets DML (events insert-only, like the auth audit);
  `auth.sessions.elevated_until` added by a new migration.
- **Errors:** `accounts.not_found` 404, `accounts.invalid` 400 (field errors), `accounts.conflict` 409 (primary rule, duplicate slug, profile in use), `auth.step_up_required` 403.

## Non-functional
Security: no secret values anywhere (not read, not returned, not logged); step-up on every risk/credential-affecting write; presence checks cannot be used to read file contents or traverse paths (reference restricted to `[a-z0-9_-]{1,40}`);
all inputs validated server-side. Performance: tiny tables, indexed lookups. Accessibility/responsiveness as AC 6. Observability: `[LoggerMessage]` events without personal data beyond the account slug.

## Test plan
Unit (domain/application), integration (Testcontainers; includes the DB unique-index race for two concurrent "make primary" calls), frontend (Vitest + MSW), architecture; live: wizard in the browser, 403 → password dialog → success, activity log, presence check flips after running `scripts/broker-secrets.sh`.

## Effort
Estimated 7 pts (P1-16 5 + P1-02 2) → realistically two agent runs (backend, frontend) plus review and deploy.

## Done when
- [ ] All acceptance criteria have evidence (AC 8 browser walkthrough pending) · [x] `scripts/check.sh` green; reviewer: no blockers/majors (2026-10-10, second pass approved) · [x] Contract regenerated · [ ] Deployed (✅ `deploy/xd-up.sh`, stack healthy) and verified in the browser (pending) · [x] Docs updated (04 §1, ADR 0007)
