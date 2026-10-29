# 0152 - The live-versus-paper report pairs the same ticket on both sides in R on the plan's risk per share, judges it by a one-sided non-inferiority bound, and is computed on read from stored rows

- **Status:** Accepted (decided in P11-03 under the owner's instruction of 2026-10-29 to build the slice; spec [P11-03](../specs/P11-03-live-vs-paper-slippage-report.md), with its "Amendments at build").
- **Date:** 2026-10-29.
- **Relation to other ADRs:** [0150](0150-the-execution-feedback-measures-each-live-entry-outcome-against-its-reference-in-delay-and-execution-estimates-slippage-per-liquidity-bucket-weekly-and-applies-nothing.md) (the executions, the waterfall function and the slippage in basis points this report reads), [0149](0149-the-live-capture-reads-the-days-trade-book-and-order-book-with-two-pinned-get-entries-stores-them-insert-only-and-gives-each-approved-ticket-one-final-outcome.md) (the outcomes), [0099](0099-the-report-engine-is-a-pure-core-with-one-population-rule-six-honest-states-and-a-ranking-by-the-credible-lower-bound.md) (the states and the evidence), [0116](0116-paper-fills-are-the-grading-replay-captured-per-armed-allocation.md) (the paper tables read here), [0151](0151-the-ramp-gates-breach-rules-and-halts-read-the-stored-live-record-and-write-only-insert-only-rows-the-evaluator-moves-no-ticket-and-the-exit-report-that-unlocks-s3-is-not-built.md) (P11-04b and P11-06 will read the same figures through the same handler).

## Context
After weeks of small live trading the owner needs to know whether real fills cost more than the paper account's did on the same tickets, by how much, and where (entry, exit, charges, missed tickets), with honest certainty at the sizes of one account.

## Decision
1. **The unit is the same ticket (plan item) on the live account and one paper account.** The shortfall is paper R minus live R in R per share on the live execution's risk per share (the plan's |entry - stop|); it splits exactly into an entry part and an exit part in decimal arithmetic (the spec's pair: 0.0175 + 0.1000 = 0.1175). Positive means live is worse.
2. **Statistics and verdict.** Mean, sd, se, the two-sided 95 % Student-t interval and the one-sided 95 % bounds from the stored t tables (the nearest lower row, the wider bound). Any figure needs 10 pairs (else `insufficient` with n); the verdict needs `Live:Report:MinPairs` (30): `within` when the one-sided upper bound is at most `Live:Report:NonInferiorityR` (0.10), `not_within` when the one-sided lower bound is above it, otherwise `inconclusive`.
3. **Nothing is dropped.** A ticket on one side only is listed with exactly one reason (`live_only` with the paper skip code, `paper_only` with the outcome, `live_open`, `paper_open`, `no_plan_levels`) and counted in the Evidence block; missed tickets appear in the execution gap at their plan R.
4. **Gross and net are never mixed.** Net is reported only when every pair has the live charges (recorded fills) and the paper charges (a stored sheet); otherwise the whole report is gross and says so, and the charges part is `unavailable`.
5. **The execution gap is one identity.** plan R - missed - missed (cause unknown) - entry slippage - exit difference - charges = result, per strategy or month, from the waterfall function of ADR 0150 for closed round trips and the plan grade's R for missed tickets. A group with a trip that has no recorded charges is gross as a whole (charges row `unavailable`); `unknown`, open and ungraded tickets are left out and listed; the total is built from the same tickets, not by adding group rows. The overrides row is `unavailable` (the decisions report of P8-04 measures it).
6. **Read only, computed on read.** No table, no migration, no setting that switches a behaviour (the three `Live:Report` options are thresholds with their spec defaults, so there is nothing to default to OFF and no owner write, no `Live:Writes:Enabled`, no password, no auto decision). The paper side is a port (`IPaperTradeSource`, select only on `paper.*`, the newest epoch only); a paper store that cannot be read gives `unavailable`, never an empty report. Nothing here calls Dhan or places, changes or cancels an order.
7. **One handler for later readers.** P11-04b (gates) and P11-06 (phase check) call `GetLiveVsPaperHandler`; they never compute the figures a second time.

## Amendments at build (what the spec left open or got wrong)
- `live_uncertain` is **not built**: live fills are not journaled yet (P11-01b), so there is no uncertain trip to read; `unknown` outcomes appear in the gap exclusions. *Update 2026-10-29: built in P11-01b (ADR 0154).*
- The pair's risk per share is the live execution's stored `risk_per_share` (the ticket level's or the plan grade's), the plan-price line uses the plan grade's entry and exit; a pair without plan prices makes only that line `unavailable`.
- Net requested without charges on both sides for every pair gives the gross report (decision 4), not a per-pair net.
- Strategy of a ticket is read from the plan item (`LiveTicket.Strategy`, an optional trailing field).
- The pairs table is a plain table (at most 200 rows a page), not a virtualised grid; the histogram reuses the stacked category chart with one stack.
- `GET .../live-vs-paper` also takes `paperAccountId`, `page`, `size`; 422 `live.paper_account_required` when several paper accounts exist and none is chosen (query or `Live:Report:PaperAccountId`); a chosen id that is not a paper account of the epoch is 422 `live.invalid`.
- Log id 5120 (one line per call: kind, rows, ms). `xd live report --account ID [--from] [--to]`.

## Consequences
- `/live` gains a lazy Live vs paper tab (read only); the first-load bundle is unchanged.
- Limits: the paired tickets are a selected sample; the paper fill is a model, so the report measures live minus a model; R per share removes a difference of quantity, not of the minute; a verdict needs 30 pairs (weeks); real Dhan and real paper rows were not seen: the pair, interval and gap are tested on hand-worked data and the paper SQL only against the empty real schema.
- Rejected: independent samples (no power at this n); a per-pair mix of gross and net; a smoothed density; computing the verdict a second time in the ramp.

## Review fixes 2026-10-29
- **One-sided t corrected.** `OneSidedT.Quantile95` used to take the next HIGHER table row (df 10 gave 1.761 against the true 1.812, df 60 and above gave 1.645 against 1.671), so one-sided bounds were too narrow (anti-conservative). It now takes the largest row at or below the df (df 10 is 1.833, df 15 is 1.761, df 60 to 119 is 1.671) and 1.645 only from df 120; the bound is wider. The same function feeds the P11-04b G3 gate (ADR 0151).
- The paper read catches every `NpgsqlException`, so a lost connection gives `unavailable`. `paper.exits` has a unique `entry_id` and `paper.entries` a unique (item, account), so the join is one row per ticket.
- The gap evidence dates come from counted tickets only. **A gross total can sit over net groups:** a group whose trips all have recorded charges is net, the total is gross as soon as one counted trip lacks them; the view's `basis` fields say which.
