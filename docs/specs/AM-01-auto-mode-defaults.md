# AM-01 — Auto-mode defaults: the evidence switches start ON where it is safe

- **Status:** Approved (owner, 2026-11-13, in chat: "Consider my approval and you do due diligence also consider mostly things should be in auto mode with best default values"). Approved for every switch in table A; table B stays the owner's, with the reason and what would unblock it.
- **Phase / roadmap row:** AM — Auto mode, row AM-01 (gap programme; includes GAP-09, the frozen evidence set).
- **Size:** about 3 agent-days in two parts:
  - **AM-01a** (1 d): the defaults of table A that need no new behaviour (services, the list filter, the kill switch).
  - **AM-01b** (2 d): the frozen evidence set (GAP-09), then the shadow record ON by default.
- **Needs:** CF-08, CF-11, CF-12, CF-10 (built), DATA-01 and OPS-01 (built). Table A rows marked HO-01, SR-03 and CF-10e are built in those slices, not here.
- **Safety:** light, SAFETY for AM-01b (evidence records). No order, no size raised, no rule loosened. Opus reviewer for both parts.
- **Design refs:** [ADR 0282](../adr/0282-auto-mode-defaults-follow-the-owners-instruction-of-2026-11-13-on-where-no-evidence-is-lost-and-no-risk-is-widened.md) (the rule); docs/design/17-deployment-and-operations.md (switch list); CLAUDE.md standing rules; ADR 0211, 0219, 0274, 0278.

## Goal
After the next deploy the evidence loops run by themselves: the shadow record writes, the monitors run, the hold-out looks and releases follow their rules, and the live plan uses the dated ban and surveillance lists it has. The owner's one act is Auto. Nothing that needs his terms, secrets or quota, or that could lead to an order, turns on by itself.

## The rule (ADR 0282)
- **ON** when no evidence is lost and no risk is widened: no order, no larger size, no looser rule, no model or learned value steering a ticket, no fetch under terms the owner has not accepted.
- **OFF / owner** when ON would forfeit evidence irreversibly, needs the owner's terms, secrets or account quota, widens risk, or is near an order.
- **Fences unchanged:** fail closed; `unavailable` never passes and never triggers an act; automatic acts are recorded `auto` with the settings snapshot; a change in either direction stays a fresh-password owner act.
- **Storage:** configuration → code and compose default `true`; a row switch → a seed row `enabled = true`, actor `system:default`, reason `ADR 0282` (no row still OFF); a service → enabled at first start, and a migration enables it once only where the owner never changed it.

## Inventory (main at 2918c76e)

### A. Default ON (approved)
| Switch | Today | Where | Why ON is safe | Built in |
|---|---|---|---|---|
| `Confidence:HoldOut:AutoRelease` (new) | — | HO-01 D4 | Releases only a sealed block with a fresh look and no look still available; an unlooked block stays the owner's. | HO-01a/b |
| `Confidence:HoldOut:AutoUnlock` | OFF (`Domain/Confidence/HoldOutOptions.cs:15`) | CF-06 D6, HO-01 D10 | Takes the strategy look by a fixed rule (pre-dated version, every development row a pass) at the seal; removes the choice after seeing forward results. Blocks k ≥ 2 only; block 1's look stays the owner's act. | HO-01a (default), HO-01b (seal step) |
| `Confidence:HoldOut:Enforce` | OFF (`HoldOutOptions.cs:15`) | CF-06 | A tightening: an explicit Lab window into the block in force is refused. Nothing is lost: released blocks are development. | HO-01b (with the deploy of 01a and 01b; HO-01a keeps it OFF, coordinator decision 2026-11-13) |
| `replay.holdout.auto_register` (new) | — | SR-03 D5 | Registers the system as it stands on the last evening before a block begins; fixed before any data of the block exists. | SR-03 |
| `replay.holdout.auto_run` (new) | — | SR-03 D5 | Runs a registration fixed before the block began, once, at the seal. | SR-03 |
| `ml.holdout.auto_score` (new) | — | CF-10e D5 | Takes the model look by a fixed rule at the seal, own slot per block. | CF-10e part 3 |
| `holdout-epochs` service (new) | — | HO-01 D10 | Acts only through the settings above. | HO-01b |
| `Selection:ListFilters:Enabled` | OFF (`Infrastructure/Market/Lists/ListSettings.cs:13`; `deploy/compose.yaml:152`) | DATA-01 | A tightening: a dated ban or surveillance row rejects a candidate; no row reads `unavailable` as today. Uses stored rows only, no fetch. The ASM/GSM rows come from the Dhan master refresh, which is already ON. | AM-01a |
| `confidence.kill.auto` | OFF, no row (`Infrastructure/Confidence/KillSettings.cs:8`) | CF-12 | Applies only pauses (risk down); proposal-only kinds stay proposals; re-enabling stays the owner's password act (ADR 0219). A kill switch that waits for the owner is late. | AM-01a |
| `ml-monitor` service | paused (`Application/Learning/MlMonitorService.cs:26`) | CF-10 | Reads and stores one monitor row a day; changes nothing. Feeds the calibration alarm. | AM-01a |
| `readiness-snapshot` service | paused (`Application/Confidence/ReadinessSnapshotService.cs:23`) | CF-11 | Stores a derived score; moves no cell, raises no size. | AM-01a |
| `decay-monitor` service | paused (`Application/Confidence/DecayMonitorService.cs:25`) | CF-12 | Writes proposals and notices; acts only through `confidence.kill.auto` (pauses only). | AM-01a |
| `learned-settings` service | paused (`Application/LearnedSettings/LearnedSettingsService.cs:25`) | AU-10 | Applies a tightening only while Auto is ON; a loosening is always a proposal for the owner's password. | AM-01a |
| `paper-capture` service | paused (`Application/Paper/PaperCaptureService.cs:24`) | P10-01 | Records paper fills of armed tickets of paper accounts; with no paper account it does nothing. It keeps the paper record current instead of waiting for the owner. | AM-01a |
| `paper-review` service | paused (`Application/Paper/PaperReviewService.cs:27`) | P10-03 | Writes the weekly paper review; read only on trading. | AM-01a |
| `paper.shadow.enabled` | OFF, no row (`Infrastructure/Paper/ShadowSettings.cs:7`) | CF-08 D7 | Writes candidates before the open and grades them after; nothing reads them for selection (CF-08 D6). Every night OFF is forward evidence lost for good. Needs the frozen evidence set first (GAP-09, AM-01b). | AM-01b |
| `shadow-write`, `shadow-grade`, `shadow-digest` services | paused (`Application/Shadow/ShadowWriteService.cs:24`, `ShadowGradeService.cs:25`, `ShadowDigestService.cs:23`) | CF-08 | As above. | AM-01b |

### B. Stays OFF or the owner's act
| Switch | Default | Why not ON | What would unblock ON |
|---|---|---|---|
| `auto.enabled` (Auto) | OFF (`Application/AutoSwitch/AutoSwitchService.cs:41`) | The owner's one act: it starts the daily pipeline with his broker token and the nightly tickets he may act on. Everything in table A runs under it without more acts. | The owner turns it ON. |
| `Data:History:Nse:Enabled`, `Data:History:Lists:Enabled`, `Baskets:System:Fetch:Enabled`, `Baskets:System:Lists:FetchEnabled`, the two user agents | OFF (`deploy/compose.yaml:142, 147, 149, 151`) | Fetches from NSE: the terms reading and the allow-list are the owner's (ADR 0195, 0256, 0274). | The owner accepts the terms and edits the allow-list; then ON. |
| `Data:History:Backfill:Enabled` | OFF (`deploy/compose.yaml:144`) | Heavy use of the Dhan Data API on the owner's subscription (rate and quota). | The owner confirms his plan's limits; then ON is recommended (more history is more evidence). |
| `Data:History:Replay:AdmitVerified` | OFF (`deploy/compose.yaml:145`) | Needs backfilled bars, and changes every research sample and run key. | After the backfill: recommended ON (research decision). |
| Depth, tick, option-chain, opening-bars recorders, options backfill | OFF (`deploy/compose.yaml:250-256`; `OpeningBars:Enabled`) | Each needs a Dhan feed connection or quota and disk; the owner's account limits. | The owner confirms connections and disk; ON is recommended, since P13 needs months of recordings from the day they start. |
| `ml.influence.enabled`, `ml.auto_promote` | OFF (`Domain/Learning/MlSwitch.cs:10, 13`) | ON would let a model steer, or change the champion: a widening. | Never by default. |
| `ml.auto_rollback` | OFF (`MlSwitch.cs:16`) | Protective, but the champion is frozen in the paper epoch, so a rollback restarts the count, while no consumer of the model exists to protect (the gate's consumers are pinned empty). | A consumer of the model; then ON. |
| `Confidence:Registration:Enforce` | OFF (`Domain/Confidence/RegistrationOptions.cs:9`) | Adds a manual registration step before every full or robust run; the `not_registered` label already shows. | An automatic registration path. |
| `Confidence:Registration:CountInFactor` | OFF (`RegistrationOptions.cs:9`) | System replays and sweeps are counted looks (`TrialCounts.SystemReplays`), so ON would tighten a family's Lab gates with every research replay. Not calibrated. | A calibration of which looks should tighten the gates. |
| `Confidence:CostStress:Enforce`, `Confidence:Nulls:Enforce` (and the placebo and mirror gates) | OFF | Report-only by design until calibrated; cost stress reads `unavailable` until ISS-25 closes, so ON would cap every PASS. | ISS-25 closed and a calibration. |
| Ticket auto-approval and reserve promotion (`AutonomySettings`, `Domain/Autonomy/AutonomySettings.cs:13`) | OFF | Approves trade tickets; the owner's P7 decisions (Advisory, no reserve promotion); near an order once P11 starts. | The owner's decision. |
| Live and execution switches (`Live__Writes`, `Live__Capture`, `Live__Estimates`, `Execution__ReadinessLiveEnabled`; `deploy/compose.yaml:128-133, 293`) | OFF | Near an order. | Never by default. |
| `Planning:OpenBook:UsePaperLedger`, `UseSelection` | OFF | P10 configuration choices that restart the paper count; the owner's pre-flight. | The owner's P10 pre-flight. |
| Ticket alerts, the assistant model, the P7 ticket services (`preopen`, `plan-expiry`, `review-cutoff`, ticket prompts), briefing, proposals, filings and system-basket fetches | as today | Not evidence switches, or they follow Auto, or they fetch under owner terms. | Out of this spec. |

## GAP-09: the frozen evidence set (AM-01b)
**Decision.** When an edge epoch of the shadow record starts (the first ON, and each later P10-05 epoch restart, CF-08 D7), the strategy versions of the eligible cells (the `cells.eligible` keys of the paper fingerprint, `Application/Paper/ConfigSnapshotSource.cs:175`) are registered as that epoch's evidence set, `auto`, and frozen.
- The owner may replace the set (fresh password) until the first shadow write of the epoch, never after.
- A shadow row whose version hash is outside the set is still written and graded, and is labelled `outside_set`.
- The edge count (CF-08 D5) and the readiness forward rows (CF-11) count only rows inside the set.
- The set is a row in `lab.test_ledger` (`trial_kind = 'evidence_set'`) and a row in a new insert-only `paper.evidence_sets(id, edge_epoch, versions jsonb, set_hash, mode, actor, created_at, first_write_at)`.

*Why this default:* it is the roadmap's recommendation ("the versions you would trade"), made by a fixed rule before any forward result of the epoch exists. A set chosen after results would be a look. *Alternative:* the owner names it by hand; kept as the replace act.

**Then the shadow default.** The same part inserts the seed row `paper.shadow.enabled = true` (actor `system:default`) and enables the three shadow services. The first write registers the set first, in the same transaction.

## Acceptance criteria
1. **Services (AM-01a).** On a database where the owner never changed them, `ml-monitor`, `readiness-snapshot`, `decay-monitor`, `learned-settings`, `paper-capture` and `paper-review` are enabled after the migration, each with an audit row `system:default`. A service the owner paused stays paused.
2. **List filter (AM-01a).** With no variable set, `podman compose config` renders `Selection__ListFilters__Enabled=true`; the other eight OPS-01 variables stay `false` or empty (`EvidenceComposeRulesTests` updated). A stored plan input with a dated ban row for one candidate rejects it; with no row the outcome is `unavailable`, as today.
3. **Kill switch (AM-01a).** After the migration `confidence.kill.auto` reads ON from one row `system:default`; with the table emptied it reads OFF. A firing kill pauses the cells as `auto`; a proposal-only kind stays a proposal; re-enabling still needs the owner's password.
4. **Evidence set (AM-01b).** On the first shadow write of an edge epoch, one set is stored with the eligible versions, `auto`, before the entries, in the same transaction. A replace before that write succeeds with a fresh password; after it, 409 `paper.evidence_set_frozen`. Rows outside the set are labelled and left out of the edge count and the readiness forward rows.
5. **Shadow default (AM-01b).** After the migration the switch reads ON from the seed row, the three services are enabled, and the next evening's write stores entries (or a `gap` row with the reason when there is no plan). Turning it OFF still needs a fresh password.
6. **Fences.** No new reference to `XDrishti.Execution`; CF-08's "no selection on shadow" architecture tests stay green; every automatic act carries `auto` and the snapshot.
7. **Table B unchanged.** A test pins the defaults of table B (OFF).

## Contract
- **Data:** AM-01a migration `ApplyAutoModeDefaults` (seed row for `confidence.kill.auto`; service enables where the owner never acted). AM-01b migration `AddEvidenceSets` (`paper.evidence_sets`, insert-only; `trial_kind` gains `evidence_set`; seed row for `paper.shadow.enabled`).
- **API (AM-01b):** `GET /api/paper/shadow/evidence-set`; `PUT /api/paper/shadow/evidence-set` (fresh password) with `{ versions[], reason }`; `gen-api`.
- **Config:** `XD_SELECTION_LIST_FILTERS_ENABLED` default `true` in `deploy/compose.yaml`; the code default of `ListSettings.Enabled` true.
- **UI (AM-01b):** the Shadow tab shows the set, its state (open or frozen) and the replace dialog; the Auto screen's evidence block shows the new states.

## Test plan
- **Unit:** the evidence-set rule; the defaults table (A ON, B OFF).
- **Integration:** both migrations on a database with and without owner changes; the seed rows; the first write and its set; the replace refusal; the list filter on and off.
- **Architecture:** the fences of AC 6.
- **Frontend (MSW):** the set card and dialog.

## Risks
- **More runs each evening.** The monitors and shadow services add work after the close; each is bounded and logged.
- **A default can surprise.** The owner reads the new defaults in docs/design/17 and on the Auto screen before the deploy.
- **The kill switch may pause a good strategy on a false alarm.** Re-enabling is one owner act; the alarm and its figures are in the notice.

## Done when
- [ ] Every AC has evidence; `scripts/check.sh` green; Opus reviewer: no blockers or majors.
- [ ] Migrations added; `gen-api` run (AM-01b).
- [ ] docs/design/17, deploy/CLAUDE.md and the Auto screen text list the new defaults; ADR 0282 linked from the roadmap.
