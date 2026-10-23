# 0078 — Lessons: a finding, a linked fix and a result read from the stored experiment

- **Status:** Proposed (spec [P6-16](../specs/P6-16-lessons.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0071](0071-automatic-loss-tags-pure-rules-over-the-closed-trade.md) (the growing-tag detector), [ADR 0074](0074-drift-monitors-frozen-band-cusum-and-calibration-on-replayed-history-proposals-only.md) (the drift alarms), [ADR 0076](0076-the-hypothesis-loop-one-knob-checked-on-stored-runs-a-state-table-and-nothing-applied.md) (the fix that can be linked), [ADR 0072](0072-experiments-same-entries-purged-folds-an-honest-rule-and-an-append-only-ledger.md) (the experiment whose numbers close a lesson), [ADR 0073](0073-the-learning-changelog-and-the-cell-lifecycle-the-log-is-the-state.md) (where an applied fix is recorded, by the owner).

## Context
Doc 09 section 10 says a monitor opens a lesson: problem, diagnosis, fix, result. The monitors exist (P6-09, P6-15) but print findings and forget them, so the same finding is re-read every time, nothing ties it to the hypothesis tried against it, and no record says whether the fix did anything.

## Decision
1. **A lesson is a finding, an optional linked fix and a result.** Monitors that can run on replayed history open lessons: growing loss tag, rolling expectancy below the band, CUSUM, model calibration. A drift finding is an alarm still on at the end of the replay. `execution_gap`, `decision_drag`, `data_quality`, drawdown beyond the Monte Carlo band and tail losses are **listed as not built** in every scan. There is no live feed before P10.
2. **Pure state table** (`LessonLifecycle`): open to linked or dismissed, linked to closed or dismissed; closed and dismissed are final. Every move is the owner's; the system only opens. Closing needs a linked fix with a recorded experiment.
3. **Opening is idempotent** by (monitor kind, subject, evidence key): the key is a stable text of the finding. A partial unique index on the opening rows, with `ON CONFLICT DO NOTHING`, makes it hold under concurrency. A changed finding is a new lesson.
4. **The result is read, never typed.** Closing loads the experiment of the linked fix (a hypothesis gives its judging experiment) and stores its id, outcome, pooled mean R before and after (null when no fold was usable) and total R before and after. `improved` only for a Better experiment; everything else is `no_measurable_change`, a legitimate outcome. No "worse" label is made: the experiment's rule does not support one.
5. **Nothing is applied.** Linking and closing write rows; applying a fix is the owner's own act with its own changelog row (P6-08).
6. **One insert-only table `research.lessons`** (`MakeAppendOnlyForAppRole`; `Down` drops it): a row per state that repeats the facts, newest is current; foreign keys to hypotheses and experiments; a composite key so a state row points at an opening row only; checks on states, links and result. Moves take a lock per lesson and re-check the state.
7. **Surface and ids.** Writes are `xd learn lesson scan|link|close|dismiss` (`scan --dry-run` writes nothing); the API is read only: `GET /api/lessons`, `/{id}`. Log ids 4600 to 4604.

## Consequences
- A finding is remembered; scanning twice opens nothing new, and a lesson shows which hypothesis or experiment was tried and what the stored experiment said.
- On today's data (about 3 usable folds) experiments are at best Inconclusive, so lessons close as "no measurable change" in practice; that is recorded as such.
- A problem that persists while its counts change opens a new lesson each time; merging them is an owner decision.
- Not verified: end-to-end on the owner's real stored runs.
