# 0085 — Smart rules in the plan: market gate, equity-curve throttle and same-bet selection, `unavailable` never a pass

- **Status:** Proposed (spec [P7-06](../specs/P7-06-smart-rules-in-the-plan.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0082](0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) (ranked list, unavailable never passes), [0075](0075-meta-labelling-policy-thresholds-from-earlier-folds-and-does-not-help-as-an-answer.md) (the model stays advisory), [0069](0069-learned-entry-windows-shrunk-bucket-runs-judged-on-purged-folds.md) (windows need acceptance).

## Context
Doc 09 section 14 puts a market gate, a correlation check and an equity-curve throttle between the ranked candidates and the plan. The gate's feeds (VIX, breadth, events) do not exist yet.

## Decision
1. **Pure functions on the ranked list**, composed in one order: gate, correlation, throttle. Each rule returns its numbers and a stable code; input order does not change the output.
2. **Gate bands** from doc 16: VIX >= 22 no-trade; VIX >= 18, breadth < 0.30 or a high-impact event: reduced (best `ceil(n/2)`, risk x 0.75). A tripped limit counts despite a missing other figure; *normal* needs every figure.
3. **Missing gate data is `unavailable`, never normal.** The list is not changed, but the plan carries "market gate unavailable ... NOT treated as a normal day". Consequence for the owner: until P3-01d/e and P15 exist, every night's plan is without a gate and says so.
4. **Throttle** cuts risk to x0.5 while the cumulative R of real taken trades is below the average of its last 20 points; with fewer than 20 real trades it is inactive, says why, and risk stays x1. It never removes tickets.
5. **Same bet:** same instrument/side, same underlying group, or side-adjusted 60-session return correlation above 0.60 with an open trade or a better kept ticket; the weaker goes to reserve with the number. No table or a pair without a figure is flagged, not passed.
6. **Honesty flag** when a rule removes half or more of what it saw. The comparison of skipped against taken trades waits for graded reserve outcomes (P7-13).
7. **Sides and data hygiene:** a side other than exactly BUY or SELL is unknown (shared helper with the fill); such a pair is counted unavailable and flagged, never a hedge. Gate figures older than 4 calendar days read unavailable. The correlation table sits on the fill's `CorrelationMatrix`; return series with conflicting duplicates drop that date. Honesty flag is per plan (docs 09/10 amended).
8. **Not applied:** the meta size multiplier (annotation only) and the entry window.

## Consequences
- Today only the correlation rule can act; the gate and throttle report unavailable and say why.
- Exactly half counts as "half or more", so a reduced gate on an even list always raises the flag; this is by the doc's wording and is a review prompt, not a failure.
- Bands and the event-day rule are assumptions until the owner confirms (settings home: P7-00 Q14).

## Alternatives considered
- **Treat a missing gate as normal.** Rejected: it would claim a calm market nobody measured.
- **Throttle by removing tickets.** Rejected: the design cuts risk, not count.
- **Sector as same bet.** Rejected: sector caps belong to the fill (P7-02); a sector is not a return relationship.
