# HO-01 — Hold-out epochs: fixed blocks, one fresh look each, released blocks become development

- **Status:** Draft — needs the owner's decision: it changes the evidence rule of CF-06 / ADR 0207 (how the hold-out is split and unlocked). **Recommended option:**
  - the stored block becomes block 1 of a chain of fixed 6-month blocks;
  - the owner releases a sealed block (fresh password), and it becomes development, labelled `seen_holdout`;
  - every block keeps exactly one fresh look, open only to versions frozen before that block began;
  - no block ever moves earlier.
- **Phase / roadmap row:** HO — Hold-out epochs, row HO-01 (gap GAP-02 of the due-diligence review of 2026-11-12).
- **Size:** about 3.5 agent-days:
  - chain, store and migration: 1;
  - release act and auto option: 1;
  - look rules and labels: 1;
  - card and tests: 0.5.
- **Needs:** CF-06, CF-10c (model looks), AU-11, SR-01 (built). LC-01 (Approved, not built) is what carries released sessions into the cell statistics; without it, a release reaches the Lab, training and replays only.
- **Safety:** light, with **irreversible records**. A release and a look are permanent. A release moves every development cut forward. With nothing released, every clamp is byte-identical to today. It places no order and changes no size; any `unavailable` read refuses.
- **Design refs:** docs/design/11-strategy-lab.md §4–6, 09-learning-engine.md §1. Builds on ADR 0207 §3–4 (amends them), ADR 0243 §2, ADR 0272 §3 and ADR 0217. Decision record: a new ADR (the next free number at build time).

## Goal
New sessions reach learning on a fixed, announced schedule. A hold-out never moves earlier, and a block never gives a second fresh look.

## Verified facts (main at 30777841)
- **One block, no advance.** `CurrentAsync` returns the earliest-created row (`Infrastructure/Lab/HoldOutStore.cs:14-21`). The port says "a later block … supersedes this one" (`Application/Abstractions/Lab/IHoldOutStore.cs:5`), but the advance is unbuilt: ADR 0207 lists "A longer block set by an owner act" under "What is not built". CF-06 review fix 2 (spec line 76) already allows a later start, set once by an owner act, with the old block burned.
- **Sized once.** The start is `last − L months + 1 day` (`Domain/Lab/HoldOutPolicy.cs:33-38`), stored at the first Lab post (`Application/Lab/LabHoldOutGuard.cs:33-51`).
- **The cut is open-ended; the look is not.**
  - A default Lab window ends the day before the start, forever (`LabHoldOutGuard.cs:65-72`). Training and replay end 30 days earlier still (ADR 0243 AU-11 §3, ADR 0272 §3).
  - The look reads only `[Start, HistoryLast]` (`Application/Lab/HoldOutPass.cs:77, 103`).
  - So sessions after `HistoryLast` are in no look and no development run. Only the forward channels reach them: P7-13 grades, CF-08 shadow, P10 paper. The review's "open-ended from its start" (`docs/specs/REVIEW-2026-11-10.md:16`) is true of the cut, not of the look.
- **Looks.**
  - Strategy and model looks share one fresh slot per block: a partial unique index on `block_start` where not `reused_window` (`Infrastructure/Persistence/Configurations/LabRegistrationConfigurations.cs:82`; CF-06 review fix 3, spec line 77).
  - A version may look once **ever**: unique on `(strategy_id, version, kind)` (`:79`).
  - Model looks are keyed `family@blockStart` and expect an advance with no overlap (`Application/Learning/ModelHoldOutLooks.cs:108-112`).
- **Readers of the block.**
  - Through the port: `LabHoldOutGuard.cs:35`, `UnlockHoldOutHandler.cs:43`, `GetLabReportHandler.cs:57`, `ModelHoldOutLooks.cs:51, 99`, `SharedHoldOut.cs:10` (four ML handlers) and `Replay/ClampedBars.cs:36`.
  - **One bypasses it:** `Infrastructure/Research/RunStore.Write.cs:101`, the training store gate, uses its own SQL.
- **The replay.** SR-01 AC 3: a replay never reads a session in the hold-out.

## Decisions (recommended; the alternative after each)
- **D1. The chain.**
  - Block 1 is the stored block exactly as it is (start, end = `history_last`).
  - Block k+1 is `[end_k + 1 day, end_k + L months]`, with L = `Confidence:HoldOut:BlockMonths` (default 6, range 3–12) read when that block is stored.
  - A stored block never changes; the chain has no gaps and no overlaps.

  *Why 6:* the CF-06 D4 minimum; about 125 sessions; two looks a year. *Alternative:* 20 % of the history (CF-06 D4), which grows with the backfill and slows the cycle; or 12 months.
- **D2. States.** `filling` (its last session not yet closed), `sealed`, `looked` (its fresh look taken), `released`. The states are derived from the blocks and from a new insert-only release table.
- **D3. The cut follows the block in force**, which is the earliest unreleased block. `CurrentAsync` (and the `RunStore` SQL, which is moved behind the port) returns it, so every clamp moves together. The cut only moves forward and only by a release.
- **D4. Release is an owner act.**
  - **Endpoint:** `POST /api/lab/holdout/blocks/{start}/release` with a fresh password. It is allowed only for the earliest unreleased block, and only once it is sealed. One transaction stores the release row and block k+1.
  - **An unlooked block** is released only with `forfeitLook: true`, and the row records `looked = false`: the block's fresh look is gone forever.
  - **Auto option:** `Confidence:HoldOut:AutoRelease`, default OFF. Turning it ON is a fresh-password act. It releases only a sealed block that **has** its fresh look, and records it as `auto` with the setting snapshot.

  *Alternative rejected:* an automatic release at the seal. The release changes every default Lab window and run key, so the owner chooses the moment.
- **D5. One fresh look per block, pre-dated.**
  - The look index is unchanged. The per-version unique key becomes `(strategy_id, version, kind, block_start)`, so a version looks once **per block**.
  - **For k ≥ 2,** a look is fresh only when the version's first plan freeze (`lab.plan_freezes.created_at`, India date) is before `S_k`. Otherwise the look is stored `reused_window` with the reason `not_predated` and cannot pass. *Why:* the block's sessions were visible through shadow, paper and reports while it filled, so only a version that existed before them is untouched by them.
  - Block 1 keeps today's rule.
  - *Alternative:* today's rule for every block.
- **D6. The look reads the block in force, `[S_k, E_k]`.** A filling block answers 409 `lab.holdout_filling` with its seal date.
- **D7. Released data is labelled.** A run, replay session, training chunk or cell row resting on trades that exit inside a released block carries `seen_holdout:<start>`.
  - Lab verdicts over such windows are allowed and labelled, and never count as hold-out evidence.
  - LC-01's end moves with the cut.
  - **Scorecard row 12** reads the version's newest fresh look. A later fail is never outweighed by an earlier pass.
- **D8. Models.** ADR 0217's "unspent hold-out" means that the block in force has no fresh look of the family, which the `family@blockStart` key already gives.
- **D9. Alternative not taken: a forward-basis cell series.** This would compute cell statistics from the shadow grades of ended P10 epochs, never the current one (CF-08 D6).
  - *For:* it uses forward data now and burns no block.
  - *Against:*
    - grades exist only for cells the nightly already ranked (a selected sample);
    - it is a second basis that must never mix with research runs;
    - P10-05 freezes the statistics in an epoch.
  - Recommended as a later, separate spec, shown beside the research basis and never pooled with it.

## Worked numbers
Block 1 is 2026-05-05..2026-11-04 (stored 2026-11-04). With L = 6:
- block 2 is 2026-11-05..2027-05-04;
- block 3 is 2027-05-05..2027-11-04.

The cuts:
- **Today:** the Lab cut is 2026-05-04; the training, replay and LC-01 cut is 2026-05-05 − 1 − 30 = 2026-04-04.
- **After the owner releases block 1 on 2026-11-20:** the Lab cut is 2026-11-04, and the other cut is 2026-11-05 − 1 − 30 = 2026-10-05.

Block 2 seals when the session of 2027-05-04 closes. Looks at block 2:
- ORB v3, frozen 2026-10-01: a fresh look on 2027-05-10 that may pass.
- ORB v4, frozen 2026-12-15: `reused_window: not_predated`, which cannot pass.

## Scope
**In:**
- the chain, states and release in Domain, Application and Infrastructure, with the migration;
- the `RunStore` SQL moved behind the port;
- the widened unique key and the pre-dated rule;
- the labels, the hold-out card, the ADR and `gen-api`.

**Out:**
- the forward basis (D9);
- re-cutting block 1;
- a longer block 1 after CF-04;
- the LC-01 build;
- deploy.

## Acceptance criteria
1. **Migration.** The existing row becomes block 1 (`prev_start` null). A second row left by the ADR 0207 race is marked `orphan` and never read. A unique index allows one first block.
2. **No release, no change.** With nothing released, every reader's cut is byte-identical to today (a golden test over the listed readers).
3. **Chain.** The chain gives the worked dates exactly. A block with L = 3 stored after a change of the setting leaves stored blocks unchanged.
4. **Release.**
   - Refusals:
     - block 2 still filling: 409 `lab.holdout_filling`;
     - not the earliest unreleased block: 409 `lab.holdout_order`;
     - unlooked without `forfeitLook`: 409 `lab.holdout_unlooked`;
     - wrong password: 401;
     - again: 409 `lab.holdout_released`.
   - A success stores the release row and block 2 atomically.
5. **Cuts move.** After releasing block 1:
   - a default Lab window ends 2026-11-04;
   - the training, replay and LC-01 cut is 2026-10-05;
   - a replay of 2026-06-01 is allowed, with its sessions labelled `seen_holdout`;
   - any session from 2026-11-05 on is still refused (SR-01 AC 3 holds for block 2).
6. **Looks.**
   - The v3 and v4 cases give `fresh` and `not_predated`.
   - A version looks once per block.
   - A look at a filling block answers 409.
   - A model family gets one fresh look at block 2 after looking at block 1.
7. **Auto release.**
   - OFF: nothing is ever released by itself.
   - ON: only a sealed, looked block is released, recorded `auto`.
   - An unlooked block, or an `unavailable` read, is never released.
8. **Scorecard row 12.** A pass on block 1 followed by a fail on block 2 reads fail.
9. **Labels.** `seen_holdout` appears on LC-01 rows, run labels and replay sessions.
10. **Fences.** Both are pinned by architecture tests:
    - no code outside `HoldOutStore` names `lab.holdout_blocks`;
    - nothing references `XDrishti.Execution`.

## Contract
- **API:**
  - `GET /api/lab/holdout` adds `blocks[]` with `{ start, end, state, freshLook, releasedAt, mode }`.
  - `POST /api/lab/holdout/blocks/{start}/release` takes `{ password, forfeitLook? }` and answers 200 `HoldOutBlockView`, or the codes of AC 4.
  - `gen-api` in the same change.
- **Data** (migration `AddHoldOutEpochs`):
  - `lab.holdout_blocks` gains `prev_start date null unique` and `orphan bool not null default false`.
  - New `lab.holdout_releases(block_start pk, released_at, mode 'owner'|'auto', actor, setting_hash, looked bool, next_block_start)`, insert-only for the runtime role.
  - `uq_holdout_unlocks_version` gains `block_start`.
  - `Down` refuses while a release exists.
- **Settings:**
  - `Confidence:HoldOut:BlockMonths` (6, 3–12).
  - `Confidence:HoldOut:AutoRelease` (false; ON needs a fresh password).
- **UI:** the Lab hold-out card lists the blocks with their states and look counts. The release button sits behind the step-up dialog, which shows the forfeit sentence for an unlooked block.

## Test plan
- **Unit:** the chain arithmetic, the states, the pre-dated rule and row 12.
- **Application with fakes:** release preconditions, auto release, and the cut used by each reader.
- **Integration:** the migration with an orphan row, the endpoints, the grants, the widened index and the atomic release.
- **Architecture:** the single reader of the block table.
- **Frontend (MSW):** the card, the dialog and the forfeit text.

## Risks
- **A released block was judged once.** Strategies built after its release are tuned on data that judged others; the label says so.
- **Swing cells may stay inconclusive.** A 6-month block may hold fewer than 30 trades for them.
- **A release changes run keys.** It changes every default window, as the first block did (ADR 0207, "Consequences").
- **Nothing new reaches learning before a release.** The first new data waits for the owner's first release.

## Owner questions (recommendation first)
1. Replace the single hold-out by a chain of fixed blocks released by an owner act? **Yes.**
2. Block length? **6 months**, range 3–12.
3. For blocks after the first, a fresh look only for versions frozen before the block began? **Yes.**
4. Allow releasing an unlooked block, which forfeits its look? **Yes, only with the explicit forfeit.**
5. Add the auto release (CLAUDE.md approvals rule), OFF, for looked blocks only? **Yes.**
6. Row 12 reads the newest fresh look, and a later fail beats an earlier pass? **Yes.**
7. Keep block 1 exactly as stored, never re-cut? **Yes.**
8. The forward-basis cell series (D9) as a later, separate spec? **Yes, defer.**
