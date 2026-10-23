# P6-17b — Remaining strategies, swing batch

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine; row P6-17b of [P6-00](P6-00-learning-engine-and-ml-plan.md) (owner question 9, recommended answer: first the strategies that need no new feature).
- **Design refs:** [doc 07](../design/07-strategies.md) §3 (starter library), [doc 11](../design/11-strategy-lab.md) (rules language, strategy file) · [ADR 0034](../adr/0034-the-rules-expression-language-lint-and-compiler.md), [0035](../adr/0035-per-session-features-keep-the-history-of-the-session-only.md), [0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md), [0055](../adr/0055-the-strategy-file-its-schema-and-the-content-hash.md), [0056](../adr/0056-stored-strategies-the-lab-tables-the-seed-and-loading-by-version.md), [0079](../adr/0079-eight-more-intraday-strategies-as-stored-text-only.md), [0081](../adr/0081-four-swing-strategies-as-stored-text-only.md); the pattern of [P6-17a](P6-17a-remaining-strategies-intraday-batch.md) and the daily test pattern of [P3-06](P3-06-base-breakout-the-third-built-in-strategy.md).

## Assumptions (all recommended answers; the owner may reverse any of them)
1. Only strategies expressible in the existing rules language and the existing 56 features, with no new indicator and no new context, are built; the rest are named as deferred with the missing piece. Where doc 07 gives only a name and no definition good enough to write without inventing numbers, the strategy is deferred (as `CPR` was).
2. Each is **stored text only** (a seed YAML in `Seed/Strategies`, embedded and written by `xd-api migrate`, idempotent by hash). No C# builder, no registry row. The eleven earlier hashes do not change.
3. The rule sets are the author's reading of the names (and of the parentheses in doc 07: "20 EMA / 50 DMA", "above 200 DMA") and are **proposals**. Nothing here says any of them has an edge; each is another trial in the ledger.
4. 1D, BUY only (a swing strategy cannot list SELL, doc 07 §7), entry `at_close` (filled at the next session's open within the limit), one `FIXED_RR` exit with `rr` a declared parameter, `max_hold_sessions` 5, stops of the kinds the simulator runs (`swing_low`).
5. The author-chosen numbers (`ema_len` 20, `trend_len` 50 and 200, `rsi_max` 10, `high_len` 252, `rr`, the stop lookbacks) are tunable parameters or text, to be judged through the ledger and the experiment margin, not conclusions.

## Goal
Four more swing strategies exist as stored, versioned text, run by `simulate --strategy <id>` and the Lab like the first built-ins, each shown on a fabricated daily series to do what its text says.

## Scope
**In (built):**

| Id | Setup | Trigger | Stop |
|----|-------|---------|------|
| `TREND_PULLBACK` | `ema(20) > sma(50)` | three items: the low is at or under `ema(20)`; the close is above `ema(20)`; the close is above the open | lowest low of 3 sessions |
| `NR7_ID_BO` | yesterday was an inside day (`inside_bar[1] == 1`) and its range is strictly the smallest of the 7 sessions ending yesterday (written out with `min` of the ranges of days 3 to 7 back; day 2 back is the mother bar, which an inside day implies is wider) | `close > high[1]` | lowest low of 2 sessions (the inside day and the trigger day) |
| `RSI2_REVERSION` | `close > sma(200)` | `rsi(2) < rsi_max` (10) | lowest low of 3 sessions |
| `HIGH_52W_MOMENTUM` | none | `close >= highest(252, source=close)` (a tie counts) and `close > close[1]` | lowest low of 5 sessions |

**Notes on the table.** The `NR7` item is written with raw lags because a lag on a catalog call (`nr(7)[1]`) is not written (ISS-24); it has the meaning of `nr(7)`, "strictly the smallest of the last n bars". `HIGH_52W_MOMENTUM` has no momentum filter on purpose: doc 07 defines none and any threshold would be invented; `close > close[1]` only keeps a flat series (every close ties the highest) from firing. Warm-up: `sma(50)` 50 sessions, `sma(200)` 200, `highest(252)` 252; a shorter history shows as not ready.

**Deferred, with the missing piece:**
- `RS_ROTATION` (strongest stocks in strongest sectors): needs `sector_rs(n)`, which does not exist and is not planned until a strategy needs it (P3-01e).
- `BB_SQUEEZE`: doc 07 gives the name only. A squeeze needs a threshold on the bandwidth relative to its own past (no definition, and a lag on `bb_width(...)` cannot be written), so the numbers and the measure would be invented. It waits for an owner definition or for P3-02f.
- `FAILED_BREAKOUT`: doc 07 gives the name only; which level was broken, by how much and how fast it failed is undefined, and the level (an N-day high before the breakout bar) cannot be written without a lag on `highest(n)` (ISS-24, P3-02f).

**Out:** SELL (swing is BUY only), ATR stops, a target at a band or level (only `FIXED_RR` runs), any API change, any new log event id, any migration.

## Acceptance criteria
1. Each seed reads, compiles on 1D BUY with no error and no warning, and the simulator accepts it (`SwingBatchTests`).
2. On a fabricated daily series each fires exactly once at the close of the trigger session with the stop of the text, is filled at the open of the next session and walks to the target at 2 R, the numbers written beside the series in `SwingBatchDays`.
3. A negative series for each gives no candidate.
4. Every setup gate and every trigger item (each is its own list item) has a series on which everything else holds and only that item is false (nothing fires), and the same series without the item gives a candidate (a trigger of one item is replaced by `close > 0`); the terms of the `NR7_ID_BO` range test also have a series where the older range is equal (a tie, binary-exact 0.75) and not only narrower, and a series too short for the warm-up gives no candidate; the five older ranges of the `NR7_ID_BO` range test each have such a series, so deleting any term of it turns a test red.
5. No look-ahead: the candidate (with its trace values) is identical when the sessions after the decision are cut off and when they are replaced.
6. The embedded seed list is the fifteen names; each new hash is pinned in `SwingBatchSeedTests`; the hashes of the eleven earlier built-ins are unchanged; the seed stores fifteen families at version 1 and a second run writes nothing.

## Contract
- **API / Data / UI:** none. The seed writes `lab.strategies` and `lab.strategy_versions` rows through the existing store.

## Non-functional
No secrets, no new log id (the seeder's 4433 to 4435 are reused).

## Test plan
There is no C# oracle, by design (ADR 0056, 0079): the oracle is the hand-calculated daily series with its stop, entry and target written next to it, plus plain-arithmetic checks of the indicator values at the trigger (a perfect ramp makes the EMA(20) the close less 9.5 steps and the SMA(50) the close less 24.5 steps; Wilder RSI(2) of the climb and two falls is 16.67 then 6.25; the highest close of the 252 sessions is 98.30). `Simulation.Tests`: `SwingBatchTests` over `SwingBatchDays`. `Infrastructure.Tests`: `SwingBatchSeedTests`, the embedded list. `Api.IntegrationTests`: the seed row count in `LabStoreTests` and the known ids in `StoredStrategyVerbTests`. One case holds a position to the fifth session (`MaxHold`). In every series the lowest low of the stop window is the oldest session of it, so a shorter lookback would give another stop. Edge cases: a close on the inside day's high (no break), a low above the average, a flat series that ties its own high, a pullback in a downtrend, a long-term-average gate.

## Open questions
None blocking. For the owner later: the definitions of `BB_SQUEEZE` and `FAILED_BREAKOUT`; whether any of the four should be dropped before the ledger counts them.

## Done when
- [x] Acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] ADR 0081, doc 07, roadmap row, `scripts/build-docs.sh`
