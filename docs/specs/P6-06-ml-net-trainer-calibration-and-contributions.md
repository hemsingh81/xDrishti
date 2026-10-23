# P6-06 — ML.NET trainer, PAV calibration and contributions

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-06 of [P6-00](P6-00-learning-engine-and-ml-plan.md)
- **Design refs:** docs/design/09-learning-engine.md · docs/design/16 `learning.meta_model` (trainer lightgbm, fallback fasttree, calibration pav, must_beat_baseline) · ADR 0068 · ADR 0067 (the harness this plugs into)

## Assumptions (taken as recommended)
- P6-00 question 8: model files live under `data/models/` with a SHA-256; **this slice only produces the bytes and the hash**, storing them is P6-07.
- The adapter implements the Domain port of P6-05 (`IScorerFactory` / `IRowScorer`) over `ValidationRow`s (a training set is turned into rows by `ValidationRows.From`), not a new `IModelTrainer`; one port, so the harness and the registry share it.
- **One fixed setting, no grid** (P6-00 said "small fixed grids chosen inside train folds"). With about two years of data every extra setting looked at is another way to fit noise; the setting count would be the `trials` of the rule. Deviation from the plan row, on purpose; a grid can be added later with `trials` raised.
- `split_by: [style, side]` is **not** built here: the caller decides which rows to hand over (strategy, style and side can be features). One model over the rows it is given.
- Calibration share 25 % of the train rows (the latest by decision time), the probability floor 0.005, 100 trees, 8 leaves, learning rate 0.05, min 20 rows per leaf: my choices, options, stored in the artifact where they matter.
- Feature contributions come from ML.NET's tree-path contribution calculator (`CalculateFeatureContribution`): exact for the margin of the trees, in raw-score units, before the calibration map. Permutation importance is replaced by the mean absolute contribution (`MlNetModel.Importance`), computed the same way for both trainers.
- No log events (ids 4500 to 4509 stay unused): the adapter is a pure function of its rows; the fall-back is recorded in the model's note and artifact, which the registry and the report print.
- ADR number 0068.

## Goal
A candidate model that the P6-05 harness can score against the baselines: trees trained on the early part of the train rows, a monotone calibration map fitted on the later part, an explanation per prediction, and bytes plus hash for the registry. It must be able to lose.

## Scope
**In:** Domain `IsotonicCalibrator` (own pool-adjacent-violators), `FeatureContribution`, `IExplainableScorer`; new project `XDrishti.Learning.MlNet` (the only one with `Microsoft.ML*`): `MlNetScorerFactory` (`IScorerFactory`, name `meta_model`), `MlNetModel` (scoring, `RawScore`, `Explain`, `Importance`, `ToArtifact`, `Load`), `MlNetTrainerOptions`, `ModelArtifact` (bytes plus SHA-256), LightGBM first and FastTree when the native library cannot load (recorded in the note), deterministic seed and one thread, architecture tests.
**Out:** the model registry and files on disk (P6-07), champion/challenger, drift, the spike's AUC-gap investigation on real data (needs P6-04 features on real trades), grids, per-`[style, side]` splitting, API/CLI/UI.

## Acceptance criteria
1. PAV by hand tables: pooled blocks, equal scores one point, cascading pools, interpolation, flat outside, never falls, deterministic, bad knots refused.
2. A set with a linear signal is scored better than the base rate out of sample (log loss and AUC); the planted feature has the largest contribution and the right sign; importance ranks it first.
3. A set with no signal comes out `CannotBeatBaselines` through the P6-05 harness; a set with an interaction that neither baseline can see is beaten pooled and in most folds.
4. Calibration is monotone in the raw score and inside `[floor, 1 - floor]`.
5. No leak: the held-back calibration rows are the latest in time; changing their labels changes the calibration but not the trees; changing fit-row labels changes the trees; a fit row whose horizon reaches the first held-back decision is dropped.
6. Determinism: the same rows in any order give the same artifact bytes and hash; another seed gives another hash.
7. Round trip: a saved model loads (hash checked), scores, explains and saves to the same hash; a wrong hash, foreign bytes or a cut file are a failure with a code, not an exception.
8. LightGBM unavailable: the model is FastTree and says so in its note; asked for FastTree there is no note.
9. Rows that cannot be trained on (too few, one class, no features, bad options) are failures with a code from `Train`; `Fit` then returns the base rate with a note.
10. Architecture: the adapter references only Domain and the three ML.NET packages; no other project references `Microsoft.ML*`; no ML.NET type in a public signature.

## Contract
- **API / Data / UI / jobs:** none. No migration, no log events.
- Artifact: `"XDMODEL1"`, int32 header length, JSON header (trainer, width, imputation means, calibration knots, floor, row counts, seed, note), then the ML.NET model zip re-packed with sorted entries and a fixed timestamp.

## Non-functional
Single thread and fixed seed for reproducible bytes. Scoring one row takes a lock (the ML.NET prediction engine is not thread-safe). Speed was measured by the P0 spike in the container (276,000 rows/s batch with LightGBM) and is not re-measured here.

## Test plan
`IsotonicCalibratorTests` (Domain.Tests); `MlNetScorerFactoryTests`, `MlNetHarnessTests` (Learning.MlNet.Tests); `MlNetArchitectureTests`.

## Open questions
None blocking. For the owner: whether a real model beats the baselines on real trades is **not known** and not claimed; it needs P6-04 features on the owner's data and at least 6 folds (ISS-19).

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Design docs/ADR updated
