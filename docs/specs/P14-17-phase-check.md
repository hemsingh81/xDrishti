# P14-17 — The phase check: "an option expression beats cash/futures OOS after costs, else stays disabled", read from stored verdicts only

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-17 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Phase check"); exit criterion of the roadmap row: "An option expression beats cash/futures OOS after costs, else stays disabled".
- **Design refs:** docs/design/18 (P14 row) · P8-00 D3 · ADR 0080 (MET / NOT MET / NOT EVALUABLE, limits always printed), 0097 and P7-18 (stored records only, never MET on thin data, exit code 0), 0190 (the P15 phase check: current versions only, violations reported, cut families listed) · the `/phase-done` skill.
- **Needs:** P14-09 (and P14-10, P14-11, P14-13, P14-15 when built) for records in `research.extension_evaluations`; P14-06 (cash-only rule); P14-02/03 switches (read only). Reads whatever exists; a missing family is NOT EVALUABLE for that family, not an error.
- **Size:** 1.5 agent-days (estimate).
- **Safety:** no — read only: no setting changed, no verdict written, no switch touched, no order.

## Goal
One command says whether P14's exit criterion holds, per family and overall, from the stored verdicts, and prints the limits that make a verdict weak. It never says MET on missing, gross, stale or thin evidence, and it says plainly when every option family is dropped (the "stays disabled" half of the criterion).

## Verified facts (read 2026-11-03 in `backend/src`)
- Phase-check verbs exist (`xd learn|reports|live|exec|plan phase-check`, `xd events phase-check`) with pure rule classes (`PhaseCheck`, `PlanPhaseCheck`, `EventsPhaseCheck`) and the words MET / NOT MET / NOT EVALUABLE; `EventsPhaseCheck` iterates `ExtensionFamilies.All` (P15 only).
- `research.extension_evaluations` stores every protocol's records with `family`, `protocol`, `version_key`, `inputs`, `verdict`, `missing`, `limits`, `holdout_read`.
- The archive switches are the newest rows of `options.chain_recorder_switch` and `options.backfill_switch`.

## Scope
**In:** `OptionsPhaseCheck` (Domain, pure) over a facts record; `GetOptionsPhaseCheckHandler`; verb `xd options phase-check [--cut …] [--json]`; tests; doc 18 P14 row note.
**Out:** running any evaluation; changing a switch or stopping the archive (an owner act); a screen.

## The rule
Families: `expr:option_buy` (the **primary** family of the criterion), `expr:debit_spread`, `expr:credit_spread`, `expr:iron_condor`, `ext:iv-1`, `pairs:intraday-1`, `basis:carry-1`. Cut families are listed as cut and not counted. For each, the newest record **for the current versions** (re-mark version, engine, archive watermark not older than `StaleAfterDays` (90), spread model id, cost sheet hash, profile set); an older one is `stale`.
- **resolved** = KEEP or DROP with its evidence block and on the net basis where the protocol requires it; **open** = INCONCLUSIVE, NOT_EVALUABLE, stale or none.
- **violation** = a non-cash expression **in use**: any stored plan run, ticket or paper capture whose cell key has a non-cash eighth part (P14-06 makes this impossible by construction; the check proves it from stored rows), or a KEEP read by a decision path (none exists). Reported, never blocked.
- **Overall:** MET iff every non-cut family is resolved, no violation, and **either** `expr:option_buy` (or another `expr:*` family) is KEEP with a hold-out-confirmed record **or** every `expr:*` family is DROP (the option expression "stays disabled": `expression` stays `cash`). NOT MET iff a violation exists. Otherwise NOT EVALUABLE, naming each open family and its missing quantity.
- **Printed always:** years of underlying bars held and needed (2.1 of 5 today, CF-04), usable chain sessions per underlying against 60, backfill progress, whether costs are net or gross per family, N per family, the hold-out read or not, `expiry: derived` and fallback shares, survivorship (ISS-22), the cut list, and — when every option family is DROP — whether the recorder and backfill switches are still ON (`archive still recording after DROP: owner may switch it off`), as information, not a violation.
- Exit code 0 in every case (P7-18).

## Acceptance criteria
1. **Verdict table.** Fixtures: all `expr:*` DROP, the others DROP or cut, no violation → MET with "option expressions stay disabled"; `expr:option_buy` KEEP confirmed on the hold-out and the rest resolved → MET; a development KEEP without a hold-out record → NOT EVALUABLE (`hold-out not read`); one NOT_EVALUABLE → NOT EVALUABLE naming it and its missing quantity; a stored plan with an eight-part non-cash key → NOT MET with the row id; no records → NOT EVALUABLE "no P14 evaluation stored".
2. **Stale and gross.** A KEEP for an older re-mark version → `stale`; a record whose protocol requires net but is gross can only be INCONCLUSIVE (as stored), so the family is open.
3. **Cut families.** `--cut basis:carry-1,expr:iron_condor` lists them as cut and leaves them out; an unknown family in `--cut` → exit 1; without the flag nothing is cut.
4. **Read only.** Architecture test: only read ports; the record store and the plan stores run through a SELECT-only test role; nothing is written.
5. **Honest output.** No digit for a non-value; the limits block always printed; `--json` has the same fields (snapshot test); P15's `xd events phase-check` unchanged (golden).
6. **Determinism.** Same stored rows → identical output.

## Contract
- **Verb:** `xd options phase-check [--cut …] [--json]`, exit 0 whatever the verdict (1 unknown family in `--cut`, 2 bad line). No API, no data change.
- **Setting:** `Options:PhaseCheck:StaleAfterDays` 90 (7-730).

## Non-functional
≤ 2 s on the owner's database.

## Test plan
Unit: the rule table. Integration (Timescale): the verb on fixture rows with the read-only role. Architecture: criterion 4; P15 golden.

## Decisions (researched)
1. **"Stays disabled" is a MET outcome.** The kill criterion's default is a legitimate end of the phase: every option family dropped, `expression` still `cash` everywhere.
2. **A KEEP needs the hold-out to close the phase.** A development KEEP is a candidate, not evidence (P13-05, P15-10 amendment 2026-11-02).
3. **The archive switches are reported, not judged.** Stopping the recorder after a DROP is the owner's call (P14-00 kill criterion "the archive jobs stop on the owner's say").

## Risks and honest limits
- Expected answer for months: NOT EVALUABLE (no 5-year history, no 60 usable sessions, no charge lines). The phase closes only when each open family is resolved or cut.

## Open questions (owner)
1. Which families to cut? **Default: none at build; recommended cut first `basis:carry-1` (P14-00), then `expr:iron_condor` and `expr:credit_spread` if no regime tagger is built.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any MET on an open, gross or stale family, any write)
- [ ] Doc 18 P14 row note; the `/phase-done` run recorded
