# P1-16d — Dhan consent flow only, simplified accounts, account status in the header

- **Status:** Approved (owner, 2026-10-11: "only use Dhan (API Key & Secret — Generate Consent) flow, rest not needed … simplify … show all added accounts and status in the header with connect option … remove unused code")
- **Phase / roadmap row:** P1 — simplification of P1-16b/P1-16c · decision [ADR 0009](../adr/0009-dhan-consent-flow-only.md)
- **Design refs:** Dhan auth guide (generate-consent, consentApp-login, consumeApp-consent); docs/design/04 §1, 05 §7/§8, 13, 16, 17

## Analysis (what stays, what goes)
| Area | Keep | Remove |
|---|---|---|
| Broker | Dhan (`broker` column stays, value `dhan`) | Upstox everywhere: enum value, connector, profile, endpoint allow-list entries, callback route, 03:30 expiry rule, UCC adoption, tests |
| Wizard | Identity, Dhan app (client id, API key, API secret, key issued-on date, redirect URL + steps), Roles, Role settings, Review, **Create and connect** | Choose-broker step (single broker), broker radio cards, per-broker field switching |
| Connection | **Connect/Reconnect** (consent flow), **Disconnect**, status + expiry + countdown, last error, 25/day guard, client-id match, encrypted vault (api_key, api_secret, access_token) | Paste token (+ dialog, endpoint, errors), Renew, Test connection, extended token, token `source`, `GET /api/brokers` catalogue (Dhan facts become constants in the UI), ReconnectBanner |
| Header | — | — |
| New: header | Accounts menu in the top bar: every account with colour dot, name, status chip; **Connect/Reconnect** button for each account that is not connected (needs API key/secret, otherwise "Add API key & secret" → Connection tab); updates after connect | — |
All other modules (roles, trading/portfolio/data settings, risk profiles, settings page, step-up, audit) stay as they are.

## Acceptance criteria
1. Only Dhan can be created (`broker` = `dhan`; anything else → `accounts.invalid` field `broker`, or the field is dropped from the contract — choose the simpler, keep the DB column). The wizard has no broker step: steps are Dhan app → Identity → Roles → Role settings → Review.
2. Removed endpoints/handlers/connectors/tests/docs for: Upstox, paste token (`PUT /accounts/{id}/token`), renew, test connection (`POST /token/test`), `GET /api/brokers`, Upstox callback. Remaining: `POST /accounts/{id}/connect` (step-up), `GET /api/brokers/dhan/callback`, `DELETE /accounts/{id}/token` (step-up), and `GET /api/brokers/dhan` → `{redirectUrl}` (the deployment-dependent redirect URL shown in the setup guide). Allow-list = the two Dhan server calls only (`generate-consent`, `consumeApp-consent`); the login page is a browser URL.
3. `connection` in the API: `{status: not_connected|connected|expired, expiresAt, brokerUserName, brokerUserId (masked), lastConnectedAt, lastError}` — no `source`, no `lastCheckedAt`. Vault names: `api_key`, `api_secret`, `access_token`. New migration tightens/clears what is no longer used (data is empty); applied migrations untouched.
4. Header (`TopBar`): an accounts control showing a summary ("2 accounts · 1 needs connecting") that opens a menu/popover listing every account (dot + name + status chip with text + expiry for connected) and a Connect/Reconnect button per not-connected account; button starts the consent flow (step-up dialog on 403, then browser navigation); while loading/error it degrades quietly (no layout shift, error text). No accounts → "Add account" link to `/accounts`. Initial bundle ≤ 200 KB gzip (the control is lazy-loaded through the accounts feature's public `index.ts`).
5. Connection tab keeps one Connect/Reconnect + Disconnect and the broker-app section (secrets replace, key expiry); the ReconnectBanner is removed.
6. No dead code: `scripts/check.sh` green with unused exports/files removed (grep for Upstox, paste, renew, extended, source, brokersQuery returns nothing in code or tests); docs 04/05/13/16/17, ADR 0008 status note, specs updated; `scripts/gen-api.sh` and `scripts/build-docs.sh` run.
7. Tests updated/added: backend (connect, callback incl. mismatch/replay/expiry/limit, disconnect, leak scan, allow-list, access matrices); frontend (wizard without broker step, header menu states: none/connected/not-connected/needs-secrets, connect from header with step-up, Connection tab, removed UI absent).

## Non-functional
Security unchanged from ADR 0008 (write-only secrets, no secret in responses/logs/audit/URLs). Accessibility: menu is a button with `aria-haspopup`, items are real buttons, status is text + icon.

## Done when
- [ ] All acceptance criteria have evidence (browser walkthrough + real Dhan login pending) · [x] `scripts/check.sh` green; reviewer: no blockers/majors (2026-10-11; minors fixed) · [x] Contract regenerated · [ ] Deployed (✅ migration applied) and verified in the browser (pending) · [x] Docs updated
