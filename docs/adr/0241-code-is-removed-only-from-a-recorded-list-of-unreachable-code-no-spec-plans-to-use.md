# 0241 — Code is removed only from a recorded list of unreachable code no spec plans to use

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Not yet built.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Governs AU-05 to AU-07.

## Context
The owner asked to remove unused code. Much switched-off code looks unused to a tool, by design.

## Decision
1. Audit first (AU-05). Removal only from a list in a later ADR, group by group, each reviewed.
2. A candidate needs: no reference, no DI, route, CLI, reflection or registry entry; and no Draft or Approved spec or roadmap row planning it.
3. **Protected, never a candidate:** P12 execution (`XDrishti.Execution`, `live-*`), P13 recorders and features (ADRs 0135, 0173), P14 archive (ADRs 0176, 0177), every service or feature that is merely off or paused, null seams kept by an ADR (`NullPlanBuilder`, `MlInfluenceSwitchOff`, `NoMlInfluenceEvidence`), migrations, owner-only CLI verbs, architecture and safety pins of retained code.
4. Halt, freeze, gate and reconciliation code needs the owner's explicit yes per item. Dropping a table or column needs the owner's yes and a backup.

## Consequences
Removal is slower than a sweep and leaves parked code in place. That is intended.
