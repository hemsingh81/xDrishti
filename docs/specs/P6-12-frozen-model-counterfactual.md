# P6-12 — Frozen-model counterfactual

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended in P6-00). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-12 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 2 d
- **Design refs:** docs/design/09-learning-engine.md (Journey view: the frozen copy, section on honesty rules) · ADR 0077 · ADRs 0067 (folds, purge, hold-out), 0068 (the trainer), 0075 (take/skip effect on realised R)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **A report, nothing stored.** The answer is a pure function of the stored runs and the trainer recipe; read-only verb `xd learn counterfactual`. No migration, endpoint or screen (the Journey view is P8).
2. **The recipe, not a champion.** The question "does continued learning help?" is about the recipe the running trainer uses, so no champion is needed (unlike P6-13). Both sides are fitted by `trainer.Candidate` from the same stored training rows.
3. **Two sides, the same trades.** (a) Frozen: fitted once on rows known before the cut and never refitted. (b) Retrained: refitted at the start of each window (default every 3 months, expanding) on everything resolved before that window. Both score exactly the same later trades (same days, same instruments, by construction and by test).
4. **The period is what the data allow, at most a year.** Requested `frozen_months` 1 to 12 (default 12) before the end of the development data, but the cut is never earlier than `min_train_months` (9) after the first trade; the report prints the months actually used. The hold-out (default last 6 months) is never read. On about 2.1 years this gives roughly 7 to 10 months today.
5. **No leak.** Frozen train rows: entry date before the cut minus the 7-day embargo and resolved (plus one bar) before the period's first decision. Retrained rows: the same rule against each window's start and first decision. A window needs 10 rows.
6. **Metrics.** The P6-05 metrics (log loss, Brier, AUC, calibration error, lift) per side, per window and pooled; plus the P6-13-style effect on realised R: a trade is taken when the score is at or above the base rate of that side's own train set (a fixed rule, no searched threshold, so nothing to leak), with kept and skipped R beside take-everything.
7. **Judgement.** The first window has the same model on both sides (same train rows), so only windows where the retrained model has seen new labels are judged. At least `min_judged_windows` 3 else `Inconclusive` whatever the numbers. The paired per-trade log loss gain (frozen minus retrained) must exceed `min_gain_log_loss` 0.005 x the multiple-testing factor in `ceil(2/3 x judged)` windows and pooled, and pooled exceed `significance_z` 1.645 x factor standard errors, for `RetrainingHelps`; the mirror image gives `FrozenBetter`; otherwise `NoDifference`. If the realised-R effect points the other way the outcome is capped at `Inconclusive`. Outcomes: `Inconclusive`, `NoDifference`, `RetrainingHelps`, `FrozenBetter`. The 6-fold minimum of P6-05 is not applied: a year of quarterly windows gives 4 and 3 judged, so a verdict would be impossible; the minimum is a knob, printed with the result.
8. **Plan prices not built.** The P6-00 row mentions plan prices; there is no live record to compare with before P7, so it is listed as not built.
9. Log ids 4590 and 4591. ADR 0077.

## Goal
To say, with out-of-sample evidence on the same later trades, whether a model that keeps learning beats one that stopped learning, or that there is no measurable difference, or that the data are too short to tell.

## Scope
**In:** pure `Domain/Learning` (`CounterfactualEvaluator`, options, outcome, window, side, pooled, report with a fixed text); `Application/Learning` query and handler `EvaluateCounterfactual` (load and hold-out as `EvaluateMetaPolicyHandler`); verb `xd learn counterfactual`; ADR 0077; docs and roadmap.
**Out:** persistence/migration, endpoint, screen (P8), plan-price comparison, a champion requirement, automatic scheduling of retraining.

## Acceptance criteria
1. Same trades on both sides: a recording scorer shows the frozen scorer scores every in-period trade and the retrained scorers together score exactly the same set; windows' row counts add up; days and instruments are reported. *`CounterfactualEvaluatorTests`.*
2. No leak: the frozen side is fitted once on rows dated before the cut minus the embargo and resolved before the period's first decision (with a late-resolving row excluded); changing the labels after the cut leaves the frozen train rows and scores unchanged and (positive control) changes the retrained fits.
3. A relationship that moves after the cut gives `RetrainingHelps`; a recipe that is worse when it has more rows gives `FrozenBetter`; random labels and equal scores give `NoDifference`; a margin above the gain gives `NoDifference`.
4. Fewer judged windows than the minimum, or a history too short, gives `Inconclusive` with the reason; a shorter history shortens the period and the report says so.
5. Determinism: the same rows in any order give the same text.
6. Bad input (no rows, bad options, duplicate key, hold-out row, probability outside [0,1]) is a failure with a code.
7. The handler needs no champion, cuts the hold-out and states it (or says none was kept), and refuses a bad request before any read. *`EvaluateCounterfactualHandlerTests`.*
8. The verb parses and refuses bad lines. *`LearnCounterfactualOptionsTests`.*
9. Architecture tests and `scripts/check.sh` green.

## Contract
- **API / Data / UI:** none. No migration.
- **Verb:** `xd learn counterfactual --run RUNKEY:STYLE:EXIT [--run ...] [--frozen-months N] [--retrain-months N] [--min-gain X] [--trials N] [--hold-out-months N]`; exit codes as the other `xd` commands.

## Non-functional
Pure functions, no clock or random; sequential sums, so the result is reproducible. One model fit for the frozen side plus one per judged window.

## Test plan
As the acceptance list: generated 22-month series with a regime flip, recording and fake scorer factories; handler with fakes.

## Honest limits
The period is at most about a year of stopped learning, less after the hold-out and the minimum training months; it says nothing about longer. With 3 judged windows the verdict is thin and trades within a window are correlated, so the standard error is optimistic. Replayed history, modelled fills, data seen before (ISS-19). The take rule (score at or above the train base rate) is a fixed convention, not the best threshold. Not exercised on the owner's real stored runs in this slice. A disagreement between the log loss and the realised-R measure is capped at `Inconclusive` and is covered by reading the code path, not by a dedicated test.

## Open questions
None blocking. For the owner: whether the minimum of 3 judged windows (below the 6 folds of P6-05) is acceptable, given that the data cannot give more.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] ADR 0077, doc 09, roadmap, site rebuilt
