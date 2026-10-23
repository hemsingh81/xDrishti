# P6-18 — The phase check and the learning API

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-18 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 2.5 d
- **Design refs:** docs/design/09-learning-engine.md §2, §6, §11, §12 · docs/design/18-roadmap.md (P6 exit) · ADR 0080 · ADRs 0067 (validation and eligibility), 0070 (registry and champion rule), 0072 (experiments) · ISS-19 (2.1 years, no hold-out), ISS-18 (modelled fills)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **The exit criteria are three**, in the roadmap's words: (1) the meta-model beats the baselines out of sample, (2) calibration error is at most 5 pp, (3) a knob change can be replayed and compared in under 10 minutes. The check evaluates them from stored records; it trains nothing, tunes nothing and computes no new statistic.
2. **Which model.** The champion if there is one, otherwise the newest challenger; the report says which. Retired models are not judged.
3. **Criterion 1 is MET only on the champion record** (P6-00 owner questions 2 to 4, as recommended): the stored verdict is Eligible and `ChampionRule` finds no reason against (a hold-out was kept, at least 6 folds, the trials of the registry). `CannotBeatBaselines` is NOT MET. `Inconclusive`, or Eligible without the champion record, is NOT EVALUABLE. With about 3 usable folds on 2.1 years the expected honest answer is NOT EVALUABLE, and that is printed as such.
4. **Criterion 1** also checks the stored verdict against its own stored counts: Eligible with fewer folds beating both than needed, or with the pooled result not beating both, is NOT EVALUABLE ("stored record inconsistent"). **Criterion 2** reads the stored pooled out-of-sample calibration error. Over 5 pp is NOT MET whatever the fold count (a fact about the pooled sample). At or under 5 pp is MET only with at least 6 folds; with fewer it is NOT EVALUABLE ("within the limit, too few folds to call it met"). No model, or a stored value that is not a number, is NOT EVALUABLE.
5. **Criterion 3 measures the comparison, not the replay.** The check takes the newest stored experiment, reads its two stored runs, judges them again with the experiment judge (default rules and the stored knob, trials and hold-out) and times that with `TimeProvider`. The time is the comparison half of "replay and compare". The replay that makes the candidate run (the Lab replay on 214 instruments) is **not** measured by the check. Without a replay time the criterion is NOT EVALUABLE (NOT MET if the comparison alone is 10 minutes or more); MET needs `--replay-seconds X`, a replay the owner timed, printed as supplied and not verified (the verdict line reads `MET (replay time supplied, not verified)`, and the summary repeats it), the total below 600 s and the judged outcome equal to the stored one. No experiment stored, or its runs gone, is NOT EVALUABLE with the reason.
6. **Limits are always printed** (about 2.1 years, quarterly folds leaving about 3 usable, 6 needed, the model's hold-out status and data range, seen-data folds and modelled fills, the Lab replay not measured, stored records only and the model advisory until P7; criterion 1 trusts the stored verdict and cannot see command-line margins that were loosened; the re-judge keeps trades dated before the hold-out start where the experiment run used the hold-out reserve set, which may give "not reproduced" but never a false MET). They are in the report object and in the text; no outcome omits them.
7. **Deterministic text.** The same facts give the same text. The measured milliseconds are an input fact, so a repeat run differs only by that measurement.
8. **Exit code 0** whatever the criteria say: the command is a report, not a gate. 1 refused (a bad `--replay-seconds`), 2 usage, 3 failure, 130 cancelled.
9. **Learning read API: one new endpoint.** Cells, models, experiments, hypotheses, lessons and the changelog already have read endpoints (P6-01 to P6-16). The plan lists the Journey screen (`GET /api/learning/journey`) as out of this slice. The one thing the Learning screen needs and nothing gives is the overview: `GET /api/learning/overview` with the rows of the newest cell snapshot and its date, and for models, experiments, hypotheses, lessons and the changelog the count of the newest 200 (flagged when there may be more), the split by role, outcome or state, and the newest record with its stored verdict (champion too for models). Nothing is computed or scored. The phase check itself is a verb, not an endpoint.
10. **Reuse, not copy.** The run reader of the experiment handler becomes `ExperimentRunReader` and is shared with the phase check; no behaviour changes.
11. **ADR 0080; log ids 4620 to 4623; no table and no migration.**

## Goal
Say, from what is stored, how far the phase is from its exit criteria, without ever printing MET for something the data cannot support, and give the Learning screen a first read of the learning records.

## Scope
**In:** pure `Domain/Learning` (`PhaseCheck`, `PhaseCheckReport`, `PhaseCriterion`, `PhaseVerdict`, `PhaseModelFacts`, `PhaseReplayFacts`); `Application/Learning` handlers `EvaluatePhaseCheckHandler` and `GetLearningOverviewHandler`; verb `xd learn phase-check [--replay-seconds X]`; endpoint `GET /api/learning/overview`; contract regenerated; ADR; docs and roadmap.
**Out:** the Journey endpoint and the Learning screen (P8); an endpoint for the phase check; training, tuning or any new statistic; a timed full Lab replay on 214 instruments (not run here); using the hold-out (the owner's call at the phase end); a write endpoint.

## Acceptance criteria
1. Each criterion has its three outcomes by a hand table, and MET is never printed without a champion record (1), 6 folds (2) and a replay time (3). *`PhaseCheckTests`.*
2. The limits are printed in every outcome, and the text is deterministic. *`PhaseCheckTests`.*
3. The handler picks the champion before a challenger, prints nothing as met when nothing is stored, measures the comparison of two stored runs, reproduces or refuses the stored outcome, reports a missing run as not evaluable with the reason, refuses a nonsensical replay time and writes nothing. *`EvaluatePhaseCheckHandlerTests`.*
4. The overview counts, splits and names the newest and the champion from stored records, flags a section at the count limit and answers on an empty system. *`GetLearningOverviewHandlerTests`, `LearningOverviewApiTests`.*
5. The command line parses `--replay-seconds` and refuses bad lines. *`LearnPhaseCheckOptionsTests`.*
6. The run on the owner's data is reported whichever way it falls. *Reported 2026-10-23: no model and no experiment were stored, all three NOT EVALUABLE (ADR 0080).*

## Design
See ADR 0080. The handler reads `IModelRegistry`, `IExperimentLedger` and `IRunStore` through their ports, builds the plain facts and hands them to the pure `PhaseCheck`. The overview reads the six stores through their ports and projects stored fields only.

## Test plan
Unit: `PhaseCheckTests` (domain), `EvaluatePhaseCheckHandlerTests`, `GetLearningOverviewHandlerTests` (application, fakes). Integration (real TimescaleDB): `LearningOverviewApiTests`; CLI parsing in `LearnPhaseCheckOptionsTests`. The real-data read is by `scripts/xd.sh learn phase-check` and is reported, not asserted.

## Open questions
None for the build. For the owner at the phase end: use the kept hold-out once (P6-00 question 2) and decide on the Dhan backfill toward 5 years (question 1, ISS-19); only those can turn criteria 1 and 2 from NOT EVALUABLE into a call.

## Done when
- [x] Acceptance criteria 1 to 6 have evidence
- [x] `scripts/check.sh` green; contract regenerated
- [x] ADR 0080, roadmap row and P6 status line, doc 09 note, `scripts/build-docs.sh` run
