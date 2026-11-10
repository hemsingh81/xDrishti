# P15-12 — The phase check: "features and filters improve OOS or are dropped", read from stored verdicts only

- **Status:** Built 2026-11-03 (ADR 0190). Approved (owner, 2026-11-03, blanket approval of all stories)
- **Phase / roadmap row:** P15 — slice P15-12 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Phase check"); exit criterion of the roadmap row: "Features/filters improve OOS or are dropped".
- **Design refs:** docs/design/18 (P15 row) · P8-00 D3 · ADR 0080 (MET / NOT MET / NOT EVALUABLE, limits always printed), ADR 0097 and spec P7-18 (stored records only, never MET on thin data, exit code 0), P12-12 (a read-only measurement) · the `/phase-done` skill.
- **Needs:** P15-10 (records in `research.extension_evaluations`, a P13-05 table), P15-06 (`filings.event_filter_decisions`), P15-02/04 (coverage). Reads whatever exists; a missing table or family is NOT EVALUABLE for that family, not an error.
- **Size:** 1 agent-day (estimate).
- **Safety:** no — read only: no setting changed, no verdict written, no model call, no order.

## Goal
One command says whether P15's exit criterion holds, per family and overall, from the stored verdicts, and prints the limits that make a verdict weak. It never says MET on missing or thin evidence.

## Verified facts (read 2026-11-02)
**Code:** phase checks exist as verbs `xd learn|reports|live|exec|plan phase-check` (`XdCommand`), with pure rule classes (`PhaseCheck` in Domain/Learning, `PlanPhaseCheck` in Domain/Planning) and the word set MET / NOT MET / NOT EVALUABLE. Nothing P15-specific exists. `filings.event_filter_decisions` and `IEventFilterVerdict` exist (P15-06); `research.extension_evaluations` does not yet (P15-10).

## Scope
**In:** `EventsPhaseCheck` (Domain, pure) over a facts record; `GetEventsPhaseCheckHandler`; verb `xd events phase-check [--json]`; tests; doc 18 row note.
**Out:** running any evaluation (P15-10); changing a switch; a screen (the P15-11 Learning section shows the verdicts; the phase check is CLI like the others).

## The rule
Families (the cut ones are listed as cut, not counted): `filter:results`, `ext:ev-1`, `ext:fu-1`, `ext:llm-1`, `pead`. For each, the newest stored verdict **for the current versions** (rule, catalog, parser, engine, lag; an older version's verdict is `stale`):
- **resolved** = KEEP or DROP with its evidence block; **open** = INCONCLUSIVE, NOT_EVALUABLE, stale or none.
- **violation** = the family is **in use** without a current KEEP: the event filter's effective switch is ON while the newest F verdict for its N is not KEEP (an owner may do this; it is reported, never blocked), or any feature set is in scoring (no switch exists in P15; reported as "not built").

**Overall:** MET iff every non-cut family is resolved and there is no violation; NOT MET iff a violation exists; otherwise NOT EVALUABLE, naming each open family and its missing quantity. Printed always: seasons of bars held and needed (8 of 20 today), filings coverage per kind, calendar coverage (instruments with an entry), **the share of entries lost to `no_upcoming_entry` from the newest F record**, the hold-out read or not per family, N per family, gross or net, survivorship (ISS-22), the cut list. Exit code 0 in every case (P7-18).

## Acceptance criteria
1. **Verdict table.** Fixtures: all five KEEP/DROP and filter OFF → MET; one NOT_EVALUABLE → NOT EVALUABLE naming it and its missing quantity; filter ON with newest F = DROP → NOT MET (violation printed with the decision row id and who made it, `owner` or `auto`); a KEEP of an older catalog version → `stale`, NOT EVALUABLE; no records at all → NOT EVALUABLE "no P15 evaluation stored".
2. **Cut families.** `--cut ext:llm-1,pead` lists them as cut and leaves them out of the overall; the cut list is printed; without the flag nothing is cut.
3. **Read only.** Architecture test: the handler uses only read ports; the two stores it reads (`research.extension_evaluations`, `filings.event_filter_decisions`) run through a test database role with SELECT only, and the other ports (filings, calendar, bars) are read-only by signature (pinned by the architecture test); no setting, ledger or record is written.
4. **Honest output.** No digit for a non-value; the limits block is always printed; `--json` has the same fields (snapshot test).
5. **Determinism.** Same stored rows → identical output.

## Contract
- **Verb:** `xd events phase-check [--cut …] [--json]`, exit code 0 whatever the verdict (1 for an unknown family in `--cut`, 2 for a bad line). No API, no data change.

## Non-functional
≤ 2 s on the owner's database.

## Test plan
Unit: the rule table. Integration (Timescale): the verb on fixture rows, read-only role. Architecture: criterion 3.

## Decisions (researched)
1. **A violation is reported, not prevented.** The owner may keep a filter ON against a DROP; the phase check makes it visible, as P7-18 does for its rules.
2. **Stale verdicts do not count.** A KEEP for an older catalog or parser says nothing about the current code.

## Risks and honest limits
- On today's data the expected answer is NOT EVALUABLE (X and P need CF-04; L needs labels); the phase can close only when each open family is resolved or cut by the owner.

## Open questions (owner)
1. Which families to cut if the history never reaches 20 seasons? **Default: none cut; the check stays NOT EVALUABLE and says why.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any MET on an open family, any write)
- [ ] Doc 18 P15 row note; the `/phase-done` run recorded
