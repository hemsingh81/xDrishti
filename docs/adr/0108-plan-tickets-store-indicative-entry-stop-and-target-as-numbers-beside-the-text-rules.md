# 0108 — Plan tickets store the indicative entry, stop and target as numbers beside the text rules

- **Status:** Proposed (spec [P7-19](../specs/P7-19-numeric-ticket-levels.md), built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (plan record), 0088 (ticket builder, levels indicative from the close), 0090 (grading keeps its own simulated levels), 0096 (pre-open).

## Decision
1. **Columns on `trading.plan_items`**, nullable: `entry_ref_price`, `stop_level`, `stop_anchor` (`level` or `close_of_d`), `target_level`, `reward_risk`, `levels_session`; check `ck_plan_items_levels` (all null, or an entry with its session; a stop has an anchor and sits on the loss side of the entry for the item's side; a target needs a stop, a reward-to-risk and sits on the profit side). No grants change; the table stays insert-only for `xd_app`. Rejected: a side table, jsonb, parsing the text.
2. **Numeric only what the builder knows at the close of D:** the close, a stop read from 1D-or-longer data, a target of a fixed reward-to-risk exit. Everything else (triggers, validity, trails, the opening-range stop) stays text.
3. **`TicketLevels.Create` is the only constructor** (rounds to four decimals, half away from zero). A stop distance of zero, on the wrong side, or above `TicketLevels.MaxRiskFraction` (30 %) is withheld with the reason `levels:implausible_stop_distance`; a stop without a number gives the entry alone and `levels:stop_not_numeric` in the item's unavailable list. The text is never changed.
4. **Hash:** the levels join the per-item line of `input_hash` only when the item has levels; an item without them hashes as before. `plan_key` is unchanged.
5. **Read:** `PlanItemView.levels` (appended, nullable, with a pinned `basis` sentence). Stored plans are not backfilled and read `levels: null`.
6. **Nothing reads the numbers** for approval, modification, grading, journal, selection or sizing. No order is placed.

## Consequences
- The first plan with numbers is the first one built after this deploy; a re-run of a date is a new version (different input), the existing behaviour.
- A `close_of_d` stop moves with the real entry; the UI labels it "from the close".
- P7-20 (alerts) and P8-14 (charts) may use the numbers without parsing text.
