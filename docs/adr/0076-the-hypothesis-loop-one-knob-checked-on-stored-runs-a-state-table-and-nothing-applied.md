# 0076 — The hypothesis loop: one knob checked on stored runs, a state table, and nothing applied

- **Status:** Proposed (spec [P6-10](../specs/P6-10-hypothesis-loop.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0072](0072-experiments-same-entries-purged-folds-an-honest-rule-and-an-append-only-ledger.md) (the experiment that judges, the trials), [ADR 0073](0073-the-learning-changelog-and-the-cell-lifecycle-the-log-is-the-state.md) (the changelog, written by the owner's act, not here), [ADR 0074](0074-drift-monitors-frozen-band-cusum-and-calibration-on-replayed-history-proposals-only.md) (the drift alarms that can trigger a hypothesis).

## Context
Doc 09 section 5 says changes to what the system does come from hypotheses that change one variable, are tested offline against the champion, and need approval per autonomy. Nothing yet holds a hypothesis, enforces "one variable", or limits how many are open, and the experiment of P6-11 judges a pair of runs without knowing why they were made.

## Decision
1. **A hypothesis is one knob** (the experiment's `ExperimentKnob`), a stored baseline run, a rationale and a source (`owner` or `grid`; the LLM source is P9). It is a record of an idea, not a change.
2. **"One variable" is checked on the stored runs.** The record of each run gives its effective parameters, its definition text and its fill rules (`RunDefinition`); `OneVariableRule` demands that exactly the named knob differs: a parameter (from and to the stated values, everything else equal), the exits only, or the `slippage.*` fill fields only. It runs at `start`, when the candidate run is attached; a pair that differs in more, or not in the knob, is refused with the differing names and the hypothesis stays approved. A parameter knob is also checked against its baseline at proposal time. Header-level sameness (data, window, universe, costs) stays the experiment's check.
3. **The state machine is a pure table** (`HypothesisLifecycle`): proposed to approved, rejected or expired; approved to running, rejected or expired; running to judged or expired; judged to accepted, rejected or expired. Accepted, rejected and expired are final. Up to three hypotheses are open (proposed, approved, running or judged).
4. **Approval per autonomy.** Only the owner approves at advisory and supervised; at autonomous the system may approve *trying* a hypothesis. The level is not stored (P8), commands default it to supervised and nothing passes anything else, so **no path approves automatically**. Rejecting and accepting are the owner's; accepting needs a Better experiment.
5. **Judging is the P6-11 experiment.** `judge` calls the experiment handler with the hypothesis's runs and knob; the experiment is recorded in the insert-only ledger and counts as a trial; its id is stored in the judged row. A refused experiment changes nothing.
6. **Accepting applies nothing.** It writes a row; changing a strategy, a config or a cell is the owner's own act and its own changelog row (P6-08).
7. **The grid source is deterministic**: `steps` (2) each side at `step_pct` (10) of the current value, bounded so no value changes sign, six significant digits. A grid hypothesis must carry one of those values. No model, no random number.
8. **Triggers are proposals computed on replayed data**: cells with at least 30 new closed trades since a date, and cells with a drift alarm in the P6-09 replay. They write nothing and start nothing. The weekly schedule belongs to the pipeline (P7).
9. **One insert-only table `research.hypotheses`** (`MakeAppendOnlyForAppRole`; `Down` drops it): a row per state change that repeats the facts, so the newest row is the current view. Foreign keys to `research.sim_runs (run_key)` and `research.experiments (id)`. Proposals take one global advisory lock (the open count), moves a lock per hypothesis with a re-check of the state.
10. **Surface and ids.** Writes are `xd learn hypothesis ...`; the API is read only: `GET /api/hypotheses`, `/{id}`. Log ids 4580 to 4584.

## Consequences
- Accepted hypotheses do not change anything, so "accept" without the owner's following act has no effect; that is the intent.
- The exit knob is judged on the stored exit text, so a respelling of the same policy counts as a change; the rule is strict, not lenient.
- On today's data every experiment is at best Inconclusive (P6-11), so no hypothesis can be accepted yet; the loop can be walked and rejected or expired.
- Autonomous approval exists in the table and is tested but is wired nowhere; turning it on needs the stored autonomy level (P8) and a decision by the owner.
- Not verified: end-to-end with candidate runs produced by the Lab on the owner's real data.
