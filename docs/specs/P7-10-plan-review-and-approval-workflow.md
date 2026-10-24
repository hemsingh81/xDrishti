# P7-10 — Plan review and approval workflow (manual approval; the first write endpoints of the plan record)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22 and the owner's binding decisions of 2026-10-23; every question taken as the recommended answer). Built 2026-10-24.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-10 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 4 d
- **Design refs:** docs/design/04-accounts-access-and-operations.md §4.2 · 10-trade-selection.md §6b, §7 · 15 (pre-trade checklist) · 19-risks-and-compliance.md §4, §5 · ADR 0083 (plan record, three safety reviews, kept whole), 0087 (checklist), 0091 (auto-approval setting), 0092 (wiring, stored standings, inbox), ADR 0095

## Assumptions (taken as recommended; the owner's decisions are binding)
1. **No order, ever.** Approving is a state in our own database. Every approval answer and the review say "Approved for watching: no order is placed and none exists." (pinned). The no-order tripwire is untouched and green.
2. **Paper accounts only.** The allocations table accepts only paper rows; the stored checklist of a non-paper allocation is refused outright and no confirmation can cover the account gate.
3. **Manual approval is the default.** The only automatic path is the optional auto pass of P7-12, called by the review job only when `autonomy.auto_approve_tickets` is ON (default OFF). With OFF the job never reads that path's inputs beyond the one settings read and calls nothing; behaviour is manual. No endpoint can write a system/auto approval or the actor `rule:autonomy`.
4. **The actor is the session.** Same as the autonomy PUT: the verified session's e-mail, `owner:` + address, built in the endpoint; no body field is read for it. Unlike the autonomy PUT, no password is asked (the autonomy PUT changes a safety setting; an approval is a reversible-by-lapse state of a paper ticket). 401 without a session, 403 without the CSRF token (middleware). **Policy note:** approvals use session + CSRF without a password because they only make a paper ticket "approved for watching" (nothing is ordered, and an unreviewed ticket lapses anyway). If the owner wants a password for approvals that override a check (confirm-despite), `StepUpMiddleware` (`RequireStepUp` on the route, as for other sensitive routes) is the place to require it; it is not applied now.
4a. **Review job catch-up.** The cycle also lapses OPEN plans of earlier sessions whose cutoff has passed (job paused, machine asleep through 09:03 to 09:10); lapse only, never an approval or an auto pass. One plan that cannot be closed does not stop the others: the run reports a failure (`plans.cutoff_failed`) naming the plans after trying all.
4b. **Ignored confirmations are said back.** The approve answer lists (`ignored`) every confirmation, or code of one, that covered nothing: an account the ticket is not sized for, or a check that needed no override. Approve-all lists reserve, modified and already-decided tickets as skipped with the reason.
5. **The checklist comes from the stored standings.** The plan has no checklist column (ADR 0092 point 5); the wiring stores `checklist:<code>:fail|unavailable` and marks every wired primary ticket with `allocation_entry_is_indicative`. `StoredChecklistSource` rebuilds the 14 checks per allocated account: a check not listed passed; a ticket without the marker, without an allocation or with an unreadable standing has NO checklist and cannot be approved, not even with a confirmation (a missing record is not a pass). The standings are those of the whole plan (all accounts), so one account's failure shows on every account; this can only make approval harder. Honest consequence today: data quality is never available, so every real ticket needs a confirm-despite or is left to lapse, and approve-all approves nothing until the inputs are wired.
6. **Confirm-despite** is a list of `{accountId, codes[], reason}` in the approve body, one per account, bound server-side to the ticket's own symbol, reason 1 to 200 characters, only confirmable codes (never the account gate); stored as `[account] overrode <codes>: <reason>` joined by ` | ` in `confirm_reason` (up to 500). A confirmation for another account or another check covers nothing.
7. **Modify is size-down only.** The owner may lower the quantity for accounts the ticket already has (at least 1, strictly below the allocation, paper only). Stop, target, trigger, side and instrument cannot be edited: their rules are text the system cannot re-validate (to trade differently the owner rejects). The change is stored canonically (`qty:<account>=<n>;...`) in the modify approval row. The ticket becomes Modified, not approved; approving it re-reads the stored change, validates it against the allocations again, and runs the same checklist. A ticket can be modified once (the table has no Modified to Modified). A modified ticket cannot be auto-approved (database rule of P7-12).
8. **Cutoff.** Approve, modify and approve-all after the plan's cutoff are refused (`plans.closed`; the database re-checks on its own clock). Reject stays allowed until the cutoff job has closed the plan. The `review-cutoff` service (interval, every minute 09:03 to 09:10 on trading days, starts paused) runs `RunReviewCycleCommand` for today's plans: before the cutoff nothing lapses; in the last 2 minutes and only with the setting ON, the auto pass runs once per run (strictly before the cutoff); at and after it, `ApplyReviewCutoffHandler` lapses everything undecided and moves the plan. Lapsing is the default and the only thing the cutoff does to a ticket.
9. **Plan state.** The first owner approval or rejection moves the plan Published to InReview through the existing lifecycle table; Approved/Lapsed/Rejected come from the cutoff handler.
10. **Reserve** tickets can be approved one by one (the table allows it, "swap with reserve"); approve-all never touches them and nothing promotes them.
11. **Idempotent clicks.** A second approve of the same ticket is `plans.move_refused` or `plans.stale` (409), never a second row (the existing one-winner lock plus the table).
12. **Suggestions inbox.** `GET .../suggestions` lists a plan's suggestions with a seen flag; `POST .../seen` appends an insert-only row `workflow.suggestion_seen` (one per suggestion, actor `owner:...`). "Accept applies the change" and snooze are not built (nothing yet is applicable by one click); listed as follow-up.
13. **Rate limit.** A new policy `plan-decisions` (120 per minute per client address), distinct from the sign-in limit so decisions cannot lock out login.
14. **Notifications / reminders before the cutoff:** no notification port exists; follow-up (the service's failure notification is the existing one).
15. **Names.** ADR 0095. Log ids 4770 to 4776. Migration `AddSuggestionSeen` (new; no plan-record migration is touched).

## Goal
The owner can read the review of the evening's plan, approve, lower or reject each ticket (or approve all that are fully clean), with a failed or unavailable check needing an explicit, recorded confirmation; whatever nobody decided lapses at the cutoff; and nothing here places an order or approves on the owner's behalf.

## Scope
**In:** Domain (`StoredChecklist`, `TicketModification`, `QuantityChange`, wording); Application (stored checklist source replacing the null one, approve/modify/reject/approve-all handlers, review query, suggestion inbox handlers, review cycle handler, `review-cutoff` service, errors, log ids); `IPlanStore.ApprovalsAsync`; suggestions seen port + table + migration; endpoints under `/api/plans/{id}/...`; rate limit; ADR, docs, roadmap, contract.
**Out:** the screen (P7-17), pre-open check and arming (P7-11), reminders (no notification port), accept/snooze of suggestions, any change to stop/target, any order, reserve promotion.

## Acceptance criteria
1. Unauthenticated writes are 401, writes without the CSRF token 403, nothing is written; the actor in `plan_events` and `approvals` is `owner:` + the session e-mail whatever the body says (`actor`, `kind`, settings fields ignored); no `rule:` actor and no kind other than `owner` exist after any endpoint call. *`PlanReviewApiTests`.*
2. Approve with all checks passed: 200 with the pinned notice, one owner approval row, ticket Approved, plan InReview. *`PlanReviewApiTests`, `ReviewHandlersTests`.*
3. A fail/unavailable check refuses the approval (409 `plans.checklist_blocked`, naming the codes); a confirmation for another account, another code, a blank reason or the account gate covers nothing/is refused; the right one approves and stores the overridden codes and reason; a ticket with no or unreadable stored checklist cannot be approved even with a confirmation. *`PlanReviewApiTests`, `ReviewHandlersTests`, `StoredChecklistTests`.*
4. Modify: reductions only, existing paper accounts only, once; the stored change is re-validated and the checklist re-run on approval; a bad stored change stops the approval. *`PlanReviewApiTests`, `TicketModificationTests`, `ReviewHandlersTests`.*
5. After the cutoff approve/modify/approve-all are refused (409 `plans.closed`) and reject is allowed. *`PlanReviewApiTests`.*
6. Approve-all approves only primary Proposed tickets with every check Pass, lists the rest with reasons, never approves despite a check, never touches reserve or modified tickets. *`PlanReviewApiTests`, `ReviewHandlersTests`.*
7. Double approval, also six at once: exactly one 200, one approval row, one event. *`PlanReviewApiTests`.*
8. The review job: lapses undecided tickets at the cutoff (also reserve), approves nothing, moves the plan; approve racing the lapse at the cutoff loses; with the setting OFF no auto row and no call of the auto path at any time; with ON the auto pass runs only in the last 2 minutes, before the cutoff, and (checks as wired) approves nothing. *`ReviewHandlersTests`, `PlanReviewApiTests`.*
9. A live allocation cannot be stored, so no live account receives a ticket to approve. *`PlanReviewApiTests`, existing allocation tests.*
10. Suggestions list and the insert-only seen mark (one row, owner actor, grants test: no UPDATE/DELETE/TRUNCATE for `xd_app`). *`PlanReviewApiTests`, `PlanGrantsTests`.*
11. Every write route carries the `plan-decisions` rate limit; the `review-cutoff` service is registered and starts paused. *`PlanReviewApiTests`, `ServicesApiTests`.*
12. Architecture rules, log id uniqueness, `scripts/gen-api.sh`, `scripts/check.sh` green.

## Contract
- **API:** `GET /api/plans/{id}/review`; `POST /api/plans/{id}/items/{itemId}/approve` `{reason?, confirmations?: [{accountId, codes[], reason}]}`; `.../modify` `{reason, quantities: [{accountId, quantity}]}`; `.../reject` `{reason}`; `POST /api/plans/{id}/approve-all`; `GET /api/plans/{id}/suggestions`; `POST /api/plans/{id}/suggestions/{suggestionId}/seen` (204). Errors: 401, 403 (CSRF, `plans.owner_only`), 400 (`checklist.*`, `plans.modification_invalid`), 404, 409 (`plans.checklist_blocked`, `plans.closed`, `plans.move_refused`, `plans.stale`), 422 (`plans.invalid`), 429.
- **Data:** `workflow.suggestion_seen` (insert-only; unique per suggestion); migration `AddSuggestionSeen`.
- **Events/jobs:** service `review-cutoff`, interval 1 minute 09:03 to 09:10, paused until switched on, idempotent (a closed plan is a no-op).

## Non-functional
Logs carry ids, counts and codes only (4770 to 4776), never a reason, confirmation text, actor or account. Time through `TimeProvider`; the database clock decides the cutoff for an approval row.

## Test plan
Domain tables (`StoredChecklistTests`, `TicketModificationTests`), application over fakes (`ReviewHandlersTests`), integration on a real database (`PlanReviewApiTests`, grants), architecture unchanged.

## Honest limits
- Stored standings are plan-wide, not per account; today data quality is always unavailable, so nothing is clean and approve-all and the auto pass approve nothing.
- The owner's "approve despite" is a string in an insert-only row; code holding `xd_app` could write it (as for ADR 0083).
- Reminders before the cutoff and accept/snooze are not built.
- Only the size can be modified.

## Open questions
None blocking.

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract regenerated; migration added; ADR 0095; docs and roadmap updated
