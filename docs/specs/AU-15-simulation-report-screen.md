# AU-15 — Simulation report screen

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-15 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-12, AU-14
- **Decisions:** [ADR 0243](../adr/0243-simulate-everything-is-training-data-fenced-from-the-hold-out-and-from-scorecard-evidence.md)
- **Design refs:** docs/design/13-reports-and-ui.md; prototype Reports

## Goal
One view to start the job, follow it, and read any report meanwhile.

## Scope
**In:** A `/reports/simulation` page: stock or basket picker, from-to, report type, progress bar, partial banner.

**Out (not in this slice):** Editing strategies.

## Acceptance criteria
1. Choosing stock or basket, a date range and a report type shows that report.
2. While the job runs, a banner says how complete the data is, and the report refreshes when the owner asks, not by itself.
3. Every page says these are training simulations and not evidence.
4. Start, pause, resume and cancel work and reflect state; the pre-run estimate from AU-12 is shown.
5. The hold-out part of a range is shown as locked.
6. Loading, empty, error and phone states; a11y by role.

## Contract
- **API:** reads AU-12, AU-13, AU-14.
- **Data:** none.
- **UI:** Lazy route `/reports/simulation`; reuses report components.
- **Events/jobs:** none.

## Non-functional
First-load budget unchanged.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Vitest with MSW; partial, locked and empty states.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
