# LC-01 — Scheduled cell-statistics refresh

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Not built.
- **Phase / roadmap row:** LC — Learning loop, row LC-01 (gap GAP-01 of the due-diligence review of 2026-11-12).
- **Size:** about 2.5 agent-days (service and handler 1, owner-act resume and screen 0.5, notice, gate text, Auto chain, paper list and ledger kind 0.5, tests 0.5).
- **Needs:** nothing unbuilt (P6-01, P7-08, CF-06, CF-08, P10-05, AU-02, AU-11, AU-17 are built). HO-01 (Draft) is what lets new sessions in; until then the behaviour is the one under "Before HO-01".
- **Safety:** light. It writes a decision input (the snapshot the nightly ranks cells with) with no human in between, so: paused by default, resuming is a fresh-password owner act, it fails closed on every missing input, it never stores a partial snapshot, it leaves the gate's rule unchanged, and it has no order, approval or arming path.
- **Design refs:** docs/design/09-learning-engine.md; ADRs 0064, 0086, 0207, 0240, 0243, 0252; specs P6-01, P7-08, CF-08 D6, P10-05 AC 11a. Decision record: a new ADR (the next free number at build time), amending ADR 0252 §1.

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
   - With no password or a wrong one: 401 `auth.invalid_credentials` (counted toward the lockout), and the service stays paused.
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
  - `POST /api/services/{id}/resume` takes an optional body `{ "password": string }`, required only when `ResumeIsOwnerAct`. A missing or wrong password is 401 `auth.invalid_credentials`.
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
- **The paper freeze cannot pass the gate (this conflict predates LC-01).** P10-00 D8 freezes the snapshot for a 12-week epoch, and the gate fails after 5 sessions. With the refresh on, the paper count restarts every session; with it off, the nightly fails. This needs an owner decision outside this slice. An epoch-pinned snapshot accepted by the gate would be a loosening and needs the password rule.
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
