# 0128 — Owner-act capabilities: one insert-only log, a generic gate by name, OFF by default, switched ON only by the owner with a fresh password

- **Status:** Proposed (spec [P8-18](../specs/P8-18-tweak-and-re-run-from-the-ui.md) Part A, approved by the owner on 2026-10-26; built 2026-10-26). Becomes Accepted when the owner has read it. **SAFETY slice: an owner act.**
- **Date:** 2026-10-26.
- **Relation to other ADRs:** [0127](0127-the-owners-confirmation-of-the-charge-sheet-is-an-insert-only-log-written-only-by-the-owners-endpoint-after-a-fresh-password-bound-to-the-rates-hash-and-off-by-default.md) (the pattern this generalises: insert-only log, lock, trigger, owner endpoint), [0091](0091-auto-approval-is-an-option-behind-an-owner-setting-default-off-a-new-approval-kind-the-database-checks.md) (fresh password, database checks), [0083](0083-no-order-is-placed-before-phase-12-an-architecture-tripwire.md) (no order).

## Context
P8-18 to P8-21 each add a write or an outward act to screens that were read-only. The owner's rule: anything that needs manual approval must also be switchable, every setting defaults OFF, turning one on is an authenticated owner act, and nothing places an order. Four slices need the same switch, so it is built once, by name.

## Decision
1. **One log.** `platform.capability_changes` (id, capability, enabled, reason 1-500, owner actor, database time, previous id, SHA-256 snapshot hash). Insert-only for `xd_app` (granted on this table alone), stamped by an insert trigger under an advisory lock (id after the lock, database clock, chain, hash; a chosen value is overwritten). The newest id of a name decides; no row means OFF.
2. **Names are code.** Five whitelisted constants (`experiments_ui`, `learning_moves_ui`, `autonomy_form`, `exports`, `pwa`); no check constraint, so a new capability is not a migration. An unknown name is OFF and cannot be switched (404).
3. **Generic gate.** `ICapabilityGate.RequireAsync(name)` answers 403 `capabilities.off`. It reads only. Endpoints ask it before the password; the handler asks it again; a job table may add a database trigger (Part B does).
4. **Switch.** `PUT /api/capabilities/{name}`: ON needs session, CSRF, rate limit and the password typed again for that act (`OwnerAct.AuthorizeAsync`, step-up window neither opened, extended nor used up); OFF needs session and CSRF only (the kill switch is never slower than the switch). Actor from the session, never the body. The attempt limit of the sign-in applies to switching ON only (a `SwitchOnLimiter` asked in the endpoint after the body is read); the OFF direction has no route limiter and never answers 429. Same state again writes nothing (`changed=false`); concurrent switches make one row.
5. **Only one writer.** `ICapabilityLogWriter` is used by `ChangeCapabilityHandler` only, called by `CapabilitiesEndpoints` only (architecture tests with sentinels). No setting, job, rule, verb or other endpoint can switch a capability.
6. **Audit.** `AccountEventType.CapabilityChanged` per real change (name and state; never reason or password), category Configuration. Log events 4900 to 4902.
7. **UI.** Lazy route `/owner-acts` (no navigation entry), a card per capability with state in words; other screens use `useCapability(name)` to show a write control or a plain link here. A shared test classifies every feature folder as writer or read-only (`writePaths.test.ts`).

## Consequences
- After the migration every capability is OFF; nothing in P8 can write until the owner acts.
- The log row and the audit event are two transactions: a crash between them leaves the log (the record) without the event.
- Limit: "owner" is a string with a database check; a process holding `xd_app` could insert an owner row. The controls are the single writer behind session, CSRF, rate limit and password, the insert-only grant and the architecture tests.

## Alternatives considered
- **`platform.app_settings`.** Rejected: updated in place, no history.
- **appsettings / environment.** Rejected: needs a restart and an edit outside the app.
- **One table per capability (as the charge sheet).** Rejected: four more copies of the same lock and trigger.
- **A password to switch OFF.** Rejected: a kill switch must be fast.
