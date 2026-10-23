# P7-02 — Portfolio fill

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; built 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-02.
- **Design refs:** docs/design/10-trade-selection.md §5; 08 and 16 (risk per trade); [P7-01](P7-01-selection-core.md) / ADR 0082.

## Assumptions (stated up front)
1. **Pure logic.** No account, broker, credential, order, persistence or autonomy; none of the safety questions of P7-00 is touched.
2. **Inputs are plain records:** ranked eligible candidates (P7-01) with risk %, sector, baskets and holding status; the open book; a correlation matrix passed in (P7-06 computes it); the constraints. Where they come from is later slices'.
3. **Unknown input = unavailable = blocks** (lesson of the P7-01 reviews). No book, nothing taken.
4. **Defaults:** the design's table; per-trade risk 1 %, `max_plan_items` 5, `reserve_size` 3 are assumptions (home: Q14).
5. **Trigger mode** checks the other limits as if all listed tickets trigger (safe side).
6. **Correlation in the direction of the bets** (effective = corr x side x side, hedges pass, negative-correlated opposite sides block); ticket risk must be positive, an open position may have 0; trigger mode does not check `MaxEntriesPerDay`, plan mode ignores entries already opened today (shortcuts); names compare ignoring case.
7. No verb: it would need sector, risk and correlation data nothing stores yet.

## Goal
From the ranked candidates, the open book and the limits, say what goes on the plan, what waits in reserve and what is rejected, each with the code and numbers.

## Scope
**In:** Domain `Selection`: `PortfolioFiller`, `FillConstraints`, `FillCandidate`, `BookPosition`, `CorrelationMatrix`, `FillCheck`, `FillDecision`, `FillResult`, codes, ADR 0084, tests.  
**Out:** sizing and accounts (P7-07), market gate and correlation computation (P7-06), plan records (P7-03), any endpoint, verb, migration.

## Acceptance criteria
1. Each constraint (plan items / entries per day, open positions, one per symbol, held long-term SELL, per-trade risk, total risk, net long / short, sector, mix, basket, correlation) blocks the next pick in turn, hand-worked, at the limit passing and beyond failing.
2. All constraints are re-checked after each pick, including the open book.
3. Unknown inputs are unavailable and block; no book takes nothing.
4. Reserve is admissible alone but not taken, capped at the reserve size; the rest rejected with reasons; every input appears once.
5. Same output for any input order (25 shuffles); inputs unchanged.

## Contract
No API, data, UI, CLI or log ids.

## Test plan
`PortfolioFillerTests` (40 tests) as above. Architecture tests unchanged.

## Open questions
None blocking. The assumed numbers (assumption 4) are the owner's to confirm.

## Done when
- [x] Acceptance criteria have tests
- [x] `scripts/check.sh` green
- [x] ADR 0084, roadmap row, docs built
