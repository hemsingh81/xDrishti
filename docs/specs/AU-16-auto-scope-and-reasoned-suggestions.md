# AU-16 — Auto scope and reasoned next-day suggestions

- **Status:** Built 2026-11-10 (ADR 0251). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-16 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** built P7 pipeline
- **Decisions:** [ADR 0244](../adr/0244-one-auto-switch-runs-the-chain-and-only-suggests.md)
- **Design refs:** docs/design/10-trade-selection.md; spec P7-04, P7-09

## Goal
For the chosen baskets or stocks, each next-day suggestion says what, why, and on what evidence.

## Scope
**In:** A stored scope (baskets and stocks), a filter over the nightly candidates, and a read model joining plan items, their reasons and evidence.

**Out (not in this slice):** The switch (AU-17); a change of ranking.

## Acceptance criteria
1. The scope accepts baskets and individual stocks, needs at least one, and refuses unknown ids.
2. Only instruments in the scope appear; the pipeline's gates and ranking are unchanged (test against the same inputs).
3. Each suggestion has instrument, side, strategy version, levels, reason codes in words, and evidence: n, n_eff, conservative expectancy, regime, data as-of.
4. A figure that is missing reads `unavailable` and is never shown as a pass; a candidate without the required evidence is not suggested and appears under `Not suggested` with why.
5. Every suggestion carries the plan's basis text (replayed-history estimate, conditional on tomorrow's bars) and the words `Suggestion only, no order is placed`.
6. Rejected and reserve items remain visible with reasons.
7. The `suggest.scope` control appears on the Essential Settings row (AU-09 AC3), with the scope store behind it.

## Contract
- **API:** `GET|PUT /api/auto/scope` (PUT audited); `GET /api/auto/suggestions?forDate=`. `scripts/gen-api.sh`.
- **Data:** Migration `AddAutoScope`: insert-only `planning.auto_scopes` versions.
- **UI:** None here; AU-18 shows it.
- **Events/jobs:** Read at nightly run; the plan stores the scope hash.

## Non-functional
p95 under 200 ms.

## Safety
A suggestion creates no approval, ticket state change or arming. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit for the filter and the unavailable rule; integration from a fixture plan.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed

## Build note 2026-11-10
Built (backend): the scope store and `GET|PUT /api/auto/scope` (ADR 0251), the filter in the selection builder with the scope hash stored on the plan, `GET /api/auto/suggestions`, `n_eff` and the statistics as-of stored per plan item, and the `suggest.scope` Essential row read from the scope store (no reset: the row says to set another scope). A plan stored before this change has no n_eff, so its primary tickets read as not suggested. The UI is AU-18; the Essential row's control is a frontend step there (AU-09 AC3/AC4 for `suggest.scope` rest on this store).

Built 2026-11-13 (ADR 0287): the scope expands basket members as of the plan session.
