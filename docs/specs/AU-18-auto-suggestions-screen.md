# AU-18 — Auto and next-day suggestions screen

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-18 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-16, AU-17
- **Decisions:** [ADR 0244](../adr/0244-one-auto-switch-runs-the-chain-and-only-suggests.md)
- **Design refs:** docs/design/13; prototype Today

## Goal
The owner flips one switch, picks what to cover, and reads tomorrow's suggestions with reasons.

## Scope
**In:** A `/auto` page and a Today tile: switch with password dialog, scope picker, chain status, suggestion cards.

**Out (not in this slice):** Approving or arming anything from this page.

## Acceptance criteria
1. The switch states what will happen and that no order is placed, before asking for the password.
2. Suggestions can be filtered by basket or by stock, each card with its reason and evidence expandable.
3. Fail-closed or stale nights say why and when data was last good, with a link to the recovery screen.
4. The page has no approve, arm or send control.
5. Loading, empty, error, phone, a11y.

## Contract
- **API:** reads AU-16 and AU-17.
- **Data:** none.
- **UI:** Lazy route `/auto`, tile on Today.
- **Events/jobs:** none.

## Non-functional
First-load budget unchanged.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Vitest with MSW; a test that no order-like control exists.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
