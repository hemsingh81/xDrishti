# 0075 — Meta-labelling policy: thresholds from earlier folds, and "does not help" as an answer

- **Status:** Proposed (spec [P6-13](../specs/P6-13-meta-labelling-policy.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (folds, purge, hold-out), [ADR 0068](0068-the-ml-net-trainer-adapter-pav-calibration-on-a-held-back-slice-and-honest-explanations.md) (the trainer), [ADR 0070](0070-model-registry-content-addressed-files-and-an-append-only-champion-record.md) (champion). ADR 0074 belongs to another slice.

## Context
Doc 09 section 14 rule 6: a score below a threshold puts a candidate on reserve. A threshold tuned on the same trades it is judged on always looks good, and a score with no skill still yields a lucky threshold. The rule must be chosen without the data it is judged on and must be allowed to answer "does not help".

## Decision
1. **Pure core** `MetaPolicyEvaluator.Evaluate(rows, model, options, validation)` in `Domain/Learning` over the `ValidationRow`s and the shared folds of P6-05. In each fold the champion's recipe is re-fitted on the train rows and scores the test rows.
2. **Threshold** from the out-of-fold scores of earlier folds only (purged by label horizon plus one bar and embargoed like a train set), never the fold itself, never the hold-out. Grid 0, 0.05 ... 0.95; at least `min_kept_share` 0.5 of the candidates kept; maximise net R of the kept trades per candidate (skipped add nothing); ties to the lower threshold, so "take everything" (0) is always possible. Optional split by style, side or both (doc 16 `split_by`); a group with fewer than 30 scores takes everything.
3. **Judgement:** lift over take-everything above 0.02 R per candidate in `ceil(2/3 x evaluable)` folds and pooled, and the pooled skipped trades worse than the kept ones, else `DoesNotHelp`. Fewer than 2 evaluable folds (fold 1 never is) is `NotEnoughEvidence`. The section 14 flags (over half removed out of sample; skipped not worse over the last 100) and fewer than 6 usable folds cap `Helps` at `Inconclusive`.
4. **A champion is required** (P6-07). The use case checks that the champion was made from the same stored training set and with the running trainer's settings, else `NoPolicy` with the reason. The champion is advisory until P7 (P6-00 question 7).
5. **Evidence only.** Read-only verb `xd learn policy` (handler `EvaluateMetaPolicy`, log ids 4570 to 4572), hold-out 6 months by default. No migration: a recorded policy needs a consumer that does not exist before P7, and the policy is a pure function of the stored runs and the champion; the report names the champion (id, hash prefix). No sizing multiplier (0.5 to 1.5): there is no plan to apply it to.

## Consequences
- With the hold-out about 3 folds exist, so at most 2 are evaluable and the answer is at best inconclusive; the same code gives verdicts on longer history (ISS-19).
- The fold figures re-fit the recipe per fold; they are not the champion file (which has seen later folds). The proposed final thresholds are chosen on all development scores and are optimistic; only the fold figures are out of sample.
- Net R is on modelled fills. Not exercised on the owner's real stored runs in this slice.
