# 04 — Accounts, Access & Operating Model

Four rules shape how xDrishti is used day to day:

1. **Personal use only** — one owner, only the owner's broker accounts.
2. **Login required** — nobody else can open or use the application.
3. **Configure once, services run it** — what you set up is executed by background services on the machine,
   not by your browser session.
4. **Human in the loop** — the system and the AI *propose*; you *confirm* the decisions that matter.

## 1. Broker accounts and their roles

You can register any number of broker accounts. Each account is given one or more **roles**:

| Role | What the account is used for | Requirements |
|------|------------------------------|--------------|
| **Data** | Market data: instrument master, historical & end-of-day 1-minute data, live feed for the active set, quotes | Dhan Data API subscription on this account |
| **Trading** | Receives next-day plan tickets (sized to its capital and risk profile); auto-journal of fills; later order execution | Risk profile, capital source, allowed styles/instruments |
| **Portfolio** | Holdings (and later positions, trades, ledger) are imported with acquisition dates, synced by hand or automatically and monitored ([ADR 0012](../adr/0012-holdings-acquisition-dates-and-returns-model.md)) | Connected account; optional refresh schedule; gets its own default bucket automatically |

One account can hold several roles at once, for example:

| Account | Data | Trading | Portfolio | Notes |
|---------|:----:|:-------:|:---------:|-------|
| Dhan · Main | ✅ primary | ✅ | ✅ | Pays the ₹499 Data API; trades and is monitored |
| Dhan · Long-term | — | — | ✅ | Monitoring only (holdings, XIRR) |
| Dhan · Swing | ✅ standby | ✅ | ✅ | Can take over data if the main account's token fails |

**Rules enforced by the system**
- Exactly **one primary Data account**; optionally one **standby** that takes over automatically if the primary's
  token or subscription fails (only if it also has an active Data API subscription).
- A Trading account must have a risk profile; plan allocation only goes to accounts with the Trading role.
- Portfolio views include only accounts with the Portfolio role (filterable per account).
- Removing a role stops the related services for that account; history is kept.
- Every role change is audited and needs re-authentication (password).

**Per-account settings:** display name, broker (Dhan only, [ADR 0009](../adr/0009-dhan-consent-flow-only.md)), Dhan client id (shown
masked: first 4 + last 2), the Dhan API key and secret (write-only, encrypted — see below), the day the API key was generated, roles,
mode (`paper` / `manual_live`; automated modes in a later phase), risk profile, capital source, sync schedule, token status & last successful sync.

**Dhan accounts** ([ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md), narrowed by [ADR 0009](../adr/0009-dhan-consent-flow-only.md)): Dhan is the only
broker and the documented *API Key & Secret* consent flow the only way to connect. Adding an account asks for what Dhan needs; the setup steps and the
redirect URL are shown in the wizard and on the Connection tab (Dhan facts are constants in the UI):

| | Dhan |
|---|---|
| Identity | **Client id** — required (e.g. `1000000001`) |
| Secrets (write-only) | **API key** (`app_id`), **API secret** (`app_secret`) — valid **12 months**; the owner gives the day it was generated (default today, India time); warning 30 days before expiry |
| Setup steps shown | web.dhan.co → My Profile → Access DhanHQ APIs → switch on *API key* → app name, **Redirect URL** (copy button), Postback optional |
| Notes shown | A static IP is needed only for placing orders (not for data/portfolio); 25 consents a day |

The redirect URL is `<Brokers__PublicBaseUrl>/api/brokers/dhan/callback` (default base `http://localhost:8080`); the UI reads it from
`GET /api/brokers/dhan` → `{redirectUrl}`. Another broker later means a new decision (the `broker` column and the connector port stay).

**Connecting an account** ([P1-16c](../specs/P1-16c-connect-dhan-and-upstox.md), [P1-16d](../specs/P1-16d-dhan-consent-only-and-header-accounts.md), ADR 0008, 0009) follows Dhan's documented flow (details in doc 05 §8):

1. **Connect** — `POST /api/accounts/{id}/connect` (step-up) → `{authorizeUrl, expiresAt}`. The API asks Dhan for a consent (`generate-consent`,
   at most **25 per account and India day**; the 26th answers `429 accounts.consent_limit` with `retryAfterSeconds` until midnight IST) and returns
   Dhan's login page. A **pending sign-in** (`accounts.pending_connects`) is stored: bound to the session, valid **10 minutes**, usable **once**; a newer
   sign-in of the same session closes an older one.
2. The browser opens Dhan's login page (`consentApp-login`) in the same tab; the owner signs in there with Dhan's own 2FA.
3. **Callback** — Dhan redirects to `GET /api/brokers/dhan/callback?tokenId=…`. The callback needs the signed-in session (the session cookie is
   `SameSite=Lax` so it comes along with this top-level redirect) and the session's open pending sign-in (Dhan sends no state), uses it up at once,
   exchanges the `tokenId` server-side (`consumeApp-consent`), checks that the token belongs to the account's client id, stores the token encrypted
   (`access_token`) and always answers **302** to
   `/accounts?account={id}&connected=1` or `/accounts?[account={id}&]connectError={code}`. Codes: `accounts.connect_expired` (too late, used
   or another session), `accounts.connect_cancelled` (no tokenId), `accounts.connect_mismatch` (other client id; nothing stored),
   `accounts.broker_error`, `accounts.app_not_configured`, `auth.unauthenticated` (no session). It never shows broker data.
4. **Disconnect** — `DELETE /api/accounts/{id}/token` (step-up; deletes the stored token locally, no broker call: Dhan has no logout for these tokens).
   Dhan refusing, failing or timing out answers `502 accounts.broker_error` with a safe message (at most Dhan's error code).

Every account shows `connection {status, expiresAt, brokerUserName, brokerUserId (masked), lastConnectedAt, lastError}`
(`accounts.broker_connections`): status `not_connected` · `connected` · `expired` (derived when read once `expiresAt` has passed — no broker call).
`tokenStatus` repeats the status for the list's health dot.
A token belongs to the client id it was issued for: **changing the client id** of an account that has a token or a connected / expired
connection deletes the stored token, resets the connection to `not_connected` and closes its open sign-ins in the same change (no broker call;
audited as "client id changed; broker connection cleared"). **Archiving** deletes the token with the other secrets, resets the connection and
deletes the account's pending sign-ins.
Audit: `BrokerConnected`, `BrokerConnectFailed`, `BrokerDisconnected` (names, times and Dhan error codes only). Tokens, secrets and codes never
appear in responses, redirects, logs or audit text.

**Credential vault** ([ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md), replaces the credential references of ADR 0007): the API key
and secret are entered in the app — in the add-account wizard (both required) or on the Connection tab (*Replace*, step-up; a value left out stays,
an empty one is refused) — and stored in `accounts.broker_secrets` (one row per account and secret: `api_key`, `api_secret`, `access_token`) encrypted with **AES-256-GCM**: random 96-bit nonce per value, the account id and secret name as associated data (a value only
decrypts for the account and name it was written for), a 256-bit key derived with HKDF-SHA256 from `BrokerVault__Key` (Keychain
`xdrishti/broker/vault-key`, ≥ 32 characters; the API refuses to start without it) and a key version per row for later rotation. Responses only
say `secrets: [{name, set, updatedAt}]` (plus `apiKeyIssuedOn` / `apiKeyExpiresOn`); values never appear in responses, logs, audit text, exceptions or
`ToString()`. Replacing is audited by name only ("API key replaced"). Archiving an account deletes its stored secrets. Accounts created before the
vault show "API key: not set".

**Sensitive changes need password re-entry (step-up, 5 minutes):** creating, changing the connection (name, client id, colour, notes), replacing broker secrets, connecting, disconnecting, changing roles or
the primary Data account, changing trading settings, removing an account, and creating, editing or deleting risk profiles. Without a recent
step-up these endpoints answer `403 auth.step_up_required`. Portfolio and Data settings do not need it.

**Portfolio settings and the default bucket (P1-16e):** an account with the Portfolio role always has exactly one default bucket named
`"{account name} · Portfolio"` (middle dot with spaces), created in the same save that creates the account or switches the role on, renamed with the account,
archived with it (and renamed "… · Portfolio (archived)", so its name is free for a new account), and hidden from `GET /api/portfolio-buckets` while the role is off (it and its holdings are kept). `PUT /api/accounts/{id}/portfolio` takes
`{defaultBucket?, includeInTotals?, refreshMode?, refreshDailyTimeIst?, firstImportDate?, firstImport?: {rows: [{key, acquiredOn?}]}}` — **every field that is left out (or null) keeps its current value**. `defaultBucket` is a
custom bucket's name; `""` (or the name of the account's own "{name} · Portfolio" bucket) selects the account's own bucket; at account creation, left out means the own bucket and `includeInTotals` true. Views return `defaultBucket` (name), `autoBucketName`, the refresh settings, `firstImportDate` and `hasHoldings`. **Automatic holdings refresh and first import** ([ADR 0012](../adr/0012-holdings-acquisition-dates-and-returns-model.md), [ADR 0013](../adr/0013-portfolio-import-with-settings-and-daily-refresh.md)):
`refreshMode` `off` (default) | `daily`, `refreshDailyTimeIst` `HH:mm` (India time, default 16:00); `firstImportDate` (default 2025-01-01, not in the future, not before 1990-01-01) is the date holdings
get when imported without a date of their own. A field that is left out keeps its current value, invalid values are `400 accounts.invalid` with the field name, a refresh change is audited (`PortfolioRefreshSettingsChanged`). **A changed `firstImportDate` re-dates** ([ADR 0015](../adr/0015-first-import-date-editable-and-no-bucket-ui-in-accounts.md)): on an account with holdings, every imported lot still on the default date (`uses_default_date`) moves to the new date in the same save (closed lots whose close day is before it are skipped; owner-dated, refresh-found and manual lots are untouched), audited as `FirstImportDateChanged` (old → new date and counts); the same date again changes nothing.
**The answer is `{account, import}`**: when the account has the Portfolio role, is connected and has no holdings, every holding Dhan lists is imported right after the settings are saved, dated `firstImportDate` (request, else stored, else 2025-01-01; optional `firstImport.rows` dates override per key)
(`import.status` `imported` with `imported`/`defaultDated`/`alreadyImported`/`unknown`, `skipped` when the account already has holdings (with `redated`/`redateSkipped`: the lots that followed a changed `firstImportDate`; both 0 otherwise), `deferred` when it is not connected or the role is off — the first sync then imports everything dated
`firstImportDate` —, or `failed` with `errorCode` `portfolio.import_failed`, `cause` and `message`: the settings stay saved); `import` is always present. At account creation `firstImportDate` is stored and `firstImport` is ignored (a new account is not connected).
Details: [doc 12](12-portfolio-monitoring.md) §3.
There is no "history from" setting: acquisition dates are chosen by the owner at the first import (saving these settings) and can be edited per lot ([doc 12](12-portfolio-monitoring.md) §3).
The import, sync and lot-date edits are audited in the account's activity (`HoldingsImported`, `HoldingsSynced`, `LotDateChanged`, `FirstImportDateChanged`: counts and dates only, never tokens or Dhan's payload).
Tracking instruments (P1-05, [doc 06](06-instruments-and-baskets.md) §2.3) is audited in the same table without an account (`InstrumentsTracked`, `InstrumentsUntracked`: counts only); so are the basket changes (P1-15, [doc 06](06-instruments-and-baskets.md) §3.4: `BasketCreated`, `BasketRenamed`, `BasketUpdated`, `BasketArchived`, `BasketsReordered`, `BasketMembersAdded`, `BasketMembersRemoved`, `BasketMembersReordered`, `BasketMembersTransferred`: basket names and counts, never instrument symbols or ids). The trading calendar (P1-11a, [doc 05](05-data-and-brokers.md) §4.1) is audited the same way (`CalendarDayAdded`, `CalendarDayConfirmed`, `CalendarDayRemoved`, `CalendarRefreshedFromNse`: the date, kind, window and source — never the free-text note; the automatic proposals and the daily NSE attempt after a data job are not audited, they are not the owner's act). Corporate actions (P1-10a, [doc 05](05-data-and-brokers.md) §4.2) are audited the same way (`CorporateActionAdded`, `CorporateActionConfirmed`, `CorporateActionEdited`, `CorporateActionIgnored`, `CorporateActionsImported`: the kind, ratio, symbol, ex-date and source, for an import the counts and the symbols — never the free-text note; the detection's suggestions are not audited, they are not a decision). Undoing the load of a CSV file (P1-07a, [doc 05](05-data-and-brokers.md) §3) is audited the same way (`CsvLoadUndone`: `CSV load of <file> undone, N bars removed` — the file name and the count, never any row data; no account).

**Removing an account archives it:** it disappears from lists and services, its history and audit trail (`accounts.account_events`, insert-only
for the runtime role) stay, its name becomes free again and its stored broker secrets are deleted. The primary Data account cannot be removed or lose the
Data role until another account has been made primary (an atomic swap; the old primary becomes the standby when that slot is free).

### 1.1 Accounts screen

| View | Content |
|------|---------|
| **Accounts** (list + detail) | Left: every account with roles, token health, holdings value. Right: tabs **Overview** (value, P&L, funds, token, sync) · **Roles** · **Trading** (mode, risk profile, capital source/amount, styles, allowed instruments per style, per-account overrides of risk %, max entries, max open, daily loss) · **Portfolio** (include in totals; auto refresh: Off / Daily at HH:MM IST, and the *First import* block: the first import date (default 1 Jan 2025), a calendar that stays editable — saving imports all holdings Dhan lists with that date the first time, and later re-dates the lots still on the default date; per-holding dates are edited on the Portfolio screen; no bucket choice here — imports go to the account's own bucket, [ADR 0015](../adr/0015-first-import-date-editable-and-no-bucket-ui-in-accounts.md). Later changes arrive with the refresh; the Portfolio screen only has *Sync now*) · **Data** (primary/standby, subscription & renewal, live-feed budget, failover test) · **Connection** (client id masked, name, colour, notes; secret status — set / last changed, Dhan key expiry — with *Replace* (write-only dialog, step-up); Dhan's setup steps and redirect URL; connection status chip and expiry with *Connect / Reconnect* and *Disconnect*) · **Activity** (audit for this account) |
| **Add account wizard** | 1 *Dhan app* (client id, API key and secret — masked, never pre-filled —, day the key was generated, setup steps, redirect URL with copy button) → 2 *Identity* → 3 *Roles* → 4 *Role settings* → 5 *Review & create* (secrets shown as "•••• set"; confirm with your password; *Create and connect* starts the Dhan login) |
| **Header accounts menu** | Every account with colour dot, name and status chip (expiry when connected), and *Connect / Reconnect* for each account that is not connected ([P1-16d](../specs/P1-16d-dhan-consent-only-and-header-accounts.md)) |

The **role matrix** (all accounts × roles; tick to change, step-up; pick the primary Data account) and the shared **risk profiles** (Standard,
Conservative, …: risk per trade, max new entries/day, max open positions, daily loss, max per sector, max heat; which accounts use each) live under
**Settings** (`/settings`, tabs *Risk profiles* · *Account roles*), not on the Accounts page.

**Automatic failover:** if the primary Data account's token or subscription fails, the services switch to the
standby Data account, notify you and record it in the audit log.

## 2. Access control (login)

| Control | Design |
|---------|--------|
| Login | E-mail + password on a centred sign-in panel; a locked session shows *Welcome back · Unlock* (password only, survives reload); single fixed owner account, **no sign-up**: provisioned by `xd-migrate` from the Keychain items `xdrishti/auth/owner-email\|owner-name\|owner-password` (created with `scripts/auth-secrets.sh`) if none exists. Wrong e-mail and wrong password give the same `401 auth.invalid_credentials`. No second factor: the app is personal and reachable only on this machine / home LAN (a passkey can be added later if remote access is ever enabled) |
| Password storage | PBKDF2-HMAC-SHA512, ≥ 210,000 iterations, random salt (parameters stored with the hash; upgraded at the next login); never stored or logged in plain text. Changed in the app (My account, below) or with `scripts/auth-secrets.sh` + `deploy/xd-up.sh --owner-reset` (the reset also signs out every session) |
| Sessions | Server-side, revocable, stored hashed in `auth.sessions`; opaque cookie `xd.sid` (HttpOnly, SameSite=Lax — `Strict` until ADR 0008: Dhan's sign-in redirect is a top-level cross-site GET that must carry it —, `Secure` on HTTPS); idle timeout 30 min (sliding, last-seen written ≤ once a minute); absolute timeout 12 h; CSRF header `X-CSRF-Token` on every POST/PUT/PATCH/DELETE; sign-out revokes the session; "lock" button (later) |
| Lockout | 5 consecutive failures → 15-minute lock (`423 auth.locked` + `retryAfterSeconds`; a correct password is refused during the lock); login route also limited to 10 attempts/minute per client address (`429`); every sign-in event (success, failure, lock, logout, idle/absolute expiry, owner provisioning/reset) in `auth.audit_events`, readable via `GET /api/auth/audit` |
| Step-up auth | Password re-entry for sensitive actions: account roles, broker secrets and connection, trading risk, risk profiles, removing accounts, config activation, enabling execution, deleting data. `POST /api/auth/step-up {password}` elevates the current session for **5 minutes** (`auth.sessions.elevated_until`; ends at logout/expiry), endpoints answer `403 auth.step_up_required` without it; wrong passwords count toward the same lockout as sign-in (`StepUpSucceeded` / `StepUpFailed` in the audit trail) |
| My account (P2-11) | The owner changes their own details in the app at `/account` (user menu → *My account*, also in the ⌘K palette). `GET /api/auth/account` → `{email, displayName, createdAt, passwordChangedAt}`. `POST /api/auth/change-password {currentPassword, newPassword}` verifies the current password (a wrong one counts toward the same lockout as sign-in and step-up, `423` when locked), requires 10–1024 characters different from the current one (`400 auth.password_invalid` / `auth.password_unchanged`, field message on `newPassword`), stores a new PBKDF2 hash, **signs out every other session and keeps the current one** (`PasswordChanged` in the audit trail). `PUT /api/auth/profile {displayName, email}` needs step-up (`403 auth.step_up_required` without it), validates name (≤ 128) and e-mail (an `@`, no whitespace or control characters, ≤ 256, stored lower-cased), writes `ProfileUpdated` (changed field names only) and leaves other sessions valid; saving unchanged values writes nothing. The page also lists the last 10 sign-in events (`GET /api/auth/audit?take=10`). Passwords are never logged, audited or kept after the request |
| Recovery | No recovery codes: the owner resets the password from the Keychain (`xd-up.sh --owner-reset`); there is no e-mail reset. A password or e-mail changed in the app is **not** written back to the Keychain: `--owner-reset` re-reads the Keychain items and overwrites the in-app values, so update them with `scripts/auth-secrets.sh` after a change if a reset may be needed later |
| Every endpoint protected | Closed by default (fallback authorization policy): REST, OpenAPI/API docs, SignalR, Hangfire dashboard and file downloads require a session; only `/health/*`, `POST /api/auth/login` and `GET /api/auth/session` are anonymous. The Dhan callback `GET /api/brokers/dhan/callback` passes routing without a session but check it themselves and, without one, redirect to `/accounts?connectError=auth.unauthenticated` instead of answering 401. The static docs at `/prototype/` and `/docs/` are protected by the proxy (`forward_auth` → `GET /api/auth/verify`); the React shell and its assets stay public so the login page can load |
| Network | Reachable only on the home LAN via HTTPS (Caddy); optional WireGuard for remote; MCP/LLM on the internal network only |

## 3. Operating model — configure once, services run it

The UI is a **control plane**: it records what you want. Background **services** are the **execution plane**:
they do the work on the machine, whether or not you are logged in or the browser is open.

```mermaid
flowchart LR
    U(["You (logged in)"]) -->|configure / approve| API["xd-api<br/>validates · audits"]
    API -->|desired state & commands| DB[("PostgreSQL<br/>config · catalog · approvals · commands")]
    DB --> SVC["Services (no user session)<br/>xd-worker · Hangfire jobs · xd-feed"]
    SVC -->|results · status| DB
    SVC --> BRK[("Dhan APIs")]
    DB -->|SignalR when you're online| U
```

- Every action you take is stored as **desired state** (e.g. "INFY is tracked", "basket MyChoice contains …",
  "ticket T-123 approved") plus an **audit record** (who, when, what changed).
- Services run under a **service identity**, not your session. Logging out, closing the browser or the phone
  has no effect on scheduled work.
- Services **reconcile** desired vs actual state: on every run and on start-up. If the Mac was asleep at
  15:50, the end-of-day fetch runs as soon as the services are back (catch-up for missed schedules).
- Jobs are idempotent, retried with back-off and resumable; failures raise in-app notifications.

| You do this (once) | Services then do this (automatically) |
|--------------------|----------------------------------------|
| Track an instrument | Backfill history (CSV, then Dhan), add to end-of-day fetch, build all timeframes |
| Create a basket / add instruments | Include in the next scan scope, reports and portfolio grouping |
| Assign the Data role to an account | Switch market-data jobs and the live feed to that account |
| Assign the Portfolio role | Nightly sync of holdings, trades, ledger; valuations; XIRR |
| Assign the Trading role | Allocate and size plan tickets for that account |
| Approve tomorrow's tickets | Pre-open check, arm tickets, subscribe live feed, trigger/stop/target alerts |
| Drop CSV files in the inbox | Import, validate, aggregate, report |
| Activate a config version | Apply from the next scheduled run |
| Accept an AI suggestion | Apply the change (e.g. add instrument to basket) and log it |

**Services overview** (default schedules — all editable on the Services screen):

| Service / job | Default schedule | Runs as |
|---------------|------------------|---------|
| Live feed (active set) | Window 09:00–15:35, trading days; **built** as the service `live-feed` (P2-10, [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md)): `xd-feed` supervises its own window and writes its own run rows, the Services screen lists, edits, pauses and resumes it through the stored row (the feed re-reads it every 15 s), *Run now* opens the connection for `Live:ManualRunMinutes` (30) outside the window, and its status comes from the feed's heartbeat | `xd-feed` |
| Holdings quote refresh | Every 5 min, 09:15–15:30 on trading days; **built** as the service `holdings-quotes` (P2-06, [ADR 0019](../adr/0019-holdings-quotes-day-change-and-the-first-interval-service.md); the first `interval` service: retries 0, timeout 2 min, no catch-up, overlapping ticks dropped); since P2-16 ([ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md)) a poll that fails because Dhan did not answer or limited the rate is recorded as *skipped* (the next 5-minute poll is its own retry) and fails the run — which the bell's failed-service notice shows — only after failing on two days in a row; a refused token fails it at once | `xd-api` today (`xd-feed` later) |
| **Pre-open check & arming** | **09:10** (after the 09:05 review cutoff and NSE pre-open price discovery, before the 09:15 open) | `xd-worker` |
| Price alerts & action center | Every 1 min, 09:15–15:30; **price alerts are built** (P2-09, [ADR 0022](../adr/0022-action-center-and-price-alerts.md)) and, since P2-10 ([ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md)), checked on every live flush (every 2 s while the feed is connected) as well as on the 5-minute quote tick; the Action center is evaluated on read | `xd-feed` / `xd-api` |
| End-of-day fetch & aggregation (with gap backfill) | 15:50 on trading days of the calendar, never before the session has ended ([doc 05](05-data-and-brokers.md) §6; **built** as the service `eod` (P1-13): the job `dhan → aggregate → quality`, trigger `daily`) | `xd-api` today (`xd-worker` later) |
| Data validation | Runs after the EOD fetch and aggregation, as the last step of the same data job ([doc 05](05-data-and-brokers.md) §10b; built in P1-14a: `dhan → aggregate → quality`, trigger `daily`; on demand from the Data quality screen; `Data:Quality:*`) | `xd-worker` |
| Account & portfolio sync | 16:30 (designed); **built** per account, once a day at the time set under Accounts ([ADR 0013](../adr/0013-portfolio-import-with-settings-and-daily-refresh.md)) and shown on the Services screen as a **read-only row** with a link; since P2-16 a refresh that failed for a reason that may pass is retried quietly by the holdings refresh runner (1, 5 and 15 minutes, inside the day) and a renewed Dhan login makes the missed syncs run by themselves ([doc 12](12-portfolio-monitoring.md) §3 "Self-healing and attention items (P2-16)"; this needs `Portfolio:AutoRefresh:Enabled`) | `xd-api` today |
| Portfolio trades & ledger | 16:30 on trading days, never before the session has ended; **built** as the service `portfolio-broker-history` (P2-13, [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md)): reads each connected Portfolio account's Dhan trades and ledger (a one-time backfill, then from a watermark) and rebuilds lots where that is safe ([doc 12](12-portfolio-monitoring.md) §3); an expired token is reported as `token_expired`; "Sync trades & ledger" on the Portfolio screen works at any time; since P2-16 the same run is retried quietly after a failure that may pass, reads **one earlier year of trades per day** (up to 8) while the trades do not explain a holding, and reads again the windows that dropped rows once the Dhan mapping has changed ([ADR 0027](../adr/0027-self-healing-sync-and-actionable-notifications.md)); since P2-17 a bill-level trade imported from a Dhan report is replaced when the run stores the broker's own trades for the same day, ISIN and side ([ADR 0029](../adr/0029-dhan-report-imports-and-source-precedence.md)) | `xd-api` today |
| Nightly pipeline → proposed plan | 18:30; **skeleton built** (P7-08, [ADR 0086](../adr/0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md)) as the service `nightly` in the services framework (not Hangfire), paused by default, after `eod` and the portfolio sync: six ordered gates (calendar, eod, portfolio sync, fresh data, data quality, selection inputs), any that does not pass publishes nothing and records one `plan_runs` row; only an empty skeleton plan is published until P7-09; `xd plan run [--date] [--dry-run]` | `xd-api` today (`xd-worker` later) |
| AI suggestions & briefing | After the nightly pipeline; starts `xd-llm` on demand and waits for readiness (a 30B load took 22–105 s) | `xd-worker` + `xd-llm` |
| CSV inbox watcher | Continuous | `xd-worker` |
| Weekly retrain / monthly review | Sat 10:00 / 1st of month | `xd-worker` |
| Backup | Daily 23:30 | `xd-backup` |

**Instrument master refresh** (built, not in the table above): daily at 08:45, every day of the week; the service `instrument-master` ([ADR 0010](../adr/0010-instrument-master-sources-and-scope.md)).

### 3.1 Schedule configuration

> **Built (P1-13, [spec](../specs/P1-13-services-framework-and-services-screen.md), [ADR 0018](../adr/0018-services-framework-and-editable-schedules.md)):** the framework, the two first services and the API of the Services screen.
>
> - **A service** is a registered definition (id, name, `runsAs`, allowed schedule types, default schedule and execution settings, parameter schema with bounds, constraints `MustFollow` / `MustNotOverlap` / `MustCover`, a start-time advice) plus
>   one row of the owner's choices in `platform.service_schedules` (enabled, schedule, retries, timeout, catch-up, notify on failure, parameters, `xmin` as revision, who and when). Without a row a service runs on its first-start settings.
>   The two services built so far are **`eod`** (End-of-day data run) and **`instrument-master`** (Instrument master refresh); the others of the table above register themselves when they are built, and their checks apply from then on.
> - **One scheduler** (`ServiceScheduler`, a hosted service in `xd-api`) looks at the rows at least once a minute and at the next start, so an edit counts at the next look without a restart. A service runs at most once at a time (a start while it
>   runs is recorded as a skipped run). Every attempt is a row of `platform.service_runs` (trigger `scheduled | catch_up | after | manual`); a failed attempt is retried up to *retries* times after 30 s, 2 min, 10 min, 30 min, 1 h (a sequence the previous process left in its back-off goes on after a restart); an attempt past
>   the timeout is cancelled and recorded as failed (timed out; a job that cannot be cancelled and is still running is said so; a timeout is never retried); a run that succeeds starts the services set to run after it. At start-up the runs the previous process left running are closed as failed (interrupted) first, then each enabled
>   service with catch-up on whose latest scheduled time passed without a run since is run once (`catch_up`: the same day for a daily job, 24 hours for a weekly one, any age for the instrument master); a paused service never catches up, not even
>   when resumed. The same rule applies when the machine slept through a time. Pause stops scheduled, catch-up and run-after starts; **Run now** (trigger `manual`) still works.
> - **First-start settings.** `Data:Eod:Enabled` / `Data:Eod:TimeIst` and `Instruments:AutoRefresh:Enabled` / `TimeIst` are read **once**, when a service has no row, and stored as its row; after that the Services screen is the only place to change
>   them and the configuration is ignored (a restart keeps the edit). Until the row exists the screen shows the configured values.
> - **Checks** (`ScheduleRules`, pure, over the whole set; the same rules answer the dry run): errors block saving — a type the service does not allow, an unreadable time, no day chosen, a window whose start is not before its end, an interval
>   outside 1–120 minutes or longer than its window, a run-after chain that is circular or points at an always-on or unknown service, retries outside 0–5, a timeout outside 1–240 minutes, an unknown parameter or one out of its bounds;
>   warnings are shown and allowed — a start before or after the advised time (the end-of-day fetch before 15:35), a window that does not cover what the definition requires, a run during market hours that must not, a service that should follow
>   another and does not, a chain of more than three, two services at the same minute. A finding is reported on the service it is about, so moving the end-of-day time after a dependent service's own time warns on the dependent.
> - **API** (`/api/services`, session + CSRF, no step-up, every change audited as `ServiceScheduleChanged | ServicePaused | ServiceResumed | ServiceRunRequested` in the account events): list, one service, runs (newest first, the latest 200), timeline
>   of a date, notices for the bell (`service_failed`, derived on read), save (optimistic revision, `409 services.revision_changed`), preview (issues + next three runs), restore default, pause, resume, run now (`202`, `409 services.already_running`).
>   `services.invalid` carries `issues[]`; `services.read_only` for the per-account sync row.
> - **Not in this slice:** the `xd-worker` container (the scheduler runs in `xd-api`; moving it is a deployment change), services that do not exist yet, parameters for the two built services (the framework validates them against a service's
>   schema; `eod` and `instrument-master` declare none yet), purging old runs.

Each service has an editable schedule stored in the database (not in code):

| Setting | Options |
|---------|---------|
| Schedule type | Daily at a time (chosen days) · Weekly · Run after another service · Time window · Every N minutes within a window · Continuous |
| Execution | Enabled/paused · retries (0–5) · timeout · catch up missed runs · notify on failure |
| Parameters | Service-specific, e.g. EOD throttle and gap look-back; validation sessions, tolerance and spike multiple; pre-open gap threshold; live-feed instrument cap |

**Dependency & timing rules** (checked on every change; errors block saving, warnings are shown):
pre-open check after the review cutoff and before 09:15 (warning before ~09:08); EOD fetch after 15:35; nightly
pipeline after the EOD fetch and the portfolio sync; live window covers 09:15–15:30; no backup during market
hours; no circular "run after" chains; window start before end. The Services screen also shows a trading-day
**timeline** with market hours, the review cutoff, every scheduled run and the run-after chains, plus a preview
of the next three runs while editing.

## 4. Human in the loop

### 4.1 Who decides what

| Category | Examples | Decision |
|----------|----------|----------|
| **Automatic** (no approval) | Data fetch, aggregation, quality checks, grading, cell statistics, portfolio sync, valuations | Services |
| **Proposed → you confirm** | Next-day trade tickets · AI instrument suggestions (track, add to / remove from basket) · hypothesis promotions · cell promotions to Active · config change proposals · (later) semi-auto orders | You |
| **Only you** | Accounts & roles, credentials, baskets you create, risk limits, enabling execution | You |

### 4.2 Next-day plan approval

```mermaid
stateDiagram-v2
    [*] --> Proposed: nightly pipeline
    Proposed --> Approved: you approve
    Proposed --> Modified: you edit (size, stop, account)
    Modified --> Approved: re-validated against limits
    Proposed --> Rejected: you reject (reason optional)
    Proposed --> Lapsed: not reviewed by cutoff
    Approved --> Armed: pre-open check passes (09:10)
    Approved --> Invalidated: gap / event at pre-open
    Armed --> Triggered: trigger condition met
    Armed --> Expired: validity window ends
    Triggered --> Closed: exit / square-off / max hold
```

- The nightly pipeline produces a **proposed plan** (primary tickets + reserve list) with reasons, P(win),
  expected R and the AI briefing. You review it in the evening or morning (phone works via the PWA).
- Actions per ticket: **Approve**, **Modify** (reduce size, tighten stop, change account, choose reserve instead),
  **Reject**. Bulk "approve all within limits" is available. Every modification is re-validated against risk limits.
- **Review cutoff** (default 09:05): unreviewed tickets **lapse** (safe default — `unreviewed_policy: lapse`).
- Only **Approved → Armed** tickets enter the live active set and alerts.
- Every decision is recorded. Reports compare **system proposal vs your decision** — e.g. "rejected tickets would
  have made +4.2R this month" — so you can see whether your overrides add value.
- **As built (P7-03, [ADR 0083](../adr/0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md)):** the state of a ticket or plan is the newest row of an insert-only trail (`trading.plan_events`); approvals (`workflow.approvals`) have one kind, `owner`; at the cutoff every undecided ticket lapses; Modified can also be rejected or lapse, Approved can expire before arming; the only automatic moves are lapse, arm, invalidate, expire, trigger and close, never approve. No member of the code base may be named for placing, modifying or cancelling an order (architecture test).
- **As built (P7-11, [ADR 0096](../adr/0096-the-pre-open-check-arms-only-approved-tickets-as-a-service-move-a-missing-input-invalidates-and-armed-tickets-join-the-feed-set-last.md)):** Approved to Armed is made by the paused `preopen` service (09:10) as `system:preopen`, only for tickets already Approved by the owner (or by the optional rule); the instrument joins the live feed set through `ArmedTicketsProvider` (ranked after holdings, so the feed cap never displaces another member) and leaves when the ticket expires (`plan-expiry`, 15:40) or is invalidated. Price-crossing alerts are a follow-up.

### 4.3 AI suggestions inbox

The assistant and learning engine post suggestions to an **inbox**; each has a reason, evidence and an expiry:

| Suggestion | Example | If accepted |
|------------|---------|-------------|
| Track instrument | "Track PERSISTENT — strong RS, 3 passing setups in Best stocks rules" | Instrument tracked & backfilled |
| Add to / remove from basket | "Add TATAPOWER to Best stocks (rule match, liquidity ok)" | Membership updated (dated) |
| Next-day focus | "Prefer swing over intraday tomorrow: high-VIX regime" | Shown with the plan (no automatic effect) |
| Hypothesis / promotion | "ORB 15m BUY: target 2.0R → 2.5R passed walk-forward + shadow" | Config version created for activation |

Rejected suggestions are not repeated for a configurable number of days.

## 5. Data model additions

| Table | Purpose |
|-------|---------|
| `trading.accounts` | Broker accounts (no secrets) |
| `trading.account_roles` | account_id, role (data/trading/portfolio), is_primary/standby, enabled, settings |
| `auth.*` | ASP.NET Core Identity tables (user, password hash, lockout) |
| `ops.commands` | Desired-state changes and requested actions, status, actor, timestamps |
| `ops.audit_log` | Every login, configuration change, approval and service action |
| `workflow.approvals` | Item type, item id, decision, modifications, actor, decided_at, cutoff |
| `workflow.suggestions` | Source (AI / engine), type, payload, reason, expiry, status |
| `ops.service_runs` | Service/job runs with schedule, start/end, outcome, catch-up flag |
