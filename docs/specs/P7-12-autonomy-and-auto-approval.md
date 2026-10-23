# P7-12 — Autonomy and auto-approval (an option behind a setting, default OFF)

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22 and the owner's binding instruction of 2026-10-23, CLAUDE.md "Approvals have an auto option"; every question below taken as the recommended answer). Built 2026-10-23.
- **Phase / roadmap row:** P7 — Selection, accounts & nightly plan, slice P7-12 of [P7-00](P7-00-selection-accounts-and-nightly-plan.md), 3 d (redefined: the original slice stopped for the owner's Q3/Q4/Q6; the owner answered)
- **Design refs:** docs/design/09-learning-and-adaptation.md §13 · 16 (`autonomy`) · 19-risks-and-compliance.md §4, §5 · 04-accounts-access-and-operations.md §4.2 · 10-trade-selection.md §7 · ADR 0083 (three safety reviews, kept whole), 0087 (checklist), 0086 (nightly), ADR 0091

## Assumptions (taken as recommended; the owner's answers are binding)
1. **Q1 No order, ever, in any setting.** Nothing in this slice places, modifies or cancels an order; the no-order tripwire is unchanged and green. An auto approval only makes a ticket Approved (a state); "armed" later still means watched.
2. **Q2 Paper accounts only.** The checklist already refuses a non-paper account and the allocations table only accepts paper; an auto approval needs a passing checklist for every allocated account.
3. **The setting rule (owner, 2026-10-23).** `autonomy.auto_approve_tickets` ON = no manual approval needed for what the rule allows; OFF = manual approval. **Default OFF** (no settings row = OFF). `autonomy.promote_from_reserve` (Q5): same rule, default OFF.
4. **Q3/Q4 threshold and checks (recommended answers).** Threshold: design default **0.58**, applied to the **lower bound of the shrunk cell estimate of the win rate** (`WinRateLower`) only; not the posterior mean, not a model's figure. All 14 checks of the checklist must be `Pass` on every allocated account: a failed, warned, **unavailable** or confirmable-despite check declines; the rule accepts only unconfirmed `ChecklistResult`s, so a confirmation cannot even be passed in. A ticket whose evidence lists any unavailable input declines. **Only primary** tickets, in state Proposed, **never a reserve**, never one the owner modified. **At most N auto approvals per planned session** (setting, default 3, 1 to 20).
5. **Q6 Learning changes.** The setting hook exists nowhere else yet: no learning change is applied automatically, and no apply path exists. (`autonomy.auto_apply_learning` is not stored; adding the key is a later migration when an apply path exists.) The weekly cap and auto-rollback of doc 09 §13 therefore have nothing to govern.
6. **Depth recording is out of P7.**
7. **Settings home.** The owner asked that every change be an append-only audited row and that the database check "the switch was ON at that time". The overwrite-in-place `platform.app_settings` cannot give either, so the settings are an insert-only table `workflow.autonomy_settings` (Q14's JSON pattern is not reused for this one safety setting; the others stay as planned). Each row is a complete snapshot (both switches, threshold, cap), with the actor, a reason, the database's time and the database's hash of the canonical text. The newest row is current.
8. **Who changes it.** Only an owner act: `AutonomyChange.Create` (Domain) refuses any actor that is not `owner:...`; the endpoint `PUT /api/autonomy/settings` needs a session, the CSRF token and **step-up (password re-entry, doc 16)** for every change (raising or lowering), and builds the actor from the authenticated session's e-mail, never from the body. The table also checks `actor LIKE 'owner:%'`. As with the rest of the record, any code holding `xd_app` could write that string: the control is that no other path does (see Honest limits).
9. **Where the rule runs.** `AutoApprovePlanHandler` is the one use case; it is registered, but **nothing calls it yet** (the cutoff service and the pre-open check are P7-10/P7-11). It must be called strictly before the cutoff. `ApplyReviewCutoffHandler` is unchanged: with no auto approval the cutoff lapses as before (a test proves it). The checklist inputs are not wired (P7-10), so the registered `IChecklistSource` is a null source returning nothing, and with it **no ticket can be auto-approved today**. That is deliberate and safe; the plan builder also still lists inputs it did not apply as unavailable.
10. **Q5 reserve promotion.** `ReservePromotion.Select` (pure): switch OFF returns nothing, always; ON it picks reserve tickets that meet every term of the auto rule (the same reasons, no weaker test), best rank first, at most one per freed slot and within the daily cap. The pre-open check (P7-11) will use it and **must add its own database gate and record**; none exists, and a reserve ticket can still reach Armed only through Approved.
11. **Names.** ADR 0091. Log ids 4730 to 4734. Migration `AddAutoApproval` (new; the plan-record migrations are untouched). New domain namespace `XDrishti.Domain.Autonomy`.

## Goal
Anything that today needs the owner's manual approval can also run automatically, controlled by a setting the owner alone changes, with the automatic decision recorded as automatic (never as the owner), with the settings snapshot, so that the owner can always see and undo the setting, and so that with the setting OFF nothing differs from before.

## Scope
**In:** Domain `Autonomy` (`AutonomySettings`, `AutonomyChange`, `AutoApprovalPolicy`, `AutoApprovalDecision`, `ReservePromotion`, wording and decline codes); Application ports (`IAutonomySettingsStore`, `IAutoApprovalStore`, `IChecklistSource` + null source), handlers (get, change, auto-approve a plan), log ids; Infrastructure `AutonomySettingsStore`, `PlanStore.AutoApproveAsync`; migration `AddAutoApproval` (settings table, auto kind, snapshot columns and keys, triggers, `p_win_lower` on plan items, grants, `Down`); `GET`/`PUT /api/autonomy/settings`; `PlanEvidence.PWinLower` set by the selection builder; ADR 0091; docs; contract.
**Out:** calling the pass (cutoff service, P7-10/P7-11), the checklist inputs (P7-10), the Decisions report comparison of auto versus manual (P7-14/P8), any screen, the pre-open record of a promotion, the learning-apply path (Q6), any order.

## Acceptance criteria
1. The policy hand table: approve only when the switch is ON in a stored row, the ticket is primary and Proposed, before the cutoff, lower-bound P(win) at or above the threshold, no unavailable input, a complete checklist with every check Pass for every allocated account, and the cap is not reached; every other combination declines with every reason that applies; threshold, cutoff and cap boundaries; unknown or impossible P(win) never passes. *`AutoApprovalPolicyTests`.*
2. Unavailable never auto-approves: any unavailable check, any unavailable input listed on the ticket, a missing or duplicate checklist, a checklist for another account or symbol, a live account, and the null checklist source all decline. *`AutoApprovalPolicyTests`, `AutonomyHandlersTests`, `AutoApprovalDatabaseTests`.*
3. With the setting OFF (or nothing stored) the pass reads the switch and nothing else (no plan, item, checklist or store write is touched), and the cutoff path is as before: undecided tickets lapse, there is no auto row and no settings row. *`AutonomyHandlersTests`, `AutoApprovalDatabaseTests`.*
4. Only an owner actor changes the settings (`rule:`, `system:`, `cli:`, empty and `owner:` with no name are refused before the store); values in range; every change appends a row with the database's time and hash; the endpoint needs step-up and a session and ignores any actor in the body; a stale change is a conflict. *`AutonomyChangeAndPromotionTests`, `AutonomyHandlersTests`, `AutoApprovalDatabaseTests`.*
5. The database allows an `auto` approval only against the NEWEST settings row with the switch ON (a row that was on and was later switched off, and the off row, are refused), with a matching id and hash, for a primary item of a published or in-review plan, before the cutoff by the database clock, as actor `rule:autonomy`, decision approve, with no modifications or confirmation, within the row's daily cap; the event may only be from Proposed by the rule. An owner row keeps its rules and carries no snapshot. *`AutoApprovalDatabaseTests`.*
6. An owner decision and an auto approval racing on one ticket: exactly one approval and one approving event; a second approving event is refused by the chain. *`AutoApprovalDatabaseTests`.*
7. Reserve promotion: with the switch OFF it never promotes (also with the auto-approval switch ON); ON it applies the auto terms, rank order, freed slots and the cap. *`AutonomyChangeAndPromotionTests`.*
8. `xd_app` can SELECT and INSERT but not UPDATE, DELETE or TRUNCATE on `workflow.autonomy_settings`, and cannot replace or disable its trigger or the approval trigger. *`PlanGrantsTests`.*
9. The no-order tripwire is unchanged and green; the wording and names are pinned (`rule:autonomy`, `auto`, "auto-approved by rule, no order exists", policy version). *Architecture tests, `AutoApprovalPolicyTests`.*
10. Log ids 4730 to 4734 unique; handlers internal sealed and registered; `scripts/gen-api.sh` run; `scripts/check.sh` green.

## Contract
- **API:** `GET /api/autonomy/settings` -> `AutonomySettingsView` (switches, threshold, cap, hash, no-order notice, history newest first, latest 50). `PUT /api/autonomy/settings` (step-up) body `{autoApproveTickets, promoteFromReserve, minPWin, maxAutoPerDay, reason}` -> the view; 403 `autonomy.owner_only` or `auth.step_up_required`, 400 `autonomy.values_invalid` / `autonomy.reason_invalid`, 409 `autonomy.stale`.
- **Data:** `workflow.autonomy_settings` (insert-only; checks; trigger stamps `changed_at` and `snapshot_hash`, takes an advisory lock); `workflow.approvals` + `settings_id`, `settings_hash`, `policy_version`, kind `owner|auto`, new shape check, FK `(settings_id, settings_hash)`; trigger `workflow.stamp_approval` extended; `trading.check_plan_event` extended; `trading.plan_items.p_win_lower`. Migration `AddAutoApproval` with `Down` (which fails by design while an auto approval exists).
- **UI:** none (the Decisions line and notification wording is `auto-approved by rule, no order exists`, a constant for P7-10/P7-17).
- **Events/jobs:** none.

## Non-functional
The pass reads one row when OFF. Logs carry ids, counts, flags and error codes only (4730 to 4734), never an actor or a reason. Time through `TimeProvider`; the database clock decides the cutoff and the settings in force.

## Test plan
Domain hand tables and boundaries, application tests over fake stores (OFF touches nothing, refusals before writes, stale), integration on a real database (every DB refusal, the race, the cap, the cutoff by the database clock, the hash formula, step-up and actor from the session, anonymous 401), grants as `xd_app`, architecture.

## Honest limits
- **Today nothing is auto-approved even with the switch ON**: the checklist inputs are not wired (null source) and the builder lists inputs it did not apply as unavailable. The machinery and its database guards are built and tested; it becomes live as P7-10/P7-04 wire the inputs.
- "Owner" and "rule" are strings in the database: any code holding `xd_app` could write an `owner:` actor into `autonomy_settings`. The control is that the only writer is the endpoint (session, CSRF, step-up) and the architecture/review rules; a bypass by a compromised process is not stopped by the database.
- The lower bound is of a shrunk estimate on replayed history of about 2.1 years with modelled fills and no hold-out; 0.58 is the design default, not evidence of an edge.
- The advisory-lock ordering makes the settings change and an auto approval mutually exclusive in the database, but a settings change committed after an approval's transaction began is, by design, ordered after it.
- The daily cap counts auto approvals of all plans of the planned date.

## Open questions
None blocking. (Owner answers Q1, Q2, Q5, Q6, depth recording are binding; Q3/Q4 taken as recommended.)

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] Contract regenerated; migration added; ADR 0091; docs and roadmap updated
