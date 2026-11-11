# BK-11b — Universe follow-up: dated basis, proposer hook, tier pivot

- **Status:** Draft (stub; carved out of [BK-11](BK-11-universe-cell-dimension-and-strategy-universe-link.md), which is partially met, ADR 0261)
- **Size:** about 2.5 agent-days
- **Safety:** no for trading; a universe restriction stays a proposal, never applied.

## Scope (the acceptance criteria BK-11 did not meet)
1. **AC 8, dated basis and structured labels.** For a rule basket with dated rows the simulator reads `members(asOf = day)` per day (basis `dated`); otherwise members as of the run date (`single_snapshot`). A run records `universe_basket_id`, `universe_params_hash` (null for manual baskets), `members_as_of` and `universe_basis` as structured fields (not only the label text), without making the run key depend on the owner's mutable settings. A `single_snapshot` run is never described as point in time.
2. **AC 11, proposer hook.** A tier observation that is read in place of its parent, has at least `learning.universe_link_min_trades` trades and a stable hold-out may become one hypothesis ("restrict scan scope to liq_high", one knob, one cell) in the existing hypothesis proposer, only by the owner's action; nothing is applied.
3. **AC 12, tier pivot.** The `universe` report dimension is available when tiers exist (a per-trade tier read through the stored tier cells or a stored tag), 422 `reports.dimension_unavailable` with the reason otherwise; cells under 30 trades are masked.

## Open questions
- Does the per-trade tier need a stored tag on `sim_trades` (insert-only), or can it be recomputed from the tier source at report time?
