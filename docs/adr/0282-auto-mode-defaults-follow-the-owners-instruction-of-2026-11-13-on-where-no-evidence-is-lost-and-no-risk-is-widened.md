# 0282 — Auto-mode defaults follow the owner's instruction of 2026-11-13: ON where no evidence is lost and no risk is widened

- **Status:** Accepted (owner, 2026-11-13, in chat).
- **Date:** 2026-11-13.
- **Relation to other ADRs:** Amends the default-OFF part of the standing approvals rule (CLAUDE.md, owner 2026-10-23) for the settings listed below. Amends the default of the auto options of [0207](0207-a-strategy-version-is-pre-registered-the-ledger-counts-every-extra-look-and-a-locked-hold-out-is-fixed-at-the-first-post-and-read-once-by-an-owner-unlock-with-enforcement-and-the-honest-count-as-the-owners-switches.md) (`HoldOut:AutoUnlock`, `HoldOut:Enforce`), [0211](0211-the-shadow-record-writes-every-nightly-candidate-before-the-open-forward-only-and-insert-only-chained-by-digest-as-a-count-separate-from-the-p10-paper-count-and-the-switch-is-the-owners-and-off-by-default.md) D7 (the shadow switch), 0219 (`confidence.kill.auto`), [0274](0274-the-live-plan-and-the-replay-read-the-fno-ban-and-surveillance-filters-from-dated-list-rows-through-an-as-of-port-and-a-dated-ban-file-backfill-is-an-off-by-default-guarded-fetch.md) (`Selection:ListFilters:Enabled`) and [0278](0278-the-forward-evidence-switches-are-reachable-the-shadow-form-nine-compose-variables-all-off-and-a-read-only-auto-block-that-resumes-nothing.md) ("a new default of `true` needs an ADR": this is it). Sets the defaults of the new settings of HO-01 (ADR 0283), SR-03 (ADR 0284) and CF-10e part 3 (ADR 0285). The full list and the reasons are in spec [AM-01](../specs/AM-01-auto-mode-defaults.md).

## Context
The standing rule of 2026-10-23 says: anything that needs a manual approval also has an auto option, the option defaults OFF, turning it on is a password owner act, an auto decision is recorded as auto with the setting snapshot, `unavailable` never passes, and no setting ever places an order. By 2026-11-13 about thirty switches ship OFF, and the evidence loops (shadow record, hold-out looks, releases, monitors) do not run until the owner flips each one. On 2026-11-13 the owner approved HO-01, SR-03 and decision 9 and added: "consider mostly things should be in auto mode with best default values".

## Decision
1. **The default of a switch is a researched default, ON where safe.** A switch defaults ON when both hold:
   - **no evidence is lost:** ON never forfeits a look, a block or a forward session that OFF would keep (taking a pre-registered look by a fixed rule uses evidence; it does not lose it);
   - **no risk is widened:** ON places no order, raises no size, loosens no rule, lets no model or learned value steer a ticket, and fetches nothing under terms the owner has not accepted.
2. **It stays OFF, or the owner's act,** when ON would irreversibly forfeit evidence (release an unlooked block), needs the owner's terms, secrets, passwords or account quota (NSE fetches, Dhan backfill, feed recorders), widens risk (ML influence, auto-promotion, ticket auto-approval), or is near an order (every live and execution switch, never by default).
3. **The fences do not move.**
   - No setting ever places an order.
   - Fail closed: an `unavailable` or unreadable input never counts as a pass and never triggers an automatic act.
   - An automatic act is recorded as `auto`, with the actor `system:<name>` and the settings snapshot hash, never as an owner approval.
   - Changing one of these settings, in either direction, stays a fresh-password owner act.
4. **How a default ON is stored.**
   - A configuration setting: the code default and the compose default become `true`.
   - A switch stored as rows (no row = OFF): a migration inserts one seed row `enabled = true`, actor `system:default`, reason `ADR 0282`. No row still reads OFF, so a lost table never turns anything on.
   - A service: its definition is enabled at first start. A migration applies the new default once to a service whose state the owner never changed; a stored owner choice is never overwritten.
5. **The defaults set ON** (each with its spec): `Confidence:HoldOut:AutoRelease` (new), `Confidence:HoldOut:AutoUnlock`, `Confidence:HoldOut:Enforce` (HO-01); `replay.holdout.auto_register`, `replay.holdout.auto_run` (SR-03); `ml.holdout.auto_score` (CF-10e part 3); `paper.shadow.enabled` with the services `shadow-write`, `shadow-grade`, `shadow-digest`, once the frozen evidence set exists (AM-01b, GAP-09); `Selection:ListFilters:Enabled`, `confidence.kill.auto`, and the services `ml-monitor`, `readiness-snapshot`, `decay-monitor`, `learned-settings`, `paper-capture`, `paper-review`, `holdout-epochs` (AM-01a, HO-01b).
6. **The frozen evidence set (GAP-09) gets an automatic default.** When the shadow record starts an edge epoch, the strategy versions of the eligible cells are registered as the epoch's evidence set in the trial ledger and frozen; the owner may replace the set before the first shadow write of the epoch, never after.
7. **Auto (`auto.enabled`) stays the owner's one act.** It starts the daily pipeline with the broker token; every evidence switch below it is ON so that this one act is enough.

## Consequences
- The evidence loops run from the first deploy after the builds, without the owner flipping each switch.
- `HoldOut:AutoUnlock` ON never claims block 1 (coordinator, 2026-11-13): block 1 has no pre-dating horizon and its look is irreversible, so its look (or its release with the forfeit) stays the owner's act; automatic looks start at block 2.
- `Selection:ListFilters:Enabled` ON changes the live plan wherever a dated ban or surveillance row exists (a tightening). If a paper epoch is open, the change is classed as P10-05 says.
- Tests that pinned the old defaults (`EvidenceComposeRulesTests`, the settings descriptor tests, the service definition tests) are updated to the new defaults, never deleted.
- docs/design/17 and deploy/CLAUDE.md list the new defaults at build time.
