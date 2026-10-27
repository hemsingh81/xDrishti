# 0145 — The simulator reads the market context point-in-time from the stored days, a missing context is a named skip, and the run key carries the days' fingerprint

- **Status:** Accepted (decided in P3-lift under the owner's standing run instruction; spec [P3-lift](../specs/P3-lift-simulator-reads-the-market-context.md)).
- **Date:** 2026-10-27.
- **Relation to other ADRs:** [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) (the context and its age bound), [0034](0034-the-rules-expression-language-lint-and-compiler.md) and [0036](0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) (`needs_context`, `needs_expiry_days` as lint facts), [0037](0037-simulation-conventions-and-the-two-stage-seam.md) (the simulator), [0039](0039-an-engine-module-is-reached-from-an-adapter-in-infrastructure-never-from-application.md) (layering).

## Context
The simulator refused every strategy that reads a context feature (`simulation.needs_context`) and every strategy that reads `is_expiry_day` (`simulation.needs_expiry_days`) because it had nothing to give them. P3-01d and P3-01e now store the daily context. A run is stored under a key, and a second run under the same key must produce the same record.

## Decision
1. **Context in, as Domain values.** The replay input carries the stored `ContextDayRecord` list; the simulation library (which may name the indicator library) builds one `MarketContext` per list instance and gives it to the frames. Application and the engine port name no indicator type.
2. **Point-in-time is the library's rule.** A decision on date d reads the latest context day strictly before d (`MarketContext.TryAsOf`), and nothing past the age bound (`MaxAgeDays`, 7). The simulator adds no read of its own; a truncation property test guards it.
3. **Not ready is a false condition; a missing context is a skip.** A warm-up or stale day makes no trade (the DSL's NaN rule). When the whole context is missing or refused, every instrument is skipped with the new reason `context_unavailable`, because the empty result then has one cause the owner can act on.
4. **The run key carries a fingerprint of the days on or before the window end, only for a strategy that reads the context.** A context that can change under a closed window (a breadth rebuild) then gives a new key instead of a conflict with the stored record; the key of every other strategy is unchanged. The stored record does not print the fingerprint (no schema bump); a later bump may.
5. **`is_expiry_day` stays refused.** No stored data holds the expiry dates: the master has no option expiries, no options data exists (P14), and NSE changed its expiry weekdays more than once, so a calendar rule would be a guess. The refusal keeps its code and path and now states the reason.

## Consequences
- Context-reading strategies can be tested in the Lab; their results depend on the stored context (a stale or short history gives fewer trades, shown as skips and as `features_not_ready`).
- The hash in the key makes a re-run after a breadth rebuild a new run, not an error.
- Limits: the record does not show which context a run used (only the key does); the VIX basis (`index_and_vix` or `index_only`) is not printed per trade (the regime tags of P6-02 carry it); the plan-time ticket path is unchanged.
- Rejected: a context port inside the engine (the engine does no I/O); a built `MarketContext` passed through Application (breaks ADR 0039); a field on `SimulationRun` now (it would change every stored key); an expiry rule by weekday.
