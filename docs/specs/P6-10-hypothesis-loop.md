# P6-10 — Hypothesis loop (one variable, owner and grid sources)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-10 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 3 d
- **Design refs:** docs/design/09-learning-engine.md §5 (hypothesis loop), §6 · docs/design/16 (`hypothesis_loop: trigger weekly / min_new_trades_per_cell 30 / on_drift, one_variable_only, max_open_hypotheses 3, require_approval per_autonomy`; `autonomy.level`) · ADR 0076 · ADRs 0072 (experiments, trials), 0073 (changelog), 0074 (drift)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **A hypothesis is one knob.** The same knob type as an experiment (`ExperimentKnob`: `parameter`, `exit` or `slippage`, a name, a baseline value and a candidate value), a stored **baseline run**, an optional cell, a rationale (500 characters) and a source: `owner` or `grid`. The LLM source arrives with P9.
2. **The one-variable rule is checked against what the two stored runs say about themselves**, not against what the caller states. Each run's stored record gives its effective strategy parameters, its definition text and its fill rules. A *parameter* knob: exactly the named parameter differs (from and to the stated values) and the exits, the rest of the definition and the fill rules are equal. An *exit* knob: only the exits differ (parameters, rest of the definition and fills equal). A *slippage* knob: parameters, definition and exits equal, and the fill rules differ in the `slippage.*` fields only (the P6-11 comparison). Anything else is refused with the differing names. The data, window, universe and cost sheet are compared by the experiment (P6-11) that judges the pair. At proposal time a parameter knob must name a parameter of the baseline run at its stored value. The exits are compared as stored text: two spellings of one exit policy count as different (honest limit).
3. **State machine** (pure table, `HypothesisLifecycle`): `proposed -> approved | rejected | expired`; `approved -> running | rejected | expired`; `running -> judged | expired`; `judged -> accepted | rejected | expired`. `accepted`, `rejected`, `expired` are final. Eleven moves, every other pair is refused with the legal ones. Open states (proposed, approved, running, judged) count against `max_open_hypotheses` (3).
4. **Approval per autonomy.** `require_approval: per_autonomy` is read as: at `advisory` and `supervised` only the owner approves; at `autonomous` the system may approve **trying** a hypothesis. The autonomy level is not stored anywhere yet (P8), so every command takes it as `supervised` unless a caller states otherwise and the verb never states anything else: **no code path approves automatically today**. Rejecting and **accepting** are always the owner's; accepting needs the judging experiment to be **Better**. The owner is the actor `cli:xd` or `owner:...`; anything else is the system.
5. **Running = attaching the candidate; judging = the P6-11 experiment.** The candidate run is made separately (Lab or `xd simulate`) with the one knob changed. `start` (approved to running) attaches it after the one-variable check; a refusal leaves the hypothesis approved. `judge` (running to judged) calls the P6-11 `RunExperimentHandler` with the hypothesis's runs and knob: the experiment is recorded in the insert-only experiment ledger whatever its outcome and **counts as a trial**; the judged row stores the experiment id. A refused experiment (entries added, assumptions differ, ...) leaves the hypothesis running and records nothing, as for any refused experiment. The same experiment already recorded is linked and counted once.
6. **Accepting records the verdict only.** It writes a row. It changes no strategy, no config and no cell, and writes no changelog row: applying a change is the owner's own act (`xd learn lifecycle` / P6-08 changelog by the owner). The text and the API say so.
7. **Grid source.** `HypothesisGrid`: `steps` (default 2, 1 to 4) each side at `step_pct` (default 10, 1 to 50) of the current value, `steps x step_pct` under 100 so no value changes sign; six significant digits; none equal to the current value; none for a current value of zero. Deterministic, no random number, no model. A grid hypothesis must carry a grid value of its baseline's parameter and must be a parameter knob. The grid listing writes nothing.
8. **Triggers on replayed data only, as proposals.** (a) cells with at least `min_new_trades_per_cell` (30) closed trades whose exit date is after a given date, from stored runs; (b) cells with a drift alarm that went on in the P6-09 replay (its pause proposals). The result is a list of reasons to think about a hypothesis plus the open count. It never writes a hypothesis, starts a run or accepts anything. The weekly schedule is the pipeline's (P7) and is not built.
9. **Append-only record.** `research.hypotheses`: one row per state; every row repeats the facts (source, cell, knob, values, rationale, baseline run, candidate run, experiment id) with the new state, who and why, so the newest row of a hypothesis is its current view and history is the rows in order. Insert-only for `xd_app`. Foreign keys to `research.sim_runs (run_key)` and `research.experiments (id)`. Proposals are serialised under one lock (open limit), moves under a lock per hypothesis with a re-check of the state (stale is refused).
10. **Surface.** Verb `xd learn hypothesis grid|propose|approve|reject|expire|accept|start|judge|triggers`; read endpoints `GET /api/hypotheses` and `/{id}`; no write endpoint and no screen (P8). ADR 0076; log ids 4580 to 4584; migration `AddHypotheses`.

## Goal
Write a single-knob idea down, from the owner or from a deterministic grid, try it only after the owner approves and only against a candidate run that differs in that knob alone, judge it with the honest experiment, and keep every step in an append-only record, without any automatic change.

## Scope
**In:** pure `Domain/Learning` (`HypothesisState`, `HypothesisSource`, `AutonomyLevel`, `HypothesisFacts`, `HypothesisLifecycle`, `RunDefinition`, `OneVariableRule`, `HypothesisGrid`, `HypothesisTriggers`); `Application/Learning` handlers (propose, move, start, judge, list, get, grid, triggers) and port `IHypothesisLog`; `research.hypotheses` and migration; endpoints; verb; ADR; docs.
**Out:** an LLM source (P9), a write endpoint or screen (P8), automatic approval or application, the weekly schedule (P7), storing the autonomy level (P8), shadow and paper stages (P10), writing changelog rows from an accepted hypothesis.

## Acceptance criteria
1. State table: the eleven moves are exactly those of assumption 3; every other pair of the 7 x 7 is refused naming the legal moves; final states cannot move. *`HypothesisLifecycleTests`.*
2. Approval by autonomy: owner at advisory/supervised; the system only at autonomous; accepting needs the owner and a Better experiment at every level; rejecting needs the owner; at most 3 open. *`HypothesisLifecycleTests`, `HypothesisHandlersTests`.*
3. One-variable rule on stored definitions: one changed parameter passes; two changed, a different one, an unchanged candidate, a wrong stated value, changed exits/fills/definition with a parameter knob, a different strategy are each refused with the names; exit and slippage knobs likewise. *`OneVariableRuleTests`, `HypothesisHandlersTests`.*
4. A candidate with two knobs changed is refused at start and the hypothesis stays approved. *`HypothesisHandlersTests`, `HypothesisLogApiTests`.*
5. Grid: the same values every time (2 gives 1.6, 1.8, 2.2, 2.4), bounded options, none around zero or a non-number, a grid hypothesis must use a grid value. *`HypothesisSupportTests`, `HypothesisHandlersTests`.*
6. Triggers: threshold and date rules; drift pause proposals once per cell; the handler writes nothing. *`HypothesisSupportTests`, `EvaluateHypothesisTriggersHandlerTests`.*
7. Judge calls the P6-11 experiment, links its id, leaves the hypothesis running when the experiment is refused, and only a Better experiment can be accepted; accepting says nothing was changed. *`HypothesisHandlersTests`, `HypothesisLogApiTests` (real experiment).*
8. Integration: the migration applies and goes down; `xd_app` can only INSERT and SELECT; at most 3 open under concurrent proposals; two concurrent moves write one row; the endpoints read the log. *`HypothesisLogApiTests`, `ResearchGrantsTests`, `ResearchStoreMigrationTests`.*
9. The verb parses and refuses bad lines. *`LearnHypothesisOptionsTests`.* Architecture tests, `scripts/gen-api.sh`, `scripts/check.sh` green.

## Contract
- **API:** `GET /api/hypotheses?state=&limit=` (1 to 200, default 50; 422 `learning.invalid`), `GET /api/hypotheses/{id}` (404 `hypotheses.not_found`) -> `HypothesisView` (state, knob, runs, experiment id, next states, history, a note that accepting applies nothing).
- **Data:** `research.hypotheses` (see assumption 9); checks on states, shape, links (running needs a candidate, judged and accepted need an experiment); insert-only; migration `AddHypotheses` (with `Down`).
- **Errors:** `hypotheses.not_found`, `hypotheses.move_refused`, `hypotheses.not_one_variable`, `hypotheses.stale`, `hypotheses.open_limit`, plus `learning.invalid`, `learning.run_not_found` and the experiment's codes.
- **Verb:** see `xd learn hypothesis` usage; exit codes as the other `xd` commands.

## Non-functional
Pure table, rule and grid; a few small reads per command. Logs carry ids, state codes, counts and error codes only (4580 to 4584), never a knob value or a rationale.

## Test plan
As the acceptance list: exhaustive table test, rule tables, fake-log handler tests, a real-database walk through propose, approve, start, judge with the real experiment, concurrency (limit, double move), grants and migration.

## Honest limits
Replayed history only; nothing was run on the owner's real stored runs. The rule compares what the runs *record*: the exit text and the definition text are compared as stored, and data, window, universe and costs rely on the experiment's own checks. A hypothesis chosen by looking at this history is a look: each judged one is a trial that raises the bar for the next (P6-11), and on today's data (about 3 usable folds) **every experiment is at best Inconclusive, so no hypothesis can be accepted yet**. Autonomy is a parameter, not a stored setting; the weekly trigger, shadow and paper stages and the LLM source are not built. Not verified: behaviour with the Lab producing the candidate run end to end.

## Open questions
None blocking. For the owner: whether `autonomous` may ever approve without you (it is wired nowhere), and where the autonomy level should live (P8).

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] `scripts/gen-api.sh`; migration with `Down`; ADR 0076, doc 09, roadmap, site rebuilt
