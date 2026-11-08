# 0226 — The owner records the `verify-pack --gate` result per strategy version in the database, and readiness row 15 reads it: only a PASS counts, no record is `unavailable`

- **Status:** Proposed (built as scorecard-source slice S4a, 2026-11-08). Becomes Accepted when the owner has read it. **Safety slice: no.** The record only feeds one row of a derived scorecard; it moves no cell, changes no verdict and places no order. It can only unblock an owner act that the owner's enforce switch (OFF by default, ADR 0221) already governs.
- **Date:** 2026-11-08.
- **Relation to other ADRs:** completes row 15's P3-09 term left `unavailable` by [0221](0221-the-readiness-scorecard-is-derived-and-read-only-unavailable-is-a-fail-and-the-owners-enforce-switch-off-by-default-makes-the-two-lifecycle-gates-refuse-instead-of-warn.md); follows the insert-only, fresh-password owner-act pattern of the same ADR (the enforce switch). Records the owner's decision of 2026-11-07.

## Context
Row 15 asks that the P3-09 20-trade check passed. The check's result is printed by `xd simulate verify-pack --gate` (PASS, PASS_WITH_DATA_FINDINGS, RERUN_NEEDED, DATA_REPAIR_NEEDED, BLOCKED_DEFECT) and was kept only in the owner's signed record, so the scorecard could not read it.

## Decision
1. **A new insert-only table `readiness.pack_gate_results`** (migration `AddPackGateResults`, tested up and down): strategy id, version, pack id, the gate's status in its own spelling, trade count, actor (`owner:%` by a check constraint), reason, and the database-stamped id (drawn under an advisory lock, so the highest id is the last to commit) and time. The runtime role has SELECT and INSERT only.
2. **The write is an owner act:** `POST /api/confidence/pack-gate-results` with the session, the CSRF token and a fresh password in the body (403 `auth.step_up_required` without one, 401 with a wrong one, sign-in rate limit). The actor comes from the session, never from the body. The handler also checks that the strategy version exists. The server cannot read the owner's pack folder, so the record is the owner's claim; it is audited by actor and time.
3. **The reader:** the newest record of the strategy version decides. A `PASS` over at least 20 trades is a pass; any other status (or a short pack) is a fail; no record, no version, or a record that cannot be read is `unavailable`, which counts as a fail. A later FAIL after a PASS makes the row fail again (a rerun is a new record).
4. `IReadinessGaps` loses `TwentyTradeCheckPassedAsync` (two methods left: row 13 and the risk share); an architecture pin forbids its return. The scorecard reader takes the new `PackGateResultReader` instead.

## Consequences
The owner must record a PASS per strategy version for row 15 to pass (and the P10 phase check and the edge-series row still apply). Recording a PASS is a manual step after running the gate; a later automatic recorder would need a new decision. No CLI verb was added: the endpoint is the act (the existing `xd` verbs here run without a session). Tests: Domain status rule, reader and handler with fakes, architecture pins, integration for the migration (up and down), the grants, and the endpoint (step-up, actor, refusals, row 15 states).
