# 0084 — Portfolio fill: greedy, every limit re-checked after each pick, unavailable blocks

- **Status:** Proposed (spec [P7-02](../specs/P7-02-portfolio-fill.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0082](0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) (the ranked candidates it consumes; unavailable never passes).

## Context
Doc 10 section 5 lists the portfolio limits and says "greedy, constraints checked after each addition". Several inputs will not always exist (sector, correlation pairs, the open book, holdings), and a limit that cannot be checked must not be assumed met.

## Decision
1. **Pure greedy walk** over ranked eligible candidates, best first (rank, then symbol, side, cell key ordinal, so input order never matters). A candidate is taken only if all checks pass with it added to the book and the picks so far.
2. **Checks** (stable codes): `plan_items` (trigger mode) or `entries_per_day` (plan mode), `open_positions`, `one_per_symbol`, `held_long_term` (SELL only, configurable), `per_trade_risk`, `total_risk`, `net_exposure`, `sector`, `mix`, `basket` (only when basket limits are set), `correlation`. Each carries value and limit. Comparisons round to 9 places.
3. **Unavailable blocks.** A missing book, risk, sector, style, basket list, holding status or correlation pair (or a non-finite figure, also on an open position) is `unavailable` and blocks like a fail; with no book nothing is taken (`book`). The first pick has nothing to correlate with and passes that check.
4. **Trigger mode** (`count_entries_on: trigger`, default) caps the plan at `max_plan_items` and checks the other limits as if every listed ticket triggers, which is stricter than needed (safe side).
5. **Reserve** = not taken, but all checks pass against the open book alone; the first `reserve_size` in rank order, with the reasons at its turn. Not admissible alone, or beyond the reserve size (`reserve_full`), is rejected. A reserve is never promoted here: promotion needs a fresh check (P7-06 / pre-open).
6. **Correlation** compares signed values against the limit for every pair, hedges included (conservative). Matrix pairs are passed in; a duplicate pair keeps the higher value.
7. **Assumptions:** max per-trade risk 1 % (twice the standard 0.5 % profile), `max_plan_items` 5, `reserve_size` 3. Reversible; their home is P7-00 Q14.
8. No verb, table, migration or log id: the facts it needs (sector, risk, baskets, book) come with later slices.

## Consequences
- With no sector, risk or correlation data wired yet, a real run would take nothing and say why. That is the honest state.
- Greedy is not optimal (a better set may exist); it is predictable and explainable.

## Alternatives considered
- **Treat unknown inputs as passing.** Rejected: it would claim a sector or correlation limit nobody checked.
- **Optimal subset search.** Rejected: opaque, and doc 10 asks for greedy.
