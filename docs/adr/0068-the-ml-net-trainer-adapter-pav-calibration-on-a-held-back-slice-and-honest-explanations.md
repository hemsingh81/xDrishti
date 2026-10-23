# 0068 — The ML.NET trainer adapter: PAV calibration on a held-back slice and honest explanations

- **Status:** Proposed (spec [P6-06](../specs/P6-06-ml-net-trainer-calibration-and-contributions.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (the port `IScorerFactory` and the eligibility rule), [ADR 0066](0066-decision-time-features-a-side-table-and-the-training-set-record.md) (the features). ADR 0069 belongs to another slice.

## Context
P6-00 D4: a model is used only if it beats the cell statistics and a logistic regression out of sample and is calibrated. The P0 spike showed ML.NET trains, saves and loads fast and deterministically on linux/arm64, and that its isotonic calibrator matches our PAV; it also showed LightGBM at AUC 0.571 against a logistic baseline of 0.734 on synthetic data (cause not investigated), so the model may well lose.

## Decision
1. **A new project `XDrishti.Learning.MlNet`** is the only one that references `Microsoft.ML`, `.LightGbm` and `.FastTree` (versions already pinned); it references Domain only. Architecture tests assert both, and that no ML.NET type is in a public signature. It implements the Domain port of ADR 0067 (`IScorerFactory`, name `meta_model`), so the harness, the registry and the baselines are not aware of ML.NET.
2. **Training.** Train rows are ordered by decision time. The earliest 75 % fit the trees; the latest 25 % are held back and fit only the calibration map; fit rows whose label horizon reaches the first held-back decision are dropped. Test rows never reach `Fit` (the harness guarantees it). Missing or non-finite features are replaced by the mean of the fit rows. One fixed conservative setting (100 trees, 8 leaves, rate 0.05, 20 rows per leaf), one thread, fixed seed, no grid (a grid would be more trials against 2 years of data).
3. **Trainer.** LightGBM first; if its native library cannot load (macOS arm64 has none, `DllNotFoundException`), FastTree is used and the model's note, its artifact and the report limit say so. The numbers are never to be read as LightGBM's.
4. **Calibration.** Own PAV in Domain (`IsotonicCalibrator`): equal scores pooled, adjacent violators and equal neighbours pooled, linear between block means, flat outside; the probability is kept within [0.005, 0.995] so that a confident miss cannot have an unbounded loss. The P0 spike found it equal to ML.NET's isotonic.
5. **Explanations.** `Explain(row, top)` returns the largest per-feature contributions to the raw margin from ML.NET's tree-path calculator (positive pushes towards label 1). They are in raw-score units, before the monotone but non-linear calibration, and are stored with the score by the registry. `Importance(rows)` is the mean absolute contribution. This replaces permutation importance (P6-00), which would need the test metric and a second code path per trainer.
6. **Artifact.** `"XDMODEL1"`, header length, JSON header, ML.NET zip re-packed with sorted entries and a fixed timestamp; the SHA-256 is over all bytes. Same rows in any order give the same bytes. `Load(bytes, hash)` checks the hash first. Where the bytes are kept (`data/models/`) is P6-07.
7. **Failures.** `Train` returns `ml.too_few_rows`, `ml.one_class`, `ml.no_features`, `ml.bad_feature_width`, `ml.bad_options`; `Load` returns `ml.hash_mismatch`, `ml.bad_artifact`. `Fit`, which has no failure channel, returns the smoothed base rate with a note.
8. No migration, API, CLI or log events (ids 4500 to 4509 unused). The Dockerfile copies the project file; no host references the project yet (P6-07 does).

## Consequences
- Whether the model beats the baselines on real trades is **unknown**; nothing here claims it. On synthetic noise it does not (tested); on a synthetic interaction the baselines cannot see it does (tested). The first real answer needs P6-04 features on the owner's trades and at least 6 folds.
- On this development machine (macOS arm64) only FastTree runs; the LightGBM path is exercised only where the native library loads (the container). The tests accept either trainer.
- The spike's AUC gap is not explained by this slice (it used one-hot synthetic features and defaults); the planted-signal tests here pass with FastTree.
- The calibration map is fitted on about a quarter of the train rows: with few rows it is coarse, and the harness's calibration error will show it.
