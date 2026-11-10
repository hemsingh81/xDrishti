# AU-03 — Multi-day data proof for the end-of-day service

- **Status:** Built 2026-11-09 (ADR 0240). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-03 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 1 agent-days (an estimate, not calibrated) · **Needs:** AU-02
- **Decisions:** [ADR 0240](../adr/0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md)
- **Design refs:** docs/design/05-data-and-brokers.md §6; spec P1-12a

## Goal
A single recovered `eod` run is shown to have filled every missed session, so recovery reports what it actually recovered and not merely that a job finished.

## Scope
**In:** A post-run check that compares each missed session with the 1-minute bars, the aggregates and the quality result, and marks each session `recovered` or `still_missing`.

**Out (not in this slice):** A new fetcher; repair of bars older than the reach.

## Acceptance criteria
1. Given five missed sessions and a fetch that fills all five, then five sessions are `recovered`.
2. Given a run that fills three of five, then the item is `partial`, two sessions are `still_missing` by date, and dependents run only if the fail-closed gates pass.
3. A session that is `unfillable` in `market.bar_gaps` is reported as that and never as recovered.
4. The check reads the existing gap and quality stores and scans no full bar history (query-plan test or bounded per-instrument read, as P1-12a).
5. A service run that reports success while a session has no bars still leaves that session `still_missing`.

## Contract
- **API:** none.
- **Data:** `recovery_items` gains `detail jsonb` (per-session state), migration `AddRecoveryItemDetail`.
- **UI:** none.
- **Events/jobs:** Runs at the end of the `eod` step.

## Non-functional
Read cost as P1-12a: one small read per instrument.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit for the session classifier; integration on TimescaleDB with seeded gaps.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
