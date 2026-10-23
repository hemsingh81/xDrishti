# P7-15 — Weekly learning digest and the weekly service (proposals only)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; owner decisions binding: Q6 no automatic learning change in P7, no auto approval, no orders). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-15.
- **Design refs:** docs/design/09-learning-engine.md §10 (weekly digest), 15, 16; P7-00 Q6, Q10, Q16; ADR 0018 (services framework), 0086 (a paused service), 0090 (grades), 0064 to 0081 (the P6 records); ADR 0094 (this slice).

## Assumptions (stated up front)
1. **Safety: nothing is applied, approved or ordered.** The digest only lists. It reads stored records and writes exactly one thing, the digest. No lesson, hypothesis, experiment, changelog row, cell state, model designation, approval or plan is written, and no broker, order or credential path is touched. Q6 and Q16 hold: the weekly service runs no P6 verb that writes.
2. **The service is called `weekly`**, not `retrain`: it retrains nothing. P7-00 named a Saturday 10:00 `retrain`/digest service that would also run cell statistics, drift replay and hypothesis triggers and store their results; those P6 verbs write records (snapshots, lessons, hypotheses). The owner's binding decision (no automatic learning change, and the instruction that the service must not call a writing handler) cuts that part: it is left to the owner's own `xd learn ...` runs. The digest reads what they stored.
3. **Starts paused**, no first-start seed (ADR 0086 pattern); Saturday 10:00 India time, weekly schedule of the framework (ADR 0018); catch-up within 24 hours.
4. **Digest = one stored text and its sections, per ISO week**, immutable, insert-only. The first digest of a week is kept; a second build for the same week returns it and writes nothing. Content is as of when it was built (Saturday 10:00 by default), so later records of that week are not in it.
5. **Week** is Monday to Sunday (ISO), India time; the default is the week of today; a future week is refused.
6. **Drift proposals and loss-tag trends are the open or linked lessons** raised by the monitors (rolling band, CUSUM, calibration; growing loss tag), because that is where the P6 monitors store their findings (ADR 0078). The digest does not replay the monitors (that would recompute). Entry-window proposals (ADR 0069) are not stored anywhere, so they are not in the digest; this is stated here, not hidden.
7. **Records are read newest 200 of each kind** (as the learning overview does); a section says when that limit was hit.
8. **Plan outcomes** come from the stored P7-13 grades of the sessions in the week, through the existing report (curves and groups), labelled `simulated on plan prices`. Summing the stored R over a set is the grade report's own arithmetic; the digest adds none.
9. **The phase check** is read through its read-only handler for the stored verdicts and reasons of criteria 1 and 2; criterion 3 prints only its verdict (its detail holds a time measured at each run and would make the stored text differ between runs).
10. Not built: the in-app notification, the "progress against the frozen-model counterfactual" and "focus for next week" parts of doc 09 (the first needs a run of the counterfactual which computes; the second is a judgement). A screen is not built (the endpoints and the verb are).

## Goal
Once a week the owner can read one summary built from the stored learning and plan records, with ids, listing only what awaits their decision, and saying in every section that nothing was applied.

## Scope
**In:** Domain `Learning`: `DigestWeek`, `DigestItem`, `DigestInput`, `DigestSection`, `WeeklyDigest`, `DigestBuilder` (pure); Application: port `IDigestStore`, `BuildDigestHandler`, `GetDigestHandler`, `ListDigestsHandler`, `WeeklyService`; Infrastructure: `DigestStore`, record and configuration, migration `AddWeeklyDigests` (with `Down`, insert-only for `xd_app`); Api: `GET /api/learning/digest`, `GET /api/learning/digests`, `xd plan digest [--week] [--dry-run]`; tests; ADR 0094; docs and roadmap row.
**Out:** any write to lessons, hypotheses, experiments, changelog, cells, models, approvals or plans; running cell statistics, drift replay or hypothesis triggers; notifications; a screen; the counterfactual progress line; the monthly review.

## Acceptance criteria
1. A fixture of stored records gives exactly the hand-checked text (eight sections: cells, drift, lessons, hypotheses and experiments, loss tags, plan outcomes, models and phase check, limits).
2. Every section ends with the pinned line `Nothing was applied in this section: ...` and the text opens with `Nothing was applied: every item below is a proposal; applying any of it is the owner's own act.`; the database refuses a stored text without `Nothing was applied`.
3. Nothing is applied: a spy shows that only read calls reach the learning and grade stores and that the digest store gets `FindByWeek` and `Insert` only; the handler holds no plan, approval, broker or order port; after a build on a real database the lesson, hypothesis, changelog, approval and plan tables are unchanged.
4. Idempotent per ISO week: a second build returns the stored digest (`repeated`) and writes nothing; a concurrent second insert is skipped by the unique index.
5. The service starts paused, runs Saturday 10:00, and its run only calls the digest handler as `system:weekly`.
6. Determinism: the same stored records give the same text.
7. `xd_app` can insert and select digests but not update, delete or truncate (grants test).
8. A bad or future week is refused (`learning.invalid`, 422); an unknown week is `learning.digest_not_found` (404).

## Contract
- **API:** `GET /api/learning/digest?week=2026-W43` (default: the newest stored), `GET /api/learning/digests?limit=` (1 to 52, default 12). Read only.
- **CLI:** `xd plan digest [--week YYYY-Www] [--dry-run]`.
- **Data:** `research.digests` (iso_week unique, week_start, week_end, text, sections_json, built_by, created_at; check on the shape and on the pinned phrase). Log ids 4760 to 4769.
- **Jobs:** service `weekly`, Saturday 10:00, paused by default.

## Non-functional
Eight bounded reads (200 rows each), the grade report (at most 5000 tickets) and the phase check's two stored runs; seconds. Not measured on the owner's data (UNVERIFIED).

## Test plan
Unit: builder text by hand, determinism, empty input, graded week, week parsing; handler over fakes (spy, repeat, dry run, bad week, concurrent insert); service definition and run. Integration: migration, grants, endpoints, real build with unchanged write tables, DB check on the phrase.

## Open questions
None blocking. For the owner later: whether the weekly service should also run the P6 computing verbs (cell statistics, drift replay), which write records, once P10 evidence exists.

## Done when
- [x] Criteria have evidence; `scripts/check.sh` green
- [x] ADR 0094, roadmap row, docs built
