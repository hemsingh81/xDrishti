# 0262 — The basket profile is one lazy screen over two read-only endpoints, with a bounded in-process cache and a rank that needs separation

- **Status:** Accepted (spec BK-07, approved by the owner 2026-11-03).
- **Date:** 2026-11-11.
- **Relation to other ADRs:** Wires the pure engine of ADR 0257; reads the baskets as ADR 0102 does and the strategy trades only through the fenced research readers (ADR 0243). The median split is not the locked hold-out of ADR 0207.

## Decision
1. `GET /api/reports/baskets/{id}/profile` returns the whole profile of one basket (m1b to m16 and the median split m23) from one pass over the stored daily bars; `GET /api/reports/baskets/ranking` returns only the index and the relative strength of 2 to 12 baskets, with the member overlap. Reads only, session protected, no table, no job, no migration.
2. Membership is the existing ports: the members at the window start, the history of additions and removals replayed day by day (members at the end of a day), the members at `membersAsOf`. One day of change or none is a `single_snapshot` (survivorship, ISS-22, printed in the header); two or more is `dated`.
3. The window defaults to the last 252 sessions ending at the newest stored daily bar of the index and says so in `window.source`; a window under 20 weekdays, a reversed one or a basket above the instrument cap is `reports.invalid`. The bars are read once per instrument, from 420 calendar days before the window start (the 200-day average, the 252-session high and the 60-day correlation need them).
4. The answer is cached in process under a key of the basket, its parameter hash, a membership version, the member date, the window, the index, the newest bar of the index and the default run set. The cache is bounded (first in, first out) and holds nothing that is not also stored. One small read of the index finds the newest bar; the bars of the window are not read again on a hit.
5. Breadth above the 20, 50 and 200 day average reuses `BreadthCalculator` (P3-01e: a complete bar on each session of the average, no corporate-action data is read) and ADV20 is `LiquidityTiers.Median` (CF-05, the median of 20 traded values), not second versions of either.
6. A rank is on the longest relative strength window that most baskets have a value for. A basket without a value has its state and no rank. The board says "not separated" unless every neighbouring gap is larger than the standard error of the difference (the standard deviation of the daily difference of the two indexes times the square root of the common days). It is indicative, pairwise and uncorrected; it is not a significance test. No basket is labelled best.
7. Figures against the index are `unavailable` with the owner's way to track one (text only) when the index has no bars. The passive difference to the index is never shown beside a trading result and is never called alpha, excess or outperformance.
8. Settings `Reports:BasketProfile` (minimum voters 5, days per regime 30, capacity share 0.01, instrument cap 300, baskets 12, window 252 sessions, cache entries 64), Internal in the catalog; the discontinuity threshold stays the single `Reports:Benchmark` value. The capacity share waits on the owner's question Q-cap (one share with CF-05).

## Limits
A single snapshot is survivorship biased; thin histories leave most 252-day figures `insufficient`; the profile describes the past and predicts nothing. The cache can show a restated bar of a member until the index gets a newer bar.
