# SR-03 — A pre-registered system replay over a hold-out block

- **Status:** Draft — needs the owner's decision: a one-time irreversible read of a hold-out block; needs HO-01 and a confirmed charge sheet. Recommended option: register the configuration now and freeze its hash. Read the block once, by an owner act with a fresh password, only on a block that HO-01 grants to system looks, and only when the charge sheet's bill check has passed (ISS-25). Judge it by a rule fixed at registration. Keep the result research-only.
- **Phase / roadmap row:** SR — System replay, row SR-03 (GAP-07 of the 2026-11-12 due-diligence review)
- **Size:** about 3 agent-days (estimate, uncalibrated)
- **Needs:**
  - SR-01 (built) and SR-02 part a (the baseline arm);
  - **HO-01** (`docs/specs/HO-01-hold-out-epochs.md`, Draft);
  - **ISS-25** closed: the P3-03f exit check is passed and `checked` is written.
- **Design refs:** ADR 0272 (replay fence, hold-out clamp), ADR 0207 and the CF-06 spec (pre-registration, one look, `reused_window`, auto option D6), P3-03f (charge sheet).

## Goal
The owner fixes, in advance, exactly what system he would have run and what result would count. The system then runs once over months it has never read. The answer, net of costs, is "pass", "fail" or "inconclusive". It is the only replay figure free of "data seen before".

## Facts (main at 30777841)
- **Replays never read the block.** Every replay bar read goes through `ClampedBars`. A cut after the hold-out start less one day less the embargo throws before anything is read, and with no block the read fails closed (`Application/Replay/ClampedBars.cs:11-14, 36-47`). SR-01 AC 3 pins it.
- **One fresh look per shared block.** `lab.holdout_unlocks` allows `kind IN ('strategy','model')` only (`Infrastructure/Persistence/Configurations/LabRegistrationConfigurations.cs:65`). Any look marks the shared block as seen, and every later look is `reused_window`, which cannot pass (`Application/Lab/HoldOutUnlocker.cs:13-14`; CF-06 spec, amendment 3). So under today's rule a system look would burn the strategies' and the models' one block. That is why this slice needs HO-01.
- **Today's runs are gross.** A run is gross and labelled `before_costs` while no sheet is chosen (`Domain/Replay/ReplayParams.cs:4`). ISS-25 is Open: the sheet is not filled in (docs/design/18-roadmap.md, ISS-25 row).
- **Determinism.** A replay's input hash covers its parameters and settings snapshot, and the same input gives the same result hash (SR-01 AC 16; `Application/Replay/ReplayInputHasher.cs`).
- **Trials.** A replay already writes `system_replay` trial-ledger rows (`Application/Abstractions/Lab/ITestLedger.cs:31`).

## Decisions (recommended; the owner decides)
- **D1. Pre-registration.** An insert-only registration stores the configuration's canonical JSON and its SHA-256 (`config_hash`). The configuration is:
  - the strategy version hashes under `versions=start`;
  - the settings snapshot hash;
  - the resolved scope (instrument ids);
  - capital and profile;
  - the cost sheet id and its content hash;
  - arms selection and baseline, mode frozen;
  - the engine version;
  - the pass rule (D3);
  - the target block (HO-01 id).

  A registration is free and can be replaced by a new one until the read; nothing is read before then.
- **D2. One read, an owner act.**
  - `POST …/run` takes a fresh password. It recomputes `config_hash` from the live state, and any difference answers 409 `replay.registration_drift` before the claim, so nothing is burned.
  - It then claims the look, kind `system` per HO-01, insert-only and burned even if the run is abandoned.
  - It runs both arms over [block start, block end] with a block reader that only this handler may construct, after the claim, and only up to the block end.
  - A second run answers 409 `replay.holdout_used` with the stored result.
  - A run on a block already seen by another look is shown as `reused_window` and never counts.
  - Auto option (CLAUDE.md rule): `replay.holdout.auto_run`, default **OFF**, and turning it ON is an owner act with a fresh password. ON runs the registration once when its block is available and the sheet is `checked`. The run is recorded as `auto` with the snapshot. An unavailable input never runs it.
- **D3. The pass rule, fixed at registration.** All figures are net.
  - **Pass** needs all of:
    - at least 30 closed trades in the selection arm;
    - CAGR > 0;
    - max drawdown ≤ the registered limit (default 15 %);
    - the SR-02 arm comparison is not `baseline_better`.
  - **Fail:** 30 or more trades and any of the other conditions false.
  - **Inconclusive:** fewer than 30 trades.
  - *Alternative:* require `selection_better`. That is stricter, and on a 6-month block (about 125 sessions) P ≥ 0.95 is unlikely even for a real edge.
- **D4. Research only.** The result stays behind the ADR 0272 fence. No evidence reader (scorecard, Lab verdicts, CF-12, training) reads it. It is shown to the owner with its hindsight label: "unseen by the system; the owner may have seen these prices elsewhere".
  - *Alternative:* feed the readiness scorecard. That would be a new evidence path with its own ADR, and is not part of this slice.

## Scope
**In:**
- Registration table and endpoint.
- The one-run handler with the block reader.
- The `system` look (through HO-01's API).
- The verdict, the auto setting, the result card, the architecture pins and `gen-api`.

**Out:**
- Advancing or creating blocks (HO-01).
- Filling in the charge sheet (an owner act).
- More than one read per block.
- Any evidence use of the result.
- Any order.

## Acceptance criteria
1. **Register.** A registration with sheet `dhan-2026-04` (`checked`) stores `config_hash`, and the same inputs give the same hash. Each of an unchecked sheet, a gross choice, or a block HO-01 does not grant to system looks answers 422 `replay.registration_invalid`, naming the reason.
2. **Drift.** After registration a setting changes. The run answers 409 `replay.registration_drift`, writes no look and reads no bar of the block.
3. **One look.** The first run claims the look and stores two runs flagged `holdout_block`.
   - A second run answers 409 `replay.holdout_used` with the stored result unchanged.
   - Without a fresh password it answers 403, and a wrong password answers 401 and counts toward the lockout.
4. **Verdict.** Over a 6-month block:
   - 41 trades, net +3.1 % (CAGR about 6.3 %), max DD 7.4 % and `no_measurable_difference`: `pass`;
   - the same with DD 16.0 %: `fail`;
   - 22 trades: `inconclusive`;
   - `baseline_better`: `fail`;
   - any unavailable figure: never `pass`.
5. **Reach.** An architecture test pins that only the SR-03 handler constructs the block reader, and only after the claim. Every other replay path keeps `ClampedBars`. A property test checks that no bar after the block end is read.
6. **Auto.** `replay.holdout.auto_run` OFF (no row) never runs. ON runs it once, recorded `auto`, and does not run while the sheet is unchecked.
7. **Fence.** The SR-01 fence and write-list tests stay green, and `ExecutionBoundaryArchitectureTests` stays green.

## Contract
- **API (owner-only, `gen-api`):**
  - `POST /api/replay/holdout-registrations` (session, CSRF) answers `{ id, configHash }`.
  - `GET /api/replay/holdout-registrations`.
  - `POST /api/replay/holdout-registrations/{id}/run` (fresh password) answers 202 `{ runIds[] }`.
  - `GET …/{id}` answers the verdict, the figures, the rule and the look.
  - `PUT /api/replay/settings` accepts `autoRun`.
- **Data (migration `AddReplayHoldOutRegistrations`, schema `research`, insert-only):**
  - `replay_holdout_registrations(id, config jsonb, config_hash, block_id, created_at, actor)`.
  - `replay_holdout_results(registration_id PK, unlock_id, run_ids, verdict, figures jsonb, mode, actor, at)`.
  - The look is a `kind = 'system'` row as HO-01 defines it.
- **UI:** on the System replay tab, a "Hold-out replay" card with the registration form, the frozen hash, a run button behind the step-up dialog, and the verdict with its rule shown first.

## Test plan
- **Unit:** the config hash; the verdict rule (AC 4).
- **Integration:** register, drift, the one look and its refusals, the owner act, the auto switch.
- **Architecture:** AC 5 and AC 7.
- **Frontend (MSW):** the card states.

## Risks and shortcuts
- **A single block is one draw.** "Inconclusive" is the likely answer on 6 months.
- **Irreversible.** Burning the block by mistake cannot be undone: the drift check runs before the claim, and the button states that the read is irreversible.
- `// shortcut:` realised-only equity (SR-01).

## Safety
**Yes (light).** It writes an irreversible record (the look) and reads a hold-out block once.
- It loosens no rule. A reused block never counts, `unavailable` is never a pass, and the auto setting defaults OFF.
- It changes no live state: no plan, size, setting or model.
- It places no order, and it references no `XDrishti.Execution`.
- It needs the Opus reviewer.

## Owner questions
1. Which block? Does a system look get its own HO-01 epoch, or the next shared one only after strategies and models have taken theirs? *Recommended:* its own epoch, if HO-01 allows it.
2. The pass rule: is it D3 with a 15 % drawdown limit and "not `baseline_better`", or the stricter "`selection_better`"? *Recommended:* D3.
3. Should net figures from a `checked` sheet be required, with no gross read ever counting? *Recommended:* yes.
4. Should the result stay research-only (D4)? *Recommended:* yes.
5. Should `replay.holdout.auto_run` exist, default OFF? *Recommended:* yes, and keep it OFF.
