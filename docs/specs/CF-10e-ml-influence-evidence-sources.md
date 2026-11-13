# CF-10e — The three evidence sources of the ML influence gate

- **Status:** Parts 1–2 built 2026-11-12 and deployed 2026-11-13. **Part 3 approved for build after HO-01b** (owner, 2026-11-13, in chat, with HO-01 and SR-03; changes of the due diligence in *Part 3 readiness*). (Parts 1–2 approved: coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended.)
- **Phase / roadmap row:** CF — Confidence programme, row CF-10e (after CF-10 10a–10d; GAP-04 of the 2026-11-12 due-diligence review)
- **Size:** about 6.5 agent-days in three parts (estimate, uncalibrated): **10e-1** scores on shadow candidates and the forward figures 2.5 d · **10e-2** the calibration alarm from the stored monitor 1 d · **10e-3** the hold-out scoring pass 3 d. Build order 10e-1, 10e-2, then 10e-3 once HO-01 is decided and built.
- **Needs:** CF-10 (built, ADRs 0214–0217, 0220), CF-08 shadow record (ADR 0211), CF-06 hold-out (ADR 0207); part a (10e-3) also needs **HO-01b** (`docs/specs/HO-01-hold-out-epochs.md`, Approved 2026-11-13): the per-kind fresh slot, the horizon rule and the seal service.
- **Design refs:** docs/design/09-learning-engine.md §6–§8; ADR 0217 (predicate and gate), 0220 (monitor, switches, designate tripwire), 0211, 0207.

## Goal
The gate `MlMayInfluence` (ADR 0217) can say "yes" for a model that earned it. Today it refuses every model because none of its three inputs has a source. This slice builds the three sources and leaves the predicate, its bars, the switch and the advisory-only rule exactly as they are.

## Facts (main at 30777841)
- The evidence port has one implementation, and it returns `MlInfluenceEvidence.None` (`Application/Learning/NoMlInfluenceEvidence.cs:8-13`). It is registered at `Application/DependencyInjection.cs:965`, and the two sister ports are also empty: `NoRollbackEvidence` at :967 and `NoChallengerEvidence` at :968. An architecture test pins it as the only implementation (`tests/XDrishti.Architecture.Tests/MlInfluenceArchitectureTests.cs:272-274`).
- The evaluator passes the hold-out figures through only when the model owns a fresh look (`Application/Learning/MlInfluenceEvaluator.cs:28-35`). A null figure is `Unavailable`, which is not a pass (`Domain/Learning/MlInfluence.cs:81-83`). The gate also requires `IsChampion` (ADR 0217 §2).
- **Shadow rows already have the model columns, written null.** `paper.shadow_entries` has `model_id`, `p_win`, `feature_version` and `features` (`Infrastructure/Persistence/Configurations/ShadowConfigurations.cs:30-32`). The writer fills them with null, or with the plan's note figure (`Application/Shadow/WriteShadowHandler.cs:178-181`). They are inside the row hash (`Domain/Shadow/ShadowEntry.cs:78-79`), so they cannot be filled in after the write. The live plan never sets `AdvisoryPWin` (`Application/Planning/SelectionPlanBuilder.cs:75`), so today `p_win` is always null.
- **The monitor exists; its input does not.** `ml-monitor` runs daily at 18:15 and starts paused (`Application/Learning/MlMonitorService.cs:10-16`). It evaluates champions only (`EvaluateMlModelsHandler.cs:26-33`) and stores one row per model per day in `research.ml_monitor_checks` (`Infrastructure/Learning/MlMonitorChecks.cs:7-22`). The calibration detector reads `RollbackInputs.Calibration` (`Domain/Learning/RollbackPolicy.cs:25-27`) through `IRollbackEvidence` (`ProposeRollbackHandler.cs:63`), and that port answers None.
- **Hold-out rows do not exist anywhere.** Training leaves out every trade that reaches the block (`Application/Learning/SimTraining/TrainFromSimulationHandler.cs:201`). Lab windows are cut at the hold-out start (CF-06 D4). `ModelHoldOutLooks.ClaimAsync` (`Application/Learning/ModelHoldOutLooks.cs:81`) has no production caller.
- **One look per shared block.** Any unlock, of a strategy or a model, marks the shared block as seen, and every later look is `reused_window` (`Application/Lab/HoldOutUnlocker.cs:13-14`; CF-06 spec, amendment 3). So under today's rule a model look burns the block for the strategies. That is why part a waits for HO-01.
- **Tripwire.** `MlRollbackArchitectureTests.cs:405-425` fails as soon as a real `IMlInfluenceEvidence` or `IChallengerEvidence` exists while `OwnerDesignateModelHandler` (`xd learn designate`) does not depend on the promote command.
- The shadow writer runs only with `paper.shadow.enabled` ON, which is OFF by default (`Infrastructure/Paper/ShadowSettings.cs:7`).

## Decisions (recommended, taken)
- **D1. Scores go in their own insert-only table.** New table `paper.shadow_scores`, one row per (shadow entry, model), written by the shadow writer in the same transaction as the session and before the 09:00 cut-off. Every registered champion and challenger is scored, so challengers also build forward evidence, which can never be back-filled.
  - The existing `shadow_entries.model_id/p_win` columns keep their meaning: the plan's own figure.
  - *Alternative:* fill those columns for the champion only. Rejected: it holds one model per row, and the columns are hashed at write time.
- **D2. Features at decision time, never later.** The feature vector comes from the P6-04 `TradeFeatureExtractor` (look-ahead-free by construction) as of the decision day's close, in the model's own catalog version. A candidate whose features cannot be built gets no score row; nothing is imputed beyond what the model already does.
- **D3. Forward figures.** Graded rows are `shadow_grades` with status graded. A candidate counts only if it is `CountsForEdge` and the model scored it.
  - Count n; weeks = (last − first session) / 7.
  - Sort by `p_win` descending, ties broken by trade key in ordinal order. Take the top ⌊n/2⌋ and the bottom ⌊n/2⌋; with an odd n the middle candidate is left out. The figure is the top mean R minus the bottom mean R.
  - Rolling ECE comes from the existing 10a ECE over the last 100 graded rows, or all of them if there are fewer.
- **D4. The calibration alarm is read from the stored monitor row.** The new `IRollbackEvidence` source feeds the detector the champion's graded scored outcomes (D1). `CalibrationAlarm` is the calibration verdict of the model's newest `ml_monitor_checks` row:
  - no row on the last trading day, or the calibration trigger reads `Unavailable` (e.g. fewer than 100 observations): `null`, which is unavailable and not a pass;
  - `Fires`: `true`;
  - `Quiet`: `false` (`RollbackTriggerState`, `Domain/Learning/RollbackPolicy.cs:6-15`).

  The band-trigger cells stay unavailable (out of scope).
- **D5. The hold-out scoring pass (part a, after HO-01).** (Amended 2026-11-13, see *Part 3 readiness*.)
  - It is an owner act with a fresh password, plus the auto option `ml.holdout.auto_score`: default **ON** (ADR 0282; seed row), turning it OFF or back ON is an owner act with a fresh password, and an automatic run is recorded as `auto` with the setting snapshot.
  - It first claims the look (`ClaimAsync`, insert-only, burned even if the pass is abandoned). It then runs the strategy versions of the model's training set once over the block that HO-01 grants the family. It extracts the features, scores the rows, and compares the model's log loss with the best no-ML baseline (the better of the base rate and the cell rate) by `PairedDayBootstrap`.
  - It applies the P6-13 take rule (`MetaPolicyEvaluator`) to get the effect on realised R.
  - One stored row per model.
  - *Alternative:* score under today's single shared block. Rejected: it burns the strategies' one look. HO-01 D5 now gives the model its own slot per block.
- **D6. The designate tripwire.** In part 10e-1, `xd learn designate … champion` goes through `PromoteChallengerHandler` whenever there is an incumbent champion. With no incumbent, the champion rule alone still applies (the first champion). This satisfies the tripwire.
  - *Alternative:* relax the test. Rejected: that loosens a pinned safeguard.
- **D7. Nothing else changes.** The ADR 0217 truth table, the thresholds (100 rows, P ≥ 0.95, 30 candidates, 4 weeks, ECE ≤ 5 pp), `IsChampion` and the switch `ml.influence.enabled` (OFF) all stay as they are.
  - The gate's consumer list stays pinned empty, so even a passing gate steers nothing.
  - ML stays advisory and never raises a size.

## Scope
**In:**
- `paper.shadow_scores` and its writer.
- The forward figures.
- `IRollbackEvidence` (calibration part).
- The real `IMlInfluenceEvidence` source, `ShadowMlInfluenceEvidence`.
- The monitor-row reader.
- The hold-out scoring pass with its setting, endpoint and verb.
- The interval in `MlInfluenceView`.
- Architecture-test updates, the ADR and `gen-api`.

**Out:**
- Any consumer of the gate.
- `IChallengerEvidence` (the paired comparison against the champion is a follow-up that will read D1's rows).
- The band trigger's cells.
- Promotion changes beyond D6.
- Advancing hold-out blocks (HO-01).
- Any order.

## Acceptance criteria
1. **Score rows.** Given the shadow switch is ON, champion 17 and challenger 18, and 40 candidates on 2026-11-13, when the 19:00 write runs, then 80 `shadow_scores` rows are stored in the session's transaction. Each row has the model id, the file hash, the feature version and a `p_win` in [0, 1]. An attempt at 09:01 IST stores nothing (`ShadowCutoff.IsOpened`, as for the entries). A second attempt for the same (entry, model) is a no-op. UPDATE and DELETE are refused for the runtime role.
2. **No look-ahead.** A property test mutates the bars after the decision close and checks that every stored score is unchanged.
3. **Forward holds.** With 34 graded candidates scored by 17 over 5 weeks, a top-17 mean of +0.21 R, a bottom-17 mean of +0.05 R and an ECE of 3.8 pp, `ForwardShadowOk` holds with a figure of +0.16.
   - 29 candidates: fails `forward_too_few_candidates`.
   - 3.9 weeks: fails `forward_too_short`.
   - 35 candidates: the 18th is left out.
   - Zero rows: unavailable.
4. **Calibration.** The newest monitor row is today's:
   - calibration `Quiet`: `CalibrationAlarm=false`;
   - `Fires`: `true` (`calibration_alarm`);
   - `Unavailable` with 64 observations: `null` (`calibration_alarm_unavailable`, not a pass);
   - newest row two trading days old: `null`.
5. **Hold-out holds (part a).** A fresh look with 140 rows, log loss 0.652 against a cell-rate baseline of 0.671, P = 0.97 and a take-rule effect of +0.04 R gives `HoldOutBeatsNoMl` holds.
   - 96 rows fails `holdout_too_few_rows`.
   - P = 0.93 fails `holdout_not_beating_baseline`.
   - An effect of −0.02 R fails `holdout_policy_effect_negative`.
   - A second claim answers 409 `learning.holdout_reused` and the stored row is unchanged.
   - With no HO-01 block for the family, the answer is 409 `learning.holdout_unavailable` and nothing is written.
6. **Owner act.** The scoring endpoint without a fresh password answers 403 (step-up), and a wrong password answers 401 and counts toward the lockout.
   - `ml.holdout.auto_score` OFF (no row) never runs the pass.
   - ON runs it once for a champion whose development verdict is Eligible, recorded as `auto` with the snapshot.
   - An unavailable input never triggers it.
7. **Truth table unchanged.** All four terms holding with the switch OFF gives `MayInfluence=false` (`switch_off`). The ADR 0217 table tests stay green unchanged.
8. **Still advisory.**
   - `MlInfluenceArchitectureTests` stays green with `ShadowMlInfluenceEvidence` as the one named source.
   - The gate's consumers stay pinned empty.
   - Planning, Selection and Autonomy name none of the new types.
   - `ExecutionBoundaryArchitectureTests` stays green.
9. **Tripwire.** `MlRollbackArchitectureTests` stays green because the designate path now depends on the promote command (D6). Designating with an incumbent and no challenger evidence is refused with the promotion reasons.

## Contract
- **Data:**
  - Migration `AddShadowScores`: `paper.shadow_scores(id identity, entry_id → shadow_entries, model_id → research.model_versions, model_sha256 char(64), feature_version, p_win float8 CHECK 0..1, features_hash char(64), scored_at timestamptz default now(), UNIQUE(entry_id, model_id))`. Append-only for the app role.
  - Part a, migration `AddMlHoldOutScores`: `research.ml_holdout_scores(model_id PK, unlock_id, block_start, block_end, rows, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, mode, actor, scored_at)`. Insert-only.
  - `ml.holdout.auto_score` is a row in `research.ml_switch_settings`.
- **API:**
  - `POST /api/learning/models/{id}/holdout-score` (owner, CSRF, fresh password) answers 200 with the `MlHoldOutView` plus its figures. Errors: 403, 401, 404 `models.not_found`, 409 `learning.holdout_reused` or `learning.holdout_unavailable`, 422 `learning.holdout_no_rows`.
  - `PUT /api/learning/ml-settings` accepts `autoHoldOutScore`.
  - `GET …/influence` gains the interval.
  - `gen-api`.
- **CLI:** `xd learn influence` shows the figures; new verb `xd learn holdout-score MODEL-ID`.
- **UI:** the Models drawer's Influence section shows the figures (built in 10d-2). A "Score on hold-out" button opens the existing step-up dialog (part a).

## Test plan
- **Unit:** forward figures (odd and even n, ties); the calibration mapping; the hold-out comparison with an oracle bootstrap.
- **Property:** look-ahead on the scores.
- **Integration:** the score write and its cut-off; the grants; the claim and its refusals; the owner act; the auto switch.
- **Architecture:** as in AC 8 and AC 9.
- **Frontend (MSW):** the button and the step-up flow.

## Risks and shortcuts
- **The gate stays shut in practice for months.** It needs the shadow switch ON, 30 graded and 100 calibration rows, a named champion, an HO-01 block and the owner's switch. That is the intended outcome.
- **Scoring time is unmeasured.** Scoring every registered model each night is cheap but not yet timed: a hard cap of 5 models (`Confidence:MlInfluence:MaxScoredModels`), with the oldest challengers skipped and logged.
- `// shortcut:` the band trigger and `IChallengerEvidence` stay None.

## Safety
**Yes.** This slice gives real inputs to the predicate that gates model influence, adds an irreversible hold-out look (part a) and changes the designate path.
- No rule is loosened. `unavailable` is never a pass, every new switch of parts 1–2 defaults OFF (part 3's `ml.holdout.auto_score` defaults ON by ADR 0282, and takes only a pre-dated look), and the owner switch is unchanged.
- No consumer of the gate is added, so no plan, size, rank or order changes. ML stays advisory and never raises a size.
- No order is placed, and nothing references `XDrishti.Execution`.
- All three parts need the Opus reviewer.

## Done when
- [ ] Every AC has evidence; `scripts/check.sh` green; Opus reviewer: no blockers or majors.
- [ ] Migrations added; `gen-api` run; an ADR amending 0217 and 0220 written; docs/design/09 updated; roadmap row.

## As built (parts 10e-1 and 10e-2, 2026-11-12, ADR 0281)
- **Scores.** Migration `AddShadowScores` (raw SQL, insert-only, database-stamped time, refused from 09:00 India time, unique per entry and model). `ShadowScorer` (Application/Shadow) scores the champion and the challengers, up to five, for every candidate; the writer stores the scores in the session's transaction and a failed scoring writes the session without scores. Features: the extractor given only bars before the close preceding the session, plus the newest stored cell statistics before the session; no imputation.
- **Forward figures.** `ShadowForwardFigures` (Domain, pure) behind `ShadowMlInfluenceEvidence`, read through `IShadowScoreReader` (graded, counts-for-edge candidates).
- **Calibration (10e-2).** `ShadowRollbackEvidence` feeds the champion's graded scores to the P6-09 detector inside the monitor; `ShadowMlInfluenceEvidence` reads the newest `ml_monitor_checks` row: Fires true, Quiet false, anything else, no row or a row older than the last closed session null.
- **D6.** `OwnerDesignateModelHandler` asks `PromoteChallengerCommand` (actor `owner:` plus the command line's actor) when a different champion exists. The tripwire test stays green and unchanged.
- **Hold-out (10e-3) not built.** The hold-out source is still "unavailable", so the gate still refuses every model. No endpoint, setting, verb or UI of part a exists yet; AC 5 and 6 are open.
- **Deviations / shortcuts.** The cap of five models is a constant, not `Confidence:MlInfluence:MaxScoredModels`. A win is R above zero. The known-at instant of an observation is the close of the candidate's session. Cell features need a stored cash snapshot of window `all` before the session, else the candidate gets no score. The scorer reads 730 days of bars per instrument (a monthly cell gets no score); the nightly time is logged. No API change, so no `gen-api`. The interval in `MlInfluenceView` belongs to part a.
- **Evidence.** Unit: `ShadowForwardFiguresTests` (6), `ShadowEvidenceTests` (8), `ShadowScorerTests` (4, including the no-look-ahead property over mutated later bars), `OwnerDesignateModelHandlerTests` (two added); integration: the grants and guards test in `PaperShadowGuardsTests`; architecture: `MlInfluenceArchitectureTests` names `ShadowMlInfluenceEvidence` as the one source.

## Part 3 readiness (2026-11-13, main at 2918c76e)
The owner approved HO-01, SR-03 and this part on 2026-11-13 ("mostly things should be in auto mode with best default values"). The due diligence changes part 3 as follows; everything else in D5, AC 5 and AC 6 stands.
- **Own slot.** HO-01 D5 makes the fresh slot per `(block, kind)`, so a model look no longer burns the strategies' block (today: one shared slot, `LabRegistrationConfigurations.cs:82`). The `learning.holdout_reused` refusal now means "this family, or another model, already took the model slot of this block".
- **Pre-dated model.** A model look at block k ≥ 2 is fresh only when the model was registered before `S_k`; at block 1, before the first stored look of any kind (HO-01 D5). Otherwise it is stored `reused_window` with `not_predated` and cannot pass. In practice a model trained on a released block gets its look one block later.
- **Newest look, across blocks.** `ModelHoldOutLooks.ReadAsync` reads only the block in force (`Application/Learning/ModelHoldOutLooks.cs:47-52`); HO-01 D8 changes it to the model's newest fresh look across blocks, so a release does not erase the figures. `MlInfluenceEvaluator` keeps passing the figures only for the look's owner (`Application/Learning/MlInfluenceEvaluator.cs:27-33`).
- **When it runs.** The auto pass is a step of HO-01's `holdout-epochs` seal service: on the evening a block seals, with `ml.holdout.auto_score` ON, it scores the champion whose development verdict is Eligible and pre-dated. HO-01's auto release waits while such a champion has not looked.
- **Default ON.** `ml.holdout.auto_score` is a seed row `enabled = true`, actor `system:default`, reason `ADR 0282`, in `research.ml_switch_settings` (`Domain/Learning/MlSwitch.cs` gains the name). No row still reads OFF. It takes the look by a fixed rule at the seal, so no human chooses after seeing results.
- **Unchanged.** `ml.influence.enabled` stays OFF and the gate's consumers stay pinned empty (D7). The source registration is `ShadowMlInfluenceEvidence` (`Application/DependencyInjection.cs:981`), whose hold-out figures are null today (`Application/Learning/ShadowMlInfluenceEvidence.cs:11, 22`); part 3 fills them from `research.ml_holdout_scores`.
- **Decision record.** ADR **0285** (number reserved), amending ADR 0217 and 0281.
- **Size.** About 3 agent-days, as planned; SAFETY, Opus reviewer.
