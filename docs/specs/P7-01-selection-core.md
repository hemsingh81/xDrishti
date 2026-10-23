# P7-01 — Selection core: eligibility filters, merge, rank

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; built 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-01.
- **Design refs:** docs/design/10-trade-selection.md §2 to §4; 09 (cell statistics, lifecycle); 16 (`selection`).

## Assumptions (stated up front)
1. **Pure logic on stored and candidate data.** No order, no account, no credential, no broker call, no autonomy or auto-approval. None of the safety questions of P7-00 is touched; the slice stops there.
2. **Minimum cell state is Candidate** (P7-00 Q7, recommended). Every candidate below Active carries `Provisional`, and the list prints "provisional: cell not yet paper-proven". Research, Paused and Retired never pass.
3. **The model is advisory only** (P7-00 Q8): `AdvisoryPWin` is carried to the output and read by no filter, no merge and no rank (a test pins it).
4. **P(win) means the shrunk posterior mean** of the stored cell statistics (all-time window). The conservative expectancy is `pLower * avgWinR + (1 - pLower) * avgLossR` with the lower credible bound `pLower`; statistics that are gross subtract the ticket's cost in R (unavailable without it).
5. **"Min OOS sample 30"** reads the cell's trade count `N` (the stored statistics hold no separate out-of-sample count). Stated limit, not a hold-out.
6. **Stated defaults beyond the design:** liquidity floor 10 million rupees average daily turnover; rank weights `w_conf` 0.05, `w_regime` 0.05, `w_corr` 0.10, `w_event` 0.10; confluence capped at 2; conflict margin 0.05 R. Reversible; their home is P7-00 Q14 (settings), not decided here.
7. **Every filter is required by default**, including events, F&O ban and surveillance (data does not exist until P15): unavailable holds the candidate, a known exclusion rejects. The owner can loosen a filter through `OptionalFilters`; an unavailable loosened filter is a caveat on the ranked candidate, printed by the verb, and a missing event filter lists `event_risk` as a missing rank input. A cell with zero wins or zero losses has no average, so its expectancy is unavailable and it is held.
8. **No persistence.** The read-only verb needs none.

## Goal
Given plain candidates, say which are eligible and why not otherwise, merge duplicates and rank them, deterministically, with a reason code for everything left out. A read-only verb shows the stored cells through the filters.

## Scope
**In:** Domain `Selection` (filters, rules, merger, ranker, selector), `ShowSelectionHandler` (`xd select cells --date D [--min-state ...]`), log ids 4640 to 4641, ADR 0082, tests.  
**Notes:** the placeholder symbol `*` is the verb's only; the merger refuses it. The provisional label is printed for ranked and held cells. A Held stronger opposite side does not take part in conflict resolution (shortcut).  
**Out:** candidates from a strategy and a symbol (ticket builder, P7-04), portfolio fill (P7-02), smart rules (P7-06), plan records and tables (P7-03), any endpoint, any migration.

## Acceptance criteria
1. Each filter returns pass, fail or unavailable with a stable code and its numbers; hand tables cover at the limit, just beyond it and missing. `unavailable` (missing, not finite, or statistics not before the decision date) is never a pass; a required one holds the candidate, a fail rejects.
2. No look-ahead: statistics whose as-of date is on or after the decision date make the evidence filters unavailable.
3. Merge: same symbol and side keeps the highest conservative expectancy (ties: shrunk win rate, trades, cell key ordinal); confluence counts other independent strategies (not timeframes), capped; opposite sides keep the stronger only when it leads by more than the margin, otherwise both are dropped; policy `DropBoth` drops both. Every dropped candidate has a code.
4. Rank: `score = expLower + wConf*confluence + wRegime*regimeFit - wCorr*correlation - wEvent*eventRisk`; a missing input counts 0 and is listed; ties fall to a stated total order; any input order gives the same output.
5. The advisory value changes no verdict, no merge, no rank.
6. The verb is as-of its `--date` (newest snapshot with as-of before it; changelog rows before that day, India time), reads stored statistics and changelog states, writes nothing, prints the limits, and exits 0/1/2/3/130 as the other verbs.

## Contract
- **API:** none. **Data:** none. **UI:** none. **CLI:** `xd select cells --date YYYY-MM-DD [--min-state candidate|paper|active]`.
- **Logs:** 4640 `selection.shown`, 4641 `selection.refused`.

## Non-functional
Pure and allocation-light; the verb reads one snapshot and one changelog history per cell. No secret read.

## Test plan
Domain: filter tables, unavailable handling, look-ahead, merge, conflict, rank, ties, determinism over 25 shuffles, advisory neutrality. Application: the view with fakes. CLI: option parsing. Architecture tests unchanged.

## Open questions
None blocking. Weights, floor and margin (assumption 6) and the settings home (Q14) are the owner's to confirm.

## Done when
- [x] Acceptance criteria have tests
- [x] `scripts/check.sh` green
- [x] ADR 0082, roadmap row, docs built
