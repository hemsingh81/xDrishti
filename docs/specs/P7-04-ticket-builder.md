# P7-04 — Ticket builder: tomorrow's conditional tickets from the last close

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; spike done and written before the build, 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-04.
- **Design refs:** docs/design/10-trade-selection.md (ticket anatomy), 07 §1 and §2 (setup versus trigger), 11 (stops, exits); ADR 0034/0036 (rules compiler, three-valued logic), [0082](../adr/0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md), [0083](../adr/0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md), [0086](../adr/0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md); ADR 0088 (this slice).

## Assumptions (stated up front)
1. **Safety: this slice touches none.** A ticket is a conditional record of what to watch tomorrow. No order, no account, no credential, no approval. The new builder is registered but **off by default**: the nightly keeps publishing the empty skeleton (`NullPlanBuilder`) until the owner sets `Planning:Builder:UseSelection` to true.
2. **The strategy version of a cell is the latest stored version of its strategy** (a cell key names the strategy id, not a hash). The ticket carries the hash it used.
3. **Instruments come from the baskets of the scan scope**; this slice uses all non-archived baskets' members now (the `scan_scope` table is P7-07/09) and a basket's allowed styles and sides are **not** yet applied (stated as unavailable on the ticket). Reversible.
4. **(Review 2026-10-23: polarity, exit, performance.)** Not-ready parts are enumerated as true and false per condition text; the cell's exit and style are matched, never the first exit; strategy compiled once per cell, bars prepared once per instrument; weekly and monthly parts need the calendar; a short session ends at the calendar's end.
4a. **A part that reads only 1D, 1W or 1M features is known at the close of D; a part that reads any intraday feature, the clock (`time`) or a market context is knowable only tomorrow** and is classified Unknown, whatever the frame says at the last minute of D (an intraday feature at that minute describes D, not tomorrow).
5. **Anything that should be known at the close but is not** (history not ready, last minute of D missing, the strategy does not compile, the frame refuses) is **unavailable**: no ticket, listed by reason, never a guess.
6. **Levels**: the stop level is a number only when the stop reads a 1D or longer feature (a swing low, bar low); the target is a rule with an indicative level from the close of D, labelled indicative. Intraday stops (opening range) are rules only.
7. **Selection** is P7-01 as it stands. **Not wired, listed as unavailable on every ticket:** portfolio fill (P7-02 needs accounts and the book, P7-07), checklist (P7-05 needs an account), smart rules (P7-06 is not on main). Allocations are empty. Cost, events, F&O ban and surveillance are unavailable (so under default rules most candidates are held and listed as rejected with their reasons, as P7-01 intends).
8. Turnover is computed from the bars up to the close of D (mean of the last 20 sessions' close x volume); no data after D is read.

## Spike (0.5 d): can the compiler classify true / false / unknown from closed data only?
**Question (P7-00 "unverified").** When the entry trigger depends on intraday bars that do not exist yet, can the compiler and evaluator say true, false or unknown per part, from daily/closed data?

**Read:** `Truth` (True, False, Unknown), `BoundSide.Evaluate` (the Kleene truth of every item and of the setup, trigger and invalidate lists), `CompiledItem`/`CompiledCondition.Features` (the features each condition reads, with their timeframe), `FeatureFrame` (fed 1-minute bars; a 1D bar closes at the 15:29 step), `EntryScanner`, the stop and exit plans, the built-ins ORB, BASE_BREAKOUT, VWAP_PULLBACK.

**What works with no compiler change.**
- Three-valued logic is complete: `and`, `or`, `not`, lists `all`/`any`, "data not ready" gives Unknown, not False. `BoundSide.Evaluate` returns every item's truth, so the planner can classify per part.
- The timeframe of every feature a condition reads is public (`CompiledCondition.Features`). A frame stepped with the minute bars up to the close of D closes the 1D, 1W and 1M bars of D and gives exact daily values. So "known at the close" is a property of the features read, and can be decided per item.
- Stop level features and exit plans are public (`StopPlan.Level`, `ExitPlan.Values`).

**What needs a compiler change: one small, additive one.** The `time` keyword is a bound node with no feature; whether a condition reads the clock was internal. Added `CompiledCondition.ReadsClock` (a tree walk, 8 lines, no behaviour change). Nothing else in the compiler or the frame changes.

**Result of the spike (checked in tests on fabricated data):** BASE_BREAKOUT (all parts daily) classifies fully at the close: setup and trigger are True or False. ORB (trigger on 15m, no setup) is setup True (no list), trigger Unknown: a conditional ticket. An intraday strategy with a daily setup gives True/False for the setup and Unknown for the trigger. VWAP_PULLBACK-like parts (vwap, session features) are Unknown by assumption 4, which is conservative (a `prev_day_*` feature would be knowable but its value at the close of D is D-1's, so it is not used).

**Deferred.** Applying basket styles/sides; a stop that depends on the entry price beyond a rule; intraday-only strategies' levels as numbers; evening sessions; per-basket scan scope; compiling every version of every cell (only the latest version of each cell's strategy).

**Not large or risky, so the build goes ahead.**

## Goal
From the stored cells, the stored strategies and the bars up to the close of D, build a plan for the next session whose every ticket says what is already true, what is still to watch, where the stop and target are, the evidence behind the cell and why it is provisional, deterministically and without any bar after the close of D.

## Scope
**In:** Domain `Planning`: `SetupTruth`, `TicketPart`, `TicketDraft`, `TicketVerdict`, `TicketFacts`; Simulation `Scanning`: `TicketBuilder` (pure: compiled side + minute bars + session to classification, levels, texts); Strategies: `CompiledCondition.ReadsClock`; Application: port `ITicketSource`, `SelectionPlanBuilder` (an `IPlanBuilder`), options `PlanBuilderOptions` (flag, off), registration; Infrastructure: `StoredTicketSource` (cells, strategies, baskets, bars) and registration; ADR 0088; tests.  
**Out:** fill, checklist, smart rules, accounts, allocations, approval, any endpoint or screen, the scan scope table, depth.

## Acceptance criteria
1. Kleene hand tables: a known-at-close part gives True/False; an intraday part gives Unknown; `all`/`any` folds follow Kleene; an Unknown is never counted as True (a plan has no ticket whose setup is False).
2. A known-at-close part that is Unknown (not ready) makes the instrument unavailable with a reason, not a ticket.
3. Truncation property: for any set of bars after the close of D (added, changed, removed), the built ticket is identical.
4. Determinism: the same inputs give the same ticket text, levels and plan input hash.
5. BASE_BREAKOUT end to end on fabricated data gives a ticket (BUY, stop level, indicative target, validity); ORB gives a conditional ticket with its trigger text; a False setup gives no ticket.
6. `SelectionPlanBuilder` builds a plan (primary/reserve/rejected with reasons, evidence with the unavailable inputs listed), an empty source gives an empty plan, a source failure is a failed result, and by default the `NullPlanBuilder` is the registered `IPlanBuilder`.
7. No type behind this slice can place, modify or cancel an order (the architecture rule of ADR 0083 stays green).

## Contract
- **API:** none. **Data:** none, no migration (the plan is stored through the existing `IPlanStore`). **UI:** none.
- **Setting:** `Planning:Builder:UseSelection` (bool, default false). Log ids 4700 to 4709.

## Non-functional
Deterministic by inputs. Cost: one frame of about 100 sessions of minute bars per (instrument, cell) in the worst case; measured only on fabricated data (UNVERIFIED on the owner's 214 instruments). Logs carry counts and reason codes, no prices, no secrets.

## Test plan
Unit: Domain Kleene fold and draft text; Simulation `TicketBuilder` (hand tables, truncation property, determinism, built-ins end to end); Application `SelectionPlanBuilder` with fakes; DI default. Architecture tests unchanged.

## Open questions
None blocking. For the owner later: Q3 baskets/styles (assumption 3); whether to turn the flag on after P7-09.

## Done when
- [x] Criteria have evidence; `scripts/check.sh` green
- [x] ADR 0088, roadmap row, docs built
