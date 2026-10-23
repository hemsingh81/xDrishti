# P7-14 — Auto-journal: the stored fills matched to approved tickets, the rest tagged discretionary, a fill grade beside the plan grade

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; owner answers of 2026-10-23 binding: no orders in P7, paper only, no auto-approval, no automatic learning change). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-14.
- **Design refs:** docs/design/12-portfolio-monitoring.md §8 (plan link, discretionary tag), 10 §8, 09 §9 (basis fills), 04 §1 (the Trading role journals fills; execution is later); [0083](../adr/0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (plan record, items, allocations), [0090](../adr/0090-grading-replays-each-ticket-through-the-simulator-on-its-own-session-and-stores-one-insert-only-grade.md) (plan-price grade); ADR 0093 (this slice).

## Assumptions (stated up front)
1. **Safety: this slice only READS.** It reads `portfolio.transactions` (the fills the existing broker trade-history sync already stored, ADR 0025) and the plan record, and writes three insert-only tables of its own. It adds no Dhan endpoint, holds no credential, writes nothing to the portfolio, changes no ticket state and places no order. Manual-live data present in the sync is read like any other fill and is **never turned into a ticket**: a fill with no ticket is a discretionary journal row, nothing more. Nothing here touches orders, credentials or broker writes (the stop rule of the task was checked: no new broker call exists).
2. **`portfolio.transactions` has no `plan_item_id`** (checked 2026-10-24: the column exists in no migration; P7-00 marked it UNVERIFIED). The link is therefore its own table (`trading.journal_fills`), not a change to the portfolio table, which stays untouched.
3. **What a "fill" is.** A transaction with source `broker` that is not voided. Hand-typed (`manual`), file (`csv`) and bill-level (`bill`) rows are not fills of the sync and are not read. Only accounts that are Trading-enabled or hold an allocation are read.
4. **Which tickets a fill can match.** A primary or reserve ticket of the newest plan version of the date whose newest state is approved, armed, triggered or closed, and whose approval row exists, **once per paper allocation (account)**. A rejected, lapsed, modified, invalidated or expired ticket is not matched: its fills are discretionary.
5. **Match key: account, instrument, side and day.** Account = the allocation's account; instrument = the normalised trading symbol (`NSE:` prefix and `-EQ` suffix removed, upper case) of the item and of the trade; side = the ticket's side for an entry; day = the planned session. A fill matches **at most one** ticket; a ticket may have several fills. A fill before the ticket's approval instant (when the fill has a time) is not the ticket's. **Ambiguity is never guessed:** a fill that fits two or more approved tickets (same account, symbol, side, day) is left unmatched with the reason, not stored and listed by the verb.
6. **Exits.** An exit is a fill of the other side on the same account and symbol, strictly after the first entry fill (the same day only when both have a time), at most 30 calendar days later, first-in first-out up to the entered quantity, never also an entry. Stated shortcut: a sale of shares held before the ticket cannot be told from the ticket's own exit; the quantity cap bounds the error.
7. **Fill grade.** Only a finished round trip (exit quantity reaches entry quantity) is graded, and only once the **plan-price grade of the ticket exists**, so the two sit side by side. Plan levels are the plan grade's simulated entry and initial stop (the plan ticket stores levels as text only); 1R = their distance. `slippage` = side-signed fill entry minus plan entry (positive is worse for the owner), also in R; R = (gross profit minus the charges the sync recorded) / (1R x quantity). If the plan grade is not a trade, or was stored before this slice (it has no stop), the round trip is stored as `no_plan_levels` with the rupee result and **no R**; never invented. The label is `realised fills, not simulated`. Grades stored before this slice have no stop: `trading.grades` gets a nullable `stop_price`, set from now on.
8. **No look-ahead.** The journal for a session reads only fills dated from that session up to today (India date) and refuses a session after today. An exit counts only if it is strictly after the entry it closes. A fill grade uses only the links stored and the plan grade already stored.
9. **Idempotent and insert-only.** A trade has one journal row (unique), a ticket-on-an-account one entry, an entry one grade; a second run writes only what is new. A first link or grade is never changed: if a fill arrives late and would change a decision already stored, the stored one stays (stated limit). Sessions are processed oldest first so an exit is linked by its entry's session before a later session could tag it discretionary.
10. **Not wired into the nightly service this slice** (the verb and the read endpoint are; the roadmap row says after the 16:30 sync): a service entry is a follow-up. Post-trade prompts are not built (a notification, not a form: later with the notifications work).

## Goal
After the owner's fills are synced, each approved ticket shows what was actually done (entries, exits, slippage, realised R) beside what the plan would have returned, and everything else the owner traded is reported separately as discretionary, reproduced by hand on a fixture day.

## Scope
**In:** Domain `Planning`: `JournalFill`, `JournalTicket`, `JournalRole`, `JournalLink`, `JournalResult`, `JournalUnmatched`, `JournalMatcher` (pure), `FillGrade`, `FillGrader` (pure), `TicketGrade.StopPrice`; Application: port `IJournalStore`, handlers `JournalPlansHandler` (command) and `GetPlanJournalHandler` (query), views, log ids 4750 to 4752; Infrastructure: `JournalStore`, records and configurations, migration `AddJournal` (with `Down`; `trading.journal_entries`, `journal_fills`, `fill_grades`, nullable `grades.stop_price`; insert-only for `xd_app`); Api: `GET /api/plans/{id}/journal`, `xd plan journal [--date]`; tests; ADR 0093; docs and roadmap row; `scripts/gen-api.sh`.
**Out:** the nightly service entry, post-trade prompts, partial-exit grading of open positions, grading discretionary trades (reported, not graded), the screen, any change of a ticket state, any order, any learning change, any new broker endpoint.

## Acceptance criteria
1. A fixture day of stored fills is matched by account, instrument, side and day (entries, an exit, discretionary), and the fill grade for a hand-computed round trip is exact (R and slippage in price and R).
2. An ambiguous fill (two approved tickets fit) is left unmatched with a reason and nothing is stored for it; a fill of another account, symbol, side or day, before the approval, from a typed-in or voided row is never an entry.
3. A fill with no approved ticket is tagged discretionary and reported apart from the tickets.
4. Running twice writes nothing new; a fill grade whose plan grade came later is written on the later run and the first grade is unchanged.
5. `xd_app` can select and insert the three tables but not update, delete or truncate (grants test); the database refuses a second link for one trade, an entry without an allocation or for another plan, a discretionary fill with an entry and a fill grade with the wrong label.
6. No look-ahead: the window of fills never passes today, an exit must be strictly after the entry, a future session is refused.
7. Determinism: the same fills and tickets in any order give the same links.
8. The journal writes nothing to the portfolio and moves no ticket; no type can place, modify or cancel an order (architecture rule of ADR 0083 stays green).

## Contract
- **API:** `GET /api/plans/{id}/journal` (entries with fills, fill grade and plan grade side by side, the day's discretionary fills, the count of approved tickets without fills; 404 `plans.not_found`). Read only.
- **CLI:** `xd plan journal [--date YYYY-MM-DD]` (no date: the newest 30 plans up to today); prints the counts and every unmatched fill with its reason; exit codes as `xd plan grade`.
- **Data:** `trading.journal_entries` (unique item + account, FK to the item's plan and to the allocation), `trading.journal_fills` (unique trade, FK to `portfolio.transactions`, role check), `trading.fill_grades` (unique entry, label check); `trading.grades.stop_price` nullable. All insert-only for `xd_app`. Log ids 4750 to 4752.

## Non-functional
Reads fills of one session plus a 30-day exit window per plan and tens of tickets; no external call. Not measured on the owner's data (UNVERIFIED: the sync has not yet stored a real fill for an approved ticket, as no paper ticket has been traded).

## Test plan
Unit: `JournalMatcher` (fixture day, several fills, ambiguity, two accounts, before approval, exits by order/quantity/day, shared exits, order independence, discretionary), `FillGrader` (buy, sell, weighted prices, partial exit quantity, open, plan without trade, plan without stop), `JournalPlansHandler` with fakes (approved family only, ambiguity reported, window never past today, future date refused, grade beside plan grade and pending, no ticket moved, deterministic). Integration (real DB): fixture day end to end, idempotency and late plan grade, ambiguity stores nothing, portfolio and trail untouched, the endpoint with both labels, the database refusals, grants.

## Open questions
None blocking. For the owner later: the nightly service entry after the 16:30 sync; whether the post-trade prompt wants a notice; whether discretionary trades should be graded in a discipline report (P8).

## Done when
- [x] Criteria have evidence; `scripts/check.sh` green (see the run record in the report)
- [x] ADR 0093, roadmap row, docs built, `scripts/gen-api.sh` run
