# P1-16e — One default portfolio bucket per Portfolio account; no manual "history from" date

- **Status:** Approved (owner, 2026-10-11, after trying Accounts → Portfolio: "why are these buckets predefined? only a default bucket per Portfolio account, named like 'Dhan · Main · Portfolio' … and the import date should come from Dhan automatically, not be typed, so XIRR and returns are accurate")
- **Phase / roadmap row:** P1 — Accounts (revision of P1-16 portfolio settings); prepares P2-01/P2-04/P2-05 (sync, buckets, XIRR) · design refs docs/design/04 §1, 12 §3–§5

## Analysis
1. **Seeded buckets.** P1-16 seeded three portfolio buckets (`Trading`, `MyLongTerm`, `MyChoice`) from doc 12 and the account form forces the owner to pick one ("Choose…"). The owner has no use for presets: what is needed is one bucket per account that holds its imported holdings, created automatically, named after the account.
2. **"Import trade history from" date.** The field asks the owner for something they cannot know ("not sure from when I have those holdings"). XIRR, % return and absolute return need the **date and size of every cash flow**. What Dhan provides (docs, read 2026-10-11):
   - `GET /v2/holdings` → quantity, **average cost**, last price, ISIN — **no purchase date**;
   - `GET /v2/trades/{from}/{to}/{page}` → every executed trade with exchange time, side, price, quantity, charges (paginated by date range);
   - `GET /v2/ledger?from-date&to-date` → dated credits/debits (fund transfers, charges, settlements).
   So purchase dates must be **derived by the sync from trades (and the ledger for cash flows)**, walking back in date windows until Dhan returns nothing; the earliest date found is *stored*, never typed. Holdings that predate Dhan's trade history (e.g. shares moved from another broker) have no dated trade: they are shown with an explicit "purchase date unknown" state (opening position at the first sync date, XIRR flagged as approximate, optional per-holding date override later) — never silently guessed. The depth Dhan serves for trade history is not documented and is probed by P2-01.

## Scope
**In (this story, no Dhan portfolio calls yet):** default bucket per Portfolio account; removal of the seeded buckets and of the manual "history from" input (UI, API, DB); read-only explanation in the Portfolio tab; docs. **Out:** the sync itself (P2-01: holdings/trades/ledger import, FIFO lots, XIRR) and the per-holding date override.

## Design (defaults — object to any)
- **Default bucket:** whenever an account has the Portfolio role (on create, or when the role is enabled), exactly one default bucket exists for it, named `"{account name} · Portfolio"` (e.g. `Dhan · Main · Portfolio`), linked to the account (`default_for_account_id`, unique). Renaming the account renames the bucket (the suffix stays); disabling the Portfolio role keeps the bucket (its holdings and history stay) but hides it from pickers while the role is off; archiving the account archives its bucket; a name collision gets a numeric suffix. It is the bucket imported holdings go to unless another is chosen.
- **Seeds removed:** the migration deletes the three seeded buckets when unreferenced (the owner's current account is re-pointed to its new default bucket); no buckets are seeded any more.
- **Custom buckets:** the owner can still add more buckets later (kept as is, "Add bucket"), and may pick one as an account's default for imports; the account's own default bucket is always available and never empty ("Choose…" disappears).
- **History date:** `portfolio_settings.history_from` and the form field/API property are removed. The Portfolio tab shows, instead of the date input, a short factual note: "Dates come from Dhan automatically — your trade history and ledger are read when the portfolio sync is built (P2-01). Holdings Dhan cannot date are flagged so returns stay honest." (Later: shows "History found from {date}" once synced.)
- **Contract:** `PortfolioInput {defaultBucket?, includeInTotals}` (no `historyFrom`); account/portfolio views expose `defaultBucket` (name) and `autoBucketName`; `GET /api/portfolio-buckets` returns `{id, name, isAccountDefault, accountId?, accountName?}` and hides buckets of accounts whose Portfolio role is off; `POST /api/portfolio-buckets` unchanged.

## Acceptance criteria
1. Creating an account with the Portfolio role creates `"{name} · Portfolio"`; enabling the role later creates it if missing (idempotent, never duplicates); renaming the account renames it; archiving archives it; role off hides it; collision suffix works — covered by tests incl. a concurrency test on the unique link.
2. A fresh database has **no** seeded buckets; the migration cleans the existing three (when unreferenced) and re-points the existing account's default bucket; applied migrations untouched; `history_from` column dropped.
3. API/contract no longer contain `historyFrom`; the Portfolio tab and the wizard's role-settings step no longer show a date field; the default-bucket selector always has a valid value (account default pre-selected, custom buckets selectable).
4. Portfolio tab explains where dates will come from (text above); wizard Portfolio step shows the same note; accessibility intact.
5. Docs updated: 12 §3 (how acquisition dates are derived from Dhan trades/ledger, unknown-date state, probing depth in P2-01), §5 (default bucket per account, no presets), 04 §1 (account portfolio settings), 16 (accounts fields); `scripts/gen-api.sh`, `scripts/check.sh` green; reviewer no blockers/majors; deployed and checked in the browser.

## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer: no blockers/majors · [ ] Deployed and checked in the browser
