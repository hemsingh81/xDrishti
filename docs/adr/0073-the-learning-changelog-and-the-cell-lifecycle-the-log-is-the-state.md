# 0073 — The learning changelog and the cell lifecycle: the log is the state

- **Status:** Proposed (spec [P6-08](../specs/P6-08-lifecycle-and-changelog.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0070](0070-model-registry-content-addressed-files-and-an-append-only-champion-record.md) (the append-only designation record). ADR 0072 belongs to another slice.

## Context
Doc 09 section 7 defines how a cell moves from Research to Active and back, and the plan says every learned change is explained and reversible ("the changelog is never cut"). Nothing yet records a move, refuses an illegal one, or lets a mistake be undone without erasing history.

## Decision
1. **The lifecycle is a pure table in the Domain** (`CellLifecycle`): the seven moves of the doc 09 diagram and nothing else; Retired is final. An illegal move is refused with a reason naming the legal ones.
2. **Gates are stated and recorded.** Research to Candidate: gates passed; Candidate to Paper: the owner; Paper to Active: the owner, within the band, and at least `paper_days_before_active` (20, doc 16) paper days; Paused to Paper: recovered in shadow. Every move except the owner's approval needs an evidence id. The facts are claims by the caller (the Lab, drift and paper data do not feed them yet), kept with the row; the paper-period rule is therefore enforced where a row is written, not derived. **Nothing reads a cell's state** — Paper and Active are records until paper data (P10) and live (P7+) exist.
3. **One insert-only table `research.learning_changelog`** (insert-only for `xd_app`, `MakeAppendOnlyForAppRole`; `Down` drops it): kind (`transition`, `designation`, `revert`), subject (a cell key or `model:ID`), from and to, reason, actor, evidence ids, before/after references, paper days, `reverts_id`, time. Shape checks in the database.
4. **The log is the state.** A cell's state is the replay of its rows (`CellReplay`); there is no state column to disagree with the log. A row that does not follow the one before is reported, and no move is written on top of a log that does not add up. Appends take an advisory lock per subject and re-check the latest row under it (`changelog.stale`).
5. **A revert is a new row**, never an update or delete: kind `revert`, from the state after the reverted row to the state before it, `reverts_id` pointing at it (unique, so once). Only the latest row of a cell can be reverted; a revert is not reverted (record the move again); designations are changed by designating again; **a revert can never restore Active**, because that would skip the paper period.
6. **Designations are in the same log.** `ModelRegistry.DesignateAsync` writes the changelog row inside the designation transaction, and one for each champion it replaces; the P6-07 record and its rules are unchanged. Registration's initial challenger role is not a decision and has no row.
7. **Verb and API.** Writes are `xd learn lifecycle` (a move, or `--revert ROW`); the API is read only: `GET /api/changelog`, `/{id}`, and `/cell-state?cell=` (replayed state, legal next moves, problems, a note that it is a record only). P6-00 listed `POST /api/changelog/{id}/revert`; as in ADR 0070 a write endpoint waits for a screen.
8. Log ids 4550 to 4552 (row ids, state codes, error codes).

## Consequences
- The paper rule can be bypassed by a caller who states 20 paper days falsely; the row and the evidence id make the claim visible, and nothing acts on the state. Computing the facts is later work (P6-09 drift, P10 paper).
- Retired is final: a retired cell comes back only as a new cell definition.
- Reverting a pause is refused (it would restore Active); a paused cell returns through Paper.
- A champion designation and its changelog row cannot disagree (one transaction); the registry's own tables stay the source for roles.
- Not verified: behaviour under concurrent verbs beyond the per-subject lock test (a unit-level race and the port's stale check are covered).
