# 0243 — Simulate-everything is training data, fenced from the hold-out and from scorecard evidence

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Not yet built.
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

## Consequences
All history is slightly less than all. The job pauses for market hours and low disk, so it may take days.
