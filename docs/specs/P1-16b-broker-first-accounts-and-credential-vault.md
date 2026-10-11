# P1-16b — Broker-first accounts: choose broker, broker-specific setup, encrypted credential vault

- **Status:** Approved (owner, 2026-10-11: "make the Accounts page broker-first … based on Dhan and Upstox documentation"; answers: secrets in the wizard encrypted, configure + connect,
  token flows as the documentation says, move role matrix & risk profiles out of the Accounts page)
- **Status note:** narrowed by [P1-16d](P1-16d-dhan-consent-only-and-header-accounts.md) (ADR 0009): Dhan only, consent flow only; Upstox, pasted/renewed/tested tokens and the broker catalogue are removed.
- **Phase / roadmap row:** P1 — revision of P1-16 (Accounts) · decision [ADR 0008](../adr/0008-broker-credentials-vault-and-connection.md) · followed by [P1-16c](P1-16c-connect-dhan-and-upstox.md)
- **Design refs:** docs/design/04 §1; 05 §1/§7 (brokers); 16 (accounts); 17 (secrets); Dhan auth guide; Upstox authentication; prototype `docs/prototype/` → Accounts

## Goal
Adding an account starts with the broker. The wizard then asks only for what that broker needs, explains where to get it, and stores the broker's API key and secret
encrypted — never shown again. The Accounts page is only about accounts; shared risk profiles and the role matrix live under Settings.

## Scope
**In:** `Broker.Upstox`; broker profiles (backend + frontend); encrypted vault (`IBrokerSecretVault`, AES-256-GCM, key from secret file); write-only secret fields on create and on
the Connection tab; removal of credential references (domain, API, DB via a new migration, scripts, deploy); broker-first wizard; Accounts page without role-matrix / risk-profile
views; new `/settings` page (Risk profiles · Account roles); docs.
**Out:** talking to the brokers (P1-16c); TOTP/PIN; market data / portfolio sync.

## Broker profiles (from the brokers' docs)
| | Dhan | Upstox |
|---|---|---|
| Identity | **Client id** (required, e.g. `1000000001`) | **UCC / user id** — optional, filled in automatically after the first login |
| Secrets (write-only) | **API key** (`app_id`), **API secret** (`app_secret`) — valid 12 months; owner enters the date generated (default today) → warning 30 days before expiry | **API key** (`client_id`), **API secret** (`client_secret`) |
| Setup steps shown | web.dhan.co → My Profile → Access DhanHQ APIs → toggle *API key* → app name, **Redirect URL** = `http://localhost:8080/api/brokers/dhan/callback` (copy button), Postback optional | account.upstox.com/developer/apps → New app → **Redirect URI** = `http://localhost:8080/api/brokers/upstox/callback` (must match exactly; copy button); notifier URL not needed |
| Notes shown | Static IP is needed only for placing orders (not for data/portfolio) | Token valid until 03:30 IST next day |

The redirect base URL comes from configuration (`Brokers__PublicBaseUrl`, default `http://localhost:8080`).

## Acceptance criteria
1. **Vault:** secrets are stored in `accounts.broker_secrets (account_id, name, ciphertext, key_version, updated_at)` encrypted with AES-256-GCM (random 96-bit nonce, AAD = account id + name);
   decrypting with the wrong account/name fails; the key is read from `BrokerVault__Key` (≥ 32 chars, derived with HKDF-SHA256) and the API refuses to start without it; values never
   appear in responses, logs, audit text, exceptions or `ToString()`. `xd_app` has DML on the table.
2. **Create:** `POST /api/accounts` (step-up) takes `broker` plus broker-specific `clientId` (required for Dhan, optional for Upstox), `secrets {apiKey, apiSecret}` (both required),
   `apiKeyIssuedOn` (Dhan, optional, default today), identity, roles and role settings; invalid input → `accounts.invalid` with field errors (`secrets.apiKey`, …). The response and
   `GET` show `secrets: [{name, set, updatedAt}]` and, for Dhan, `apiKeyExpiresOn`.
3. **Update secrets:** `PUT /api/accounts/{id}/secrets` (step-up) replaces any given secret (omitted = unchanged; empty string = invalid); audited as "API key replaced" without values.
4. **Credential references removed:** column, index, `GET /accounts/{id}/credentials`, `GET /accounts/credentials/{ref}`, `scripts/broker-secrets.sh`, `brokers.index` handling and the
   `brokers` mount are gone; a new migration (never editing applied ones) drops them and adds the vault table and `api_key_issued_on`. Existing accounts show "API key: not set".
5. **Broker-first wizard:** step 1 *Choose broker* (cards: Dhan, Upstox — logo-free text cards with one-line description); step 2 *Broker app* (that broker's fields + setup steps
   + redirect URL with copy button, secret fields masked with show/hide, never pre-filled); step 3 *Identity* (name suggested from broker, colour, notes); 4 *Roles*; 5 *Role settings*;
   6 *Review & create* (secrets shown as "•••• set"). Changing the broker in step 1 clears broker-specific fields. After creation the account opens with a *Connect* call to action (P1-16c).
6. **Accounts page:** list + detail only (tabs Overview · Roles · Trading · Portfolio · Data · Connection · Activity); the Role matrix and Risk profiles views are removed from it; the
   Connection tab shows broker, client id (masked), secret status (set/updated, Dhan key expiry) with *Replace* (write-only dialog, step-up), and the broker's setup steps.
7. **Settings page:** `/settings` (no longer "coming soon") with tabs **Risk profiles** and **Account roles** (the existing views, unchanged behaviour); nav entry stays under System.
8. Docs: ADR 0008, doc 04 §1, 05 (Upstox enabled for accounts), 17 (vault key, no brokers mount, egress network from P1-16c), 16 (accounts fields); `scripts/README.md`, `deploy/README.md`.

## Contract
- `POST /api/accounts` **S** — as AC 2. `GET /api/accounts`, `GET /api/accounts/{id}` — `broker`, `maskedClientId` (null when not set), `secrets[]`, `apiKeyExpiresOn?`.
- `PUT /api/accounts/{id}/secrets` **S** `{apiKey?, apiSecret?, apiKeyIssuedOn?}` → `AccountDetail`.
- `GET /api/brokers` → profiles `[{broker, name, clientIdLabel, clientIdRequired, secretFields[{name,label}], redirectUrl, setupSteps[], notes[], apiKeyValidityMonths?}]` (static, from code).
- `PUT /api/accounts/{id}/connection` **S** — no longer takes `credentialRef`.
- Data: `accounts.broker_secrets` (PK account_id+name, FK cascade), `broker_accounts.api_key_issued_on`, drop `credential_ref` + its index. Errors unchanged (`accounts.*`).

## Non-functional
Security: write-only secrets, AES-GCM, key only in Keychain/secret file, step-up on every secret write, no secret in any response/log/audit (tests scan bodies, audit rows and logs
for a marker). Performance: tiny tables. Accessibility: cards are radio buttons with labels; secret inputs have show/hide toggles with labels.

## Test plan
Domain/application unit tests (broker rules, secrets validation, Dhan expiry), vault unit tests (round trip, wrong AAD/key fails, nonce uniqueness, no plaintext in ToString),
integration (create per broker, secrets write-only and never in bodies/audit/logs, replace secrets, migration drops refs, `GET /brokers`), architecture; frontend (broker choice →
fields per broker, clearing on change, secret fields never pre-filled, review masks, Connection tab replace dialog, Settings page tabs). Live: wizard for Dhan and Upstox.

## Open questions
None blocking. TOTP/PIN unattended login is a separate decision.

## Done when
- [ ] All acceptance criteria have evidence (browser walkthrough pending) · [x] `scripts/check.sh` green; reviewer: no blockers/majors (2026-10-11) · [x] Contract regenerated · [ ] Deployed (✅ `deploy/xd-up.sh`, vault key created, migration applied) and verified in the browser (pending) · [x] Docs updated
