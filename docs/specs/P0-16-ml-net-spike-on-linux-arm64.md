# P0-16 — ML.NET spike on linux/arm64

- **Status:** Approved
- **Phase / roadmap row:** P0 — Foundations & data trial (feeds P6 "Learning engine & ML.NET model")
- **Design refs:** docs/design/03-target-architecture.md §2.4 (platform check); 09 §1 layer 3, §4, §9; 17 §2 (`xd-worker`); 02 NFR-2

## Goal
Prove, before P6 depends on it, that **ML.NET's gradient-boosted trees really work inside the containers we ship** (`linux/arm64` on this Mac,
Ubuntu "chiseled" runtime image, no Python) — training, calibration, explanations, save/load and speed — and decide which trainer P6 uses.
Doc 03 already names the risk: the LightGBM native library may not load on arm64, with a source build or the managed FastTree trainer as fallbacks.
This spike turns that into measured facts and a ready-to-use Dockerfile recipe.

## What is already known
- The worker/API images use `mcr.microsoft.com/dotnet/runtime:10.0.12-noble-chiseled` (no shell, no package manager, non-root); the SDK image is `10.0.400-noble`.
- Latest **stable** ML.NET is **5.0.0** (`Microsoft.ML`, `.LightGbm`, `.FastTree`); 6.0.0 is preview only and is not used.
- Open risks to test: (1) a `linux-arm64` build of `lib_lightgbm.so` may be missing from the NuGet package; (2) LightGBM needs OpenMP (`libgomp`), which a *chiseled*
  image does not contain even when the native library itself is present; (3) FastTree is managed code and should just work.

## Scope
**In:** a throwaway console spike on synthetic data; building and running it in the SDK image **and in the real runtime image**; the findings; a recipe for the
worker image; a trainer decision; a proposed `IProbabilityModel` shape; doc/ADR updates.
**Out:** real market data and features (P6), walk-forward validation and triple-barrier labels (P6), model registry/lifecycle (P6-06), any change to the running stack or its images,
GPU training.

## Approach
1. **Spike project** `backend/spikes/XDrishti.MlSpike` (+ Tests) in the solution under `/spikes/` (same build rules as everything else), packages pinned in `Directory.Packages.props`.
2. **Synthetic data, deterministic (seeded):** `N` rows × ~40 columns shaped like P6 will be — ~30 numeric features (returns, ATR %, RSI, volume ratio, gap %, distance to levels …),
   ~6 categorical (strategy, timeframe, side, style, regime, weekday), a time order, and a binary label from a *known* function (logistic of a few features + two interactions + noise, ~45% positive).
   The true probability is known, so calibration and "how close to optimal" are measurable. Default `N = 1,000,000` (roadmap number); optional 5,000,000.
3. **What the spike exercises:** `LightGbm` and `FastTree` binary trainers (categorical features, early stopping), a logistic baseline (SDCA), **PAV/isotonic calibration**,
   `CalculateFeatureContribution` per prediction, permutation feature importance, model **save → load → predict** equality, single-row and batch scoring through `PredictionEngine`/`Transform`.
4. **Where it runs (all three, same code):** (a) host-native macOS arm64 (reference), (b) `linux/arm64` container from the **SDK image**, (c) `linux/arm64` container from the **chiseled runtime image**
   (publish → copy, exactly like the worker Dockerfile). Container limits like production: ≤ 8 CPUs and 8 GB so the stack stays healthy; sequential runs, one at a time.
5. **If LightGBM fails to load:** diagnose (`ldd`/missing symbol, RID folder), fix in this order — add the missing system library to the chiseled image (copy `libgomp` from the build stage),
   then build `lib_lightgbm.so` for arm64 from the LightGBM source at a pinned release tag in a throwaway build stage — and record the exact working Dockerfile lines. Only if both fail: FastTree-only.
6. **Report:** `docs/research/p0-16-mlnet-spike.md` with tables, the recipe, the decision, and the raw result files (small) under `docs/research/mlnet-spike-results/`.

## Acceptance criteria
1. **Environment recorded:** architecture (`aarch64`), RIDs, .NET and ML.NET package versions (exact), base-image digests, CPU/memory limits used, and which of the three environments each result came from.
2. **Native library fact established:** for LightGBM in the SDK image **and the chiseled runtime image**, the spike either loads and trains, or the failure is diagnosed and the minimal fix found and demonstrated
   (the fix runs in the chiseled image, non-root, read-only root filesystem as in `compose.yaml`). The working configuration is written down as Dockerfile lines.
3. **FastTree works** in the same images (managed fallback proven, no native dependency).
4. **Capabilities proven in the container:** categorical + numeric features; PAV calibration; per-prediction feature contributions for both trainers; permutation importance; model save/load gives
   identical predictions (max |Δp| ≤ 1e-6); scoring in a fresh process after load.
5. **Quality sanity on the planted signal:** both GBDTs beat the logistic baseline by AUC ≥ 0.02 and get within 0.02 AUC of the known optimal; **expected calibration error ≤ 5 pp after PAV** (the P6 exit criterion),
   with ECE before/after reported.
6. **Training and scoring speed measured** (median of 3, 10 threads and 4 threads): training time for 1M rows × 40 features (e.g. 300 trees, 63 leaves, early stopping on a time-ordered validation split),
   peak memory (RSS), single-row prediction latency (p50/p95), batch throughput (rows/s), model file size — for LightGBM and FastTree, in container vs host-native (overhead %).
7. **Determinism recorded:** same seed → same model/metrics across runs (or the size of the spread if multi-threaded training is not bit-identical).
8. **P6 workload extrapolated:** from the measurements, an estimate of the weekly retrain (per style × side models) and the 12-fold walk-forward against NFR-2 "full re-learn < 4 h", with the training-time budget per model.
9. **Decision recorded:** trainer for P6 (LightGBM or FastTree), the exact `Dockerfile`/compose changes the `worker` image needs, thread/memory settings, and the proposed `IProbabilityModel` port; doc 03 §2.4 updated
   (ADR if it changes an accepted decision); plan stories P6-05/P6-06 adjusted.
10. **Safe and reversible:** no change to the running stack, compose file or existing images; spike images are separate and removable; synthetic data only (no market data, no credentials, no network at run time);
    NuGet packages are pinned; any source download for a LightGBM build is approved first and pinned by tag + SHA-256.
11. **Reproducible:** `scripts/mlnet-spike.sh` rebuilds and runs the spike in each environment; unit tests cover the data generator (determinism, label rate), AUC/ECE/PAV metric code and the timing harness;
    `scripts/check.sh` stays green; reviewer agent: no blockers/majors.

## Proposed thresholds (owner may change)
| Measure | Target |
|---------|--------|
| LightGBM trains and predicts in the chiseled image | works (with a documented fix if needed) — otherwise FastTree is the answer |
| Train 1M × 40, 10 threads, container | LightGBM ≤ 10 min · FastTree ≤ 20 min |
| Container overhead vs host-native | ≤ 25% |
| Single-row prediction p95 | ≤ 5 ms |
| Batch scoring | ≥ 50,000 rows/s |
| Calibration error after PAV | ≤ 5 percentage points |
| AUC vs logistic baseline | ≥ +0.02 |
| Peak memory | ≤ 6 GB (fits next to the stack) |

## Contract
- **API / Data / UI / Jobs:** none in the product; the spike writes only under `data/mlnet-spike/` (git-ignored) and small result files in `docs/research/`.
- **Files (expected):** `backend/spikes/XDrishti.MlSpike*`, `backend/spikes/XDrishti.MlSpike/Dockerfile` (SDK and runtime stages; separate from `backend/Dockerfile`), `scripts/mlnet-spike.sh`,
  `docs/research/p0-16-mlnet-spike.md`, `docs/research/mlnet-spike-results/`, updates to `Directory.Packages.props`, `XDrishti.slnx`, doc 03 §2.4, plan data, `scripts/README.md`; an ADR only if needed.
- **Machine changes (each needs approval):** NuGet restore of `Microsoft.ML`, `Microsoft.ML.LightGbm`, `Microsoft.ML.FastTree` 5.0.0; building two throwaway images; (only if required) downloading the LightGBM source at a pinned tag.

## Non-functional
- Heavy CPU for ~1–2 hours in total: runs are sequential, limited to 8 CPUs / 8 GB in containers, and paused while the stack must stay responsive.
- Reproducible: fixed seeds; results stored as JSON + Markdown; the same code path in all three environments.
- No secrets; nothing leaves the machine.

## Test plan
- **Unit (offline):** generator is deterministic and hits the target positive rate; the true-probability function is bounded; AUC, log loss, Brier and ECE against hand-computed small cases; PAV calibration is monotone and
  reduces ECE on a deliberately miscalibrated set; stopwatch/percentile helpers.
- **Live (agent-run, no credentials):** `scripts/mlnet-spike.sh host | sdk | runtime` → results JSON per environment; chiseled-image run as non-root with read-only root.
- A second run of one configuration to show run-to-run spread.

## Decisions (owner, 2026-10-10)
Q1–Q5: accepted defaults — packages 5.0.0 approved; LightGBM source build (pinned tag + SHA-256) approved if the NuGet library does not work on linux-arm64; 1,000,000 rows (5,000,000 stretch if time allows);
start as soon as approved, sequentially, capped at 8 CPUs / 8 GB; keep 3 pts and extend if the source build is needed.

## Open questions (answered)
1. **Packages:** OK to add `Microsoft.ML`, `Microsoft.ML.LightGbm` and `Microsoft.ML.FastTree` **5.0.0** (stable, pinned; the 6.0 previews are excluded)?
2. **Source build fallback:** if the NuGet LightGBM has no working linux-arm64 library, OK to download the LightGBM source at a pinned release tag (about 10 MB, GitHub, SHA-256 recorded) and build it in a throwaway image stage?
   Otherwise the answer would be FastTree-only.
3. **Data size:** 1,000,000 rows as in the roadmap — add a 5,000,000-row stretch run if time allows?
4. **Timing:** run during the day with an 8 CPU / 8 GB cap (the stack stays up), or in a window you name? Default: start as soon as it is approved, sequentially, pausing if the stack gets slow.
5. **Effort:** the plan says 3 pts; I expect 3–5 depending on the native-library findings — OK to keep 3 and extend if the source build is needed?

## Done when
- [ ] All acceptance criteria have evidence (result files + findings document)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Trainer decision and worker-image recipe recorded; doc 03 §2.4 (and an ADR if needed) updated
- [ ] Plan updated: P0-16 done; P6-05/P6-06 adjusted if needed
