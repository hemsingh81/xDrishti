# P6-16 — Lessons (problem, linked fix, stored result)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-16 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 3 d
- **Design refs:** docs/design/09-learning-engine.md §10 (lessons) · ADR 0078 · ADRs 0071 (loss tags, growing tag), 0073 (changelog), 0074 (drift), 0076 (hypotheses), 0072 (experiments)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **A lesson is a problem, an optional linked fix and a result.** The problem comes from a monitor finding with its numbers; the fix is a hypothesis (P6-10) or an experiment (P6-11) that exists; the result is the before and after of the stored experiment. Diagnosis text beyond the finding is not generated (no LLM until P9).
2. **Monitors that open lessons (replayed history only):** growing loss tag (P6-15), rolling expectancy below the band, CUSUM alarm, model calibration drift (P6-09). A drift finding is an alarm **still on at the end of the replay** (the last event of that monitor and subject is a firing); one that fired and cleared is not a finding. Drawdown beyond the Monte Carlo band and tail losses are **not built in this slice**; `execution_gap`, `decision_drag` (need real fills and decisions, P10/P11/P7) and `data_quality` (not a learning input yet) are listed as not built in every scan.
3. **State machine** (pure table, `LessonLifecycle`): `open -> linked | dismissed`; `linked -> closed | dismissed`; `closed` and `dismissed` are final. Four moves; every other pair of the 4 x 4 is refused naming the legal moves. All moves are the owner's (`cli:xd` or `owner:...`); the system only opens. An open lesson cannot be closed directly: closing needs a linked fix whose experiment is recorded.
4. **Idempotent open** by (monitor kind, subject, evidence key). The evidence key is a stable text of the finding (growing tag: tag and the two counts; drift: instant and check number of the firing). The same finding never opens a second lesson, also when scanned twice or at once (partial unique index on the opening rows plus `ON CONFLICT DO NOTHING`). A changed finding (other counts, a later firing) is a new lesson: a persisting problem can open several lessons over time (honest limit).
5. **Result from stored numbers only.** Closing reads the experiment of the linked fix from the ledger (a linked hypothesis gives its judging experiment; if it has none yet, closing is refused). The row stores the experiment id, its outcome, the pooled mean R before (baseline) and after (candidate), which may be null when no fold was usable, and total R before and after. The caller types no number. The result is `improved` only when the experiment outcome is Better; **every other outcome is `no_measurable_change`**, which is legitimate. There is no "worse" label: the experiment's rule does not support one.
6. **Linking and closing apply nothing.** A fix is applied by the owner's own act and its changelog row (P6-08). Texts and the API say so.
7. **Append-only record.** `research.lessons`: one row per state, every row repeats the facts, newest row is the current view, insert-only for `xd_app`; foreign keys to `research.hypotheses (id)` and `research.experiments (id)`; composite key so a state row points at an opening row only; checks on states, shape, links and result. Moves run under a lock per lesson with a re-check (stale is refused).
8. **Surface.** Verb `xd learn lesson scan|link|close|dismiss` (`scan --dry-run` lists findings and which are already lessons); read endpoints `GET /api/lessons` and `/{id}`; no write endpoint, no screen (P8). ADR 0078; log ids 4600 to 4604; migration `AddLessons`.

## Goal
Turn a replayed monitor finding into a durable lesson, tie it to the fix that was tried, and close it with the before and after numbers of the stored experiment, without ever inventing a number or applying a change.

## Scope
**In:** pure `Domain/Learning` (`LessonState`, `LessonMonitorKind`, `LessonMonitors`, `LessonFacts`, `LessonLifecycle`, `LessonFinding`, `LessonFindings`); `Application/Learning` handlers (open from monitors, link, close, dismiss, list, get) and port `ILessonLog`; `research.lessons` and migration; endpoints; verb; ADR; docs.
**Out:** a live feed (P10), `execution_gap`, `decision_drag`, `data_quality`, drawdown-beyond-band and tail-loss monitors, generated diagnoses (P9), a write endpoint or screen (P8), applying a fix, automatic rollback, the schedule (P7).

## Acceptance criteria
1. State table: the four moves are exactly those of assumption 3; every other pair is refused naming the legal moves; final states cannot move; every move is the owner's; closing needs a recorded experiment. *`LessonLifecycleTests`.*
2. Findings: alarms still on are findings with stable keys; cleared alarms are not; growing tags give keys with the counts. *`LessonFindingsTests`.*
3. Open, link, close with before/after: the closing row carries the stored experiment's outcome and numbers; Better gives `improved`, any other outcome `no_measurable_change`. *`LessonHandlersTests`, `LessonLogApiTests` (real hypothesis and experiment).*
4. Idempotent open: a second scan opens nothing; a dry run writes nothing; six concurrent openings write one lesson. *`LessonHandlersTests`, `LessonLogApiTests`.*
5. Illegal transitions are refused with a code and nothing is written; a missing fix and a non-owner are refused; stale is refused. *`LessonHandlersTests`, `LessonLogApiTests`.*
6. Grants and migration: `xd_app` can only INSERT and SELECT; the migration applies and goes down; a state row cannot point at a state row; a closed row without numbers is refused by the database. *`ResearchGrantsTests`, `ResearchStoreMigrationTests`, `LessonLogApiTests`.*
7. The verb parses and refuses bad lines. *`LearnLessonOptionsTests`.* Architecture tests, `scripts/gen-api.sh`, `scripts/check.sh` green.

## Contract
- **API:** `GET /api/lessons?state=&limit=` (1 to 200, default 50; 422 `learning.invalid`), `GET /api/lessons/{id}` (404 `lessons.not_found`) -> `LessonView` (state, monitor, subject, problem, fix links, result, before/after numbers, next states, history, note).
- **Data:** `research.lessons` (assumption 7); migration `AddLessons` with `Down`.
- **Errors:** `lessons.not_found`, `lessons.move_refused`, `lessons.stale`, `lessons.fix_not_found`, plus `learning.invalid` and the monitors' codes.

## Non-functional
Pure table and finding rules; a scan costs one loss-tag analysis and one drift replay of the named runs plus one read per finding. Logs carry ids, state codes, counts and error codes only (4600 to 4604), never a problem text or a reason.

## Test plan
As the acceptance list: exhaustive table test, finding tests, fake-log handler tests, a real-database walk through a judged hypothesis and its experiment, concurrency (open, double move), grants and migration.

## Honest limits
Replayed history only; nothing was run on the owner's real stored runs. A finding is a count or an alarm, not a diagnosis. On today's data (about 3 usable folds) every experiment is at best Inconclusive, so a lesson will in practice close as "no measurable change". The same persisting problem can open a new lesson when its numbers change. Execution gap, decision drag, data quality, drawdown beyond the Monte Carlo band and tail losses are not built.

## Open questions
None blocking. For the owner: whether a persisting problem should be one lesson rather than one per changed finding.

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] `scripts/gen-api.sh`; migration with `Down`; ADR 0078, doc 09, roadmap, site rebuilt
