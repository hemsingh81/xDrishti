# AU-14 — Simulation report API

- **Status:** Built 2026-11-10 (ADR 0250). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-14 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-11
- **Decisions:** [ADR 0243](../adr/0243-simulate-everything-is-training-data-fenced-from-the-hold-out-and-from-scorecard-evidence.md)
- **Design refs:** docs/design/13-reports-and-ui.md; ADR 0106

## Goal
Read simulation results by stock or basket, for any from-to range, while the job is still running.

## Scope
**In:** Extending the existing `/api/reports/*` engine with `purpose=training`, a job selector, and a completeness block.

**Out (not in this slice):** New report mathematics.

## Acceptance criteria
1. Every report type (summary, groups, pivot, equity, distribution, MAE/MFE, what-works, benchmarks, trades) accepts `simJob`, `from`, `to` and a stock or basket filter.
2. Without `purpose=training`, reports exclude training runs (golden test against current output).
3. Every response during a job carries `complete: false`, chunks done of total and the as-of time; only finished chunks are read.
4. A range overlapping the hold-out answers with the part before it and says the rest is locked; no number from inside it.
5. Basket membership follows the trade's stored basket as today; a stock with no results says so.
6. Bad ranges answer 422 `reports.invalid` (amended 2026-11-10: every `reports.*` refusal of the engine is 422; ADR 0250).

## Contract
- **API:** additive query parameters and `completeness` on the response; `scripts/gen-api.sh`.
- **Data:** Index on `(purpose, job)` if the plan needs it.
- **UI:** none.
- **Events/jobs:** none.

## Non-functional
p95 under 500 ms on one stock or basket, measured; a whole-universe report may be pre-aggregated per finished chunk.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Golden tests of unchanged output; integration with partial jobs; hold-out range.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
