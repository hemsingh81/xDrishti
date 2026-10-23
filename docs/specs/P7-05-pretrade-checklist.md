# P7-05 — Pre-trade checklist

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; recommended answers taken; built 2026-10-23). Slice of [P7-00](P7-00-selection-accounts-and-nightly-plan.md).
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, row P7-05.
- **Design refs:** docs/design/10-trade-selection.md §6b; 15 §2; 16 (`pretrade_checks`); 04 (accounts, trading mode, risk profile); [P7-03](P7-03-plan-record-and-state-machines.md) / ADR 0083.

## Assumptions (stated up front)
1. **Pure logic, no persistence.** The owner's confirmation is a value that P7-10 writes into the existing `workflow.approvals.confirm_reason` (ADR 0083: owner-only actor, insert-only, cutoff rules). No table, migration, endpoint, verb or log id: a verb over stored plan items would need sector, ADV, band and book facts nothing stores yet.
2. **Paper-mode Trading accounts only.** A live (manual_live) or non-Trading account is refused with one check; nothing else is evaluated and no confirmation can override it. No order, credential or autonomy path; Q2 (which accounts) is not decided here, the account set is an input.
3. **Unavailable never passes.** A missing, non-finite or invalid input gives `unavailable`; it needs the owner's confirmation with a reason, whatever the check's policy. The review shows it as `confirmed_despite`, the outcome stays `unavailable`.
4. **Policy** from doc 16: `holdings_conflict` and `sector_exposure` warn; every other check blocks with confirmation (safe side). Overridable per code.
5. **Assumed numbers:** liquidity limit 1 % of average daily volume; entry at a price band edge fails (locked circuit); daily loss at the limit fails; results within 3 days (doc) inclusive. Reversible.
6. **Results dates and the F&O ban list have no data source (P15)**, and correlation waits for P7-06: those checks are `unavailable` in any real run today, and say so.
7. **A confirmation is bound** to one account, one symbol and the check codes it names (no ticket id exists yet); it covers nothing else, a check that needs one and is not named stays `needs_confirmation`. It is ignored (and not recorded) if nothing it names needed it. P7-10 stores `ChecklistReview.ConfirmationText` (`overrode code1,code2: reason`, at most 500 characters, the reason at most 200) in `approvals.confirm_reason`.
8. Result and review can only be built by `PreTradeChecklist.Evaluate`/`Review` (internal constructors); `CanApprove` needs every expected code once, in order, with the account gate first.
9. Limits and policy overrides are validated (`ChecklistLimits.Create`, `ChecklistPolicy.Create`). The doc 16 policy key `sector_limit` is the code `sector_exposure` here.

## Goal
For a ticket and a paper account, say per check pass, fail or unavailable with a stable code and numbers, and whether the owner may approve, and how.

## Scope
**In:** Domain `Checklist`: `PreTradeChecklist`, inputs (`ChecklistTicket`, `ChecklistAccount`, `ChecklistFacts`, `ChecklistLimits`, `ChecklistPolicy`), `ChecklistResult`, `ChecklistReview`, `OwnerConfirmation`, codes, ADR 0087, tests.  
**Out:** approval endpoint and screen (P7-10), correlation computation (P7-06), results and ban data (P15), persistence, allocation (P7-07).

## Acceptance criteria
1. 13 checks plus the account gate, each hand-tabled at the limit, just beyond, and missing.
2. Unavailable never passes; blocks approval until confirmed.
3. Confirmation needs an `owner:` actor, a non-blank reason (at most 200), an account, a symbol and known check codes; it yields `confirmed_despite`, not a pass; unneeded confirmation is not recorded.
4. A live or non-Trading account is refused, even with a confirmation.
5. Warn-policy fails do not block; unavailable with warn policy does.
6. Deterministic; inputs unchanged.

## Contract
No API, data, UI, CLI or log ids.

## Test plan
`PreTradeChecklistTests` and `ChecklistBindingTests` (completeness, binding, validation, NaN/infinity/zero/negative inputs per check); `PlanHandlersTests` (confirm reason trimmed and not blank, one owner prefix). Architecture tests unchanged.

## Open questions
None blocking. Assumption 5 numbers are the owner's to confirm.

## Done when
- [x] Acceptance criteria have tests
- [x] `scripts/check.sh` green
- [x] ADR 0087, roadmap row, docs built
