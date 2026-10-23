# P6-07 — Model registry and scoring

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-07 of [P6-00](P6-00-learning-engine-and-ml-plan.md)
- **Design refs:** docs/design/09-learning-engine.md (champion/challenger) · docs/design/16 `learning.meta_model` · docs/design/03 (persistence: model file + `research.model_versions`) · ADR 0070 · ADR 0067 (the verdict) · ADR 0068 (the artifact and `Load` with a hash)

## Assumptions (taken as recommended)
- P6-00 question 7: the champion is **advisory and unused until P7**. Naming one changes nothing anywhere; the registry only records it. Nothing reads "the champion" yet except the API.
- P6-00 question 8: model files under `data/models/<sha256>` (flat, content-addressed), the hash in `research.model_versions`. A size cap of 64 MB is the artifact loader's own (ADR 0068). The database holds no model bytes.
- A file is **never trusted without its recorded hash**: it is read, hashed and compared to the recorded SHA-256 before it reaches the loader, and the loader (`MlNetModel.Load`) checks the hash again. `LoadUnverified` is not used by the registry.
- **Champion / challenger / retired is an append-only designation record** (`research.model_designations`, no UPDATE, no DELETE). A model's role is its latest designation; a new model starts as a challenger (written with the registration). Naming a champion appends a `retired` row for the previous champion and a `champion` row, in one transaction.
- **Only a model whose validation verdict is `Eligible` may be named champion, and the verdict alone is not trusted** (the command line sets the harness options). `ChampionRule` also requires a kept hold-out, at least 6 folds used (`ValidationOptions.DefaultMinFolds`) and the model's `Trials` at least the trials the registry shows (the distinct models by file hash of the same feature catalog version registered up to it, itself included); a refusal lists every reason. At registration `Trials` is the larger of `--trials` and (distinct other models of that feature version already registered + 1); `--min-folds` below 6 caps an `Eligible` verdict at `Inconclusive`. `CannotBeatBaselines` and `Inconclusive` models can be registered (as challengers) and kept for comparison. What the rule reads is immutable (insert-only), so the check needs no lock. Migration `ModelRegistryTrialsAndPurged` adds `trials` and `purged_rows`. The development rows purged by the hold-out rule are in the report text and the record.
- The same bytes can be registered twice with different validation (other hold-out, other folds): the registration key is the SHA-256 of the model hash, the training-set id, the parameters and the report text; the file is shared. The same inputs give the same key and a second registration writes nothing (`AlreadyRegistered`).
- The production trainer is **FastTree** (ADR 0068 decision 3: the container images carry no `libgomp1`, LightGBM is not proven there). So the registered `trainer` is `FastTree` with no fall-back note. Switching is a one-line change after a container build proves LightGBM; the note and the trainer are recorded per model either way.
- The final model is trained on **all development rows** (after the hold-out cut, default last 6 months as in `xd learn windows`); the validation report of the same rows, the same trainer settings and the same seed is its verdict. The final model's own in-sample numbers are not stored as performance.
- The scorer port is `IModelScorerLoader` in Application (Domain types in and out: `IExplainableScorer`). Only Infrastructure references `XDrishti.Learning.MlNet`; only that project references `Microsoft.ML*` (the architecture test keeps both).
- `score` is a handler (`ScoreWithModelQuery`) for experiments and P7, not an endpoint or a verb: no score is shown to the owner yet.
- `research.model_versions.training_set_id` is a foreign key to `research.training_sets.set_id`: the training-set record is stored by the same use case before the model.
- Log ids 4520 to 4525. ADR number 0070.

## Goal
A trained model is kept as a file with its hash and a record of what made it (training set, parameters, calibration table, feature importance, validation verdict), can be loaded back only if its bytes still match, can be named champion only when it was proven eligible, and can be listed and read through the API.

## Scope
**In:** migration `AddModelRegistry` (`research.model_versions`, `research.model_designations`, insert-only for `xd_app`, Down); Domain `ModelRole`, `ChampionRule`; Application ports `IModelRegistry`, `IModelFiles`, `IModelTrainer`, `IModelScorerLoader`; handlers `TrainModelHandler` (load the training data from stored runs, store the training-set record, cut the hold-out, validate with the P6-05 harness, train the final model, write the file, register it), `DesignateModelHandler`, `ListModelsHandler`, `GetModelHandler`, `ScoreWithModelHandler`; Infrastructure `ModelRegistry` (raw SQL), `ModelFiles` (atomic temp-file and rename, hash checked on write and read), `MlNetModelTrainer`, `MlNetScorerLoader`; `GET /api/models` and `GET /api/models/{id}`; verbs `xd learn model` and `xd learn designate`; the shared extraction of the training data out of `BuildTrainingSetHandler`.
**Out:** using a score to change a plan (P7), drift of a champion (P6-09), meta-labelling policy (P6-13), automatic promotion, retraining schedules, a UI, an endpoint to designate or to train (a write path with no screen to use it; the verbs do it), LightGBM in the container.

## Acceptance criteria
1. Migration: both tables exist, `xd_app` can SELECT and INSERT but not UPDATE, DELETE or TRUNCATE; the migration has a `Down`; shape checks (hash is 64 hex, verdict and role are from their lists, arrays are the same length).
2. `ModelFiles`: write is atomic (temp file in the same folder, then rename) and idempotent (the same hash is not rewritten); a write whose bytes do not hash to the name is refused; a read of a file whose bytes no longer hash to the name is `models.file_corrupt`, a missing file `models.file_missing`; a malformed hash is refused before any path is built (no path traversal).
3. `ChampionRule`: only `Eligible` may be champion; `CannotBeatBaselines` and `Inconclusive` get `models.not_eligible` (409) with the verdict and the reasons.
4. `TrainModelHandler`: a bad command is `learning.invalid` before any read; the training-set record, the file and the model record are written once; a second run of the same inputs writes nothing new and says `AlreadyRegistered`; the model is registered as a challenger whatever its verdict; the record carries the training-set id, the verdict, the folds, the pooled numbers, the reasons, the report text, the parameters, the calibration table and the importance; the hold-out rows are never in the validation rows or the final training rows.
5. `DesignateModelHandler`: unknown model is `models.not_found`; champion needs `Eligible`; naming a champion retires the previous one in the same transaction; naming the role it already has writes nothing; designations are only ever appended (history is kept and returned).
6. `ScoreWithModelHandler`: reads the record, the file (hash checked), loads through the port, returns the probability and the top contributions with the feature names; a wrong feature count is `learning.invalid`; a file that fails the hash is `models.file_corrupt` and nothing is scored.
7. API: `GET /api/models` (newest first, optional `role`, `limit` 1 to 200) and `GET /api/models/{id}` (404 `models.not_found`); the view carries the verdict, the reasons, the limits and a sentence that the model is advisory and unproven on real trades; no model bytes.
8. Architecture: only `XDrishti.Infrastructure` (among libraries) references `XDrishti.Learning.MlNet`; no project other than the adapter references `Microsoft.ML*`; the new handlers are internal sealed and registered; the writers of the research tables list includes the registry.
9. Determinism: the same stored runs give the same model hash and the same registration key.
10. Verb `xd learn model` prints the verdict, the folds and the honest limits; `xd learn designate` prints the new role; both parse their options without I/O; exit codes as the other `xd` verbs.

## Contract
- **API:** `GET /api/models?role=&limit=` → `ModelView[]`; `GET /api/models/{id}` → `ModelDetailView` (model, parameters, report text, calibration table, importance by name, designations, limits). Errors: 422 `learning.invalid`, 404 `models.not_found`.
- **Data:** `research.model_versions` (id, version_key unique, sha256, artifact_bytes, trainer, note, training_set_id FK, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds, folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores[], calibration_values[], importance[], created_at, created_by); `research.model_designations` (id, model_id FK, role, reason, created_at, created_by). Both insert-only for `xd_app`. Migration `AddModelRegistry`.
- **UI:** none.
- **Files:** `data/models/<sha256>` (config `Models:Folder`; `scripts/xd.sh` sets it to `<repo>/data/models`).

## Non-functional
Training uses the P6-06 adapter (one thread, fixed seed). Files are at most 64 MB. Logs carry model ids, first 12 hash characters, counts, codes and milliseconds only. The hash is integrity, not authenticity (ADR 0068): whoever can replace both the file and the database row can replace the model.

## Test plan
Unit: `ChampionRuleTests`; handlers with fakes (`TrainModelHandlerTests`, `DesignateModelHandlerTests`, `ModelQueryHandlerTests`, `ScoreWithModelHandlerTests`). Infrastructure: `ModelFilesTests` on a temp folder. Integration (real TimescaleDB): registry round trip, idempotence, designation history and champion replacement, grants, `GET /api/models`. MlNet adapter through `MlNetModelTrainer`/`MlNetScorerLoader` (Infrastructure.Tests). Architecture: the MlNet reference rule. CLI option parsing.

## Open questions
None blocking. Not known and not claimed: that any registered model beats the baselines on real trades.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract regenerated; migration added; ADR 0070; docs and roadmap updated
