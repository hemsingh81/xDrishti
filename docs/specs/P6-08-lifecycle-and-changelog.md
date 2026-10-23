# P6-08 — Lifecycle and changelog

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-08 of [P6-00](P6-00-learning-engine-and-ml-plan.md)
- **Design refs:** docs/design/09-learning-engine.md §7 (cell lifecycle) · docs/design/16 (`qualification_gates`, `paper_days_before_active`, `drift`) · ADR 0073 · ADR 0070 (the append-only designation record and the champion rule)

## Assumptions (taken as recommended)
- The lifecycle is exactly the seven moves of the doc 09 §7 diagram: Research→Candidate, Candidate→Paper, Paper→Active, Active→Paused, Paused→Paper, Active→Retired, Research→Retired. Everything else (Retired→anything, Candidate→Retired, Paused→Retired, Paper→Retired) is refused with a reason that names the legal moves. Retired is final. A new cell is Research with no row.
- **The log is the state.** No state column exists anywhere; a cell's state is the replay of its changelog rows (`CellReplay`), so "replay reconstructs state" is true by construction and checked by a test.
- Gates are **stated by the caller and recorded**, not re-derived: Research→Candidate needs `gates passed`, Candidate→Paper and Paper→Active need the owner, Paper→Active also needs "within band" and at least `paper_days_before_active` (20, doc 16) paper days, Paused→Paper needs "recovered in shadow". Every move except the owner's approval of a candidate needs at least one evidence id. Nothing yet computes these facts (the qualification gates run in the Lab, drift is P6-09, paper data is P10), so the facts are claims with evidence ids, not proofs; the row says who claimed them.
- **Nothing is live.** Paper and Active are records only; no component reads a cell's state. The paper-period rule is recorded and enforced when a row is written; live use is P7+ and paper data P10.
- A **revert** is a correction, not a lifecycle move: it appends a `revert` row (from = the state after the reverted row, to = the state before it, `reverts_id` = the reverted row, before/after references swapped). Only the **latest** row of a cell can be reverted, and only once; a revert is not reverted (record the move again); a designation row is not reverted here (designate again, ADR 0070); a revert can **never restore Active** (that needs the paper period and the owner again). The unique index on `reverts_id` and the latest-row check under the subject's lock enforce it.
- The champion designation of P6-07 writes a `designation` changelog row **in the same transaction** (and one for each replaced champion). P6-07 behaviour and tests are unchanged. The registration's initial challenger role is not a changelog row (it is not a decision).
- The cell does not have to exist in `research.cell_stats` (every cell key is Research until a row says otherwise); a cell key must be six valid parts.
- `paper_days_before_active` is the doc 16 default (20) as a constant of the Domain; reading it from the configuration store is later.
- P6-00 listed `POST /api/changelog/{id}/revert`. As for the models (P6-07) there is no screen to use a write endpoint, so writes are the verb `xd learn lifecycle`; the API is read only. (Owner-only promotion beyond Candidate stays an explicit `--owner-approved` flag on the verb.)
- Log ids 4550 to 4552. ADR number 0073.

## Goal
Every change of a cell's life and every champion designation is a permanent row saying what changed, by whom or what, why, with before/after references and evidence ids; illegal moves are refused with a reason; a revert is another row; the current state is the replay of the rows.

## Scope
**In:** Domain `CellState`, `CellLifecycle` (the table, the gates, the revert rule), `TransitionFacts`, `CellMove`, `CellReplay`, `CellId.TryParse`; Application port `ILearningChangelog`, handlers `RecordCellTransitionHandler`, `RevertChangelogHandler`, `ListChangelogHandler`, `GetChangelogEntryHandler`, `GetCellStateHandler`; Infrastructure `LearningChangelog` (raw SQL), the shared insert used by `ModelRegistry` for designations; migration `AddLearningChangelog` (`research.learning_changelog`, insert-only for `xd_app`, `Down`); `GET /api/changelog`, `/api/changelog/{id}`, `/api/changelog/cell-state?cell=`; verb `xd learn lifecycle`.
**Out:** drift monitors and automatic pause (P6-09), the hypothesis loop (P6-10), any use of the state by planning or the UI, a write endpoint, reverting designations, paper trading (P10), computing the gates from Lab results (the facts are stated).

## Acceptance criteria
1. The transition table has exactly the seven moves; for every pair of states, a move outside the table is refused with a reason (naming the legal moves, or "final" for Retired) even when every fact is stated.
2. Gates: Research→Candidate needs the gates, Candidate→Paper the owner, Paper→Active the owner, the band and 20 paper days (19 is refused, the reason lists every missing item), Paused→Paper recovery; Active→Paused, Active→Retired, Research→Retired need none. Every move but Candidate→Paper needs an evidence id.
3. A recorded move is one row with kind, subject, from, to, reason, actor, evidence ids, before/after references, paper days (Paper→Active only) and time (from `TimeProvider`).
4. Revert: a new row pointing at the reverted one; the old row is untouched; only the latest row, once; not a revert, not a designation; never to Active; the state afterwards is the pre-move state.
5. Replay: the rows in order give the state, `research` without rows, and report a row that does not follow the one before or that reverts an unknown row; a move is not recorded on top of a log that does not add up; a race is `changelog.stale` and writes nothing.
6. Migration: the table exists with its shape checks, `xd_app` can SELECT and INSERT but not UPDATE, DELETE or TRUNCATE; `Down` drops it; `ResearchGrantsTests` and the migration shape test include it.
7. A champion designation writes a changelog row in the same transaction; replacing a champion writes a row for the old one; a repeated or refused designation writes nothing; P6-07 tests stay green.
8. API: `GET /api/changelog` (newest first, `subjectKind`, `subject`, `limit` 1 to 200), `GET /api/changelog/{id}` (404 `changelog.not_found`), `GET /api/changelog/cell-state` (state, legal next moves, problems, the note that it is a record only); bad input is 422 `learning.invalid`.
9. Verb `xd learn lifecycle` parses its options without I/O (a move, or `--revert`), exit codes as the other `xd` verbs.
10. Handlers are internal sealed and registered; the research-writers architecture list includes the new adapter files.

## Contract
- **API:** as in criterion 8 (`ChangelogEntryView`, `CellStateView`). Errors: 422 `learning.invalid`, 404 `changelog.not_found`; refusals by the verb: `changelog.transition_refused`, `changelog.not_revertible`, `changelog.stale`, `changelog.inconsistent` (409).
- **Data:** `research.learning_changelog` (id identity, kind `transition|designation|revert`, subject_kind `cell|model`, subject, from_state, to_state, reason ≤500, actor ≤100, evidence_ids text[] ≤20, before_ref, after_ref, paper_days, reverts_id self-FK unique, created_at); checks on kind, revert shape, states per subject kind, evidence count. Migration `AddLearningChangelog`.
- **UI:** none.

## Non-functional
Row ids, state codes and error codes only in logs (4550 to 4552). Appends take a transaction advisory lock per subject. The log is small (decisions, not trades).

## Test plan
Unit: `CellLifecycleTests` (all 36 pairs, gates, boundaries, replay), `ChangelogHandlersTests` with an in-memory log that has the table's append rules. Integration (real TimescaleDB): `LearningChangelogApiTests` (moves, revert, state endpoint, stale/wrong-from, designation rows, refused champion writes none), `ResearchGrantsTests` and `ResearchStoreMigrationTests` extended, CLI parsing, architecture writers list.

## Open questions
None blocking. Whether the stated gate facts should later be computed from Lab verdicts and drift monitors is for P6-09/P10.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract regenerated; migration added; ADR 0073; docs and roadmap updated
