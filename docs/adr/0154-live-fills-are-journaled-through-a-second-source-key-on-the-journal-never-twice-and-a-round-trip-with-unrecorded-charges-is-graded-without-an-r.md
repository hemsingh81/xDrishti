# 0154 - Live fills are journaled through a second source key on the journal, never twice, and a round trip with unrecorded charges is graded without an R

- **Status:** Accepted (decided in P11-01b under the owner's instruction; spec [P11-01b](../specs/P11-01b-journal-live-fills.md)).
- **Date:** 2026-10-29.
- **Relation to other ADRs:** completes the deferral of [0149](0149-the-live-capture-reads-the-days-trade-book-and-order-book-with-two-pinned-get-entries-stores-them-insert-only-and-gives-each-approved-ticket-one-final-outcome.md) and of [0152](0152-the-live-versus-paper-report-pairs-the-same-ticket-on-both-sides-in-r-on-the-plans-risk-per-share-judges-it-by-a-one-sided-non-inferiority-bound-and-is-computed-on-read-from-stored-rows.md) (`live_uncertain`); builds on ADR 0093 (journal) and ADR 0025 c (intraday stays out of the portfolio).

## Context
P11-01 stored every fill of the owner's day in `trading.live_fills` but left the journal untouched. Intraday fills therefore never had a journal entry or a fill grade, and the report had no uncertain trip to read.

## Decision
1. `trading.journal_fills` gets `live_fill_id` (FK `live_fills`, unique); `transaction_id` becomes nullable; a check requires exactly one of the two. Insert-only grants are unchanged (table level).
2. The journal reads live fills only when the product is not a delivery product, the account has an allocation, and no stored trade with the identity `t:yyyyMMdd:<broker_trade_id>` exists. A delivery fill is journaled as the stored trade, as before. The matcher, the roles and the exit window are unchanged; a live fill and a stored trade can be entry and exit of one ticket.
3. Defence in depth: the trigger that keeps a link on the account of its entry reads the account from either source, and a second trigger refuses the same trade through both sources in either order of arrival.
4. A live fill without a `live_fill_charges` row has unknown charges. A round trip using one is graded `uncertain` (rupee result, no R) with a marker sentence; it is not 0. A trip with a charges row on every fill is graded as any other.
5. `LiveTrip.Uncertain` carries the journal's doubt (`journal_entries.uncertain_reason`, or an uncertain fill grade that is not the charges marker); the live-versus-paper report excludes such a ticket as `live_uncertain`, after `live_open` and `paper_open`, before `no_plan_levels`.
6. Both reads agree with the trigger in either order of arrival. The stored query leaves out a stored trade whose live twin is already journaled; the live query leaves out a fill with a non-voided stored twin; the trigger ignores a voided stored twin, so a voided twin never blocks the live fill (it is the only live record then). The trigger's check-then-insert is not serialized (the journal runs as one service at a time; the unique keys still hold per source). `PositionsBeforeAsync` reads `portfolio.transactions` directly for earlier days, so excluding a same-day stored twin does not change it. The migration's Down deletes live-linked links first (the live fills stay).
7. No new setting, endpoint, table, route or API field; nothing calls a broker; paper and stored-trade journaling are unchanged.

## Consequences
Intraday live tickets now have journal entries; their fill grades carry no R until intraday charges are read. The execution gap is unchanged (it reads `live_executions`). A delivery fill still waits for the history sync to reach the journal.
