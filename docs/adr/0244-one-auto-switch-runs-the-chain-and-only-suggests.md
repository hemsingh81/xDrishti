# 0244 — One Auto switch runs the chain and only suggests

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Built in AU-17; the final service ids are in [0252](0252-the-auto-switch-resumes-five-chain-services-follows-the-stored-switch-for-the-builder-and-restores-only-what-it-resumed.md).
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Applies the owner's rule of 2026-10-23. Independent of [0086](0086-the-fail-closed-nightly-pipeline-gates-one-row-per-attempt-a-null-builder-and-a-paused-service.md)'s lapse default.

## Context
Many services start paused and each has its own control. The owner wants one switch and next-day suggestions with reasons, by basket or stock.

## Decision
1. Auto defaults OFF. ON needs a fresh password; OFF does not.
2. ON resumes `eod`, `portfolio-broker-history`, the weekly learning service, `nightly` and `suggestions` (the build confirms and lists each service's final id here) and selects the real plan builder. OFF restores exactly what ON changed.
3. Auto produces suggestions limited to the chosen scope, each with reason and evidence; `unavailable` is never a pass.
4. Auto does not change autonomy, approve, arm, or reach `XDrishti.Execution`. Pinned by architecture tests. No setting makes it place an order.
5. Automatic steps are recorded as `auto` with the setting snapshot.

## Consequences
The owner has one control. Auto-approval (P7-12) and live trading (P12) remain separate owner acts.
