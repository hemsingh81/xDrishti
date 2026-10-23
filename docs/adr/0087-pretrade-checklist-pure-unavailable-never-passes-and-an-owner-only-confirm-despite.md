# 0087 — Pre-trade checklist: pure, unavailable never passes, owner-only confirm-despite

- **Status:** Proposed (spec [P7-05](../specs/P7-05-pretrade-checklist.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [0083](0083-the-plan-record-insert-only-state-trails-owner-only-approvals-lapse-by-default-and-no-order-by-name.md) (owner-only approvals, `confirm_reason`, paper-only allocations), [0082](0082-selection-core-filters-with-unavailable-merge-and-a-deterministic-rank.md) and [0084](0084-portfolio-fill-greedy-with-every-limit-rechecked-and-unavailable-blocking.md) (unavailable blocks).

## Context
Doc 10 section 6b and doc 15 section 2 put automatic checks on every ticket and require explicit confirmation to approve despite a failure. Several inputs (results dates, F&O ban list, correlation) have no source yet; they must not read as clean.

## Decision
1. **Pure Domain checklist** per ticket and per account, in `XDrishti.Domain.Checklist`: account gate, risk profile, capital headroom, exposure headroom, daily loss, data quality, holdings conflict, sector exposure, results within days, F&O ban, surveillance, price band, liquidity, correlation. Each answers pass, fail or unavailable (the P7-01 outcome) with stable code, value, limit and a line of words.
2. **Unavailable never passes.** Missing, non-finite or invalid input is unavailable. A known failure still wins over an unknown part of the same check (holdings conflict).
3. **Policy:** fail with `warn` shows only; fail with `block_with_confirm` and every unavailable needs confirmation. Defaults per doc 16, everything else blocks.
4. **Confirm-despite** is an `OwnerConfirmation`, created only with an `owner:` actor, a non-blank reason (at most 200), an account, a symbol and known confirmable check codes. It is **bound**: it covers only those codes, for that account and symbol; any other check that needs confirmation stays `needs_confirmation`, and a confirmation for another account or symbol covers nothing. Applying it sets the standing to `confirmed_despite`; the outcome is untouched, so it is never shown as a pass. **Storage (P7-10):** `ChecklistReview.ConfirmationText` is `overrode <codes>: <reason>` with only the codes actually overridden, at most 500 characters (tested for the worst case), and goes in `approvals.confirm_reason` in the same transaction as the approval, so the Decisions report can show which checks were overridden. There is no ticket id in the binding yet (the plan item id arrives with P7-10).
4a. **Completeness.** `ChecklistResult` and `ChecklistReview` have internal constructors: only `PreTradeChecklist.Evaluate` builds a result, and `CanApprove` needs every expected code once in order with `account_paper` first, so an empty or partial list never approves.
4b. **One owner prefix.** `OwnerConfirmation.OwnerPrefix` is the single definition; the plan record's `PlanActors.OwnerPrefix` is that constant (a test pins it) and the database check repeats it. The actor must come from the authenticated identity, never from a request. The move handler validates the confirm reason like `OwnerConfirmation.Create` (trimmed, not blank).
5. **Paper only.** An account that is not a paper-mode Trading account yields one refused check and no others; no confirmation covers it.
6. **No persistence yet.** Review and approval are P7-10; a separate checklist table would duplicate the approval trail.

## Where this differs from the docs (stated, not hidden)
- **Policy:** doc 15 marks results and correlation as warnings; here they block with confirmation (the safe side). Doc 16's `sector_limit` key is the code `sector_exposure`.
- **Doc 10 section 6b** says a failed check needs confirmation; doc 16's defaults make holdings conflict and sector exposure warn-only. We follow doc 16. Warn on "an open trade already exists" is a duplicate-position risk; that the owner can approve it without confirmation is intended per doc 16 and is one line to change in the policy.
- **Price band:** when no band is supplied (for example F&O underlyings with dynamic bands) the check is unavailable, a known limit that makes the owner confirm; it is never assumed to pass.
- **Comparisons** use an explicit tolerance of 1e-9 so "at the limit passes" survives float sums.
- Limits are validated (days 0 to 30, volume share in (0, 100], correlation in (0, 1]) and policy overrides refuse unknown codes and undefined values.

## Consequences
- Until P15 and P7-06, results, ban and correlation are always unavailable, so every real ticket needs a confirmation. That is the honest state.
- The liquidity share (1 %) and the edge-fails rule are assumptions, reversible.

## Alternatives considered
- **Treat unknown as pass or warn.** Rejected: claims a check nobody ran.
- **Store results in a new table now.** Rejected: nothing reads it before P7-10, and the approval row already carries the reason.
