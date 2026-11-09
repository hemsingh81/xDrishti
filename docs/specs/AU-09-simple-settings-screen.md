# AU-09 — Simple settings screen

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-09 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-08
- **Decisions:** [ADR 0242](../adr/0242-few-visible-settings-and-learned-changes-tighten-automatically-while-loosening-needs-the-owners-password.md)
- **Design refs:** docs/design/16-configuration.md; docs/design/13

## Goal
The Settings page opens on a handful of plain choices; everything else is one named step away.

## Scope
**In:** A rebuilt first view with Essential settings only, a section named `Advanced settings` holding the existing tabs (role matrix, event filter, thresholds), and each value's `learned` or `default` mark.

**Out (not in this slice):** Removing any existing control; the Services Configure dialog (stays where it is).

## Acceptance criteria
1. The first view shows only Essential rows, at most eight.
2. Every existing setting stays reachable in one step under `Advanced settings`, and each existing owner-act keeps its password.
3. Each changed-from-default value shows who changed it and when (owner or learned).
4. `Reset to default` is available per setting; if that loosens, it asks for the password as any loosening does.
5. Loading, error, phone and a11y states; settings tabs recover from a failed chunk (P8-16 `LazyBoundary`).

## Contract
- **API:** reads the catalog; existing write routes.
- **Data:** none.
- **UI:** `/settings`, feature `features/settings`; lazy Advanced panel.
- **Events/jobs:** none.

## Non-functional
First-load budget unchanged.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Vitest with MSW; a test that the first view has no Advanced control.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed

## Build note 2026-11-09 (partial, approved by delegation)
Built: the five-row essentials view, the Advanced reveal, loading/empty/error states. AC3 and AC4 are not met yet: `auto.enabled` and `suggest.scope` have no store until AU-17 and AU-16, and `notices.failure` has none; the rows are read-only and offer no control. The change-history line and Reset to default follow with AU-10 (learned changes). Those slices must finish AC3 and AC4 here. Review addendum: the default/changed mark cannot run in production yet, because the catalog returns no stored value for any Essential key (it is a null today); the risk is that the owner reads "Not changeable here yet" for everything. `risk.profile` and `chargesheet.gate` also lack who-changed and Reset to default until AU-10 (explicit criteria added to AU-10, AU-16 and AU-17).
