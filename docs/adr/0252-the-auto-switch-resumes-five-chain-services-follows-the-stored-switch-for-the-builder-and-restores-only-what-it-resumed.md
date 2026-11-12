# 0252 — The Auto switch resumes five chain services, the builder follows the stored switch, and OFF restores only what ON resumed

- **Status:** Accepted by delegation (owner, 2026-11-09, in chat, AU programme).
- **Date:** 2026-11-10.
- **Relation to other ADRs:** Builds [0244](0244-one-auto-switch-runs-the-chain-and-only-suggests.md) (this fixes its service list) on the stored `auto.enabled` of [0247](0247-learned-setting-changes-are-bounded-steps-on-a-short-allow-list-stored-in-an-insert-only-log-and-applied-at-the-next-start.md) and the scope and suggestions of [0251](0251-auto-scope-is-an-insert-only-id-list-that-filters-candidates-before-selection-and-suggestions-are-a-read-model-over-the-stored-plan.md).

> **Amended 2026-11-12 by [0277](0277-the-cell-statistics-refresh-repeats-the-newest-snapshot-to-the-hold-out-cut-fails-closed-and-resumes-only-with-the-owners-password.md) (LC-01):** §1's chain is now **six** services: `eod`, `portfolio-broker-history`, `cells-refresh`, `weekly`, `nightly`, `suggestions`. The new one resumes only with a fresh password (Auto ON is one), and OFF pauses it only if ON resumed it, like the others.

## Context
Spec AU-17: one switch for the chain from data to next-day suggestions, safe by default, with a record of what it changed. The owner's rule of 2026-10-23 applies: default OFF, ON is an authenticated owner act, an automatic step is recorded as `auto`, an `unavailable` input never passes, and no setting allows an order.

## Decision
1. **The chain** is the services `eod`, `portfolio-broker-history`, `weekly`, `nightly` and `suggestions` (the final ids of 0244 point 2). ON resumes those that are paused and stores their ids; OFF pauses only those ids that are still enabled. A service the owner had on before, or paused again later, is left as the owner has it. The resume and pause are the normal service acts, so a service keeps its own schedule and audit row; the actor is the owner.
2. **Preconditions** are read when ON is asked and shown by `GET /api/auto`: a scope is set, the primary Data account is connected, the charge sheet is confirmed (the chain needs costs: the suggestions are net of them), and every chain service is registered. Each is `pass`, `fail` or `unavailable`; anything but `pass` refuses ON with `auto.preconditions_failed` naming each item. A read that throws is `unavailable`.
3. **Passwords:** `POST /api/auto/on` and the Essential row `auto.enabled` set ON need a fresh password for the act; `POST /api/auto/off` and OFF on the row need a session only; Reset to default of the row is OFF. The stored value `auto.enabled` is written only by the switch.
4. **The builder** is not copied into a second setting: `IPlanBuilder` is the selection builder when `Planning:Builder:UseSelection` is true, else it reads the stored switch at build time (selection while ON, the skeleton while OFF). OFF therefore restores the builder with nothing to remember.
5. **The record** is `planning.auto_switch_changes`, insert-only: kind (`on`/`off`), actor (`owner:...` or `auto`, never an approval), time, the ids changed, and a snapshot (the switch before and after, each chain service before, the builder). If any step of ON fails, what was resumed is paused again and the switch stays OFF.
6. **Incomplete chain:** while ON, a chain service that is paused or missing makes `Complete` false and `Note` names it; it is never hidden.
7. **A fail-closed night** shows no suggestions: `GET /api/auto/suggestions` answers `auto.night_failed` with the gate and reason when the newest run failed closed and no plan exists for that night, instead of the plan of an older night.
8. **Boundaries:** Auto reads nothing from autonomy and calls neither plan approval nor arming; it holds no order-capable port and does not reference `XDrishti.Execution`. Architecture tests pin all three.

## Consequences
The owner has one control. Auto-approval (P7-12) and live trading (P12) stay separate owner acts. The Settings screen control is the frontend slice AU-18.
