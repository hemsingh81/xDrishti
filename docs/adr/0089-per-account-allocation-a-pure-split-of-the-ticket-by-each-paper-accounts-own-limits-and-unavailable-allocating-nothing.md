# 0089 — Per-account allocation: a pure split of the ticket by each paper account's own limits, unavailable allocating nothing

- **Status:** Proposed (spec [P7-07](../specs/P7-07-per-account-allocation.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (`plan_item_allocations`, paper-only check), [0084](0084-portfolio-fill-greedy-with-every-limit-rechecked-and-unavailable-blocking.md), [0087](0087-pretrade-checklist-pure-unavailable-never-passes-and-an-owner-only-confirm-despite.md).

## Context
Doc 10 section 6 sizes a ticket per Trading account from its risk profile. The owner decided (Q2, 2026-10-23) that tickets go to paper-mode Trading accounts only, that live is switched on later one account at a time, and that P7 places no order and auto-approves nothing.

## Decision
1. **Pure Domain allocator** (`XDrishti.Domain.Allocation.PerAccountAllocator`): one decision per supplied account, ordered by account id, deterministic for any input order.
2. **Gates first, in the type, not the caller:** an account without the Trading role (`account_not_trading`) or not in paper mode (`account_not_paper`) is excluded with a code and can never receive a quantity, whatever else is true. A duplicate account id excludes every copy.
3. **Capacity** is the least of the risk-per-trade, capital-per-trade, funds and open-risk-headroom limits, rounded down to whole lots; the binding limit is named on the decision.
4. **The ticket quantity is a pool** shared by capacity (proportional, largest remainder, lowest id); the sum never exceeds it. An account squeezed out says `pool_exhausted` with its capacity.
5. **Unavailable allocates nothing:** missing, non-finite, zero or negative inputs (account or ticket) give a code naming the fields, never a guess.
6. **Rows:** `AllocationRows.ToNew` produces only paper rows with a quantity above 0, the shape the plan store and the database check accept. No new store method: allocations are written with the plan item at publish, insert-only, unchanged owner-only and cutoff rules.
7. **Verb** `xd plan allocate` runs the allocator over supplied inputs and prints each decision. It reads and writes nothing.

## Where this differs from the docs (stated, not hidden)
- Doc 10's `replicate` (default, Q4 of 2026-10-09) and `distribute` modes and the cross-account caps (`max_accounts_per_symbol`, total risk across accounts) are not built. The pool split is the safe reading of "sum of allocations at most the ticket"; the modes need the allocation setting (Q14) and cross-account holdings. Per-account total risk headroom is respected.
- Liquidity cap, cost-R and learned multipliers (doc 08 section 6) are not applied here.

## Consequences
- Until P7-09 wires it, nothing calls the allocator in the pipeline; the nightly plan still has empty allocations.
- Turning on a live account later is an owner decision that changes the DB check, this gate and the owner-facing flow together; nothing here pre-empts it.

## Alternatives considered
- **Replicate to each account independently.** Rejected for now: the sum could exceed the ticket quantity, which the owner's acceptance test forbids.
- **Treat unknown holdings or funds as zero risk.** Rejected: that is a guess in the unsafe direction.
