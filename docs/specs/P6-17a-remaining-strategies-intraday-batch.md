# P6-17a — Remaining strategies, intraday batch

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine; row P6-17a of [P6-00](P6-00-learning-engine-and-ml-plan.md) (owner question 9, recommended answer: first the strategies that need no new feature).
- **Design refs:** [doc 07](../design/07-strategies.md) §3 (starter library), [doc 11](../design/11-strategy-lab.md) (rules language, strategy file) · [ADR 0034](../adr/0034-the-rules-expression-language-lint-and-compiler.md), [0035](../adr/0035-per-session-features-keep-the-history-of-the-session-only.md), [0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md), [0055](../adr/0055-the-strategy-file-its-schema-and-the-content-hash.md), [0056](../adr/0056-stored-strategies-the-lab-tables-the-seed-and-loading-by-version.md), [0079](../adr/0079-eight-more-intraday-strategies-as-stored-text-only.md); test pattern of [P3-05](P3-05-vwap-pullback-the-second-built-in-strategy.md) and [P3-06](P3-06-base-breakout-the-third-built-in-strategy.md).

## Assumptions (all recommended answers; the owner may reverse any of them)
1. Only strategies expressible in the existing rules language and the existing 56 features, with no new indicator and no new context, are built; the rest are named as deferred with the missing piece.
2. Each is **stored text only** (a seed YAML in `Seed/Strategies`, embedded and written by `xd-api migrate`, idempotent by hash). No C# builder, no registry row: the three first built-ins keep theirs and their hashes do not change.
3. The rule sets below are the author's reading of one-line names in doc 07 and are **proposals**, to be hand-checked on the owner's first look. Nothing here says any of them has an edge; every one is another trial in the ledger.
4. 15m only, BUY and SELL both written out by hand (the mirror, P3-02e, is not built), one `FIXED_RR` exit with `rr` a declared parameter, square-off 15:15, entry kind `at_close`, stops only of the kinds the simulator runs.

## Goal
Eight more intraday strategies exist as stored, versioned text, run by `simulate --strategy <id>` and the Lab like the first three, each shown on a fabricated day to do what its text says.

## Scope
**In (built):** `PDH_PDL_BREAK`, `PDH_PDL_REJECT`, `VWAP_RECLAIM`, `EMA_PULLBACK`, `GAP_GO`, `GAP_FILL`, `INSIDE_BAR_BO`, `MEAN_REV_BANDS`.

| Id | BUY (SELL is the written-out mirror) | Stop | Window |
|----|------|------|--------|
| `PDH_PDL_BREAK` | `close > prev_day.high and close[1] <= prev_day.high` | lowest low of 3 bars | 10:00 to 13:30 |
| `PDH_PDL_REJECT` | wick below `prev_day.low`, close back above it and above the open, previous close not below it (SELL: at the previous high) | the bar's low | 09:30 to 14:30 |
| `VWAP_RECLAIM` | daily close above daily EMA(20); two closes below `vwap`, then a close above `vwap` and the open | lowest low of 3 bars | 10:00 to 14:30 |
| `EMA_PULLBACK` | `ema(9) > ema(21)` and hourly close above hourly EMA(21); the bar's low reaches `ema(21)` and it closes above `ema(9)` and its open | the bar's low | 09:45 to 14:30 |
| `GAP_GO` | `gap_pct >= gap_min` (0.5); close breaks above the opening range (`or_min` 15; being above the range high it is already above the day's open, so no separate test of the open) | opposite end of the opening range | 09:30 to 11:00 |
| `GAP_FILL` | gap down within `gap_min` (0.5) to `gap_max` (3) percent; close back above the day's open after a close at or below it (SELL: gap up, mirror) | lowest low of 3 bars | 10:00 to 12:00 |
| `INSIDE_BAR_BO` | previous bar `inside_bar`, close above the mother bar's high | lowest low of 3 bars | 10:00 to 14:00 |
| `MEAN_REV_BANDS` | `adx(14) < adx_max` (25); `crosses_above(close, bb_lower(20, 2))` and close above open (rr 1.5) | lowest low of 3 bars | 10:00 to 14:30 |

**Notes on the table.** `PDH_PDL_BREAK` starts at 10:00 on purpose: its stop is the lowest low of the last 3 bars, which at 09:30 or 09:45 would reach into the previous session, and a gap through the level at the open has no later bar that crosses it from below, so it never fires (`GAP_GO` is the strategy for gaps). `GAP_FILL` is a gap fade that exits on `FIXED_RR` like the others, not a fill-to-previous-close target (only `FIXED_RR` runs). The thresholds chosen by the author (`gap_min`, `gap_max`, `adx_max`, `rr`, the windows and lookbacks) are tunable parameters or text, to be judged through the ledger and the experiment margin, not conclusions.

**Deferred, with the missing piece:** `CPR` (central pivot range). Its levels can be written from `prev_day.*`, but doc 07 and the roadmap row give no definition (narrow-range threshold, which side, which levels as entry) and the row marks it UNVERIFIED; the numbers would be invented, so it waits for an owner definition. The swing batch is P6-17b.
**Out:** the BUY to SELL mirror (P3-02e), ATR stops and buffers, a target at a band or level (only `FIXED_RR` runs), any API change, any new log event id, any migration.

## Acceptance criteria
1. Each seed reads, compiles on 15m on both sides with no error and no warning, and the simulator accepts it (`IntradayBatchTests`).
2. On a fabricated day each fires exactly once at the decision instant of the table above with the stop of the text, and walks to the target at 2 R (1.5 R for `MEAN_REV_BANDS`) with the entry, stop, target written beside the day in `IntradayBatchDays`.
3. The mirrored day gives the SELL candidate at the same instant with the mirrored stop.
4. A negative day for each gives no candidate on either side.
5. No look-ahead: the candidate (with its trace values) is identical when the bars after the decision are cut off and when they are replaced.
6. The embedded seed list is the eleven names; each new hash is pinned in `IntradayBatchSeedTests`; the hashes of `ORB`, `VWAP_PULLBACK`, `BASE_BREAKOUT` are unchanged; the seed stores eleven families at version 1 and a second run writes nothing.

## Contract
- **API / Data / UI:** none. The seed writes `lab.strategies` and `lab.strategy_versions` rows through the existing store.

## Non-functional
No secrets, no new log id (the seeder's 4433 to 4435 are reused).

## Test plan
There is no C# oracle for these strategies, by design: ADR 0056 kept the builders of the first three only as the test oracle for their seeds, and these have no builder (a second hand-written copy of the same rules would test the copy against itself). The oracle is the hand-calculated fabricated day, each with its stop, entry and target written next to it, plus the plain-arithmetic EMA check of the `EMA_PULLBACK` day. Every setup gate has a day on which the trigger holds and only that gate is false, and the same day with the gate deleted gives a candidate, so deleting a gate turns a test red.
`Simulation.Tests`: `IntradayBatchTests` (63 cases) over `IntradayBatchDays`. `Infrastructure.Tests`: `IntradayBatchSeedTests`. `Api.IntegrationTests`: the seed row count in `LabStoreTests`. Edge cases: a wick that closes on the level (no break), a gap outside the band, a close inside the mother bar, a ranging versus trending ADX gate.

## Open questions
None blocking. For the owner later: the definition of `CPR`; whether any of the eight should be dropped before the ledger counts them.

## Done when
- [x] Acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] ADR 0079, doc 07, roadmap row, `scripts/build-docs.sh`
