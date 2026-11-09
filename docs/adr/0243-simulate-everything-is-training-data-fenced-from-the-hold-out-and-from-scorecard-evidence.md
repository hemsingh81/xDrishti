# 0243 — Simulate-everything is training data, fenced from the hold-out and from scorecard evidence

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Built in AU-11 (record and fence); the job is AU-12.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Extends [0207](0207-a-strategy-version-is-pre-registered-the-ledger-counts-every-extra-look-and-a-locked-hold-out-is-fixed-at-the-first-post-and-read-once-by-an-owner-unlock-with-enforcement-and-the-honest-count-as-the-owners-switches.md). Does not change [0217](0217-a-model-may-influence-a-decision-only-through-one-gate-whose-predicate-needs-an-unspent-hold-out-forward-evidence-no-calibration-alarm-and-the-owners-switch.md), [0220](0220-a-challenger-is-promoted-only-on-the-spec-rule-and-a-champion-is-rolled-back-by-the-p6-09-detectors-behind-three-owner-switches-that-are-all-off-by-default.md) or [0221](0221-the-readiness-scorecard-is-derived-and-read-only-unavailable-is-a-fail-and-the-owners-enforce-switch-off-by-default-makes-the-two-lifecycle-gates-refuse-instead-of-warn.md).

## Context
The owner wants every stock over all history simulated and learned from. Left unfenced, that would spend the hold-out and manufacture evidence.

## Decision
1. Runs carry `purpose`: `research` (default) or `training`. Verdicts, the scorecard, cell lifecycle and selection exclude `training`; becoming evidence needs a normal Lab post that meets the existing rules.
2. "All history" means up to the hold-out block of [0207](0207-a-strategy-version-is-pre-registered-the-ledger-counts-every-extra-look-and-a-locked-hold-out-is-fixed-at-the-first-post-and-read-once-by-an-owner-unlock-with-enforcement-and-the-honest-count-as-the-owners-switches.md). Without a determinable block the job refuses.
3. Each job adds one ledger row per strategy version (`training_sweep`) before any number exists.
4. Training uses the purged quarterly folds of ADR 0067 plus a short embargo, on a separate basis. A model is at most a challenger; [0217](0217-a-model-may-influence-a-decision-only-through-one-gate-whose-predicate-needs-an-unspent-hold-out-forward-evidence-no-calibration-alarm-and-the-owners-switch.md) and [0220](0220-a-challenger-is-promoted-only-on-the-spec-rule-and-a-champion-is-rolled-back-by-the-p6-09-detectors-behind-three-owner-switches-that-are-all-off-by-default.md) are unchanged.
5. Reports read only finished chunks and state how complete they are.

## Built in AU-11 (decisions taken by delegation)
Approved by delegation (owner, 2026-11-09: unsettled decisions are researched, the best chosen, recorded here).
1. **Where the fence sits.** At the data access, so a new reader cannot miss it: the run store reads, the report source, the comparison member check and the `aiv` views take `purpose = 'research'` only; the ledger reads leave out `training_sweep`. `EvidenceReaderFenceArchitectureTests` fails any source file that reads `research.sim_runs`/`sim_trades` or `lab.test_ledger` without the fence, and any later migration that redefines an evidence view without it; `TrainingRunFenceTests` proves each existing reader against a real database. Scorecard, Lab verdicts, cell statistics, plan selection and model promotion read runs only through those paths, so they are fenced without a per-handler flag.
2. **No conversion path.** The same run key under the other purpose is `store.run_conflict`, never a no-op; a comparison cannot name a training run; the only way to evidence is a normal Lab post.
3. **Embargo.** Training windows end `HoldOut:TrainingEmbargoDays` (default 14, range 7..90) calendar days before the block start, on top of the block itself. Best practice for purged and embargoed cross-validation (Lopez de Prado) is an embargo at least the label horizon; 14 days covers the longest holding period the simulator allows with margin. It is a validated setting, not part of the hold-out snapshot hash (it does not size the block).
4. **Refusal does not depend on `Enforce`.** A Lab window may reach the hold-out when Enforce is off (the report warns); a training window never may. No determinable block (no bars, history too short) means no window: fail closed.
5. **Ledger.** `training_sweep` is a new `trial_kind`; the counts of looks (`run`, `replan`, `null`) never include it, so bulk simulation cannot inflate or hide the honest count.
6. **Down migration** refuses while a training run exists (dropping the column would turn it into evidence).

## Consequences
All history is slightly less than all. The job pauses for market hours and low disk, so it may take days.
