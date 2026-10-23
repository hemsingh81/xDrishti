# 0079 — Eight more intraday strategies as stored text only

- **Status:** Proposed (spec [P6-17a](../specs/P6-17a-remaining-strategies-intraday-batch.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0055](0055-the-strategy-file-its-schema-and-the-content-hash.md) (file and hash), [ADR 0056](0056-stored-strategies-the-lab-tables-the-seed-and-loading-by-version.md) (the seed, loading by id), [ADR 0034](0034-the-rules-expression-language-lint-and-compiler.md) and [0035](0035-per-session-features-keep-the-history-of-the-session-only.md) (language, per-session levels).

## Context
Doc 07 names nine more intraday strategies. The first three built-ins were C# builders first and became text second (ADR 0056); that route doubled the work and left the builders as an oracle. The owner's answer to question 9 of the P6 plan was to build first what needs no new feature.

## Decision
1. **Stored text is the only source.** Eight strategies (`PDH_PDL_BREAK`, `PDH_PDL_REJECT`, `VWAP_RECLAIM`, `EMA_PULLBACK`, `GAP_GO`, `GAP_FILL`, `INSIDE_BAR_BO`, `MEAN_REV_BANDS`) are embedded seed files only: no builder, no registry row. The seeder writes them as version 1 by hash; a later release that changes a text is the next version. The simulator, the Lab and the verb load them by id through the store.
2. **Only the existing language and features.** No indicator, no context, no stop kind and no exit is added. Where doc 07 gives a one-line name, the rule set is the author's reading and a proposal. Conditions that need the previous value of an indicator use `crosses_*` (which lags whole expressions); a lag on an indicator call is not written (ISS-24).
3. **`CPR` is deferred.** It could be written from `prev_day.*`, but no document defines it; inventing the thresholds would put numbers in the ledger that nobody chose.
3a. **Two choices to read the names correctly.** `GAP_FILL` is a gap fade that exits on `FIXED_RR` like the others, not a fill-to-previous-close target (only `FIXED_RR` runs). `PDH_PDL_BREAK` takes entries from 10:00 on purpose: its 3-bar stop would reach into the previous session earlier, and a gap through the level at the open never crosses it from below, so it never fires (`GAP_GO` covers gaps). The author-chosen thresholds (`gap_min`, `gap_max`, `adx_max`, `rr`, windows) are tunable parameters or text, judged through the ledger.
4. **Tests are the P3-05/06 pattern at smaller size:** compile with no warning, a hand-checked fabricated day per strategy with its stop, entry and target (the oracle, since no builder exists), the mirrored SELL day walked as a sell, a negative day, for every setup gate a day where only that gate is false (and the same day with the gate deleted gives a candidate), and the cut-off and replaced-future identity. The three older hashes are unchanged.

## Consequences
- Eleven built-in families are seeded; each new one is another trial for the ledger and the experiment margin (P6-05, P6-12).
- The strategies are not evidence of an edge and are not claimed to be; the mechanics test says the text does what it says.

## Honest limits
- The rules are proposals read from names; the fabricated days prove mechanics only. `MEAN_REV_BANDS` uses a fixed 1.5 R target because a target at the middle band cannot be written (only `FIXED_RR` runs).
- Hourly and daily set-ups need warm-up (about 3 sessions for the hourly EMA(21), 20 for the daily EMA(20)) and a cold start shows as not ready.
- The fixtures are 15m, one instrument, exact fills; slippage, costs and the order inside a minute are the engine's, tested elsewhere.
