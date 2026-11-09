# AU-10 — Learned tightening and password-gated loosening

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-10 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** AU-08
- **Decisions:** [ADR 0242](../adr/0242-few-visible-settings-and-learned-changes-tighten-automatically-while-loosening-needs-the-owners-password.md)
- **Design refs:** docs/design/09-learning-engine.md §14; docs/design/19 §risks

## Goal
The system adjusts a small allow-list of thresholds from what it has learned, only ever more cautious by itself; anything less cautious waits for the owner's password.

## Scope
**In:** `LearnedSettingProposer` (pure), a change log, apply-tighten when Auto is on, a proposal with a password for loosening, and one-step undo.

**Out (not in this slice):** Any strategy or risk increase; automatic model promotion (ADR 0220 unchanged).

## Acceptance criteria
1. Only `learnable` descriptors are touched; a direction is taken from the descriptor, and `none` is never automatic.
2. Given Auto ON and proof that passes, a tightening is applied and recorded `auto` with the setting snapshot and evidence reference, never as an owner approval.
3. Given Auto OFF, a tightening is a proposal only.
4. A loosening is never applied without a fresh owner password (same `OwnerAct` flow), at any Auto state; a test proves there is no code path otherwise.
5. Undoing an automatic tightening is itself a loosening, so it asks for the password (amends doc 09's one-click Revert for this case).
6. Unavailable or missing evidence yields no change, and it never counts as a pass.
7. Guardrails from doc 09 hold: one variable per change, out-of-sample proof, at most `Learned:WeeklyCap` automatic changes (0 to 10, default 3).
8. A change is applied only from evidence that is not sealed: hold-out data is never read here.
9. If a tightening would leave the coming plan empty for 5 sessions in a row, a notice names the setting; it does not self-loosen.

## Contract
- **API:** `GET /api/settings/learned`; `POST /api/settings/learned/{id}/approve` (fresh password when loosening) and `/dismiss`.
- **Data:** Migration `AddLearnedSettingChanges`: insert-only `config.learned_setting_changes` with key, old, new, direction, actor kind, settings snapshot hash, evidence ref, timestamps; no UPDATE or DELETE to the runtime role except a state column grant.
- **UI:** A `Learned changes` panel under `Advanced settings`.
- **Events/jobs:** Runs after the weekly learning service; idempotent per evidence hash.

## Non-functional
Weekly job under a minute on the owner's data (UNVERIFIED until measured).

## Safety
**SAFETY.** The owner's approval-auto rule applies: Auto defaults OFF, turning it on is a password-protected owner act, decisions are recorded as auto with a snapshot. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Hand-worked tighten and loosen tables; property test that automatic changes always move in the safer direction; the DB grant test.

## Open questions
See AU-00 owner questions.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
