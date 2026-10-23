# P6-13 — Meta-labelling policy

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended in P6-00). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-13 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 3 d
- **Design refs:** docs/design/09-learning-engine.md §14 (rule 6 and the honesty rules), layer 3 · docs/design/10-trade-selection.md (meta-labelling row) · docs/design/16 `meta_model`, `meta_label` · ADR 0075 · ADRs 0067 (folds, purge, hold-out), 0068 (the trainer), 0070 (registry, champion)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **Policy and evidence only; nothing is wired.** P6-00 question 7: the champion is advisory and unused until P7. The policy is a pure function plus a read-only verb `xd learn policy`; no scanner, plan or selection reads it.
2. **A champion is required** (P6-07 registry, role champion). Without one the answer is the outcome `NoPolicy` with the reason, before any run is read. The champion must also match what is evaluated: the same stored training set (id) and the same trainer settings as the running trainer; otherwise `NoPolicy` says why (the policy would judge a model other than the champion).
3. **Out of sample, per fold, honestly.** The folds are those of P6-05 (quarterly, first test 9 months in, 7-day embargo, purge by label horizon plus one bar). In each fold the champion's recipe is re-fitted on the train rows only and scores the test rows; the threshold used in a fold is chosen **only from the out-of-fold scores of earlier folds** (purged and embargoed the same way), never from the fold itself and never from the hold-out. A fold without enough earlier scores (30) is not evaluable. So fold 1 is never evaluable.
4. **Threshold rule.** Candidates are 0 (take everything) and 0.05, 0.10 ... 0.95 (a coarse grid, to limit overfit). A candidate must keep at least `min_kept_share` 0.5 of the candidates (doc 09 §14: a rule removing more than half is flagged, so the search never goes there). Value = net R of the kept trades divided by **all** candidates (skipped trades add nothing), the "expected net R per candidate". Highest value wins; ties go to the lower threshold (keep more). Threshold 0 is always allowed, so "skip nothing" is a legitimate result.
5. **Split.** The model is one model (strategy and side are features), so the default is one threshold. `--split none|style|side|style-side` learns separate thresholds per group of the cell key (doc 16 `split_by: [style, side]`); a group with fewer than 30 earlier scores takes everything and is flagged.
6. **Outcomes:** `NoPolicy` (no champion or champion mismatch), `NotEnoughEvidence` (fewer than 2 evaluable folds), `DoesNotHelp`, `Inconclusive`, `Helps`. Rules: it must show **skill**, not just a lift (skipping any share raises R per candidate when taking everything loses money): in at least `ceil(2/3 x evaluable)` folds and pooled, with at least `min_side_trades` 5 on both sides, (a) lift over take-everything above `min_lift_r` 0.02 R per candidate, (b) the lift beyond that of skipping the same share at random (`-skippedShare x baselineMean`) above `min_lift_r`, and (c) kept mean R minus skipped mean R above `min_skill_r` 0.1. Pooled it must also exceed `significance_z` 1.645 standard errors (unpooled sample variances of the two sides). All margins and the z are multiplied by `MultipleTesting.Factor(trials)`; trials = the larger of `--trials` and the champion's registry count (distinct models of its feature version), as P6-07; re-running with other splits or knobs counts as trials. A fold whose every group takes everything (too few earlier scores) is not evaluable, with the reason printed. Fewer than 6 usable folds cap `Helps` at `Inconclusive` (P6-05, P5-03). Doc 09 §14 flags cap it at `Inconclusive` too: the pooled out-of-sample policy removes more than half of the trades, or over the last 100 evaluated trades the skipped ones are not worse than the kept ones (checked when both sides have at least 5).
7. **Sizing multiplier (0.5 to 1.5) is not built.** `meta_label.size_range` needs a risk plan to apply to (P7); a multiplier fitted now would be an untested rule. Take/skip only.
8. **Lineage as a report, not a table.** Storing the policy with the model registration needs a migration of an insert-only table whose consumer (P7) does not exist, and the policy is a pure function of the stored runs and the champion. So no migration: the report text names the champion (id and hash prefix) and prints the policy; a recorded policy waits for P7.
9. **Hold-out:** the verb keeps the last 6 months out (`--hold-out-months`, 0 keeps none and the report says so).
10. Log ids 4570 to 4572. ADR 0075.

## Goal
For the champion model, a take/skip threshold per candidate entry with honest out-of-sample evidence that it beats taking everything, the realised R of the skipped trades beside the kept ones, or the plain statement that it does not help or that there is no champion.

## Scope
**In:** pure `Domain/Learning` (`MetaPolicyEvaluator`, options, split, totals, fold, threshold, outcome, report with a fixed text); `Application/Learning` query and handler `EvaluateMetaPolicy` (champion check, load and hold-out as `TrainModelHandler`); verb `xd learn policy`; ADR 0075; docs and roadmap.
**Out:** persistence/migration, endpoint, screen, sizing multiplier, wiring into the scanner or selection (P7), a second model for "worth taking", automatic retuning.

## Acceptance criteria
1. Hand table: with fixed scores and R, the threshold maximising net R per candidate under the kept-share floor is the expected grid value; ties go to the lower one; threshold 0 wins when no cut helps. *`MetaPolicyEvaluatorTests`.*
2. **Threshold without the hold-out and without the fold itself:** a recording scorer shows every fitted row is before the fold's test window minus the embargo and resolved before its first decision (with an embargo-boundary case); changing the labels of a fold's test rows or of later folds leaves that fold's train rows and threshold unchanged and (positive control) does change its totals and later thresholds; rows on or after `HoldOutStart` are refused.
3. Skipped-trade R is reported per fold, pooled and over the last 100.
4. A scorer with no skill (random, including on a book whose mean R is negative, where any skip raises R per candidate) gives `DoesNotHelp`; more trials or a stricter z turn a modest edge into `DoesNotHelp`; a scorer that separates winners from losers gives `Helps` when 6 folds exist and `Inconclusive` otherwise.
5. Determinism: the same rows in any order give the same text.
6. Bad input (no rows, bad options, duplicate key, label not 0/1, probability outside [0,1]) is a failure with a code.
7. The handler: no champion gives `NoPolicy` and reads no run; a champion on another training set, other trainer settings, another hold-out or other fold settings (compared in the champion's parameter text) gives `NoPolicy` with the reason; a bad request is refused before any read; otherwise it evaluates after cutting the hold-out and states it. *`EvaluateMetaPolicyHandlerTests`.*
8. The verb parses and refuses bad lines. *`LearnPolicyOptionsTests`.*
9. Architecture tests and `scripts/check.sh` green.

## Contract
- **API / Data / UI:** none. No migration.
- **Verb:** `xd learn policy --run RUNKEY:STYLE:EXIT [--run ...] [--split none|style|side|style-side] [--min-kept-share X] [--min-lift-r X] [--min-skill-r X] [--trials N] [--hold-out-months N]`; exit codes as the other `xd` commands.

## Non-functional
Pure functions, no clock or random; sequential sums, so the result is reproducible. One model fit per used fold (the trainer's cost).

## Test plan
As the acceptance list: hand tables and generated 14 to 30 month series for the folds, a fake scorer factory; handler with fakes.

## Honest limits
Trades within a fold are correlated, so the standard error is optimistic; the per-fold requirement is the check on that. An unreadable cell key with a split fails with `policy.cell_key`. The evaluator takes no cancellation token: it is a bounded in-memory computation (one model fit per fold) and the handler is the I/O boundary. Replayed history, modelled fills, data seen before (ISS-19). With the 6-month hold-out about 3 folds exist and fold 1 is never evaluable, so today at most 2 folds are evaluable and the outcome is at best `Inconclusive`. The threshold is searched, so the in-sample figures of the final threshold are optimistic; only the fold figures are out of sample, and those use the champion's recipe re-fitted per fold, not the champion's file (its training rows include later folds). Net R per candidate is on modelled fills. Not exercised on the owner's real stored runs in this slice.

## Open questions
None blocking. For the owner: whether the sizing multiplier is wanted at all once P7 has a plan to apply it to.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] ADR 0075, doc 09, roadmap, site rebuilt
