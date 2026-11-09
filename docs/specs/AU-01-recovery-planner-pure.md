# AU-01 — Recovery planner (pure)

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-01 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** built services framework
- **Decisions:** [ADR 0240](../adr/0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md)
- **Design refs:** docs/design/04-accounts-access-and-operations.md §3

## Goal
From the registered services, their last runs, the trading calendar and the clock, one pure function says what was missed over several days and in what order it can be made up.

## Scope
**In:** A `RecoveryClass` on `ServiceConstraints` (`Convergent`, `LatestOnly`, `Dated`, `TimeBound`), declared for every registered service; `RecoveryPlanner` in `Domain/Platform/Services` returning an ordered plan; validated setting `Recovery:MaxSessions`.

**Out (not in this slice):** Running anything (AU-02); screens (AU-04).

## Acceptance criteria
1. Given a daily service with no run for the last five trading sessions, when planned, then the plan lists all five occurrences as missed; weekends and NSE holidays are not missed (calendar table test).
2. Given a `Convergent` service with several missed sessions, then the plan has one step covering them all, with the session range on it.
3. Given a `LatestOnly` service with several missed sessions, then the plan has one step for the latest closed session, and the older sessions are listed `superseded`.
4. Given a `Dated` test service, then there is one step per session, oldest first.
5. Given a `TimeBound` service, then each missed occurrence is `not_recoverable` with its reason and no step exists.
6. Steps are topologically sorted on `MustFollow` and `after` chains, so the order is data, aggregates and quality (the `eod` service), then learning, then `nightly`; a cycle is an error, not a guess.
7. The `nightly` step exists only when the plan's review cutoff (`NightlyRules.ReviewCutoff`) is still ahead; otherwise it is `not_recoverable: cutoff passed`.
8. A paused service, or one with catch-up off, is not missed (ADR 0018 stands).
9. Sessions older than `Recovery:MaxSessions` are reported `out_of_reach`, not silently dropped. The setting is validated at 1 to 250, default 30 (equal to `Data:Gaps:LookbackSessions`), and an invalid value stops the start-up.
10. An architecture or registry test fails when a registered service has no explicit class.

## Contract
- **API:** none.
- **Data:** none.
- **UI:** none.
- **Events/jobs:** none (pure).

## Non-functional
Pure, no I/O; a plan over 40 services and 250 sessions in under 50 ms.

## Safety
The class table is the safety content: anything that needs a live decision-time observation (`preopen`, `review-cutoff`, `shadow-write`, `paper-capture`, `live-*`, the recorders, `live-feed`) is `TimeBound`, because making it up later would write data that was not seen at the time (ADR 0232). **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Table-driven unit tests with a fake clock: holidays, special sessions, midnight in India time, reach limit, cycles, class per service.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
