# LC-01 — Scheduled cell-statistics refresh

- **Status:** Built 2026-11-12, merged and deployed 2026-11-13 (approved by the coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). **Roadmap open decision 9 answered by the owner on 2026-11-13: option (a)**, built as slice DEC9 (see *Decision 9*).
- **Phase / roadmap row:** LC — Learning loop, row LC-01 (gap GAP-01 of the due-diligence review of 2026-11-12).
- **Size:** about 2.5 agent-days (service and handler 1, owner-act resume and screen 0.5, notice, gate text, Auto chain, paper list and ledger kind 0.5, tests 0.5).
- **Needs:** nothing unbuilt (P6-01, P7-08, CF-06, CF-08, P10-05, AU-02, AU-11, AU-17 are built). HO-01 (Approved 2026-11-13, not built) is what lets new sessions in; until then the behaviour is the one under "Before HO-01".
- **Safety:** light. It writes a decision input (the snapshot the nightly ranks cells with) with no human in between, so: paused by default, resuming is a fresh-password owner act, it fails closed on every missing input, it never stores a partial snapshot, it leaves the gate's rule unchanged, and it has no order, approval or arming path.
- **Design refs:** docs/design/09-learning-engine.md; ADRs 0064, 0086, 0207, 0240, 0243, 0252; specs P6-01, P7-08, CF-08 D6, P10-05 AC 11a. Decision record: [ADR 0277](../adr/0277-the-cell-statistics-refresh-repeats-the-newest-snapshot-to-the-hold-out-cut-fails-closed-and-resumes-only-with-the-owners-password.md), amending ADR 0252 §1.

## Goal
With Auto ON, the nightly plan keeps running after its first week. A service recomputes the cell statistics every session from stored research runs, so the selection-inputs gate sees a snapshot as of the last closed session. When the service could not run, the gate's reason says why.

## Verified facts (main at 30777841)
- Only the CLI writes cell statistics. `ComputeCellStatsCommand` is handled only for `xd learn cells` (`Api/Cli/LearnCellsCommand.cs:30-31`) and in tests. `WeeklyService` states it computes none (`Application/Learning/WeeklyService.cs:9-10`).
- The gate. `SelectionInputsGate` reads `ICellStatsStore.NewestAsOfAsync` (`Application/Planning/NightlyGates.cs:199-206`).
  - No snapshot gives `unavailable` (`:120-123`).
  - Age above `NightlyRules.MaxCellStatsAgeSessions = 5` fails (`Domain/Planning/NightlyRules.cs:16`, `NightlyGates.cs:130-133`).
  - Auto ON resumes `nightly` (`Application/AutoSwitch/AutoSwitchService.cs:43`), so the plan fails closed on the 6th session after the last manual `xd learn cells`.
- The handler is idempotent. The snapshot key is the SHA-256 of the as-of date, the options and the sorted sources (`ComputeCellStatsHandler.cs:92-98, 126-134`), and it reads closed trades of named runs through `IRunStore` (`:42`), which is fenced to `purpose = research` (ADR 0243, AU-11 §1).
- Recency weighting. A trade's weight is `0.5^(days/half-life)` (`Domain/Learning/CellStatsCalculator.cs:166`), with defaults of 180 days half-life, prior strength 20 and 30 minimum trades (`CellStatsOptions.cs:10`). `Enough` counts raw trades (`CellStatsCalculator.cs:237`).
- A stored run is simulated and kept through `RunSimulationQuery` + `StoreRunCommand`, idempotent by run key (`SimAll/SimAllChunkExecutor.cs:86-97`, there with `RunPurpose.Training`).
- The hold-out cut. Readers take `IHoldOutStore.CurrentAsync().Start` (`Lab/LabHoldOutGuard.cs:35`, `Replay/ClampedBars.cs:36`, `Learning/SharedHoldOut.cs:10`). The training and replay cut is a further `HoldOut:TrainingEmbargoDays` (default 30) earlier (`Domain/Confidence/HoldOutOptions.cs:15`).
- Services.
  - A resume needs a session only (`Api/Endpoints/ServicesEndpoints.cs:82-84, 194-200`).
  - A paused service in a `MustFollow` blocks its dependents in a recovery pass (`Domain/Platform/Services/RecoveryPlanner.cs:79-82, 124-128`).
  - `LatestOnly` supersedes older occurrences and makes one step for the latest (`:155-165`).
- Paper governance. A new snapshot key is a `frozen` change that restarts the paper count (P10-05 AC 11a). Blockers are listed by id in `PaperConfigKeys.LearningWriterServices = ["cells","retrain","weekly"]` (`Domain/Paper/PaperConfigKeys.cs:70`), so a service called `cells-refresh` would not be caught.
- CF-08 D6: cell statistics do not ingest shadow grades. `ShadowArchitectureTests.cs:23-29` pins Planning, Selection and Autonomy, but not Learning.

## Decisions (each as recommended)
- **D1. The service.** Its definition:
  - id `cells-refresh`, host `xd-api`, schedule `After eod`, `NotBeforeSessionEnd`;
  - first start paused;
  - recovery class `LatestOnly`, catch-up `SameDay`.

  *Why LatestOnly:* one run writes one snapshot as of the latest closed session, and the gate reads only the newest. That is ADR 0240's `LatestOnly`. The review proposed `Convergent`, which gives the same single step but would report older occurrences as recovered. **Not** in `nightly`'s `MustFollow`: a paused refresh would then block the nightly's recovery. The stage order and the gate are the check.
- **D2. What it recomputes.**
  - **Template:** the newest snapshot (its sources and options; the regime and universe splits are read from its rows). It never invents a source set. With no snapshot it fails with `cells_refresh.no_snapshot`: the first snapshot is the owner's `xd learn cells`.
  - **Per source run:**
    - Its version has a CF-06 registration and its window ends before the refresh end: the exact request is re-simulated (strategy hash, parameters, timeframe, exits, universe, cost choice, start), with only the end moved. It is stored as `research`.
    - Otherwise the run is kept and labelled `kept: up_to_date` or `kept: not_registered`.
  - **Compute:** then `ComputeCellStatsCommand(asOf = last closed session, sources', options, "system:cells-refresh", splits)`.

  *Alternative rejected:* re-simulate the active versions over the auto scope. That changes the sample a cell earned on (a re-plan in CF-06 D2 terms), and it creates or drops cells silently. A newer version is never swapped in: that is a new trial and the owner's act.
- **D3. Refresh end** = `min(last closed session, block start − 1 day − HoldOut:TrainingEmbargoDays)`. With no block, it fails with `cells_refresh.no_holdout` (fail closed, as training does). *Alternative:* the Lab cut (block start − 1 day), which gives 30 more days. Rejected because the snapshot reaches decisions unattended, so the stricter cut applies.
- **D4. Counted.** Each newly stored refresh run appends one `lab.test_ledger` row with `trial_kind = 'cells_refresh'`.
  - It is in `Total`, but it is not a variant hash. It is idempotent by run key.
  - A re-run that is already stored adds nothing.
  - *Why:* CF-06 D2 makes a changed window a new trial. There is at most one row per source each time the cut moves.
  - *Alternative:* list it beside the total, not in it, as `training_sweep` is.
- **D5. All or nothing.** Any source that fails to simulate or store, or a failed compute, ends the run `failed` with the code, and no snapshot is stored. Nothing re-stamps on failure, so a failure never turns into a stale pass.
  - The gate's rule is unchanged.
  - When it fails for age, its reason adds the newest `cells-refresh` state: `paused`, `failed on <date>: <code>`, or `never ran`.
- **D6. Owner control.**
  - **Resume:** an owner act. `ServiceConstraints.ResumeIsOwnerAct` (new, true only here) makes `POST /api/services/cells-refresh/resume` require a fresh password. Every other service is unchanged. Pause needs a session (it lowers risk).
  - **Auto chain:** it joins the chain, which becomes six services (ADR 0252 §1 amended). Auto ON, already a fresh-password act, resumes it and records it as `auto` in `planning.auto_switch_changes`. OFF pauses it only if ON resumed it.
- **D7. Paper epoch.** `cells-refresh` is added to `LearningWriterServices`, so while enabled during an epoch it shows as a blocker (each run is a `frozen` change). It does not pause itself.
- **D8. Notice.** A bell notice `cells_stale` is derived on read in `GetServiceNotices`, one per day.
  - It shows while `nightly` is enabled and the newest as-of is at least `Learning:CellsRefresh:NoticeAfterSessions` sessions behind the last closed session (default 3, range 1–4, Internal descriptor).
  - Text: "Cell statistics are N sessions old; the nightly plan fails at 6. Last refresh: <state>."
- **D9. Fence.** An architecture test pins that the types of `XDrishti.Application.Learning.CellsRefresh` name no `Shadow` or `Paper` type, and read runs only through `IRunStore` (CF-08 D6).

## Before HO-01
The cut is fixed (block start − 31 days). After the first run, which may extend a shorter source window once, every run re-stamps **the same trades** with a later as-of. This is not neutral:
- Weights shrink uniformly, so the posterior drifts toward the prior. Example: a cell of 60 trades with 36 wins, prior rate 0.5 and s = 20, with exits taken on one day for the arithmetic:
  - as of the exit day: (36 + 10)/(60 + 20) = **0.575**;
  - 180 days later: (18 + 10)/(30 + 20) = **0.560**;
  - 360 days later: (9 + 10)/(15 + 20) = **0.543**.
- `Enough` stays true (60 ≥ 30).
- The gate passes on statistics that are honest but not newer. Every run's message says "data through <refresh end>; no new trades can enter until a hold-out block is released (HO-01)".

## Scope
**In:**
- the service and `RefreshCellStatsHandler`;
- the resume flag, the request body and the Services-screen step-up dialog;
- the notice and the gate reason text;
- the Auto chain entry and the paper blocker entry;
- the ledger kind and the setting descriptor;
- the ADR, `gen-api` and tests.

**Out:**
- HO-01;
- any change to the gate's limit;
- new sources or versions;
- shadow or paper grades as a basis;
- retraining;
- deploy.

## Acceptance criteria
1. **Registration.** `cells-refresh` is registered and paused at first start, `After eod`, `LatestOnly` (a `RecoveryClassTableTests` row), and absent from `nightly.MustFollow`.
2. **Resume.**
   - With no password: 403 `auth.step_up_required` (nothing counted); with a wrong one: 401 `auth.invalid_credentials` (counted toward the lockout); the service stays paused in both.
   - With a fresh password: resumed, with an audit row.
   - Every other service resumes with a session, as today.
3. **Auto.** ON resumes and records six services. OFF pauses `cells-refresh` only if ON resumed it.
4. **Kept sources.** Fixture: the newest snapshot is as of 2026-11-05, with sources R1 (ORB, registered, window 2024-11-01..2026-04-04) and R2 (VWAP, not registered). The block starts on 2026-05-05 and the embargo is 30 days, so the end is 2026-05-05 − 1 − 30 = **2026-04-04**. When the run for the session of 2026-11-12 is made, then:
   - no simulation runs (R1 `kept: up_to_date`, R2 `kept: not_registered`);
   - a snapshot as of 2026-11-12 is stored with sources {R1, R2} and the options of the template;
   - the gate for 2026-11-12 passes;
   - no ledger row is written.
5. **Extended source.** With R1's window ending 2026-03-31, R1 is re-simulated over 2024-11-01..2026-04-04 with the same hash, parameters, exits, universe and costs. It is stored as research run R1′, with one `cells_refresh` ledger row, and the snapshot sources are {R1′, R2}. A second run on the same day stores no run, no snapshot and no ledger row.
6. **Fail closed.** No snapshot gives `cells_refresh.no_snapshot`, and no block gives `cells_refresh.no_holdout`. If one simulation fails, the run fails with its code, and no snapshot and no row are written. In every case the gate's pass or fail is exactly as today. A golden table: age 0 and 5 pass, 6 fails, none is unavailable. The age failure names the last refresh state.
7. **No look-ahead.** With every bar after the refresh end mutated, the snapshot key is unchanged.
8. **Notice.** With `nightly` enabled:
   - as-of 3 sessions behind: one `cells_stale` that day;
   - 2 sessions behind: none.

   With `nightly` paused: none.
9. **Paper.** With an epoch open and `cells-refresh` enabled, `GET /api/paper/config` lists it as a blocker.
10. **Fences.** Both hold, as architecture tests:
    - D9;
    - no `XDrishti.Execution` reference and no approval or arming port.
11. **Label.** The run message carries "data through <refresh end>" and the HO-01 sentence.

## Contract
- **Service:** as D1.
- **API:**
  - `POST /api/services/{id}/resume` takes an optional body `{ "password": string }`, required only when `ResumeIsOwnerAct`. A missing password is 403 `auth.step_up_required`, a wrong one 401 `auth.invalid_credentials`.
  - `ServiceView.resumeNeedsPassword: bool`.
  - `scripts/gen-api.sh` in the same change.
- **Data:**
  - Migration `AddCellsRefreshTrialKind` adds `'cells_refresh'` to `ck_test_ledger_trial`. Its `Down` refuses while such rows exist.
  - No new table: the service run holds the outcome (snapshot key, sources kept or extended, data-through), and idempotency is the run key and the snapshot key.
- **Settings:** `Learning:CellsRefresh:NoticeAfterSessions`, Internal, 3 (1–4).
- **UI:**
  - On the Services screen, the resume of a flagged service opens the existing step-up dialog, and its row says "needs your password to resume".
  - The bell notice.

## Test plan
- **Unit:** the refresh-end arithmetic, the source plan (kept or extended), the notice rule and the gate text.
- **Application with fakes:** ACs 4–7 and all-or-nothing.
- **Integration:** resume with and without the password, Auto ON and OFF, the migration up and down, the ledger row and the paper blocker.
- **Architecture:** the fence, no Execution, `RecoveryClassTableTests`, and the `SettingsRegistry` descriptor test.
- **Frontend (MSW):** the dialog for a flagged service; other services unchanged.

## Risks
- **The paper freeze cannot pass the gate (this conflict predates LC-01).** *Decided 2026-11-13: option (a), see* Decision 9. P10-00 D8 freezes the snapshot for a 12-week epoch, and the gate fails after 5 sessions. With the refresh on, the paper count restarts every session; with it off, the nightly fails. This needs an owner decision outside this slice. An epoch-pinned snapshot accepted by the gate would be a loosening and needs the password rule.
- **Before HO-01 a passing gate may look like learning.** The labels say otherwise.
- **One evening after a release can be long.** It may re-simulate up to 50 sources. The run is after `eod`, outside market hours.

## Shortcuts
- Sources are capped at the handler's 50.
- The splits are inferred from the template's rows.
- The gate limit stays a constant.

## Done when
- [ ] Every acceptance criterion has evidence.
- [ ] `scripts/check.sh` is green and the reviewer finds no blockers.
- [ ] `gen-api` is run and the migration is added.
- [ ] The ADR is written (amending 0252 §1), and doc 09 and doc 16 are updated.

## As built (2026-11-12)
Built on `feature/lc-01`, not deployed. Decisions D1 to D9 as written; ADR 0277 records them and amends ADR 0252 §1 (six services).
- **Owner constraint: roadmap open decision 9 (paper epoch against cell refresh) is NOT decided** (at build time; decided 2026-11-13, see *Decision 9*). The P10 paper-epoch rule (ADR 0119) is unchanged: a refresh remains a `frozen` change that restarts the paper count, and `cells-refresh` is only listed as a blocker while enabled (D7). The conflict in Risks stands until the owner decides.
- **Code.** Domain: `CellsRefreshRules`, `ServiceConstraints.ResumeIsOwnerAct`, `TrialCounts.CellsRefreshes`. Application (`Learning/CellsRefresh`): `CellsRefreshService`, `RefreshCellStatsHandler`, `CellsRefreshState`, `CellsRefreshOptions`, `ISourceRequestPlanner` (the replay planner proves the exact request). Resume body, `ServiceView.resumeNeedsPassword`, gate reason, `cells_stale` notice, Auto chain, paper blocker list, ledger kind (migration `AddCellsRefreshTrialKind`), setting `Learning:CellsRefresh:NoticeAfterSessions` (Internal, 1 to 4, default 3). `gen-api` run.
- **Deviations and shortcuts.** (1) A run with no snapshot fails before the hold-out is read, so with neither, the code is `cells_refresh.no_snapshot`. (2) Extra failure codes beyond the spec: `cells_refresh.not_rebuildable` (a source that needs extending and whose request cannot be proven, never guessed), `cells_refresh.source_failed`, `cells_refresh.template_unreadable`. (3) A source whose window ends after the refresh end is kept as `up_to_date` (no new failure code). (4) All simulations are made first and held in memory (up to 50) so a failed one stores nothing; the ledger row follows the store without a shared transaction, its id derived from the run key. (5) The frontend step-up dialog and the Services-screen text are not built here (frontend untouched; OPS-01 does the Auto screen); the API contract carries `resumeNeedsPassword`. (6) `MustFollow = [eod]` is declared on the service itself.
- **Review fixes.** The ledger row is appended for every extended source (idempotent by run key); the Services screen asks for the password in a dialog (`PasswordReasonDialog`, no reason) and `/api/services/{id}/resume` is a password-check path of the client, so a wrong password never signs out; more than 50 sources fails closed (`cells_refresh.too_many_sources`); one formula for the cut (`HoldOutOptions.TrainingCut`). Decision 9 follow-up: with an epoch open, Auto ON resumes `cells-refresh` and each run restarts the paper count; the owner decides this with decision 9.
- **Tests.** Domain rules; handler with fakes (ACs 4 to 6, 11, all or nothing); service definition, resume, gate text, state and notice (ACs 1 to 3, 6, 8); architecture fences (AC 10 and D9); integration for resume with and without the password, the paper blocker and the ledger kind (ACs 2, 9); `RecoveryClassTableTests` row.

## Decision 9 (owner, 2026-11-13)
**Answer:** option (a). The paper epoch freezes the strategy versions and the rules, but not the cell-statistics snapshot. The owner approved it in chat on 2026-11-13 with HO-01 and SR-03 ("Consider my approval and you do due diligence also consider mostly things should be in auto mode with best default values"). Options (b), keep the snapshot frozen and let Auto stop after 5 sessions, and (c), raise the age rule for the epoch, are not taken.

**What "paused" and "frozen" mean in the code today (main at 2918c76e):**
- `cells-refresh` is registered disabled (`Application/Learning/CellsRefresh/CellsRefreshService.cs:28`), so it is paused at first start. Its resume is a fresh-password owner act (`ServiceConstraints.ResumeIsOwnerAct`), and Auto ON resumes it (`Application/AutoSwitch/AutoSwitchService.cs:44`).
- The paper fingerprint carries the snapshot keys as one component, `cells.snapshot` (`Application/Paper/ConfigSnapshotSource.cs:179`). The key is in no table of `PaperConfigKeys`, so it falls through to `Frozen` (`Domain/Paper/PaperConfigKeys.cs:38, 233`). A new key between two captures starts a new epoch (P10-05 rule 4).
- `cells-refresh` is in `PaperConfigKeys.LearningWriterServices` (`:70`), so `GET /api/paper/config` lists it as a blocker while enabled (`Application/Paper/GovernanceQueryHandlers.cs:85-91`), with the text "enabled during an epoch: each run restarts the count" (`PaperConfigKeys.cs:73`).
- The gate's age rule is a constant of 5 sessions (`Domain/Planning/NightlyRules.cs:16`; `Application/Planning/NightlyGates.cs:131-133`).
- The P10-04 band reads the newest snapshot before the epoch start (`Application/Paper/BacktestReferenceReader.cs`), so it does not depend on later snapshots.

**What changes (slice DEC9, about 1 agent-day, SAFETY light, Opus reviewer):**
1. `cells.snapshot` is classified `free`: a new snapshot is a `noted` change and never restarts the count. `Describe` says "noted: a cell-statistics refresh (decision 9)".
2. A new component `cells.basis`, classified `frozen`: the SHA-256 of the options of the newest cash snapshots and their sorted source identities **without the window end** (strategy hash, parameters, timeframe, exits, universe, cost choice, start). A refresh only moves ends, so it keeps the basis; a manual `xd learn cells` with other sources or options changes it and starts a new epoch. So the rules that produce the statistics stay frozen; only the data moves. Its first appearance (no value before) is `free`, so the deploy itself does not restart an open epoch.
3. `cells-refresh` leaves `LearningWriterServices`; `cells`, `retrain` and `weekly` stay (a champion change is still `frozen`).
4. The epoch view and the P10-06 phase check add the caveat `cells_refreshed: N` (noted snapshot changes in the epoch). The P10-04 reference is unchanged.
5. The OPS-01 Auto block line for `cells-refresh` says "each refresh is noted in the paper epoch" instead of "restarts the paper count".
6. The age rule stays 5 sessions: with Auto ON the refresh keeps the snapshot inside it.
7. CF-08 D6 still holds: cell statistics never read shadow grades (LC-01 D9 fence). The parenthetical "the pre-flight keeps cell snapshots frozen in an epoch" no longer applies.

**Acceptance criteria (DEC9):**
- A refresh between two captures, same basis: one `free` change with effect `noted`, the same epoch, entries `measured`.
- A recompute with another source set: `cells.basis` changes, `frozen`, a new epoch.
- The first capture after deploy, with an epoch open: `cells.basis` added as `free`; the epoch continues.
- `GET /api/paper/config` with `cells-refresh` enabled: no blocker.
- The P10-04 output for a fixed fixture is byte-identical before and after (golden).
- The `PaperConfigKeys` reflection and classification tests are updated, not relaxed elsewhere.

**Records.** ADR **0286** (number reserved) amends ADR 0119 (the snapshot is no longer frozen) and ADR 0277 §7 and §11; P10-00 D8 and pre-flight step 4 and P10-05 criterion 11a and question 2 carry an amendment line.

**Honest cost.** The paper count now measures "the frozen versions and rules, with statistics refreshed from research runs", not one fixed snapshot. P10-05 question 2 had rejected that; the owner chose it on 2026-11-13 because the alternative stops the nightly plan after 5 sessions. After a HO-01 release, one refresh can add a block of trades; the epoch shows it as a noted change.

**DEC9 as built (2026-11-13, ADR 0286).** `cells.snapshot` is `free`; `cells.basis` is the SHA-256 of the snapshot options and, per source, the identity read from the stored record (definition hash, parameters, timeframe, run data options, csv flag, fills, cost choice with capital and profile, exit, time stop, universe, start, style and exit labels), without the window end; `cells-refresh` is no paper blocker; the epoch view shows `cellsRefreshed` (noted refreshes of captures without a frozen change) and the phase check adds `cells_refreshed: N`. A source that cannot be rebuilt makes the cells group unreadable (fail closed). Not in the basis: the regime and universe splits and the Confidence/Shrinkage options. Deploy notes: no `xd learn cells` across the deploy; check `GET /api/paper/config` for unreadable groups afterwards.
