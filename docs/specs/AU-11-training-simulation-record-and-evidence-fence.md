# AU-11 — Training-simulation record and evidence fence

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-11 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** built simulator, ADR 0207
- **Decisions:** [ADR 0243](../adr/0243-simulate-everything-is-training-data-fenced-from-the-hold-out-and-from-scorecard-evidence.md)
- **Design refs:** docs/design/09 §2; docs/design/11 §5; ADR 0207; CF-11

## Goal
Before any bulk simulation exists, make it structurally impossible for it to leak into verdicts or scorecards, or to read the hold-out.

## Scope
**In:** A `purpose` on `research.sim_runs` (`research` default, `training`), a window resolver ending before the hold-out block, and the readers' exclusions.

**Out (not in this slice):** The job (AU-12).

## Acceptance criteria
1. Every existing run reads as `research` after the migration, and all existing reports are unchanged (golden test).
2. A training run's window always ends before the hold-out block start, from the same `LabHoldOutGuard` policy; a request that reaches into it is refused.
3. If no block can be determined, the training run refuses to start (fail closed).
4. The readers behind Lab verdicts, the readiness scorecard, cell lifecycle transitions and plan selection exclude `training` runs; a test per reader.
5. A training result becomes evidence only through a normal Lab post that meets the existing rules; there is no conversion path.
6. Each job appends one ledger row per strategy version with `trial_kind = training_sweep`, before any number exists.
7. A truncation test proves no look-ahead: results up to day T are identical with the data cut at T.

## Contract
- **API:** none.
- **Data:** Migration `AddSimRunPurpose`: `purpose text not null default 'research'` with a check; widen `lab.test_ledger.trial_kind`.
- **UI:** none.
- **Events/jobs:** none.

## Non-functional
Index addition only; no table rewrite beyond the default.

## Safety
**SAFETY (research integrity).** **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Reader-by-reader exclusion tests; hold-out refusal; truncation; architecture pin.

## Open questions
None.

## Done when
- [x] All acceptance criteria have evidence (AC6 job-side append is AU-12; the kind and the fence are here; AC7 by the existing simulator truncation and look-ahead suites)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
