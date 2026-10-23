# P7-13 — Grading: primary, reserve and rejected tickets walked through the stored bars

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; owner answers of 2026-10-23 binding: no orders in P7, paper only, no auto-approval, no automatic learning change). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-13.
- **Design refs:** docs/design/10-trade-selection.md §8, 09 §9 (grade on fills; here plan prices), ADR 0037/0038/0041/0042/0045 (replay, fills, costs, insert-only runs), [0083](../adr/0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md), [0088](../adr/0088-ticket-builder-kleene-classification-from-the-close-bars-after-the-close-dropped-selection-builder-off-by-default.md); ADR 0090 (this slice).

## Assumptions (stated up front)
1. **Safety: this slice touches no order, account, credential or approval.** It reads plan tickets and stored bars and writes grades. It changes no state of any ticket and no learning setting.
2. **A ticket is replayed by the simulator, not re-derived from its text.** The stored strategy version (by hash) of the ticket, its cell (timeframe, side, style, exit label) and the instrument are run through the existing `RunSimulationQuery` over the window `[D, R]`, where F is the planned session, D the session the ticket was built on (the last trading session before F) and R is F for an intraday ticket and the last closed session for a swing ticket. The label is `simulated on plan prices`.
3. **Which trade is the ticket's (corrected after review).** The engine cuts trades by the day of the DECISION (ADR 0043 D6), not of the entry, so the window starts on D. A trade belongs to the ticket when it was decided on F (an intraday signal, or a daily signal that fills on the next session), or decided at the close of D on a daily-or-longer trigger (known at the close of D, filled at the open of F; ADR 0088 section 2). A trade decided before the close of D (an intraday trigger on D) used information the ticket did not have and is never graded. The first such trade by entry instant is the outcome. History before D is indicator warm-up only (the replay starts at the first stored session, ADR 0037 amendment 2). Nothing after the last closed session is read.
4. **Costs follow a stored charge sheet when one is in force** (`Planning:Grading:CostSheetId`; the default is unset, so grades are gross and say so). With a sheet the grade R is `RNet`, gross it is `RFilled` (the convention of P6 cells). The basis is stored on every grade.
5. **Exit label.** Only a label spelt as a candidate (`FIXED_RR{rr=2}`) can be replayed. A bare policy name, an overlay (`+time_stop`) or a label that does not parse is **not gradable** (final) with the reason; never guessed. Other final cases: strategy hash not stored, a trade dropped by the sizing or cost rule.
6. **Pending is not a grade.** Nothing is written, and the next run tries again, when: the stored bars do not yet reach F; a position is still open or an order still pending at the end of the read; the replay refused (any failure, such as a charge sheet not in force yet), skipped the instrument or met a flagged day; or the symbol is in no basket now. A grade, once written, is final (one per ticket, insert-only, idempotent); there is no superseding mechanism (ADR 0090 says why).
7. **Decision groups use the newest ticket state.** Approved (approved, armed, triggered, closed, invalidated, expired), modified, rejected, lapsed, undecided; only primary and reserve tickets have a decision. A ticket modified and then approved counts as approved (the trail has the rest). **No auto-approved group** (Q3). A group with fewer than 30 triggered tickets carries a note that it is indicative only.
8. **Top-K curves rank primary then reserve by plan rank**; rejected candidates are not ranked by the selector and are compared as a group. A ticket that did not trigger or is not gradable adds 0 R to a day and is counted separately.
9. **Not wired into the nightly service this slice** (the verb and the read endpoints are); the roadmap row says nightly: a service entry is a follow-up.
10. **Limits stated, not hidden.** A configured `CostSheetId` that is not stored fails the run (no silent gross fallback). The replay uses the default fill rules and replay options, not those of the run a cell's statistics came from. Ticket levels and the owner's modifications are not replayed (a modified-then-approved ticket is graded at the original plan prices). One position at a time: an earlier open position in the replay can block an entry on F, which is then "not triggered" with that reason (it is not the owner's book). Symbol to instrument goes through the non-archived baskets, first match wins. The decisions report reads at most 5000 graded tickets, newest first, the newest plan version of each date only, and says so when truncated; its notes give the not-gradable share and top reasons.

## Goal
Every ticket of a past plan, in all three groups, gets one stored, labelled grade; the owner can read the top-1/3/5/all curves and the decision groups with honest sample sizes, reproduced by hand on a fixture.

## Scope
**In:** Domain `Planning`: `TicketGrade`, `GradeStatus`, `TicketGrader` (pure: run result to grade), `ExitLabel` (label to exit flag), `GradeReport` (curves and groups), `DecisionGroup`; Application: ports `IGradeStore`, `IGradeSettings`, handler `GradePlanHandler`, queries `GetPlanGradeQuery` and `GetDecisionsReportQuery`; Infrastructure: `GradeStore`, entity and configuration, migration `AddGrades` (with `Down`, insert-only for `xd_app`); Api: `GET /api/plans/{id}/grade`, `GET /api/reports/decisions`, `xd plan grade [--date]`; tests; ADR 0090; docs and roadmap row.
**Out:** nightly service entry, grades on fills (P7-14), partial fills, the screen, any change of a ticket state, any learning change.

## Acceptance criteria
1. A hand-checked fixture day reproduces the per-ticket R, the top-1/3/5/all totals and the decision groups exactly.
2. No look-ahead: only trades the ticket could know are graded (decided on F, or at the close of D on a daily-or-longer trigger); an intraday trade decided before the close of D is never graded; bars after the last closed session are never read. Shown end to end with the real simulator on fabricated bars: a daily signal at the close of D filled at the open of F, a swing ticket decided on F filled on F+1, an intraday ticket decided and filled on F, and a position left open past the last closed session staying pending.
3. Pending is not written; a not-gradable ticket is stored with its reason; a not-triggered ticket is stored as such.
4. Grading twice stores one grade per ticket (idempotent); `xd_app` can insert and select but not update or delete grades (grants test).
5. Determinism: same inputs, same grades and report.
6. Every grade and report says `simulated on plan prices` and the cost basis.
7. No type here can place, modify or cancel an order (architecture rule of ADR 0083 stays green).

## Contract
- **API:** `GET /api/plans/{id}/grade` (grades of the plan's tickets with the day's curves and groups; 404 `plans.not_found`), `GET /api/reports/decisions?from=&to=` (curves and decision groups over the graded days; 422 `plans.invalid`). Both read only.
- **CLI:** `xd plan grade [--date YYYY-MM-DD]` (no date: every past plan with ungraded tickets, newest 30).
- **Data:** `trading.grades`, insert-only, unique per `plan_item_id`, composite FK to the item's plan. Log ids 4720 to 4729.
- **Setting:** `Planning:Grading:CostSheetId` (default unset = gross), `Capital`.

## Non-functional
One replay per ticket reads that instrument's history (about 270 sessions of minutes in the worst case); tens of tickets a night. Not measured on the owner's data (UNVERIFIED).

## Test plan
Unit: `ExitLabel`, `TicketGrader` (trade, none, other side, other date, costed and gross, skipped by cost), `GradeReport` hand fixture, `GradePlanHandler` with fakes (request shape, pending, idempotent, not gradable). Integration: migration, grants as `xd_app`, store idempotency, the two endpoints.

## Open questions
None blocking. For the owner later: the nightly service entry; whether to add Q3-style auto-approved group (not while no auto-approval exists).

## Done when
- [x] Criteria have evidence; `scripts/check.sh` green
- [x] ADR 0090, roadmap row, docs built
