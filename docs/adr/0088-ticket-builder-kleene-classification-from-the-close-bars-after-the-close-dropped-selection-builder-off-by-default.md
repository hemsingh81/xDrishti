# 0088 — Ticket builder: Kleene classification from the close, bars after the close dropped, selection builder off by default

- **Status:** Proposed (spec [P7-04](../specs/P7-04-ticket-builder.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0034](0034-the-rules-expression-language-lint-and-compiler.md)/0036 (three-valued logic, compiler), [0082](0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) (selection), [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (plan record, no order), [0086](0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md) (null builder, `IPlanBuilder`).

## Context
P7 speaks about tomorrow for the first time. The hazard is look-ahead: a ticket for D+1 may use only data up to the close of D. Part of a setup (an opening-range break) can only be decided on tomorrow's bars. P7-00 listed as unverified whether the compiler can say "unknown" for such parts.

## Decision
1. **Spike result: no compiler redesign.** The compiler already has Kleene logic and per-item truths; the feature timeframes of every condition are public. One additive property, `CompiledCondition.ReadsClock`, was added (the `time` keyword has no feature).
2. **Known at the close** means: the condition reads only 1D, 1W or 1M features, not the clock, a market context or expiry days. Every other condition is classified **Unknown** whatever the frame shows at the last minute of D (an intraday value there describes D, not tomorrow). Conservative. On a 1D (or longer) timeframe `prev_day_*` and `gap_pct` are daily parts like any other and are evaluated as of the close of D; on an intraday timeframe they are Unknown. Weekly and monthly parts need the trading calendar to close their bars; without one they count as not ready.
3. **Kleene fold in Domain** (`Kleene`, `SetupTruth`): Unknown is never True. Overall = setup and trigger and not invalidate. False: no ticket (Rejected). True or Unknown: a conditional ticket.
4. **Not ready is Unavailable, not a guess.** The not-ready known parts (one per condition text, so the same text in two lists is one part) are enumerated as true and false, every assignment (at most 8 parts; more is Unavailable). Setup and trigger are monotone increasing and the invalidate list goes through Kleene NOT, so no single forcing is the best or worst case for all. If the outcome is the same for every assignment it stands (False everywhere is Rejected); if not, the pair is Unavailable. So are an incomplete session, a strategy that does not read, compile or match its hash, and a cell style that is not the strategy's style.
5. **Truncation by construction.** `TicketBuilder` drops every bar at or after the close of D before it steps the frame (tested: bars of later days, shuffled, change nothing). Pure and deterministic.
6. **The cell's exit is matched, never guessed.** The cell exit label is matched to one exit candidate of the strategy (`FIXED_RR{rr=2}`, or the policy name when the strategy has exactly one candidate of it). No match: no target, reward to risk null, `target` listed unavailable. Never the first fixed-RR exit. `rr` is read by name.
7. **Levels** (stop and target both labelled indicative): a stop level is a number only when its feature is 1D or longer; the target is a rule with an indicative level from the close of D; intraday stops are rules only.
8. **`SelectionPlanBuilder`** (`IPlanBuilder`, id `selection`) turns tickets into P7-01 candidates (reward to risk from the ticket, turnover from the bars), runs the selection with default rules, and writes primary (first 10), reserve and rejected items with reasons and evidence. Fill, checklist, smart rules, basket styles and sides, cost, events, ban and surveillance are listed unavailable on every item; no allocation. Under default rules most candidates are therefore held and listed as rejected, which is the honest state.
9. **Off by default.** `IPlanBuilder` resolves to `NullPlanBuilder` unless `Planning:Builder:UseSelection` is true. The nightly keeps publishing the empty skeleton until the owner turns it on.
10. Ports: `ITicketSource` (stored cells, latest stored version of each strategy, basket members now, minute bars 400 days back to the close). Each strategy is read, hash-checked and compiled once per cell; each instrument's bars are truncated and sorted once; a side that reads only 1D or longer data is stepped with one daily bar per session instead of every minute (tested equal). Every item says `stats may belong to an older strategy version`. The setting reads real configuration; anything but `true` (garbage included) is off., `IPlanBuilderSettings`. Log ids 4700 to 4702.

## Where this differs from the docs (stated, not hidden)
- Doc 10 puts the trigger "as of the close"; for intraday strategies the trigger text is the list of conditions left to watch, not a price level.
- The cell has no version hash; the latest stored version of its strategy is used.

## Consequences
- Cost: one frame per (instrument, cell). Timing note (not a gate, `Timing_of_214_instruments_by_several_cells_on_fabricated_bars`): 214 instruments x 4 cells over 120 sessions of fabricated minute bars took about 3 seconds in a debug test run, two of the four cells on the minute path. Not measured on the owner's data or with the database reads.
- A short or special session closes at the calendar's end for D when the calendar has one; without a calendar the regular 15:30 close is assumed and a short session is Unavailable.
- The infrastructure glue (`StoredTicketSource`) is unit-tested over fake ports (reason grouping and cap, cancellation, a real seeded strategy end to end), not against a database.

## Alternatives considered
- **Run the replay on the last day to find fires.** Rejected: it is the replay P7 must not re-run, and it cannot say unknown.
- **Change the compiler to evaluate "as of tomorrow".** Rejected: large and risky for no gain; the feature timeframe already says what is knowable.
