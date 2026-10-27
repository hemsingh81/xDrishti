# P3-lift — The simulator reads the market context (and keeps refusing the expiry days, with the reason)

- **Status:** Approved (owner's standing run instruction, 2026-10-22/26: go slice after slice; the owner asked for this row to be built next, with the agent deciding the gaps). Decisions D1 to D8 are the agent's and are recorded in [ADR 0145](../adr/0145-the-simulator-reads-the-market-context-point-in-time-from-stored-days-and-the-run-key-carries-their-fingerprint.md).
- **Phase / roadmap row:** P3 — row "P3-lift: lift `simulation.needs_context` and `needs_expiry_days`".
- **Design refs:** docs/design/11-strategy-lab.md §2 (market context) · docs/design/07-strategies.md §6 (regime) · ADR 0033 (context, Amendment 14 age bound) · ADR 0034, 0036 (lint `needs_context`, `needs_expiry_days`) · ADR 0037 (the two-stage simulator) · ADR 0039 (Application names no engine type).
- **Needs (built):** P3-01c (`MarketContext`, `FrameOptions.Context`), P3-01d (`market.context_daily`, `IContextDayStore`, `ContextDayRecord`), P3-01e (breadth), P3-04 (`InstrumentReplayer`, run key).
- **Size:** 2 agent-days.
- **Safety:** no. Read-only over stored days; no order, switch, network path or secret.

## Goal
A strategy that reads the market context (`index_trend`, `vix_pct`, `regime()`, `breadth` and the other context names) runs in the simulator and the Lab on the context the data job stored, point-in-time, and says plainly when the context is missing. A strategy that reads `is_expiry_day` is still refused, with the exact reason.

## Scope
**In:** (1) the replay input carries the stored context days; the simulator hands a `MarketContext` to the frames; (2) `simulation.needs_context` is no longer raised; (3) the instrument skip `context_unavailable`; (4) the run key carries a fingerprint of the context days when, and only when, the strategy reads the context; (5) the application reads the days once per run, only for a strategy that reads them; (6) the refusal text of `simulation.needs_expiry_days`; (7) tests: look-ahead guard, determinism, speed guard, unchanged existing tests; (8) docs and ADR 0145.
**Out:** `is_expiry_day` (D5); the regime on the trades or in cells (P6-02); the training-set `regime` feature (the extractor adapter still passes no context: P6-02b); a context screen; the plan-time ticket path (`TicketBuilder` keeps treating a context condition as not knowable at the previous close); changing `ContextOptions.MaxAgeDays`.

## Acceptance criteria
1. **Accepted.** Given a strategy whose `setup` reads `index_trend`, `vix_pct` or `regime() == "UP_LOW_VOL"`, when `Simulator.Create` is called, then a simulator exists and no `simulation.needs_context` is raised; the existing tests that pinned the refusal now pin the acceptance.
2. **Reads the stored days, not the future.** Given stored context days D1..Dn and a strategy gated on `index_trend == 1`, when a decision is taken on date d, then the value read is the one of the latest day strictly before d (`MarketContext.TryAsOf`). Truncation test (40 seeds): replacing or removing every context day dated d or later leaves every decision, entry and trade dated before or on d unchanged; a day dated d itself is never read for d.
3. **Not ready is false, never a default.** Given the days before the first readable date of a feature (warm-up), or past `MaxAgeDays`, when a decision date falls there, then the condition is false (NaN) and no trade is made on it; a gap in the stored days longer than the age bound recovers on the next stored day.
4. **Run-level unavailable.** Given a context-reading strategy and no stored day (or a set the library refuses), when a run starts, then every instrument is skipped with `context_unavailable` and the run succeeds with no trades (a run in which every instrument is skipped succeeds, as today); the skip detail names the cause (`no_context` or `context_invalid`). A strategy that reads no context is unaffected whether days exist or not.
5. **Determinism.** Given the same bars and the same days, two runs give the same trades and the same run key; the key changes when a day dated on or before the window end changes (a breadth rebuild) and does not change when a day after the window end is added. For a strategy that reads no context the run key is byte-for-byte the key of before this slice.
6. **Shared, not rebuilt.** Given a run of 214 instruments, the `MarketContext` is built once per run (one build per day list), not once per instrument or per side; the comparison path (`ReplayComparison`) and `Explain` read the same context as `Replay`.
7. **Speed.** A context-reading strategy over a 250-session day-bar fixture costs at most 1.3 times the same strategy with the context conditions removed, plus the one build (about 3 ms for 1,300 days); the allocation guard of the scanner step stays green (the context read allocates nothing per step).
8. **Expiry days stay refused, precisely.** Given a side that reads `is_expiry_day`, then `simulation.needs_expiry_days` is raised with the path of the side and a message that says: no expiry calendar is stored; the days cannot be derived from stored data (the instrument master holds no option expiries and NSE moved its weekly and monthly expiry weekdays more than once, so a rule would be a guess); the fix is the options data of P14. The code and the path are unchanged.
9. **Docs.** Doc 11 §2 loses "the simulator refuses a strategy that reads the context"; ADR 0145 written; the roadmap and plan rows updated.

## Contract
- **Domain.** `InstrumentReplayInput` gains `IReadOnlyList<ContextDayRecord>? ContextDays = null` (last parameter; null for a strategy that reads none) and `string? ContextNote = null` (why there are none). `ContextFingerprint.Of(days, through)` (pure; SHA-256 of the canonical text of the days dated on or before `through`: date, high, low, close, VIX, breadth). `RunHeader` gains `string? ContextSha256 = null`; `RunAssembler.KeyText` adds the line `context=<sha>` only when it is set. `SkipReason.ContextUnavailable` (`context_unavailable`). `SimulationRun` is not changed (no schema bump).
- **Application.** `IPreparedStrategy.ReadsContext`. `RunSimulationHandler` reads `IContextDayStore.ListAsync` once when `ReadsContext`, passes the list in every input, puts the fingerprint in the header. No engine type is named here (the list is a Domain value).
- **Simulation.** `ContextReader` (internal): builds a `MarketContext` from the days once per list instance (a `ConditionalWeakTable`), null when the library refuses a row. `Simulator.Run`, `SimulatorComparison.Run` and `EntryScanner.Create` take an optional `MarketContext`; `Simulator.Run` refuses a context-reading side with no context (`simulation.context_not_given`, a programming error that the replayer never reaches). `InstrumentReplayer` builds the context, skips with `context_unavailable` when it cannot, and passes it to scan, comparison and explain.
- **API/UI:** none. The skip reason appears as a string in the run record (`skipped`), as every other reason does.
- **Data:** none.

## Non-functional
No allocation per step added (the frame already reads the context by cursor). The days (about 1,300 records) are read once per run. Nothing is logged about prices.

## Test plan
Unit (Simulation): acceptance, look-ahead truncation (40 seeds), warm-up and stale to false, determinism, shared build, comparison and explain parity, speed ratio, allocation. Domain: fingerprint prefix property (days after `through` do not change it), key line only when set, existing key golden unchanged. Application: reads days once for a context strategy and never for others; `context_unavailable` for an empty store. Existing tests: `RunnerRefusalTests`, lint and compile tests, `LabRunner` tests.

## Decisions
**D1 — the context goes in by the replay input, as Domain values.** Application cannot name `MarketContext` (ADR 0039, `ContextArchitectureTests`); the days are a Domain record, and the simulation library, which may name Indicators, builds the context. Rejected: a context port in the engine adapter (the engine is pure and does no I/O); passing a built `MarketContext` through Application (breaks the layering test).
**D2 — point-in-time is the library's, not re-implemented.** `MarketContext.TryAsOf(date)` already returns the latest day strictly before `date`. The simulator adds no read of its own, and the truncation test is the guard.
**D3 — not ready is a false condition; a missing context is a skip.** The DSL treats NaN as false, so a warm-up day makes no trade and is not counted as a default. Only the case where the whole context is missing is a named skip, because then the result would be empty for a reason the owner can act on (the data job). This is the same split as `features_not_ready`.
**D4 — the fingerprint is in the run key, not in the record.** A run is stored under its key and a second run under the same key must have the same record; a context that changes under a closed window (only a breadth rebuild can) must give a new key. Adding a field to `SimulationRun` would need a schema bump and change every stored key; a key-only line changes nothing for strategies that read no context. Limit: the stored record does not print the fingerprint (a later schema bump may).
**D5 — `is_expiry_day` stays refused.** No stored data holds the expiry dates: the instrument master carries no option expiries, no options data exists (P14 is not built), and NSE changed the weekday of weekly and monthly expiries more than once (the library itself says it "does not guess them"). A calendar rule would make a plausible but unprovable set of days; the honest state is `unavailable`. The refusal keeps its code and path and gains the precise reason; the lift for it belongs to P14 (options data) or to an owner-supplied dated list (a separate slice if wanted).
**D6 — one context per run.** Read once, shared by all instruments, sides and candidates; the list instance is the cache key, so nothing is rebuilt and nothing is global.
**D7 — the window end bounds what the key hashes.** A day dated after `To` can never be read by a decision on or before `To`, so it cannot change the run; hashing only days on or before `To` keeps the key stable while the data job appends new days.
**D8 — the plan-time ticket path is unchanged.** `TicketBuilder` marks a context condition as not knowable at the previous close; the nightly plan reads the context through `IContextFigures` (P3-01e/P7-09). Not part of this lift.

## Open questions
None blocking. Owner choice, default stated: an owner-supplied expiry list (default: none, `is_expiry_day` refused).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer pass: no blockers/majors
- [ ] ADR 0145; doc 11 and the roadmap rows updated
