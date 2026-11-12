# OPS-01 — Make the forward-evidence switches reachable

- **Status:** Built 2026-11-12, not deployed (approved: coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended).
- **Phase / roadmap row:** OPS — operations, row OPS-01 (new; GAP-05)
- **Size:** about 1.5 agent-days (estimate) · **Needs:** CF-08 (shadow switch, ADR 0211/0212), AU-17 (Auto, ADR 0252), CF-04 (history switches, ADR 0195/0270/0273), P8-19 (owner-act form pattern)
- **Safety:** SAFETY light. It adds a UI for an existing owner act and compose variables for existing switches; every default stays OFF, no rule is loosened, nothing places an order.
- **Design refs:** docs/design/17-deployment-and-operations.md; ADR 0211, 0212, 0252, 0195, 0270, 0273

## Goal
The owner can switch the shadow record, the history fetches and the verified pre-seam admission without hand-editing code or calling the API by hand, and the Auto screen shows which evidence services are running.

## Verified facts (main at 30777841)
- The shadow switch is API only. The Paper → Shadow tab prints `Shadow record switch: ON/OFF` with no control (frontend/src/features/paper/components/ShadowTab.tsx:59). The route is `PUT /api/paper/shadow/settings` (backend/src/XDrishti.Api/Endpoints/PaperShadowEndpoints.cs:17,23), body `ShadowSwitchRequest(Enabled, Reason, Password, AcknowledgeGap)` (ShadowSwitchRequest.cs:8), fresh password on every change, errors `paper.shadow_gap` and `paper.shadow_digest_unwritable` (PaperShadowEndpoints.cs:24).
- `Data:History:Nse:Enabled`, `Data:History:Backfill:Enabled` and `Data:History:Replay:AdmitVerified` are configuration only, default false (backend/src/XDrishti.Infrastructure/Market/Eod/HistoryOptions.cs:9,41,47,79). `deploy/compose.yaml` sets only `Data__History__ProbeFolder` (compose.yaml:138-139); none of the three has a variable.
- **Also unreachable (beyond the review):** `Baskets:System:Fetch:Enabled` (BasketFetchOptions.cs:13,16), `Baskets:System:Lists:FetchEnabled` (BasketListOptions.cs:11,14) and the two `UserAgent` settings (BasketFetchOptions.cs:20; HistoryOptions.cs:56) have no compose variable either, yet the runbook needs the Nifty 500 basket and the ban list (ADR 0254, 0256).
- The compose pattern exists: `Options__Backfill__Enabled: ${XD_OPTIONS_BACKFILL_ENABLED:-false}` (compose.yaml:243), `XD_DEPTH_ENABLED`, `XD_TICKS_ENABLED` (compose.yaml:237,239).
- The Auto chain is exactly `eod`, `portfolio-broker-history`, `weekly`, `nightly`, `suggestions` (docs/adr/0252-…md:11). The evidence services `shadow-write` (ShadowWriteService.cs:16), `shadow-grade` (ShadowGradeService.cs:17), `shadow-digest` (ShadowDigestService.cs:15), `paper-capture` (PaperCaptureService.cs:16), `history-eod` (HistoryEodService.cs:16) and `history-backfill` (HistoryBackfillService.cs:18) are outside it. The shadow switch ON only *allows* `shadow-write` and `shadow-grade`, which start paused (PaperShadowEndpoints.cs:24).

## Decisions (each as recommended)
1. **Should Auto also resume the evidence services? No.** They stay separate switches, shown on the Auto screen. ADR 0252 §1 fixes the chain and "OFF restores only what ON resumed"; adding forward-evidence services to it would make one switch start the irreversible forward count (ADR 0211) and blur which act started it.
2. **Shadow form = the P8-19 owner-act pattern:** a dialog on the Shadow tab with reason, fresh password, and an "acknowledge the gap" checkbox shown only after a 409 `paper.shadow_gap`. Same route; no new endpoint.
3. **Compose variables, all default false/empty:** `XD_HISTORY_NSE_ENABLED`, `XD_HISTORY_NSE_USER_AGENT`, `XD_HISTORY_BACKFILL_ENABLED`, `XD_REPLAY_ADMIT_VERIFIED`, `XD_BASKETS_FETCH_ENABLED`, `XD_BASKETS_LISTS_FETCH_ENABLED`, `XD_BASKETS_FETCH_USER_AGENT`. A variable is configuration, not an owner act with a password: that matches the existing `XD_OPTIONS_BACKFILL_ENABLED` and is acceptable because these gate fetches, not decisions.
4. **"Evidence services" block on the Auto screen, read only:** each of the six services above with its state (running / paused / missing / unavailable) and a link to the Services screen; the shadow switch state; `Data:History:*` states. No control on it.

## Scope
**In:** the Shadow tab form; seven compose variables and their docs line in docs/design/17-deployment-and-operations.md and deploy/CLAUDE.md; the read-only block on `/auto`; tests.
**Out:** changing the Auto chain; any new switch; changing what the shadow switch allows; a UI for the history switches (configuration stays the place).

## Acceptance criteria
1. The Shadow tab shows "Switch on" / "Switch off"; submitting sends `PUT /api/paper/shadow/settings` with `enabled`, `reason`, `password`; the password field is cleared on every outcome and never stored.
2. A 409 `paper.shadow_gap` shows the gap text and an "acknowledge the gap" checkbox; resubmitting sends `acknowledgeGap: true`. 409 `paper.shadow_digest_unwritable`, 401, 423, 429 each show their own text.
3. With no variable set, `podman compose config` renders all seven settings as `false` or empty (a `ComposeRules` test asserts the defaults).
4. `XD_HISTORY_BACKFILL_ENABLED=true deploy/xd-up.sh` gives `xd-api` `Data__History__Backfill__Enabled=true` and nothing else changes.
5. `/auto` shows the six evidence services with their stored state; a service the API does not list reads `missing`; a failed read reads `unavailable`, never `running`.
6. Turning Auto ON or OFF changes none of the six (integration test over ADR 0252's handler).

## Contract
- **API:** none new (`PUT /api/paper/shadow/settings`, `GET /api/services`, `GET /api/auto` exist). No migration.
- **UI:** Paper → Shadow tab form; `/auto` block "Evidence services" (loading, empty, error states).

## Test plan
Frontend (MSW): form happy path, each problem code, gap acknowledgement, password cleared. Compose rules test for the seven defaults. Integration: Auto ON/OFF leaves the evidence services as they were.

## Risks
An owner who sets a compose variable may forget it; the Evidence services block and EV-01 preflight show the state. UserAgent values are free text: never logged in full.

## Done when
- [ ] ACs 1–6 have evidence; `scripts/check.sh` green; `scripts/gen-api.sh` unchanged; reviewer: no blockers
- [ ] docs/design/17 and deploy/CLAUDE.md list the variables

## As built (2026-11-12, branch feature/ops-01 on LC-01)
- **Shadow form:** `ShadowSwitchDialog` on the Paper → Shadow tab ("Switch on…" / "Switch off…"): reason, fresh password, the "I acknowledge the gap" checkbox shown only after a 409 `paper.shadow_gap`; 401, 423, 429 and `paper.shadow_digest_unwritable` each have their own words; the password is cleared after every try. Same route, no new endpoint (AC 1, 2).
- **Compose:** nine variables on `xd-api`, the seven of decision 3 plus `XD_HISTORY_LISTS_ENABLED` and `XD_SELECTION_LIST_FILTERS_ENABLED` (DATA-01's follow-up); all `false` or empty; `EvidenceComposeRulesTests` asserts each default and that each key appears once (AC 3, 4). Documented in docs/design/17 and deploy/CLAUDE.md. The two DATA-01 settings bind to nothing until that story lands.
- **Auto block:** `EvidenceServices` on `/auto` from `GET /api/services`: shadow-write, shadow-grade, paper-capture, paper-review and cells-refresh read running (enabled), paused or missing; a failed read shows the error state, never running; links to Services and to the Paper Shadow tab; "Auto does not resume these" (AC 5).
- **Deviations:** no read endpoint was added (none needed), so `Data:History:*` states are not on the block (configuration stays the place; EV-01 preflight shows them). The block lists five services (shadow-write, shadow-grade, paper-capture, paper-review, cells-refresh), not the spec's six (shadow-digest, history-eod, history-backfill are not shown; cells-refresh and paper-review are added). AC 6 (Auto ON/OFF leaves these services unchanged) rests on ADR 0252's fixed chain; no new integration test was added.
- **ADR 0278** records the decision.
