# P7-06 — Smart rules in the plan: market gate, equity-curve throttle, correlation-aware selection, honesty flags

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; built 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-06.
- **Design refs:** docs/design/09-learning-engine.md §14; 10-trade-selection.md (smart rules); 16 (`smart_rules`).

## Assumptions (stated up front)
1. **Pure logic on a ranked list.** No order, account, credential, broker call, autonomy or auto-approval: none of the safety questions of P7-00 is touched.
2. **The market gate is `unavailable` today** (Q13): VIX, breadth and the event calendar need P3-01d/e and P15, which do not exist. `unavailable` leaves the list and the risk unchanged but is never read as normal: the plan carries a flag whose text says "market gate unavailable ... NOT treated as a normal day". **Owner-facing consequence:** until the feeds exist, every plan opens without a gate and the owner must judge the market day themselves; the banner says so each night.
3. **Gate bands** (doc 16 defaults, reversible): VIX >= 22 no-trade; VIX >= 18, or breadth < 0.30, or a high-impact event today: reduced (keep `ceil(n/2)` best, risk x 0.75); else normal. A limit that trips on a known figure counts even when another figure is missing (safer); *normal* needs all three figures. Figures dated on or after the decision date are unknown. Event-day = reduced is an assumption (doc gives no band for events).
4. **Throttle:** equity = running sum of R of the real taken trades closed before the decision date; latest equity below the average of its last N (20) points: risk x 0.5 (doc rule: risk, not ticket count; it never removes tickets). Fewer than N real trades (including none): inactive, `unavailable`, says why, risk x1.
5. **Correlation / "same bet"** (doc 09 §14, limit 0.60, 60 sessions, minimum 30 common): a candidate is the same bet as an open trade or an earlier (better-ranked, kept) candidate when it is the same instrument and side, the same supplied underlying group and side, or the side-adjusted return correlation exceeds the limit (opposite sides flip the sign: a hedge is not a same bet). Sector is not used (doc 10 caps sectors in the fill, P7-02). Open trades are checked first, then better tickets; the highest effective correlation is quoted. No table: `unavailable` + flag. A pair with no figure is counted and flagged (`correlation_partial`), never a pass.
6. **Honesty flags:** a rule that removes half or more of the candidates it saw (gate, correlation) raises `rule_removed_half`. Exactly half counts (a reduced gate on an even list therefore flags). The second honesty test of doc 09 §14 ("skipped trades are not worse than taken ones over the last 100") needs graded outcomes of reserve candidates (P7-13) and is not built here.
7. **Not applied:** the meta size multiplier (annotation `MetaSizeMultiplierApplied = false`, ADR 0075 advisory) and the learned entry window (needs the owner's acceptance, ADR 0069); both are out of this slice.
8. **Order of rules:** gate, then correlation (on what the gate kept), then throttle. Final risk multiplier = gate x throttle. Input order does not matter: the list is re-read in rank order and trades are ordered by date, id.
9. **Review changes (2026-10-23).** Sides are read by one helper shared with P7-02 (exactly `BUY`/`SELL`; anything else is unknown): a pair with an unknown side on either end is counted unavailable and flagged, never a hedge. `CorrelationTable` is built on P7-02's `CorrelationMatrix` (one lookup and name normalisation) and adds underlying groups; `FromReturns` computes only candidate-with-candidate and candidate-with-book pairs, and a date with conflicting returns for one symbol is dropped (order independent). Gate figures older than `GateMaxAgeDays` (default 4 calendar days: the previous session across a weekend, no calendar in the Domain) are unavailable; the event flag means "an event falls on the decision date, read from the calendar published ahead". Options are validated (`Problems()`; `Apply` refuses invalid ones). Same underlying on opposite sides is a hedge. Honesty flag is per plan and "half or more"; docs 09/10 amended to say so. Namespace `XDrishti.Domain.Selection.SmartRules` is on the architecture allow-list.
10. **Not wired.** The caller (the pipeline, P7-09) and the settings home (Q14) come later; options are a plain record with the doc defaults. No persistence, endpoint, log id.

## Goal
Given the ranked list and plain inputs, return primary and reserve (with a reason and numbers for each move), the risk multiplier, each rule's status, and visible flags, deterministically, with a missing input always a flag and never a pass.

## Scope
**In:** Domain `Selection/SmartRules` (gate, throttle, correlation table with return-based Pearson, engine, result types, codes), ADR 0085, tests.  
**Out:** feeds for VIX/breadth/events (P3-01d/e, P15), meta multiplier, entry windows, outcome comparison of skipped trades, ticket sizing, persistence, API, CLI.

## Acceptance criteria
1. Gate bands per assumption 3 have a hand table at, just below and above each limit; missing, non-finite or not-before-date input is `unavailable`, never normal, and the plan carries the flag.
2. Reduced keeps the best `ceil(n/2)` and the rest go to reserve with code and numbers; no-trade sends all to reserve.
3. Throttle bands: below average cuts risk to x0.5, at or above does not; fewer than N real trades or none is unavailable with the reason; trades on or after the decision date are ignored; tickets are never removed.
4. A same-bet candidate goes to reserve with code and correlation; at the limit it stays; opposite sides are a hedge; open trade and underlying cases; no table is unavailable and flagged; pairs with no figure counted.
5. A rule removing half or more raises `rule_removed_half`; fewer does not.
6. The meta multiplier is not applied. Disabled rules report `Off`.
7. Output is identical for 25 shuffles of the input; correlation from returns uses only sessions before the decision date.

## Contract
- **API / Data / UI / CLI:** none. **Logs:** none.

## Non-functional
Pure and allocation-light; no I/O, no clock, no secret.

## Test plan
`SmartRulesTests` (Domain): gate table, unavailable inputs, reduced/no-trade, throttle bands and look-ahead, same-bet cases, partial pairs, 50% flag, off, shuffle determinism, return-based correlation. Architecture tests unchanged.

## Open questions
None blocking. Bands for the event rule, the settings home (Q14) and the real gate feeds (Q13) remain the owner's.

## Done when
- [x] Acceptance criteria have tests
- [x] `scripts/check.sh` green
- [x] ADR 0085, roadmap row, docs built
