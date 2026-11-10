# AU-19 — AU programme check

- **Status:** Built 2026-11-10 (ADR 0253). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-19 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 1 agent-days (an estimate, not calibrated) · **Needs:** AU-01 to AU-18
- **Decisions:** [ADR 0240](../adr/0240-missed-days-are-recovered-by-one-dependency-ordered-pass-and-each-service-declares-how-it-can-be-recovered.md); [ADR 0244](../adr/0244-one-auto-switch-runs-the-chain-and-only-suggests.md)
- **Design refs:** docs/design/18-roadmap.md

## Goal
A read-only check says whether the programme's promises hold on the owner's data.

## Scope
**In:** `xd auto check`, reporting each exit item as MET, NOT MET or NOT EVALUABLE.

**Out (not in this slice):** Changing anything.

## Acceptance criteria
1. Reports: a real multi-day recovery in the ledger; removal ADR present; Essential count; a finished job and its evidence fence; a night of suggestions with Auto ON.
2. Anything without stored facts is NOT EVALUABLE and never MET.
3. The verb is read-only (a test with a read-only database role).

## Contract
- **API:** CLI only.
- **Data:** none.
- **UI:** none.
- **Events/jobs:** none.

## Non-functional
Under 10 s.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Table test per state.

## Open questions
None.

Built (backend, ADR 0253): `xd auto check` (`scripts/xd.sh auto check`), five items, read-only transactions. Job and fence are read through the SimAll ports and the non-evidence training reader; recovery, switch log and plans by plain select; the removal ADR is a file check of `Auto:Check:AdrFolder` (set by `xd.sh`), NOT EVALUABLE where the folder is not visible.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
