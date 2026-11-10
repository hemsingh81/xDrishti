# 0251 — The auto scope is an insert-only id list that filters candidates before selection, and suggestions are a read model over the stored plan

- **Status:** Accepted by delegation (owner, 2026-11-09, in chat, AU programme).
- **Date:** 2026-11-10.
- **Relation to other ADRs:** Builds on [0244](0244-one-auto-switch-runs-the-chain-and-only-suggests.md) (point 3). Does not touch the ranking or gates of the selection (specs P7-04, P7-09).

## Context
Spec AU-16: suggestions limited to the baskets and stocks the owner chose, each with reason and evidence, where a missing figure is never a pass.

## Decision
1. The scope is a list of basket ids and instrument ids in `planning.auto_scopes`, insert-only; the newest row is in force. At least one entry, every id must exist, at most 500. No row means no scope: every eligible instrument is considered (the Essential row reads `eligible`).
2. The scope is a filter on the candidates the ticket source returns, applied before the selection. Baskets are expanded to their members at the time of the nightly run. Gates, ranking and the portfolio fill are not changed; a scope covering every candidate gives the same plan as no scope. The plan keeps the scope hash in its stored flags (`ScopeHash`).
3. Suggestions are a read model over the stored plan: primary tickets with all required evidence (n, n_eff, conservative expectancy, regime, data as-of) are suggestions; a primary ticket missing any is listed under not suggested with the missing figures; a missing figure reads `unavailable`. Reserve and rejected items stay visible with their reasons in words. Every answer carries the plan basis and `Suggestion only, no order is placed`.
4. To show n_eff and the as-of date from the time of the plan, `trading.plan_items` gains the nullable columns `n_eff` and `stats_as_of`; a plan stored before reads both as `unavailable` and none of its primary tickets is suggested by this read model.
5. The scope is set by the owner with the session (no extra password: it narrows or widens what is shown, it changes no safety rule), audited as an Essential setting change. There is no reset to default; the owner sets another scope.

## Consequences
Plans built before AU-16 show their primary tickets under not suggested until the next nightly plan. Nothing here creates an approval, a ticket move or an order.
