# P1-15a — Portfolio baskets: the account buckets on the Baskets page, protected

- **Status:** Approved (owner, 2026-10-12: "It should show me other default baskets which are created when adding an account — those are for portfolio in our system, currently 'Dhan · Main · Portfolio' — and these should not be allowed to archive")
- **Phase / roadmap row:** P1 — extends [P1-15](P1-15-baskets-many-to-many-with-drag-and-drop.md) · decision [ADR 0014](../adr/0014-portfolio-buckets-appear-as-system-baskets.md)
- **Design refs:** docs/design/06 §3.1 (System baskets), §3.4; ADR 0011

## Goal
The Baskets page lists the portfolio bucket of every account (and custom bucket) as a protected system basket, with its current holdings as read-only members.

## Scope
**In:** read-only system baskets of kind `portfolio` from buckets + holdings; protection (archive/rename/recolour/reorder/add/remove/drop refused); copy-out to manual baskets; UI group and badges. **Out:** history/as-of for them, creating buckets from the Baskets page (stays in Accounts → Portfolio), other system baskets.

## Design (defaults — object to any)
1. **API** (session, no step-up): `GET /api/baskets` gains `kind: manual|portfolio` on every item; portfolio items come first (account, then name), are never archived, carry `system: true`, `memberCount` (instruments with quantity > 0 in the bucket), `colour` of the account (or the neutral default) and `accountId`/`accountName` where the bucket is an account's default. `GET /api/baskets/{id}/members` accepts a bucket id and returns the holdings' instruments as `InstrumentSummary` with `quantity` and `accountName`; `asOf` on a system basket → 400 `baskets.invalid`. History → 400 `baskets.invalid`. Any change route (PUT, DELETE, members add/remove/order, order, transfer with it as target or as a `move` source) → `409 baskets.system`. `transfer` with mode `copy` and a system source is allowed. `PUT /api/baskets/order` lists manual baskets only.
2. **No new tables.** Read straight from `accounts.portfolio_buckets` and `portfolio.holdings` (module boundary: through an application port, not a cross-module DbContext shortcut).
3. **UI:** a "Portfolio" group above "Baskets" in the left column with a lock/portfolio badge and the account name; no drag handle, no edit/archive/history controls, no drop highlight; members grid read-only with quantity; row drag/menu offers **Copy to…** only; copy toast offers Undo as usual; empty state "No holdings in this bucket yet — they arrive when the account's portfolio is imported".

## Acceptance criteria
1. Each active portfolio bucket appears on the Baskets list as `kind: portfolio`, `system: true`; archived/role-off buckets do not; names follow account renames.
2. Members are the bucket's current holdings (quantity > 0), correct counts, read-only.
3. Every change on a system basket is refused with `409 baskets.system`; archive is impossible from API and UI; manual baskets behave as before; `PUT /baskets/order` unaffected.
4. Copy from a system basket to a manual basket works (auto-track, retired refusal as before); move from it is refused.
5. Tests: domain/application, integration (list merge, members, every refused route, copy-out, permissions/401), frontend (group, protections, copy-out, empty state); `scripts/gen-api.sh`, `scripts/check.sh` green; reviewer; deployed; docs 06 updated.

## Done when
- [x] backend built and tested (criteria 1–4 on the API: `PortfolioBasketsApiTests`, `PortfolioBasketHandlerTests`); docs 06 §3.1/§3.4 updated; `plan-data-1.js` P1-15 row updated
- [x] contract regenerated (`scripts/gen-api.sh`)
- [x] frontend (group, protections, copy-out, empty state): `Portfolio.test.tsx` (23 tests: group and order, protections, copy-out by menu / keyboard / mouse with Undo, move disabled, empty state, targets, stray 409, phone select, URL), `AddToBasket.test.tsx` (Instruments targets), `lib.test.ts`; typecheck, ESLint, Prettier, Vitest (971) and `npm run build` green
- [ ] check.sh green + reviewer · [ ] deployed and verified

### Built (backend) — decisions where the spec was silent
- A system basket's id is the bucket's id; a bucket that is not listed (archived, role off) is `404 baskets.not_found` on every route.
- `position` of system baskets counts 0..n-1 inside their group; `purpose` is `long_term`; `colour` is the account's colour or `#6366f1`.
- Members are grouped per instrument: a custom bucket holding one instrument in several accounts gives one member with the quantities added up and the account names comma-separated. Holdings without an instrument of the master list are skipped (list counts and members alike).
- `GET /api/instruments/{id}/baskets` stays manual-only.

### Built (frontend) — decisions where the spec was silent
- The first of the owner's baskets is still selected when the URL names none (the portfolio group sits above it); portfolio baskets are selected only by click or `?basket=<bucket id>`.
- A portfolio row keeps a grip (labelled "Drag NSE:TCS to copy") because drag-to-copy needs a handle; it never reorders. Alt is ignored for a portfolio source.
- A look-back date in the URL is kept for the owner's baskets and not sent for a portfolio basket (no 400).
- Group headings are "Portfolio" and "Your baskets", shown when there is at least one portfolio basket.
