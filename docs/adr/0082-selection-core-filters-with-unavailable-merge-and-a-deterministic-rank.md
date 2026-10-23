# 0082 — Selection core: filters with `unavailable`, a deterministic merge and rank, the model advisory

- **Status:** Proposed (spec [P7-01](../specs/P7-01-selection-core.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0064](0064-cell-statistics-bayesian-shrinkage-recency-weights-and-point-in-time-snapshots.md) (the statistics used), [0073](0073-the-learning-changelog-and-the-cell-lifecycle-the-log-is-the-state.md) (cell states), [0075](0075-meta-labelling-policy-thresholds-from-earlier-folds-and-does-not-help-as-an-answer.md) and [0080](0080-the-phase-check-exit-criteria-from-stored-records-and-never-met-on-thin-data.md) (the model stays advisory).

## Context
Doc 10 sections 2 to 4 describe filters, a merge and a rank. Three facts shape them: no cell can be Active before paper days exist (P10), several inputs do not exist (events, ban list, breadth), and the model is not proven.

## Decision
1. **Filters are a pure function** returning pass, fail or unavailable per filter with a stable code (`cell_state`, `stats_as_of`, `sample`, `win_rate`, `expectancy`, `reward_risk`, `cost`, `liquidity`, `event`, `fno_ban`, `surveillance`) and the numbers. Verdict: any fail rejects; else a required filter unavailable holds; else eligible. Unavailable is never a pass.
2. **Minimum state Candidate, plans provisional** below Active (P7-00 Q7).
3. **No look-ahead:** statistics count only when their as-of date is before the decision date.
4. **Optional filters** are those whose data is not built (events, ban, surveillance): their unavailable answer is a caveat shown on the candidate, not a pass and not a blocker; configurable.
5. **Conservative expectancy** is computed from the lower credible bound of the win rate and the stored averages; comparisons round to 9 places so float noise never decides a boundary.
6. **Merge and rank are total orders** (expectancy, shrunk win rate, trades, symbol, side, cell key, all ordinal): the output does not depend on input order. Opposite sides survive only on a lead above the margin.
7. **The model is advisory:** carried, never used.
8. **One verb, no table:** `xd select cells`, over stored statistics and changelog states, at cell level (placeholder symbol `*`), so instrument-level filters read unavailable and cells are held. Log ids 4640 and 4641.

## Consequences
- With the ticket builder missing, the verb ranks nothing; it shows which cells would pass the evidence filters and which input is missing. That is the honest state.
- Defaults beyond the design (turnover floor, weights, margin) are assumptions until the owner confirms.
- The statistics hold no out-of-sample count, so "min OOS sample" is the trade count.

## Alternatives considered
- **Treat unavailable optional data as a pass.** Rejected: it would claim a clear ban list nobody checked.
- **Require every filter.** Rejected: with no events or ban data the plan would be empty forever.
- **Rank on the model's probability.** Rejected until the phase check reads MET on the owner's data.
