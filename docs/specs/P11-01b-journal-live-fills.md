# P11-01b — Journal the live fills and read the uncertain trip in the live-versus-paper report

- **Status:** Approved (written and built 2026-10-29 on the owner's instruction; the deferral is stated in P11-01 "Amendments at build" 1 and ADR 0149). ADR 0154.
- **Phase / roadmap row:** P11 — Small live capital, slice P11-01b of [P11-00](P11-00-small-live-plan.md); the half of [P11-01](P11-01-manual-live-fill-capture.md) that was deferred, plus the `live_uncertain` reason that [P11-03](P11-03-live-vs-paper-slippage-report.md) deferred to it.
- **Design refs:** docs/design/09-learning-engine.md §3 (step 2) · 12-portfolio-monitoring.md (auto-journal) · ADR 0093 (journal), 0149 (live capture), 0152 (report).
- **Needs:** P11-01 (`trading.live_fills`, `live_fill_charges`), P7-14 (journal), P11-03. **Size:** 1 agent-day. **Safety:** no — reads stored rows and inserts into the journal's own insert-only table; no broker call, nothing places, changes or cancels an order.

## Goal
The intraday fills of a live account (the ones that never reach `portfolio.transactions`) are linked to their approved tickets by the same matcher as every other fill, exactly once, so that each live ticket has a journal entry and a fill grade, and the live-versus-paper report can leave out a round trip the journal doubts (`live_uncertain`) instead of reading an R from it.

## Scope
**In:** migration `AddJournalLiveFills` (`journal_fills.live_fill_id`, `transaction_id` nullable, one of the two, FK, unique index, two triggers); `IJournalStore.FillsAsync` also reads non-delivery live fills; the store reads a link from either source; `JournalPlansHandler` grades a round trip with unrecorded charges as `uncertain`; `ILivePairSource` carries the journal's doubt; the report excludes it as `live_uncertain`; tests; ADR.
**Out:** any new setting, endpoint, route or table; changing how paper or stored-trade fills are journaled; the history mapper that keeps intraday fills (charges for intraday stay unrecorded); the execution gap (it reads `live_executions`, which has no doubt); a visible "source" column in the journal API.

## Acceptance criteria
1. Given an intraday live fill of an account with an allocation, when the journal runs, then it is linked (entry, exit or discretionary) through `journal_fills.live_fill_id`, by the unchanged `JournalMatcher`; a second run writes nothing.
2. Given a live fill whose product is a delivery product (CNC, MTF, MARGIN), then it is never read from `live_fills` (it reaches the journal as the stored trade, ADR 0025/0149); given an intraday live fill that has a stored twin (same account, identity `t:yyyyMMdd:<broker_trade_id>`), then it is not read either. So no fill is journaled twice.
3. Given the database, then a link that names both sources or neither is refused (check), a second link for one live fill is refused (unique), a link to an entry of another account is refused (trigger, as for stored trades), and the same trade journaled through the stored trade and through its live twin is refused in either order of arrival (trigger).
4. Given a round trip with a live fill that has no `live_fill_charges` row, then its fill grade is `uncertain` with no R and says the charges are not recorded; with a charges row on every fill it is graded in R as before. An absent charge is never read as zero.
5. Given a journal entry with an `uncertain_reason` (position held before the entry) or a fill grade `uncertain` for another reason than charges, then the report lists the ticket once as `live_uncertain` with the reason, no pair, no R; an open trip is still `live_open` and a paper-open one `paper_open` (one reason per ticket).
6. Given paper accounts and stored trades, then their journaling is unchanged (the existing journal tests pass unmodified).

## Contract
No API change (`transactionId` of a journal fill view holds the live fill's id for a live fill; ids are uuids from different tables and cannot collide). New reason word `live_uncertain` in the existing exclusion list. No setting: live fills exist only once `Live:Capture:Enabled` is on, which stays OFF by default.

## Decisions
1. **Where do live fills enter the journal?** Through a second nullable foreign key on `journal_fills`, not by copying them into `portfolio.transactions`. Why: ADR 0025 c keeps intraday out of lots and XIRR. Rejected: a copy (changes returns).
2. **How is a double journal prevented?** By reading rules and by the database. Reading: delivery products and twinned fills are not read from `live_fills`. Database: a trigger in both directions. Rejected: reading rules only (a race with the history sync would raise an error late or double count).
3. **Unrecorded charges.** Graded `uncertain`, no R. Why: intraday fills have no charges row; reading 0 would make net R look better than it was ("unavailable never a pass"). Rejected: 0 charges with a note (a number that looks like a result).
4. **What is `live_uncertain`?** The journal's own doubt, the same one P7-14 already stores. Not the charges doubt, which says nothing about the position.

## Risks and honest limits
Until the intraday charges are read (a later history mapper), an intraday live round trip has a fill grade with no R. A delivery fill reaches the journal only after the history sync stored it. The position check (`PositionsBeforeAsync`) reads stored trades only, so a pre-existing position created by an intraday fill is not seen (intraday positions are squared off the same day). A ticket with no journal entry is not "certain": the report says nothing about it.

## Open questions
None.

## Done when
- [x] Acceptance criteria with tests; `scripts/check.sh` green
- [x] ADR 0154
