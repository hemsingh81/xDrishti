# P7-07 — Per-account allocation

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; owner answers of 2026-10-23 binding; built 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-07.
- **Design refs:** docs/design/10-trade-selection.md §6; 04 (accounts, Trading role, trading mode, risk profile); 08 §6; 16 (`plan_item_allocations`); [P7-02](P7-02-portfolio-fill.md), [P7-03](P7-03-plan-record-and-state-machines.md) / ADR 0083, [P7-05](P7-05-pretrade-checklist.md).

## Assumptions (stated up front)
1. **Owner answers (2026-10-23, binding).** Q2: tickets go to **paper-mode Trading accounts only**; manual-live is switched on later by the owner, one account at a time, and the database already refuses it (`plan_item_allocations.mode = 'paper'`). Q1: P7 places no order; armed is database state and alerts. Q3: default autonomy Advisory, no auto-approval anywhere. This slice touches none of them: no credential, no order, no approval, no autonomy.
2. **Pure logic plus a read-only verb.** The table is written only inside `IPlanStore.PublishAsync` (allocations are part of a plan item and the store is insert-only), so there is no store method here and no change to the owner-only or cutoff rules. `AllocationRows.ToNew` maps a result to the `NewAllocation` rows the pipeline (P7-09) will publish.
3. **The ticket's quantity is a pool.** The allocator splits it; the sum of allocations never exceeds it. Each account's own limits cap its share. (Doc 10's `replicate`/`distribute` modes and `max_accounts_per_symbol` / cross-account total risk are **not built**: they need a stored allocation setting (Q14) and every account's holdings per symbol; the split here is the safe pool form. Reversible.)
4. **Capacity of an account** = the least of equity x risk-per-trade % / |entry - stop|, equity x max-capital-per-trade % / entry, funds / entry, and (max total risk % - open risk %) x equity / |entry - stop| (zero if negative), rounded down to whole lots. The binding limit is named. Liquidity cap, cost-R and the meta/throttle multipliers are not applied (named limits; P7-06 multipliers apply to the risk percent the caller passes in).
5. **Split of the pool:** if all capacities fit, each gets its own; otherwise lots are shared in proportion to capacity (rounded down) and leftover lots go to the largest unused capacity, then the lowest account id. Deterministic for any input order.
6. **Unavailable never allocates.** A missing, non-finite, zero or negative equity, percent, funds or holding fact gives that account nothing (`unavailable_account`, naming the fields); an unusable ticket (side, entry, stop on the wrong side, lot, quantity below one lot) gives every eligible account nothing (`unavailable_ticket`). Gates come first: a non-Trading or non-paper account is excluded with its own code whatever else is missing.
7. **Held symbols:** an account that already holds the symbol gets nothing (`already_held`); unknown holdings are unavailable.
8. Equity (fixed capital or the funds the last sync read) is passed in; where it comes from is the pipeline's (P7-09). Compare tolerance 1e-9 share.

## Goal
For one ticket and the accounts, say per account the quantity it gets, or the code and numbers of why it gets none, never more than its own limits or the ticket's quantity, and never to a non-paper or non-Trading account.

## Scope
**In:** Domain `Allocation` (`PerAccountAllocator`, ticket, account, decision, result, codes), `AllocationRows` (Application mapping), verb `xd plan allocate` (read only, supplied inputs), ADR 0089, tests.  
**Out:** wiring to the stored accounts and the plan (P7-09), replicate/distribute modes and cross-account caps, any store method, migration, endpoint, log id, live accounts (owner, later), orders, approvals.

## Acceptance criteria
1. Each limit (risk, capital, funds, exposure headroom) binds in turn on a hand table; lot rounding down; below one lot gives a code and numbers.
2. A live or non-Trading account is excluded with a reason and never allocated; rows contain paper only.
3. Missing or unusable ticket or account inputs allocate nothing and say which.
4. Sum of allocations is at most the ticket quantity; no account exceeds any of its own limits (random property, 300 rounds).
5. Same output for any input order (25 shuffles); inputs unchanged; every account once.
6. Verb parses and refuses bad lines; prints a line per account.

## Contract
CLI only: `xd plan allocate --ticket ... --account ...`. No API, data, UI or log ids (4710-4719 unused).

## Test plan
`PerAccountAllocatorTests` (48), `AllocationRowsTests`, `PlanAllocateOptionsTests`; architecture allow-list gains the namespace.

## Open questions
None blocking. Replicate vs distribute and the cross-account caps wait for the allocation setting (Q14).

## Done when
- [x] Acceptance criteria have tests
- [x] `scripts/check.sh` green
- [x] ADR 0089, roadmap row, docs built
