# 0103 — The P6 phase check is also a read-only GET (no client replay time), and plan versus actual is a server read

- **Status:** Proposed (spec [P8-04](../specs/P8-04-plan-versus-actual-decisions-autonomy-and-phase-checks.md); built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** amends [0080](0080-the-phase-check-exit-criteria-from-stored-records-and-never-met-on-thin-data.md) decision 5 ("a verb, not an endpoint") and its rejected "endpoint for the phase check"; builds on [0090](0090-grading-replays-each-ticket-through-the-simulator-on-its-own-session-and-stores-one-insert-only-grade.md), [0093](0093-auto-journal-reads-the-stored-fills-matches-them-to-approved-tickets-by-account-instrument-side-and-day-and-stores-a-fill-grade-beside-the-plan-grade.md), [0099](0099-the-report-engine-is-a-pure-core-with-one-population-rule-six-honest-states-and-a-ranking-by-the-credible-lower-bound.md), [0100](0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md).

## Context
ADR 0080 kept the P6 check a verb so P6 would not grow an API. The Learning screen (P8) now shows both phase checks, and the owner would otherwise never see the P6 one in the app. The screen also needs the waterfall from the plan's R to the realised R, whose means and charges-in-R must not be computed in the browser.

## Decision
1. **`GET /api/learning/phase-check`** (`GetLearningPhaseCheck`) wraps the existing `EvaluatePhaseCheckQuery(ReplaySeconds: null)`: it returns `{ criteria: [{ number, name, verdict, detail, caveat }], limits, text, elapsedMs }`. It takes no parameter. A client cannot supply the replay seconds (it could print an unmeasured "replayed in 4 s"); the criterion that needs a replay time stays NOT EVALUABLE. It writes nothing; it re-reads two stored runs, so it can take seconds and says how long (`elapsedMs`). The "no endpoint" objection (a re-read on every page load) is accepted as a cost of opening the tab, not a reason to hide the check; the screen fetches it only when the Phase checks tab is open.
2. **`GET /api/reports/plan-vs-actual?from&to`** (`GetPlanVsActual`) joins the approved ticket lines of the plans in the range (newest 200 plans, newest version of each session) to the journal's fill grades, the plan-price grades and the approval rows, and hands plain lines to the pure `Domain/Reporting/PlanVsActual`. No new port, no migration.
3. **The chain** is plan prices, entry slippage, charges (rupees divided by quantity times the planned risk per share), a remainder "not attributed" (exit slippage and anything else) and the actual R after the recorded charges, as means over the same tickets, so plan - slippage - charges - remainder = actual. Exit slippage and the owner's overrides are listed as not decomposed with the reason, and never numbered. Fewer than `Reports:PlanVsActual:MinTickets` (15) graded tickets gives `insufficient` for every step and per-strategy figure; no graded ticket gives `empty`.
4. **Outside the chain:** uncertain, no-plan-levels and zero-risk fills are counted with their reason; approved lines without a matched fill are counted, and those whose plan-price grade triggered are a "missed trigger" count with their summed plan R.
5. **Approvals** are counted from the stored approval rows by kind; an automatic approval is counted under `auto`, never under the owner.
6. **Settings:** `Reports:PlanVsActual` (`MinTickets` 1 to 1000, default 15; `MaxRangeDays` default 366), validated on start. Log `report.read` (event 4803): kind, counts, milliseconds.

## Consequences
- Both endpoints are GET, session protected, read only.
- The Decisions figures keep the server's "indicative only" rule below 30 triggered tickets; the chain uses the stricter 15 on purpose, and the screen says which applies.
- The P7 and P6 checks stay separate lists; no composite score.

## Not verified
The cost of `GET /api/learning/phase-check` on the owner's data was not measured (the test database has no experiment). If it exceeds 5 s there, the tab should fetch it only on an explicit press (spec item 12). The plans list is read through `IPlanStore.ListAsync` (no range parameter), so a range older than the newest 200 plans is incomplete; the evidence limits say so when 200 plans were returned.
