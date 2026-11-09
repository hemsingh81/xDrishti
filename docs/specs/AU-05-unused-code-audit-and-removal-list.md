# AU-05 — Unused-code audit and the recorded removal list

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-05 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** none
- **Decisions:** [ADR 0241](../adr/0241-code-is-removed-only-from-a-recorded-list-of-unreachable-code-no-spec-plans-to-use.md)
- **Design refs:** docs/engineering/README.md

## Goal
An evidence-backed list of what is truly unused, recorded in an ADR before anything is deleted.

## Scope
**In:** Static reachability across backend and frontend, a check of every candidate against specs, ADRs and the roadmap, and a new ADR (next free number) with the list.

**Out (not in this slice):** Any deletion. No candidate is removed in this slice.

## Acceptance criteria
1. Every candidate has its evidence: no references (compiler or analyser output, or a frontend reachability report), no DI, route, CLI-verb, reflection or service-registry entry, and no test-only keep-alive.
2. Every candidate has a spec search result showing that no Draft or Approved spec and no roadmap row plans it.
3. Anything on the protected list of ADR 0241 is excluded, even when it has no reader: P12 execution, P13 recorders and features, switched-off services, `NullPlanBuilder`, the `MlInfluenceSwitchOff` seam, the migrations and owner-only CLI verbs.
4. A settings key is a candidate only if no code reads it and no ADR defers it.
5. The ADR groups candidates into removal slices of at most about 1 day each, with risk per group.
6. Any tool added for the audit is pinned to an exact version and is a developer tool only.

## Contract
- **API:** none.
- **Data:** none.
- **UI:** none.
- **Events/jobs:** none.

## Non-functional
The audit never runs a heavy gate beside another build.

## Safety
Trading-safety code (halts, freezes, gates, reconciliation) is listed separately and is removed only with an explicit owner yes. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
The evidence is in the ADR; a reviewer samples at least 10 candidates independently.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
