# 0113 — Plan flags are stored as typed JSON beside the basis; the ticket chart is plain SVG of daily bars up to the planning session

- **Status:** Proposed (spec [P8-14](../specs/P8-14-plan-follow-ups-flags-banners-and-ticket-charts.md), built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** 0083 (plan record, insert-only), 0085 and 0092 (smart rules and wiring), 0109 (numeric ticket levels), 0101 (bell notices), 0111 (counts of codes; the stored flags now let a later slice show what ran).

## Decision
1. **Flags are stored, not re-derived.** `trading.plans.basis_flags_json` (text, nullable, check that it starts with `{`; migration `AddPlanBasisFlags`) is written in the insert by `SelectionPlanBuilder` from the `SmartPlan` through the pure `PlanBasisFlags.From` (domain). Schema version 1: gate, gateMissing, throttle, correlation, correlationUnavailablePairs, openBook, riskMultiplier, ruleRemovedHalf, costBasis, banner (the engine's flag texts, full length). `PlanView.flags` returns it; a plan stored before this has `null` and the screen falls back to the old exact-marker reading, labelled. Nothing is back-filled; the input hash is unchanged. The table stays insert-only (no grant change).
2. **Cost basis** is derived from `CellStatsRow.Costed` of the chosen primary tickets: `gross`, `costed_unconfirmed`, `mixed` or `unknown`. The cell statistics do not say whether the owner confirmed the charge sheet, so `net_confirmed` is in the vocabulary but never written by this slice.
3. **Open book** (`known | assumed_empty | unavailable`) is taken from `PlanContext.Book` (null, empty, non-empty), not parsed from notes.
4. **Ticket chart:** `GET /api/plans/{id}/items/{itemId}/chart?bars=` (10 to 250, default 60). The session is the ticket's `levels.session`, else the trading day before the plan date. Bars are daily, never after the session, adjusted only by corporate actions with an ex-date on or before the session (the plan's own prices are raw, so a later split must not move the picture). The symbol resolves to exactly one active share of the instrument master (`exact`), otherwise `not_found` or `ambiguous` with no bars. Levels are a straight copy of the ticket's numbers; none means the rule texts are printed and no line is drawn.
5. **No charting dependency.** P8-11 (`PriceChart`, `lightweight-charts`) is not on main, and adding a library is the owner's decision. The chart is a hand-drawn SVG of candles (hollow = closed up, filled = closed down, so colour is never the only signal) with price lines, in a separate lazy chunk loaded on expand, with a table fallback of the last 10 bars. When P8-11 lands, `TicketChart` can swap its drawing for `PriceChart` without touching the endpoint. The P8-11 attribution link does not apply (no library).
6. **Banners:** `PlanBanner` prints the stored lines in full plus the gate's own no-trade sentence while the plan is open; `PlanRunBanner` reads `GET /api/plan-runs?limit=5` and speaks only when the newest attempt failed closed with no plan and nothing published for that session or later.
7. **Standings:** one line per account ("12 checks, 12 passed", "1 warned, 1 needs confirmation", "refused, cannot be overridden"), the not-passed checks listed under it, every check on request. Approve and confirm-despite logic is untouched.
8. Everything is read only; no setting is added; no order is placed.

## Consequences
- Rejected: a child table (a join for scalars), recomputing on read (the pair count and gate mode are not on the items), parsing prose on the server.
- The chip code lives in the lazy plans chunk, except `PlanFlags`, which the Today tile shares; the first-load growth is measured and printed by `npm run size`.
- A symbol is matched by canonical symbol among non-retired shares; the instrument list carries no exchange column, so "NSE" is the master's own scope.
