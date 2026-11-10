# AU-08 — Settings inventory and visibility tiers

- **Status:** Built 2026-11-09 (ADR 0246). Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-08 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 2 agent-days (an estimate, not calibrated) · **Needs:** AU-05
- **Decisions:** [ADR 0242](../adr/0242-few-visible-settings-and-learned-changes-tighten-automatically-while-loosening-needs-the-owners-password.md)
- **Design refs:** docs/design/16-configuration.md

## Goal
Every setting has one owner record stating whether it is visible, its validated range, and in which direction it is safer.

## Scope
**In:** A `SettingDescriptor` registry (key, label, tier `Essential|Advanced|Internal`, default, range, `safer: higher|lower|none`, `learnable`), filled for every existing owner-editable and `Confidence:*` key, and `GET /api/settings/catalog`.

**Out (not in this slice):** Moving where values are stored; the screen (AU-09); learned changes (AU-10).

## Acceptance criteria
1. A test fails when an options class bound at start-up has a key with no descriptor.
2. At most eight descriptors are `Essential`: Auto on or off, what to suggest, risk profile, charge sheet, failure notices (five at the start).
3. Every descriptor with numeric bounds is validated at start-up, and a value outside its range stops the start-up as existing options do.
4. A key with no declared safer direction is treated as loosening in both directions.
5. The catalog never returns a secret; a test scans for secret-looking key names.

## Contract
- **API:** `GET /api/settings/catalog` returns key, label, tier, value, default, range, source.
- **Data:** none.
- **UI:** none.
- **Events/jobs:** none.

## Non-functional
p95 under 100 ms.

## Safety
The safer direction is reviewed key by key by the independent reviewer because AU-10 relies on it. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit for the registry and the coverage test; integration for the endpoint.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
