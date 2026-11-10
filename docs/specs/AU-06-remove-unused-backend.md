# AU-06 — Remove unused backend code, slice by slice

- **Status:** Built 2026-11-09 (ADR 0245). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-06 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 1 to 3 agent-days (an estimate, not calibrated) · **Needs:** AU-05
- **Decisions:** [ADR 0241](../adr/0241-code-is-removed-only-from-a-recorded-list-of-unreachable-code-no-spec-plans-to-use.md)
- **Design refs:** the removal ADR written in AU-05

## Goal
Delete the backend items on the recorded list, in small reviewed slices.

## Scope
**In:** Only items on the ADR list; one list group per commit.

**Out (not in this slice):** Anything not on the list. A newly found candidate goes back through AU-05.

## Acceptance criteria
1. Each commit removes exactly one list group and names it.
2. Green `scripts/check.sh` and an independent reviewer pass per group.
3. A removed endpoint is also gone from the regenerated contract; a frontend caller makes the group stop, not proceed.
4. Tables and columns are not dropped here: dropping one needs a separate listed item, the owner's yes, and a backup taken first.
5. Every architecture test and every safety pin still passes unweakened; a pin may be removed only with the code it pins and only if the ADR lists it.

## Contract
- **API:** removals only; `scripts/gen-api.sh` when an endpoint goes.
- **Data:** none unless listed.
- **UI:** none.
- **Events/jobs:** A removed service keeps its stored rows; the registry ignores unknown rows (test).

## Non-functional
Build time and bundle size noted before and after.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
The existing suite; no test is deleted unless its subject is on the list.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
