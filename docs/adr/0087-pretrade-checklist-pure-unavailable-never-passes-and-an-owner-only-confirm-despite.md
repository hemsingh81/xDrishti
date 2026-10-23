# 0087 — Pre-trade checklist: pure, unavailable never passes, owner-only confirm-despite

- **Status:** Proposed (spec [P7-05](../specs/P7-05-pretrade-checklist.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (owner-only approvals, `confirmation_reason`, paper-only allocations), [0082](0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) and [0084](0084-portfolio-fill-greedy-with-every-limit-rechecked-and-unavailable-blocking.md) (unavailable blocks).

## Context
Doc 10 section 6b and doc 15 section 2 put automatic checks on every ticket and require explicit confirmation to approve despite a failure. Several inputs (results dates, F&O ban list, correlation) have no source yet; they must not read as clean.

## Decision
1. **Pure Domain checklist** per ticket and per account, in `XDrishti.Domain.Checklist`: account gate, risk profile, capital headroom, exposure headroom, daily loss, data quality, holdings conflict, sector exposure, results within days, F&O ban, surveillance, price band, liquidity, correlation. Each answers pass, fail or unavailable (the P7-01 outcome) with stable code, value, limit and a line of words.
2. **Unavailable never passes.** Missing, non-finite or invalid input is unavailable. A known failure still wins over an unknown part of the same check (holdings conflict).
3. **Policy:** fail with `warn` shows only; fail with `block_with_confirm` and every unavailable needs confirmation. Defaults per doc 16, everything else blocks.
4. **Confirm-despite** is an `OwnerConfirmation` that can only be created with an `owner:` actor and a non-blank reason (at most 500). Applying it sets the standing to `confirmed_despite`; the outcome is untouched, so it can never be shown as a pass. P7-10 stores it in `approvals.confirmation_reason` in the same transaction as the approval (ADR 0083 keeps the database-side owner and cutoff rules).
5. **Paper only.** An account that is not a paper-mode Trading account yields one refused check and no others; no confirmation covers it.
6. **No persistence yet.** Review and approval are P7-10; a separate checklist table would duplicate the approval trail.

## Consequences
- Until P15 and P7-06, results, ban and correlation are always unavailable, so every real ticket needs a confirmation. That is the honest state.
- The liquidity share (1 %) and the edge-fails rule are assumptions, reversible.

## Alternatives considered
- **Treat unknown as pass or warn.** Rejected: claims a check nobody ran.
- **Store results in a new table now.** Rejected: nothing reads it before P7-10, and the approval row already carries the reason.
