# 0153 - The small-live phase check is computed on read from stored records and is the only exit report that can unlock exit_confirmed; anything unreadable is not met

- **Status:** Accepted (decided in P11-06 under the owner's standing instruction; spec [P11-06](../specs/P11-06-small-live-phase-check.md), "Amendments at build 2026-10-29").
- **Date:** 2026-10-29.
- **Relation to other ADRs:** [0151](0151-the-ramp-gates-breach-rules-and-halts-read-the-stored-live-record-and-write-only-insert-only-rows-the-evaluator-moves-no-ticket-and-the-exit-report-that-unlocks-s3-is-not-built.md) (left the `NoExitReport` gap), [0152](0152-the-live-versus-paper-report-pairs-the-same-ticket-on-both-sides-in-r-on-the-plans-risk-per-share-judges-it-by-a-one-sided-non-inferiority-bound-and-is-computed-on-read-from-stored-rows.md) (C4 reads its verdict), 0097 (the pattern and the allow-list pin).

## Context
S3 (100 % risk) needs the owner's `exit_confirmed`, which is refused unless an exit report says MET. P11-04b registered a report that never says MET.

## Decision
1. A pure `LivePhaseCheck` judges C1 to C8 and integrity line I from counted facts; a criterion is NOT MET only on a stored fact against it, NOT EVALUABLE when data is short or a source is unreadable, MET only with the evidence. Overall MET needs all nine and a well-formed window; caveats are stacked in the label.
2. `EvaluateLivePhaseCheckHandler` reads the stores (reads only, no write, no broker call) and the P11-03 summary; `PhaseCheckExitReport` replaces `NoExitReport`, so `exit_confirmed` re-reads the check at the moment of the act. No setting, auto or otherwise, changes a ramp step here; the report only recommends in words.
3. Surfaces: `GET /api/live-trading/phase-check`, `xd live phase-check` (exit 0 whatever the verdict), the read-only Phase check tab; log id 5150 (date, word, counts).
4. Build choices are listed in the spec's amendments (window, C2 factor source, C6 and C7 week rules, integrity definitions, blockers).

## Consequences
S3 stays unreachable until about eight weeks of real, captured, paired live trading plus the three compliance records exist. The result is weak evidence (see the printed limits). The 150-session scan cap and per-session reads are fine at this scale.
