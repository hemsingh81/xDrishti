# AU-07 — Remove unused frontend code and settings keys

- **Status:** Built 2026-11-09 (ADR 0245). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-07 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 1 to 2 agent-days (an estimate, not calibrated) · **Needs:** AU-05, AU-06
- **Decisions:** [ADR 0241](../adr/0241-code-is-removed-only-from-a-recorded-list-of-unreachable-code-no-spec-plans-to-use.md)
- **Design refs:** the removal ADR written in AU-05

## Goal
Delete the frontend items and dead settings keys on the recorded list.

## Scope
**In:** Listed components, routes, MSW fakes and config keys.

**Out (not in this slice):** Anything not listed; the prototype in `docs/prototype/`.

## Acceptance criteria
1. Each commit removes one list group; check and reviewer as in AU-06.
2. No route in the navigation or the owner's bookmarks returns a blank page: a removed route goes to the not-found page.
3. The initial bundle stays within the 200 KB gzip budget.
4. A removed key is also gone from `docs/design/16-configuration.md`, and a stale key in a deployed file does not stop the start-up.

## Contract
- **API:** none.
- **Data:** none.
- **UI:** Per list.
- **Events/jobs:** none.

## Non-functional
Bundle size noted before and after.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Vitest and the architecture checks.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
