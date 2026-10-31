# 0169 - the P12 phase check counts thirty consecutive proved semi-auto sessions per account from stored rows and never claims MET on thin or unavailable data

- **Status:** Accepted (decided in P12-12 under the owner's standing run instruction; spec [P12-12](../specs/P12-12-phase-check-30-semi-auto-sessions.md), backend part).
- **Date:** 2026-10-31.
- **Relation to other ADRs:** same pattern as ADR 0080 (P6) and 0097 (P7): three outcomes, limits always printed, exit code 0. Reads rows of ADR 0156 (schema `execution`), 0163 (mode trail), 0166 (tickets), 0168 (screens).

## Decision
1. Pure `Domain/Execution/PhaseCheck` judges; `EvaluateExecutionPhaseCheckHandler` reads through the port `IExecutionRecordsReader` (SELECT only, adapter `ExecutionRecordsReader`); the verb `xd exec phase-check [--account ID] [--through DATE]` and `GET /api/execution/phase-check` call the one handler. No table, no migration, no grant, no stored verdict.
2. A session is `out_of_mode`, `broken`, `idle` or `counted`, in that order. Counted adds one, broken sets the streak to zero, idle and out of mode leave it. MET needs 30 or more in a row and a well-formed window; NOT MET is a broken session in the window with a streak under 30; everything else is NOT EVALUABLE. A resolved break still breaks; an unavailable or missing end-of-day run on a day with an entry or a carried position breaks; an entry without a confirmed owner ticket at most 30 s before it was sent breaks.
3. Live environment only; sandbox intents are listed and never counted. A day with `exec.canary_pending` on (or no settings row, the registry default) is `out_of_mode`.
4. The phase exit is MET when at least one account is MET, NOT MET when none is and one is NOT MET, else NOT EVALUABLE. An account that is not in `semi_auto` at the end of the window is NOT EVALUABLE.
5. The default end is the newest session whose final run (16:30 IST) plus ten minutes has passed; a non-trading date takes the session on or before it; a date after the newest recorded or closed session is 422 `execution.invalid`.
6. Facts beside the verdict (never scored): drills, not-ready days, demotions, `algoId` on live legs, foreign breaks, entry fills (insufficient below 30). Protection coverage, sends per second, refused double submits and the canary dates C1 and C2 have no stored source and read `unavailable` or `not recorded`.
7. Log ids 6750 and 6751 (a date, the verdict word and counts only).

## Known limits
`semi_auto` cannot be stored in the mode trail yet (P12-07), so the live verdict is NOT EVALUABLE; MET is proved on fakes. A working order is not read separately from the sent legs. A position is carried from the stored fills only. The Modes-tab panel is a separate frontend pass.
