# AU-17 — The Auto switch

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-17 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** AU-02, AU-10, AU-13, AU-16
- **Decisions:** [ADR 0244](../adr/0244-one-auto-switch-runs-the-chain-and-only-suggests.md)
- **Design refs:** docs/design/10; docs/design/09 §14; CLAUDE.md approvals rule

## Goal
One switch runs the whole chain, from data to next-day suggestions.

## Scope
**In:** `auto.switch` state, the chain-enablement service, a precondition readout, and the wiring of `Planning:Builder:UseSelection`.

**Out (not in this slice):** Autonomy levels and ticket auto-approval (P7-12 stays separate and unchanged).

## Acceptance criteria
1. Default OFF. Turning ON needs a fresh password; turning OFF needs a session only (the safe direction).
2. ON resumes exactly the chain services listed in the ADR, stores what it changed, and sets the builder to selection; OFF restores only what it changed, leaving the owner's own changes alone.
3. ON is refused, naming each item, when a precondition is `unavailable` or failing: no scope, no connected Data account, no confirmed charge sheet if the chain needs costs.
4. Each state change is recorded with actor, time and setting snapshot; automatic steps are recorded `auto`, never as owner approvals.
5. Auto does not change `autonomy`, does not call `AutoApprovePlanHandler`, does not arm a ticket; a pin test proves it has no such dependency.
6. Auto cannot affect `live-*` or Execution: an architecture test fails on any reference from AU code to `XDrishti.Execution` or to order-capable ports.
7. Auto ON and a service later paused by the owner: the screen shows the chain as incomplete rather than hiding it.
8. With Auto ON and a fail-closed night, no suggestions are shown, and the reason is.
9. The `auto.enabled` control appears on the Essential Settings row (AU-09 AC3), with the password rule of AC1.

## Contract
- **API:** `GET /api/auto`; `POST /api/auto/on` (fresh password via `OwnerAct`); `POST /api/auto/off`.
- **Data:** Migration `AddAutoSwitch`: insert-only `planning.auto_switch_changes`.
- **UI:** Essential row on Settings.
- **Events/jobs:** Chain listed in ADR 0244; the recovery pass honours it.

## Non-functional
Switch answers in under 500 ms.

## Safety
**SAFETY.** Follows the owner's approval-auto rule. Learned tightening is active only with Auto ON. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit for preconditions; integration with restore-exact-state; architecture pins; a test that no order port is resolvable from the chain.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
