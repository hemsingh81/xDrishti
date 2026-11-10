# AU-04 — Recovery screen

- **Status:** Built 2026-11-09. Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-04 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-02, AU-03
- **Decisions:** [ADR 0240](../adr/0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md)
- **Design refs:** docs/design/13-reports-and-ui.md; docs/design/04 §3; prototype Services

## Goal
One screen answers what was missed, what was recovered, and what is still failing.

## Scope
**In:** Read endpoints, a `/services/recovery` page (linked from Services and the bell) and a Retry action.

**Out (not in this slice):** Editing schedules (the Services screen keeps it).

## Acceptance criteria
1. `GET /api/services/recovery` returns the latest pass and, per session and service, `missed`, `recovered`, `partial`, `still_failing`, `blocked`, `waiting`, `not_recoverable`, `superseded` or `out_of_reach` with a reason.
2. The page leads with three counts (missed, recovered, still failing), then a table grouped by session, oldest first.
3. A `not_recoverable` row says why in plain words and offers no retry.
4. `POST /api/services/recovery/retry` starts a pass (authenticated, CSRF, audited) and answers 409 `recovery.already_running` while one runs.
5. The empty state says nothing was missed and shows when this was last checked.
6. Loading, error with Retry, phone layout; status is never shown by colour alone.

## Contract
- **API:** `GET /api/services/recovery?days=` (1 to 90, default 14); `POST /api/services/recovery/retry` returns 202. `scripts/gen-api.sh` in the same change.
- **Data:** none.
- **UI:** Lazy route `/services/recovery`, feature `features/services`; first-load budget unchanged.
- **Events/jobs:** Polls every 5 s only while a pass runs.

## Non-functional
p95 under 150 ms.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Integration for the API; Vitest with MSW for every state and role-based a11y queries.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
