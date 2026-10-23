# 0090 — Grading replays each ticket through the simulator on its own session and stores one insert-only grade

- **Status:** Proposed (spec [P7-13](../specs/P7-13-grading.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0037](0037-simulation-conventions-and-the-two-stage-seam.md)/0038/0042 (replay, fills, costs), [0045](0045-the-result-store-sim-runs-and-sim-trades.md) (insert-only), [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (plan record, no order), [0088](0088-ticket-builder-kleene-classification-from-the-close-bars-after-the-close-dropped-selection-builder-off-by-default.md) (tickets). The owner's answers of 2026-10-23 hold: no orders in P7, paper only, no auto-approval, no automatic learning change.

## Context
Doc 10 section 8 wants the selector proved: rejected candidates graded, primary against reserve against rejected, top-1/3/5/all curves, and the owner's decisions compared with what the system proposed. A plan ticket carries levels as text, so grading cannot parse the text and walk the bars by hand without inventing a second simulator.

## Decision
1. **A ticket is replayed by the existing simulation use case, not re-derived from its text.** The stored strategy version (hash), the cell's timeframe, side and exit, and the instrument go through `RunSimulationQuery` with `From = To = F` (the planned session). The first trade of the ticket's side entered on F is the outcome; none is "not triggered". A swing ticket is read to the last closed session. The grade says `simulated on plan prices`: it is what the P3 rules would have returned, not an execution.
2. **No look-ahead.** The window is F; a trade entered on any other date is dropped by the pure `TicketGrader`. History up to the close of D is indicator warm-up only (the replay starts at the first stored session by ADR 0037 amendment 2, as the plan did); no fill, stop, target or exit comes from a bar at or before D. Bars after the exit cannot change a grade.
3. **Cost follows a stored sheet if configured** (`Planning:Grading:CostSheetId`), else gross, named on every grade. R is `RNet` when charged, otherwise `RFilled` (the convention of the cells).
4. **Not gradable is a stored result with a reason, never a guess:** an exit label that is not exactly one candidate spelling (`FIXED_RR{rr=2}`; a bare policy, an overlay or a spelling that does not read back is refused), a strategy version not stored, a symbol in no basket, a replay that refused or skipped, a trade dropped by sizing or cost.
5. **Pending is not written.** Bars that do not reach F, a position still open at the end of the data, or an entry order still pending leave the ticket for a later run.
6. **One insert-only table `trading.grades`**, unique on the ticket, composite foreign key to the ticket's own plan, a check that the basis is the label and that R and the exit exist exactly when graded. `xd_app` has SELECT and INSERT only (granted on the table, never by re-granting the schema). Writing twice is `ON CONFLICT DO NOTHING`: the first grade stays. Migration `AddGrades` has a `Down`.
7. **Pure report** (`GradeReport`): top-K per plan takes the K best-ranked primary and reserve tickets (the selector ranks only those); non-triggered and not-gradable tickets add 0 R and are counted apart. Groups: primary, reserve, rejected. Decision groups from the newest ticket state: approved, modified, rejected, lapsed, undecided (a modified-then-approved ticket counts as approved). There is **no auto-approved group**. Comparisons give the difference in mean R and a verdict word, with a note when a set has fewer than 30 triggered tickets; never a significance claim.
8. **Surface.** `xd plan grade [--date]`, `GET /api/plans/{id}/grade`, `GET /api/reports/decisions?from=&to=`. Log ids 4720 to 4721. Config reads only. Nothing changes a ticket state; no order, account, credential or approval is touched.

## Consequences
- The grade rests on the stored bars and the modelled fills: the first year of graded tickets are re-simulations, not execution (P7-00 honest limits).
- One replay per ticket reads that instrument's history; fine for tens of tickets a night, not measured on the owner's data.
- A pending ticket can wait for ever if its bars never arrive; it shows as pending in the plan's grade.
- **Not built here:** the nightly service entry (the verb runs it), grades on fills (P7-14), partial fills, a screen.
- The grade of the ticket is for the first qualifying trade on F; a strategy that fires twice on F is graded once.

## Alternatives considered
- **Parse the ticket text and walk the bars** — rejected: a second simulator that could drift from the first.
- **Update a grade when more bars arrive** — rejected: grades are final once the trade has left; insert-only.
