# 0083 — The plan record: insert-only state trails, owner-only approvals, lapse by default, and no order by name

- **Status:** Proposed (spec [P7-03](../specs/P7-03-plan-record-and-state-machines.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0073](0073-the-learning-changelog-and-the-cell-lifecycle-the-log-is-the-state.md) (the log is the state), [ADR 0078](0078-lessons-a-finding-a-linked-fix-and-a-result-read-from-the-stored-experiment.md) (append-only record with a lock per subject). The first ADR of P7 is 0082 (written by another slice); this one is the plan record.

## Context
P7 turns stored evidence into a proposed plan for the next session and records what the owner decides. Doc 04 section 4.2 gives the ticket diagram; doc 19 section 5 says v1 places no orders. Before any pipeline or screen exists, the record, its transition tables and the rule that nothing can place an order must exist and be tested, because everything later (review, arming, grading) writes into them.

## Decision
1. **Six insert-only tables.** `trading.plans` (immutable header, unique `plan_key`, `(for_date, version)`), `plan_items` (primary, reserve, rejected with reasons, levels as text, evidence as columns), `plan_item_allocations` (size per Trading account, paper mode only for now), `plan_events` (the trail), `plan_runs` (one row per pipeline attempt) and `workflow.approvals` (the owner's decisions). `xd_app` has SELECT and INSERT only (`MakeAppendOnlyForAppRole`, a grants test on a dedicated server). New schemas `trading` and `workflow`.
2. **The state is the newest event.** No state column. A move locks the plan or item (advisory lock in the transaction), checks that the newest state is the one the caller saw, and inserts; of two concurrent moves one wins. The database also holds the legal pairs of both tables in a check on `plan_events`, and a test compares the check with the Domain tables on every pair, so the two cannot drift.
3. **Pure tables.** Tickets: 10 states, 13 moves. Plans: 8 states, 11 moves. Into Approved, Modified and Rejected only the owner moves; every other move is a service's. The plan state is bounded by its tickets. A rejected candidate is not a ticket and has no moves.
4. **Lapse is the default.** At the review cutoff every undecided ticket lapses; the plan is Approved only if the owner approved a ticket. There is no top-N arming and no promotion from the reserve. These are the safe defaults of P7-00 questions 3 to 5; changing them is the owner's decision and a new migration and ADR.
5. **Approvals are the owner's only.** `workflow.approvals.kind` has a check `= 'owner'` and the `ApprovalKind` enum has one member; there is no system, rule or automatic kind in this slice. An approval row is written in the same transaction as the ticket move it records, with actor, reason, optional confirmation reason, modifications (required for modify) and the cutoff.
6. **No order by name.** An architecture test scans every type and member name of the shipped assemblies for `PlaceOrder`, `SubmitOrder`, `ModifyOrder`, `CancelOrder` in any casing (underscores ignored) and is proved to fail on a planted member and type. It is a tripwire, not the barrier: the barrier is that no broker write endpoint is on the allow-list and no order-capable credential exists (doc 19 section 5). Every plan carries the sentence that armed is watched, not an order, as data, pinned by a test.
7. **Surface.** `GET /api/plans` and `/{id}` are read only. The write handlers exist for P7-10 and P7-11 and are not exposed. Log ids 4650 to 4653. Migration `AddPlanRecord` with `Down`.

## Consequences
- Every later P7 slice writes into tested tables and tested state rules; the pipeline, review and arming add no new way to approve.
- The trail answers "what did the system propose and what did the owner decide" from rows alone (the Decisions report of P7-13 reads it).
- Allocations to a manual-live account are refused by the database until a later migration relaxes the check on the owner's decision.
- **Shortcut:** the plan move reads ticket states and then writes under the plan's lock only; a ticket decided in between is seen by the next move, not this one. The rule is by name, so it stops the obvious order method, not a determined bypass.
- Not verified: behaviour on the owner's machine; no plan has been published by a pipeline yet.
