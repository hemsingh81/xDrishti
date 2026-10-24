# 0094 — The weekly digest lists stored proposals in one insert-only record per ISO week, and the weekly service applies nothing

- **Status:** Proposed (spec [P7-15](../specs/P7-15-weekly-learning-digest.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-24). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-24.
- **Relation to other ADRs:** [0018](0018-services-framework-and-editable-schedules.md) (the framework), [0086](0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md) (a paused service), [0090](0090-grading-replays-each-ticket-through-the-simulator-on-its-own-session-and-stores-one-insert-only-grade.md) (grades), 0064 to 0081 (the P6 records it lists). The owner's decision on P7-00 Q6 holds: no automatic learning change in P7.

## Context
Doc 09 asks for one weekly summary instead of many approvals. P7-00 Q16 recommended running the P6 verbs weekly "proposals only". Those verbs (cell statistics, drift replay, hypothesis triggers) write records, and the owner's decision is that the learning engine changes nothing by itself.

## Decision
1. **The digest is a pure function over stored records** (`DigestBuilder`): it lists and counts, computes no statistic and reads no clock. Eight sections in fixed order: new and changed cells (changelog rows of the week), drift and calibration proposals, lessons, hypotheses and experiments awaiting the owner, loss-tag trends, plan outcomes (stored grades, labelled `simulated on plan prices`), model registry with the phase check's verdicts and reasons, and the honest limits (about 2.1 years, about 3 usable folds, modelled fills, newest-200 reads). Each item carries its record id. The text opens with, and every section ends with, a pinned "Nothing was applied" sentence; the database checks the phrase.
2. **Drift proposals and loss-tag trends are the open or linked lessons** of the monitors, because that is where P6 stores their findings; the digest does not replay a monitor. Entry-window proposals are not stored, so they are not listed (stated in the spec).
3a. **No look-ahead stored under an old week (review).** The digest reads today's state, so it is stored only for the current week from its Saturday 10:00 IST or for the most recent completed week; an older week is a dry run whose text says the state is the build date's. Every digest says when it was built. `xd plan digest` prints unless `--store` is given, and the store rule refuses a Monday run on the current week. The phase check failing fails the build rather than storing a digest without it. The week and the record windows are India time.
3. **One insert-only table `research.digests`**, unique per ISO week; the first digest of a week is kept and a second build returns it. `xd_app` has SELECT and INSERT only (granted on the table). Migration `AddWeeklyDigests` has a `Down`. A week's digest is as of when it was built.
4. **The `weekly` service** is registered with the existing framework, paused, no first-start seed, Saturday 10:00. It calls one handler, which holds the read ports of the learning stores and the grades, the read-only phase-check handler, and the digest store as its only write port. It does not call cell statistics, drift replay, hypothesis, lesson, experiment, changelog, lifecycle, designation or approval handlers. Named `weekly`, not `retrain`: it retrains nothing.
5. **Phase check detail:** criteria 1 and 2 print their stored-record details; criterion 3 prints only its verdict, because its detail contains a time measured at each run and would make a stored digest differ from a rebuilt one.
6. **Surface.** `xd plan digest [--week] [--store]`, `GET /api/learning/digest`, `GET /api/learning/digests`. Log ids 4760 to 4763. No notification and no screen.

## Consequences
- Switched on, the service writes a digest every Saturday even when there is nothing to list; an empty week is an answer.
- The digest is only as fresh as the records the owner has computed with `xd learn ...`; with no recent cell statistics it says so by the snapshot date.
- A week built early (the verb on Monday) is kept as built; the later records of that week are not added.
- Records beyond the newest 200 of a kind are not listed; the section says when that limit was hit.

## Alternatives considered
- **The weekly service also runs cell statistics, drift replay and hypothesis triggers (the P7-00 recommendation).** Rejected here: those handlers write lessons, hypotheses and snapshots, and the owner's decision (Q6) and the slice's instruction forbid a service that writes learning records. It remains the owner's choice later.
- **A digest rebuilt in place each run.** Rejected: an immutable record per week is what the owner reads and compares.
