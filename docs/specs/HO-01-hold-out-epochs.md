# HO-01 — Hold-out epochs: fixed blocks, one fresh look per kind, released blocks become development

- **Status:** Approved (owner, 2026-11-13, in chat: "Consider my approval and you do due diligence also consider mostly things should be in auto mode with best default values"). All eight questions as recommended, with the changes of the due diligence of 2026-11-13 (see *Due diligence*):
  - the stored block becomes block 1 of a chain of fixed 6-month blocks;
  - a sealed block is released by the owner (fresh password) or by the auto option, and becomes development, labelled `seen_holdout`;
  - a block has **one fresh look per kind** (`strategy`, `model`, `system`), each open only to a subject frozen before the block's horizon;
  - the auto release is **ON by default** ([ADR 0282](../adr/0282-auto-mode-defaults-follow-the-owners-instruction-of-2026-11-13-on-where-no-evidence-is-lost-and-no-risk-is-widened.md)), and it never releases a block while a look is still available;
  - no block ever moves earlier.
- **Phase / roadmap row:** HO — Hold-out epochs, row HO-01 (gap GAP-02 of the due-diligence review of 2026-11-12).
- **Size:** about 4.5 agent-days, in two parts:
  - **HO-01a** (2.5 d): the chain, the store and migration, the cut behind the port, the release act and the labels.
  - **HO-01b** (2 d): the per-kind looks and the horizon rule, the model reading of its newest look, the seal step (auto look, auto release), the card and its tests.
- **Needs:** CF-06, CF-10c (model looks), AU-11, SR-01, LC-01 (all built). LC-01 carries released sessions into the cell statistics.
- **Safety:** light, with **irreversible records**. A release and a look are permanent. A release moves every development cut forward. With nothing released, every clamp is byte-identical to today. It places no order and changes no size; any `unavailable` read refuses. Opus reviewer for both parts (evidence code).
- **Design refs:** docs/design/11-strategy-lab.md §4–6, 09-learning-engine.md §1. Builds on ADR 0207 §3–4 (amends them), ADR 0243 §2, ADR 0272 §3, ADR 0217 and ADR 0282. Decision record: **ADR 0283** (number reserved for this slice).

## Goal
New sessions reach learning on a fixed, announced schedule. A hold-out never moves earlier, and a block never gives a second fresh look of the same kind.

## Verified facts (main at 2918c76e)
- **One block, no advance.** `CurrentAsync` returns the earliest-created row (`Infrastructure/Lab/HoldOutStore.cs:15-22`). The port says "a later block … supersedes this one" (`Application/Abstractions/Lab/IHoldOutStore.cs:5`), but the advance is unbuilt: ADR 0207 lists "A longer block set by an owner act" under "What is not built". CF-06 review fix 2 (spec line 76) already allows a later start, set once by an owner act, with the old block burned.
- **Sized once.** The start is `last − L months + 1 day` (`Domain/Lab/HoldOutPolicy.cs:33-39`), stored at the first Lab post or the first training window (`Application/Lab/LabHoldOutGuard.cs:33-51`; `TrainingWindowResolver.cs:27`).
- **The cut is open-ended; the look is not.**
  - A default Lab window ends the day before the start, forever (`LabHoldOutGuard.cs:65-72`).
  - Training, the training store gate and the cell refresh share one formula, `HoldOutOptions.TrainingCut` = start − 1 − 30 days (`Domain/Confidence/HoldOutOptions.cs:18`; used at `TrainingWindowResolver.cs:32`, `RunStore.Write.cs:110`, `Domain/Learning/CellsRefreshRules.cs:46`). The replay clamp writes the same sum inline (`Application/Replay/ClampedBars.cs:37`).
  - The look reads only `[Start, HistoryLast]` (`Application/Lab/HoldOutPass.cs:77, 103`).
  - So sessions after `HistoryLast` are in no look and no development run. Only the forward channels reach them: P7-13 grades, CF-08 shadow, P10 paper.
- **Looks.**
  - Strategy and model looks share one fresh slot per block: a partial unique index on `block_start` where not `reused_window` (`Infrastructure/Persistence/Configurations/LabRegistrationConfigurations.cs:82`; CF-06 review fix 3).
  - The kind check allows `strategy` and `model` only (`:65`).
  - A version may look once **ever**: unique on `(strategy_id, version, kind)` (`:79`).
  - Model looks are keyed `family@blockStart` (`Application/Learning/ModelHoldOutLooks.cs:36-37`) and expect an advance with no overlap (shortcut at `:110-111`).
  - **A model reads only the look at the block in force** (`ModelHoldOutLooks.cs:47-52`). After an advance its hold-out figures would vanish from the ML gate. (Found 2026-11-13; fixed by D8.)
- **The strategy auto look fires only after a Lab run.** `Confidence:HoldOut:AutoUnlock` (default false, `HoldOutOptions.cs:9, 15`) is tried when a Lab run finishes (`Application/Lab/LabRunner.cs:116`) and needs every development row a pass (`HoldOutUnlocker.cs:166`). For a block k ≥ 2 it would never fire by itself: the development runs end before the block, and a look at a filling block is refused (D6). (Found 2026-11-13; fixed by D10.)
- **Readers of the block.**
  - Through the port: `LabHoldOutGuard.cs:35` (also used by `TrainingWindowResolver`), `UnlockHoldOutHandler.cs:43`, `GetLabReportHandler.cs:57`, `ModelHoldOutLooks.cs:51, 99`, `RefreshCellStatsHandler.cs:62` (LC-01), `ClampedBars.cs:36` (SR-01, SR-02), and `SharedHoldOut.cs:10`, which six ML handlers call (`EvaluateCounterfactualHandler.cs:59`, `EvaluateMetaPolicyHandler.cs:90`, `ProposeEntryWindowsHandler.cs:51`, `RunExperimentHandler.cs:100`, `TrainModelHandler.cs:92`, `Jobs/GetExperimentPreviewHandler.cs:88`).
  - **One bypasses it:** `Infrastructure/Research/RunStore.Write.cs:101`, the training store gate, uses its own SQL.
- **The replay.** SR-01 AC 3: a replay never reads a session in the hold-out.

## Decisions (approved; the alternative after each)
- **D1. The chain.**
  - Block 1 is the stored block exactly as it is (start, end = `history_last`).
  - Block k+1 is `[end_k + 1 day, end_k + L months]`, with L = `Confidence:HoldOut:BlockMonths` (default 6, range 3–12) read when that block is stored.
  - A stored block never changes; the chain has no gaps and no overlaps.

  *Why 6:* the CF-06 D4 minimum; about 125 sessions; two looks a year. *Alternative:* 20 % of the history (CF-06 D4), which grows with the backfill and slows the cycle; or 12 months.
- **D2. States.** `filling` (its last session not yet closed), `sealed`, `looked` (at least one fresh look taken), `released`. The states are derived from the blocks and from a new insert-only release table.
- **D3. The cut follows the block in force**, which is the earliest unreleased block. `CurrentAsync` (and the `RunStore` SQL, which is moved behind the port) returns it, so every clamp moves together. The cut only moves forward and only by a release. `ClampedBars` uses `HoldOutOptions.TrainingCut` instead of its inline sum.
- **D4. Release.**
  - **Endpoint (owner act):** `POST /api/lab/holdout/blocks/{start}/release` with a fresh password. It is allowed only for the earliest unreleased block, and only once it is sealed. One transaction stores the release row and block k+1.
  - **An unlooked block** is released only by the owner with `forfeitLook: true`, and the row records `looked = false`: the block's fresh looks are gone forever. The auto option never does this.
  - **Auto option:** `Confidence:HoldOut:AutoRelease`, **default ON** (ADR 0282; due diligence change 3). Turning it OFF or back ON is a fresh-password act. The seal step (D10) releases a block automatically only when all of these hold:
    1. it is sealed and the earliest unreleased block;
    2. it has at least one fresh look (any kind);
    3. **no look is still available**: for each kind, the fresh slot is used, or no eligible subject exists (strategy: a version frozen before the horizon with a stored development pass; model: a champion frozen before the horizon whose development verdict is Eligible; system: an SR-03 registration targeting the block without a result). A kind whose look path is not built yet (model before CF-10e part 3, system before SR-03) has no eligible subject;
    4. every read above answered; an `unavailable` read keeps the block.

    It is recorded as `auto`, actor `system:auto-release`, with the settings snapshot hash. A block that is sealed and not released after 20 sessions gets a bell notice that names what blocks it.

  *Alternative rejected:* release at the seal whatever was looked. That would forfeit an available look without the owner.
- **D5. One fresh look per block and kind, pre-dated.** (Due diligence change 1: per kind, resolving the conflict with SR-03.)
  - The fresh-look index becomes unique on `(block_start, kind)` where not `reused_window`. The kind check adds `system` (SR-03). So a block gives at most three fresh looks: one strategy, one model, one system.
  - The per-version unique key becomes `(strategy_id, version, kind, block_start)`, so a version looks once **per block**.
  - **The horizon.** A look is fresh only when its subject was frozen before the block's horizon:
    - for k ≥ 2 the horizon is `S_k` (India date): the version's first plan freeze (`lab.plan_freezes.created_at`), the model's registration (`research.model_versions`), or the SR-03 registration, must be before it;
    - for block 1 (today's rule, kept), the horizon is the first stored look of any kind at block 1; with no look yet, any subject qualifies. A `system` look is never fresh at block 1: the system's configuration was built while block 1 was being traded and reported.
  - Otherwise the look is stored `reused_window` with the reason `not_predated` and cannot pass.
  - *Why one per kind, not one per block:* each kind tests a different claim (a strategy keeps its edge; the model beats no-ML; the whole system beats its baseline). With the horizon rule no subject can have been tuned on the block, so a look of one kind cannot contaminate another. One per kind still bounds the number of tests on a block to three, and each look is a row in the trial ledger.
  - *Why not one per strategy family:* three families would give three strategy tests on one block, and the best of them would be a selection. The `system` look is the honest aggregate of the families.
  - *Alternative:* keep one shared slot. Rejected: a model or system look would then burn the strategies' block, which is why CF-10e part 3 and SR-03 were waiting.
- **D6. The look reads the block in force, `[S_k, E_k]`.** A filling block answers 409 `lab.holdout_filling` with its seal date.
- **D7. Released data is labelled.** A run, replay session, training chunk or cell row resting on trades that exit inside a released block carries `seen_holdout:<start>`.
  - Lab verdicts over such windows are allowed and labelled, and never count as hold-out evidence.
  - LC-01's end moves with the cut.
  - **Scorecard row 12** reads the version's newest fresh look. A later fail is never outweighed by an earlier pass.
- **D8. Models.** ADR 0217's "unspent hold-out" means that the block in force has no fresh look of the family, which the `family@blockStart` key already gives. **The ML gate reads the model's newest fresh look across blocks** (as row 12 does for a version), not only the look at the block in force, so a release does not erase a model's hold-out figures. A later fail beats an earlier pass. (Due diligence change 2.)
- **D9. Alternative not taken: a forward-basis cell series.** This would compute cell statistics from the shadow grades of ended P10 epochs, never the current one (CF-08 D6).
  - *For:* it uses forward data now and burns no block.
  - *Against:* grades exist only for cells the nightly already ranked (a selected sample); it is a second basis that must never mix with research runs.
  - Deferred to a later, separate spec, shown beside the research basis and never pooled with it.
- **D10. The seal step.** (Due diligence change 4.) A new service `holdout-epochs`, `After eod`, `NotBeforeSessionEnd`, recovery class `LatestOnly`, **enabled at first start** (it acts only through the auto settings, each of which fails closed). Each evening it:
  1. marks a block sealed when its last session has closed;
  2. **strategy auto look:** with `Confidence:HoldOut:AutoUnlock` ON (default ON by ADR 0282) and the strategy slot free, claims the look for the newest pre-dated version with a stored default-options full or robust Lab run whose development rows all pass (`HoldOutAutoGate`), ties broken by the earliest first freeze; the look is queued as the existing `holdout` Lab job, `auto`. **Never for block 1** (coordinator, 2026-11-13): block 1 has no pre-dating horizon and its look is irreversible, so its strategy look stays the owner's act (a manual unlock, or a release with the forfeit); the post-Lab-run auto look (`LabRunner.cs:116`) also skips block 1;
  3. leaves room for the model step (CF-10e part 3) and the system step (SR-03), added by those slices;
  4. checks the auto release (D4).

  The steps are independent: one kind failing does not stop another; a failed read leaves the block as it was.

## Worked numbers
Block 1 is 2026-05-05..2026-11-04 (stored 2026-11-04). With L = 6:
- block 2 is 2026-11-05..2027-05-04;
- block 3 is 2027-05-05..2027-11-04.

The cuts:
- **Today:** the Lab cut is 2026-05-04; the training, replay and LC-01 cut is 2026-05-05 − 1 − 30 = 2026-04-04.
- **After block 1 is released on 2026-11-20:** the Lab cut is 2026-11-04, and the other cut is 2026-11-05 − 1 − 30 = 2026-10-05.

Looks at block 1 (sealed): the first strategy look is fresh under today's rule. A model registered before that look gets a fresh model look. No system look is fresh.

Block 2 seals when the session of 2027-05-04 closes. Looks at block 2:
- ORB v3, frozen 2026-10-01: a fresh strategy look that may pass.
- ORB v4, frozen 2026-12-15: `reused_window: not_predated`, which cannot pass.
- A model registered 2026-10-20: a fresh model look, and the strategy look of ORB v3 stays fresh too.
- No SR-03 registration can be made before 2026-11-05, so block 2 has no system look; block 3 is the first.

## Scope
**In:**
- HO-01a: the chain, states and release in Domain, Application and Infrastructure, with the migration; the `RunStore` SQL moved behind the port; `ClampedBars` on `TrainingCut`; the release endpoint and its auto setting; the labels; `gen-api`.
- HO-01b: the per-kind index and the `system` kind; the widened unique key and the horizon rule; the model reading of its newest look; the seal service with the strategy auto look and the auto release; the stalled-block notice; the hold-out card; the ADR.

**Out:**
- the forward basis (D9);
- re-cutting block 1;
- a longer block 1 after CF-04;
- the model and system steps of the seal service (CF-10e part 3, SR-03);
- deploy.

## Acceptance criteria
1. **Migration.** The existing row becomes block 1 (`prev_start` null). A second row left by the ADR 0207 race is marked `orphan` and never read. A unique index allows one first block. The existing unlock rows keep their `reused_window` flags unchanged.
2. **No release, no change.** With nothing released, every reader's cut is byte-identical to today (a golden test over the listed readers).
3. **Chain.** The chain gives the worked dates exactly. A block with L = 3 stored after a change of the setting leaves stored blocks unchanged.
4. **Release (owner).**
   - Refusals:
     - block 2 still filling: 409 `lab.holdout_filling`;
     - not the earliest unreleased block: 409 `lab.holdout_order`;
     - unlooked without `forfeitLook`: 409 `lab.holdout_unlooked`;
     - wrong password: 401; no password: 403;
     - again: 409 `lab.holdout_released`.
   - A success stores the release row and block 2 atomically.
5. **Cuts move.** After releasing block 1:
   - a default Lab window ends 2026-11-04;
   - the training, replay and LC-01 cut is 2026-10-05;
   - a replay of 2026-06-01 is allowed, with its sessions labelled `seen_holdout`;
   - any session from 2026-11-05 on is still refused (SR-01 AC 3 holds for block 2).
6. **Looks.**
   - The v3 and v4 cases give `fresh` and `not_predated`.
   - At block 2, a fresh strategy look and a fresh model look both stand; a second strategy look is `reused_window`.
   - A `system` row at block 1 is `not_predated`.
   - At block 1, a model registered after the first strategy look is `not_predated`.
   - A version looks once per block; a look at a filling block answers 409.
   - A model family gets one fresh look at block 2 after looking at block 1.
7. **Auto release.**
   - Default (no stored change): ON, and the setting's snapshot says so.
   - ON: a sealed block with a fresh look and no available look is released, recorded `auto` with the snapshot.
   - ON: a sealed block whose strategy slot is free while an eligible version exists is **not** released. (The same rule for an eligible champion and for an SR-03 registration without a result is tested by CF-10e part 3 and SR-03, through a fake look path here.)
   - ON: an unlooked block, or any `unavailable` read, is never released.
   - OFF: nothing is released by itself.
   - After 20 sealed sessions without release: one notice naming the reason.
8. **Strategy auto look at the seal.** With AutoUnlock ON, on the evening block 2 seals, the look is claimed for ORB v3 (newest pre-dated version with a development pass), not v4; with AutoUnlock OFF nothing is claimed. On the evening block 1 is in force and sealed, with AutoUnlock ON and a passing version, nothing is claimed either (block 1 is the owner's), and a Lab run that passes does not claim block 1.
9. **Scorecard row 12 and the ML gate.** A pass on block 1 followed by a fail on block 2 reads fail, for a version (row 12) and for a model family (the gate). After block 2 is released, the model's block-2 figures are still read.
10. **Labels.** `seen_holdout:<start>` appears on Lab run labels, replay sessions and the run message of the LC-01 refresh (HO-01a). The per-row label on the LC-01 cell rows (the cell tables have no label column) moves to HO-01b.
11. **Fences.** Pinned by architecture tests:
    - no code outside `HoldOutStore` names `lab.holdout_blocks` or `lab.holdout_releases`;
    - nothing references `XDrishti.Execution`.

## Contract
- **API:**
  - `GET /api/lab/holdout` adds `blocks[]` and `autoRelease`. Built in HO-01a: each block is a `HoldOutBlockView` with `{ start, historyFirst, historyLast (its end), lengthMonths, settingHash, createdAt, state, freshLook, releasedAt, mode }`; HO-01b replaces `freshLook` by `looks: { strategy, model, system }` and adds `blockedBy?`.
  - `POST /api/lab/holdout/blocks/{start}/release` takes `{ password, forfeitLook? }` and answers 200 `HoldOutBlockView` of the released block, or the codes of AC 4 (a start later than the block in force is `lab.holdout_order`, an unknown earlier date 404, a malformed date 422). Each release also writes a best-effort `LabHoldOutReleased` audit row (no database change).
  - `gen-api` in the same change.
- **Data** (migration `AddHoldOutEpochs`, HO-01a; `WidenHoldOutLooks`, HO-01b):
  - `lab.holdout_blocks` gains `prev_start date null unique` and `orphan bool not null default false`.
  - New `lab.holdout_releases(block_start pk, released_at, mode 'owner'|'auto', actor, setting_hash, looked bool, next_block_start)`, insert-only for the runtime role.
  - `uq_holdout_unlocks_version` gains `block_start`; `uq_holdout_unlocks_fresh` becomes `(block_start, kind)` where not `reused_window`; `ck_holdout_unlocks_shape` allows `kind IN ('strategy','model','system')`; a nullable `reason` column holds `not_predated`.
  - `Down` refuses while a release or a `system` row exists.
- **Settings:**
  - `Confidence:HoldOut:BlockMonths` (6, 3–12).
  - `Confidence:HoldOut:AutoRelease` (**true**; a change in either direction needs a fresh password). The auto release fails closed (`no_strategy_slot`) until HO-01b registers the strategy look slot.
  - `Confidence:HoldOut:Enforce` default ON is built in HO-01b, with the deploy of 01a and 01b.
  - `Confidence:HoldOut:AutoUnlock` default becomes **true** (ADR 0282), for blocks k ≥ 2 only; block 1 is never looked automatically.
- **Service:** `holdout-epochs` (D10).
- **UI:** the Lab hold-out card lists the blocks with their states, the three looks and what blocks a release. The release button sits behind the step-up dialog, which shows the forfeit sentence for an unlooked block.

## Test plan
- **Unit:** the chain arithmetic, the states, the horizon rule per kind, the auto-release conditions, the seal choice and row 12.
- **Application with fakes:** release preconditions, auto release, the seal step, the model reading across blocks, and the cut used by each reader.
- **Integration:** the migrations with an orphan row and old unlock rows, the endpoints, the grants, the per-kind index, the atomic release.
- **Architecture:** the single reader of the block and release tables.
- **Frontend (MSW):** the card, the dialog and the forfeit text.

## Risks
- **A released block was judged once.** Strategies built after its release are tuned on data that judged others; the label says so.
- **Swing cells may stay inconclusive.** A 6-month block may hold fewer than 30 trades for them.
- **A release changes run keys.** It changes every default window, as the first block did (ADR 0207, "Consequences").
- **Block 1 waits for the owner.** The auto look never claims block 1 (coordinator's change, 2026-11-13: an irreversible look the morning after a deploy would surprise the owner). Until the owner takes block 1's strategy look or releases it with the forfeit, block 1 stays in force, AutoRelease cannot fire for it, and no new session reaches learning. This is an owner to-do, named in the roadmap.
- **A kind not yet built cannot hold a block.** If HO-01b is deployed before CF-10e part 3, block 1 may be released before any model could look at it. Deploy the two together if a champion exists and its block-1 look matters.
- **Three looks per block.** The multiple-testing cost of three kinds is bounded and each look is counted in the ledger; a reader must not pick the best of the three.

## Due diligence (2026-11-13)
1. **Per-kind slots** (D5): resolves the conflict with SR-03 question 1 and CF-10e part 3, which both needed a look that does not burn the strategies' block.
2. **Model reading across blocks** (D8): without it a release would erase a model's hold-out figures from the ML gate (`ModelHoldOutLooks.cs:47-52`).
3. **Auto release ON** (D4, ADR 0282): the owner's 2026-11-13 instruction. It never forfeits an available look; an unlooked block stays the owner's.
4. **The seal step** (D10): without it the strategy auto look never fires for k ≥ 2 (`LabRunner.cs:116`), and the auto release would wait forever.
5. **Facts refreshed** to main 2918c76e: the reader list (LC-01's refresh, the six ML handlers, the training window), the shared `TrainingCut` and the inline sum in `ClampedBars`.

## Owner questions (answered 2026-11-13, as recommended)
1. Replace the single hold-out by a chain of fixed blocks released by an owner act? **Yes**, with the auto release of D4.
2. Block length? **6 months**, range 3–12.
3. For blocks after the first, a fresh look only for subjects frozen before the block began? **Yes**, per kind (D5).
4. Allow releasing an unlooked block, which forfeits its looks? **Yes, only by the owner with the explicit forfeit.**
5. Add the auto release (CLAUDE.md approvals rule) for blocks with no available look? **Yes**, default **ON** under the owner's 2026-11-13 instruction (ADR 0282).
6. Row 12 reads the newest fresh look, and a later fail beats an earlier pass? **Yes**, and the ML gate reads its model the same way (D8).
7. Keep block 1 exactly as stored, never re-cut? **Yes.**
8. The forward-basis cell series (D9) as a later, separate spec? **Yes, defer.**
