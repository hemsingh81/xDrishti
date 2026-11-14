# P3-09e — Independent gate readers for VWAP_PULLBACK and BASE_BREAKOUT

- **Status:** Approved (owner delegated 2026-11-14)
- **Phase / roadmap row:** P3 — Simulator, DSL & first strategies; follow-up of P3-09d (the automated gate)
- **Design refs:** [ADR 0048](../adr/0048-the-automated-gate-replaces-the-owners-chart-check-as-the-p3-exit.md), [ADR 0291](../adr/0291-the-gate-reads-vwap-pullback-and-base-breakout-and-refuses-a-strategy-it-has-no-reader-for.md); rules: [P3-05](P3-05-vwap-pullback-the-second-built-in-strategy.md), [P3-06](P3-06-base-breakout-the-third-built-in-strategy.md), ADR 0037, 0038, 0040, 0043, 0047

## Goal
The Round B pack (`p3-roundb-1`, VWAP_PULLBACK and BASE_BREAKOUT) came back `BLOCKED_DEFECT` on every row ("reader: no trade") because `XDrishti.SimReader` knew only ORB. Give the gate an independent reading of both strategies, and make it refuse a strategy it cannot read instead of reporting a defect.

## Scope
**In:**
1. `VwapPullbackReader` and `BaseBreakoutReader` in `XDrishti.SimReader`, written from the spec rule tables, sharing no code with the engine (the architecture tests stay as they are). The existing ORB reader shares its fill and exit walk with them (`ReaderWalk`, `ReaderBuckets`, `ReaderClock`), behaviour unchanged.
2. The assisted read and the gate load the stored sessions each reader needs (130 calendar days before the trade date; for BASE_BREAKOUT also 14 days after) through `IStoredDays.GetRangeAsync`.
3. A pack with a strategy that has no reader is refused with exit 2 and the text "unsupported strategy", before any bar is read or any file written.
4. ADR 0291; the exit-code table of ADR 0048 and the usage of `scripts/verify-pack.sh`.

**Out:** any engine change; a second source for the history days (the second source still replaces only the row's own date); a reader for any other strategy; re-running the gate on the real pack (the coordinator does it).

## Acceptance criteria
1. VWAP_PULLBACK reader: hand-built days give a trade to the target, to the stop, squared off at 15:15, a gap through the stop, an entry at the limit, a SELL mirror; and no trade for a down-trend, a cold start (too little history), a dip too far from VWAP, a trigger before 10:00 or after 14:30, a fill that never reaches the limit, and a first candidate that is missed (final).
2. BASE_BREAKOUT reader: hand-built series give a `max_hold` exit at the close of the fifth session, a target and a stop on later sessions, an overnight gap through the stop, an entry at the limit, and no trade for a gap above the limit, a fill after 09:20 (09:20 itself fills), an open at or through the stop, volume below `vol_x` (a tie fills), a base wider than `base_pct`, a close below the base high (a tie fills), a close below the 50-day average, fewer than 50 sessions, a missing 15:29 minute, no next session; a list that ends early is `end_of_data`; the weekly gate counts the signal week only when the signal is the last session of its week (a holiday included).
3. `verify-pack --gate` on a pack with an unknown strategy exits 2 ("unsupported strategy"), writes nothing, calls no second source.
4. The ORB reader tests and the assisted-check and gate tests stay green.

## Contract
No API, table or migration. `IStoredDays` gains `GetRangeAsync`; `ReaderExitReason` gains `MaxHold` (`max_hold`).

## Non-functional
The range read is one query per row. No secrets, no new log event.

## Test plan
`VwapPullbackReaderTests`, `BaseBreakoutReaderTests` (XDrishti.SimReader.Tests); `GateRunnerTests` (unsupported strategy); `SimReaderArchitectureTests` unchanged.

## Open questions
None; the interpretations taken are listed in ADR 0291.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green (the coordinator runs it); reviewer pass
- [x] No contract change; no migration
- [x] ADR 0291 and ADR 0048 updated
