# SR-03 — A pre-registered system replay over a hold-out block

- **Status:** Built 2026-11-13 (ADR 0284; not merged or deployed). Approved (owner, 2026-11-13, in chat: "Consider my approval and you do due diligence also consider mostly things should be in auto mode with best default values"). All five questions as recommended, with the changes of the due diligence of 2026-11-13 (see *Due diligence*): the system look has **its own fresh slot per block** (HO-01 D5), the registration must be stored **before the block begins**, the run uses the **stored** inputs, and the registration and the run are **automatic by default** (ADR 0282). Build after HO-01b. ISS-25 (a `checked` sheet) is needed to register, not to build.
- **Phase / roadmap row:** SR — System replay, row SR-03 (GAP-07 of the 2026-11-12 due-diligence review)
- **Size:** about 3.5 agent-days (estimate, uncalibrated): registration and frozen inputs 1, the one-run handler and block reader 1, verdict and auto steps 1, card and tests 0.5.
- **Needs:**
  - SR-01 and SR-02 (built; SR-02's baseline arm, ADR 0279);
  - **HO-01b** (`docs/specs/HO-01-hold-out-epochs.md`, Approved): the `system` kind, the per-kind slot and the seal service;
  - to register, a `checked` sheet (ISS-25: the P3-03f exit check passed and `checked` written; CS-01 proposes the sheet).
- **Design refs:** ADR 0272 (replay fence, hold-out clamp), ADR 0279 (baseline arm), ADR 0207 and the CF-06 spec (pre-registration, one look, `reused_window`), HO-01 / ADR 0283, ADR 0282 (defaults), P3-03f (charge sheet). Decision record: **ADR 0284** (number reserved for this slice).

## Goal
The system fixes, before a block begins, exactly what it would run and what result would count. It then runs once over months it has never read. The answer, net of costs, is "pass", "fail" or "inconclusive". It is the only replay figure free of "data seen before".

## Facts (main at 2918c76e)
- **Replays never read the block.** Every replay bar read goes through `ClampedBars`. A cut after the hold-out start less one day less the embargo throws before anything is read, and with no block the read fails closed (`Application/Replay/ClampedBars.cs:12-14, 29-49`). SR-01 AC 3 pins it.
- **One fresh look per shared block today.** `lab.holdout_unlocks` allows `kind IN ('strategy','model')` only (`Infrastructure/Persistence/Configurations/LabRegistrationConfigurations.cs:65`), and one fresh look per block (`:82`). Any look marks the block as seen, and every later look is `reused_window`, which cannot pass (`Application/Lab/HoldOutUnlocker.cs:13-14`). HO-01b replaces this by one fresh slot per kind and adds `system`.
- **A run already uses stored inputs, never today's.** The runner reads the parameters and the settings snapshot stored with the run (`Application/Replay/Job/ReplayRunner.cs:20, 177-186`); the snapshot holds the selection rules, smart rules, fill limits, basket, sizing and budgets (`Application/Replay/ReplaySettingsSnapshot.cs:18-25`).
- **Determinism.** A run's input hash covers its parameters, settings snapshot, scope, membership, statistics snapshot keys, versions and bars (`Application/Replay/ReplayInputHasher.cs:14-31`), and a changed input fails the run with `replay.diverged` (`ReplayRunner.cs:192-195`).
- **Gross by default.** A run is gross and labelled `before_costs` while no sheet is chosen (`Domain/Replay/ReplayParams.cs:6`). ISS-25 is Open: the sheet is not `checked` (docs/design/18-roadmap.md, ISS-25 row).
- **The arm comparison exists.** SR-02 answers `selection_better`, `baseline_better` or `no_measurable_difference` (`Domain/Replay/Comparison/ReplayArmResult.cs:25-27`); the baseline arm runs in frozen mode only (`ReplayParams.cs:54-56`).
- **Trials.** A replay already writes `system_replay` trial-ledger rows (`Application/Abstractions/Lab/ITestLedger.cs:31`).
- **Memory cap.** `Replay:MaxWindowMb` (300) allows about 53 instruments (docs/design/11-strategy-lab.md §7).

## Decisions (approved)
- **D1. Pre-registration with frozen inputs.** An insert-only registration stores, as canonical JSON with its SHA-256 (`config_hash`):
  - the target block, by its start date;
  - the replay parameters: scope (instrument and basket ids), capital, profile, cost sheet id and its content hash, version rule `start`, mode `frozen`, both arms (selection and baseline);
  - the full `ReplaySettingsSnapshot` taken at registration;
  - the strategy versions by hash;
  - the engine version;
  - the pass rule (D3).

  The run uses these stored inputs, as every replay run already does, so a later change of live settings does not change the registered system. *Why (due diligence change 2):* the draft recomputed the hash from live state and refused on any difference; over the 6 to 12 months between registration and seal that check would almost surely fail and leave the block unread.
- **D2. The horizon.** A registration is valid only for a block that has not begun: it must be stored before `S_k` (India date), for k ≥ 2 (HO-01 D5). A block's start is known once the block before it is stored, so the target is the earliest block of the chain whose start is after today; while the newest stored block is sealed and not released, there is none and the answer is 422 `replay.registration_invalid: no_future_block`. **Block 1 never takes a system look.** Several registrations may be stored for a block; the newest one stored before `S_k` is the one, and the others read `superseded`.
- **D3. The pass rule, fixed at registration.** All figures are net.
  - **Pass** needs all of:
    - at least 30 closed trades in the selection arm;
    - CAGR > 0;
    - max drawdown ≤ the registered limit (default 15 %);
    - the SR-02 arm comparison is not `baseline_better`.
  - **Fail:** 30 or more trades and any of the other conditions false.
  - **Inconclusive:** fewer than 30 trades.
  - Any `unavailable` figure: never pass (inconclusive with the reason).
  - *Alternative:* require `selection_better`. Stricter, and on a 6-month block (about 125 sessions) P ≥ 0.95 is unlikely even for a real edge.
- **D4. One read.**
  - The run first checks that every stored input still resolves: the versions by hash, the members, the statistics snapshot (the newest with an as-of date before `S_k` and created before `S_k`), and the cost sheet with the same content hash. Any miss answers 409 `replay.registration_drift` before the claim, so nothing is burned. A different engine version is not a refusal; the result records both and is labelled `engine_changed`.
  - It then claims the look, kind `system` (HO-01), insert-only, burned even if the run is abandoned.
  - It runs both arms over `[S_k, E_k]` with a block reader that only this handler may construct, after the claim, and only up to the block end. The block must be sealed (HO-01 D6, 409 otherwise).
  - A second run answers 409 `replay.holdout_used` with the stored result.
  - *Amended in the build (ADR 0284):* a look that cannot be fresh (the registration was stored on or after the block's start, or the slot is taken) is refused with nothing stored and nothing read, instead of being stored `reused_window`: a look that could never count is not burned.
  - *Amended:* the drift check also refuses when the stored block's end is not the registered one (a changed `BlockMonths`), when the sheet lost its `checked` note, when the scope has grown over the memory cap, and when the newest cell-statistics snapshot was created on or after the block start; the snapshot keys and creation time are stored with the result. The claim appends one `system_replay` trial-ledger row per strategy version, keyed by the look.
  - *Amended:* a run that cannot be carried out is stored `run_failed` (never `inconclusive`, which is for an unavailable figure of the rule); a database, timeout or I/O failure leaves the claim and is retried up to 5 times first.
  - **Owner act:** `POST …/run` with a fresh password.
- **D5. Automatic by default** (ADR 0282; due diligence change 3).
  - `replay.holdout.auto_register`, **default ON**: on the evening block k seals (HO-01 seal service), if no registration targets block k+1 and a `checked` sheet exists, the current configuration is registered for block k+1 as `auto` (scope = the Auto scope, capital and profile = the replay defaults, the sheet `checked` most recently, the D3 defaults). This is the last evening before `S_{k+1}`, so the registration is "the system as it stands when the block begins". A scope over the memory cap, or any unavailable input, registers nothing and raises a notice.
  - `replay.holdout.auto_run`, **default ON**: on a seal-service evening, a registration whose block is sealed and has no result is run once, recorded `auto` with the settings snapshot.
  - Turning either OFF or back ON is a fresh-password act. An `unavailable` input never registers or runs. Neither ever places an order.
  - *Why ON:* the registration is fixed before the block exists, and the run then follows a fixed rule at a fixed time, so no human chooses when or whether to look after seeing forward results. That protects the evidence.
- **D6. Research only.** The result stays behind the ADR 0272 fence. No evidence reader (scorecard, Lab verdicts, CF-12, training) reads it. It is shown with its hindsight label: "unseen by the system; the owner may have seen these prices elsewhere".
  - *Alternative:* feed the readiness scorecard. That would be a new evidence path with its own ADR, and is not part of this slice.

## Worked dates
- Block 2 (2026-11-05..2027-05-04) has begun, so it can take no system look.
- Block 3 (2027-05-05..2027-11-04): with a `checked` sheet by then, the auto registration is stored on the evening of 2027-05-04; the owner may store his own before that. The auto run follows on the evening block 3 seals (2027-11-04), provided block 2 has been released and block 3 is the block in force.

## Scope
**In:**
- the registration table, the frozen inputs and the endpoint;
- the one-run handler with the block reader;
- the `system` look through HO-01's store;
- the verdict, the two auto settings and their steps in the seal service;
- the result card, the architecture pins and `gen-api`.

**Out:**
- advancing or creating blocks (HO-01);
- filling in or checking the charge sheet (an owner act);
- more than one read per block;
- any evidence use of the result;
- any order.

## Acceptance criteria
1. **Register.** On 2027-04-20, a registration for block 3 with sheet `dhan-2026-04` (`checked`) stores `config_hash` and the frozen inputs; the same inputs give the same hash. Each of an unchecked sheet, a gross choice, no future block (`no_future_block`, which includes a block that has begun) or a scope over the memory cap answers 422 `replay.registration_invalid`, naming the reason. A registration stored on 2027-05-05 for block 3 is refused (`no_future_block`).
2. **Newest before the horizon.** Two registrations for block 3 on 2027-04-01 and 2027-04-20: the second is the one, the first reads `superseded`.
3. **Live changes do not drift.** After registration a selection setting changes; the run uses the stored snapshot and its result is the same as with the setting unchanged.
4. **Drift.** The registered sheet is replaced with a new content hash: the run answers 409 `replay.registration_drift`, writes no look and reads no bar of the block.
5. **One look.** The first run claims the `system` look and stores one result row holding both arms' figures (the arms are not stored as `replay_runs` rows, so no replay read can show the block's prices; `runIds` are the stable ids of the two arm results). A strategy look and a model look at the same block stay fresh.
   - A second run answers 409 `replay.holdout_used` with the stored result unchanged.
   - A filling block answers 409 `lab.holdout_filling`.
   - Without a fresh password: 403; a wrong one: 401, counted toward the lockout.
6. **Verdict.** Over a 6-month block:
   - 41 trades, net +3.1 % (CAGR about 6.3 %), max DD 7.4 % and `no_measurable_difference`: `pass`;
   - the same with DD 16.0 %: `fail`;
   - 22 trades: `inconclusive`;
   - `baseline_better`: `fail`;
   - any unavailable figure: never `pass`.
7. **Reach.** An architecture test pins that only the SR-03 handler constructs the block reader, and only after the claim. Every other replay path keeps `ClampedBars`. A property test checks that no bar after the block end is read.
8. **Auto register.** Default ON. On the evening block 2 seals, with a `checked` sheet and no registration for block 3: one `auto` registration for block 3. With an owner registration already stored: none. With no `checked` sheet: none, and a notice. OFF: none.
9. **Auto run.** Default ON. On the evening block 3 seals: the registration runs once, recorded `auto`. While the sheet's content hash differs: no run (drift) and a notice. OFF: no run.
10. **Auto release waits.** While a registration for a sealed block has no result, HO-01's auto release keeps the block (HO-01 AC 7).
11. **Fence.** The SR-01 fence and write-list tests stay green, and `ExecutionBoundaryArchitectureTests` stays green.

## Contract
- **API (owner-only, `gen-api`):**
  - `POST /api/replay/holdout-registrations` (session, CSRF) answers `{ id, configHash, blockStart }`.
  - `GET /api/replay/holdout-registrations`.
  - `POST /api/replay/holdout-registrations/{id}/run` (fresh password) answers 202 `{ runIds[] }`.
  - `GET …/{id}` answers the verdict, the figures, the rule, the look and the state (`waiting`, `superseded`, `run`, `drift`).
  - `PUT /api/replay/settings` accepts `autoRegister` and `autoRun` (fresh password).
- **Data (migration `AddReplayHoldOutRegistrations`, schema `research`, insert-only):**
  - `replay_holdout_registrations(id, block_start, config jsonb, config_hash, mode 'owner'|'auto', actor, created_at)`.
  - `replay_holdout_results(registration_id PK, unlock_id, run_ids, verdict, figures jsonb, engine_versions, mode, actor, at)`.
  - `replay_holdout_settings(id, name, enabled, actor, reason, at)`, append-only, with the two seed rows `enabled = true`, actor `system:default`, reason `ADR 0282` (no row reads OFF; an unreadable table reads unavailable and does nothing).
  - The look is a `kind = 'system'` row as HO-01 defines it.
- **UI:** on the System replay tab, a "Hold-out replay" card with the registration form, the frozen hash, the target block and its start, a run button behind the step-up dialog, the two auto switches, and the verdict with its rule shown first.

## Test plan
- **Unit:** the config hash; the horizon and the target block; the verdict rule (AC 6).
- **Integration:** register, supersede, drift, the one look and its refusals, the owner act, the two auto steps, the seed rows.
- **Architecture:** AC 7 and AC 11.
- **Frontend (MSW):** the card states.

## Risks and shortcuts
- **A single block is one draw.** "Inconclusive" is the likely answer on 6 months.
- **The first answer is late.** Block 3 seals on 2027-11-04; nothing before it can give a fresh system look.
- **Irreversible.** Burning the block by mistake cannot be undone: the input check runs before the claim, and the button states that the read is irreversible.
- **The auto registration takes today's Auto scope.** If that is above the 53-instrument memory cap, nothing is registered until the owner narrows the scope or raises the cap.
- `// shortcut:` realised-only equity (SR-01).

## Safety
**Yes (light).** It writes an irreversible record (the look) and reads a hold-out block once.
- It loosens no rule. A reused look never counts, `unavailable` is never a pass, and every automatic act is recorded as `auto`, never as an owner approval.
- It changes no live state: no plan, size, setting or model.
- It places no order, and it references no `XDrishti.Execution`.
- It needs the Opus reviewer.

## Due diligence (2026-11-13)
1. **Own slot per block (question 1).** HO-01 D5 now gives one fresh slot per kind, so a system look burns neither the strategies' nor the models' block. The registration must precede the block's start; block 1 is excluded.
2. **Frozen inputs instead of a live drift check (D1, D4).** The runner already runs from a stored settings snapshot; a live recomputation would have refused almost every real run.
3. **Auto register and auto run ON (D5).** The owner's 2026-11-13 instruction; both act by a fixed rule at a fixed time, which protects the pre-registration.
4. **Facts refreshed** to main 2918c76e: SR-02 is built (the arm answers), the runner's stored snapshot, the replay line numbers.

## Owner questions (answered 2026-11-13, as recommended)
1. Which block? **Its own slot at every block from block 3 on** (HO-01 D5 per kind; a registration before the block begins; block 1 excluded, block 2 already begun).
2. The pass rule: **D3**, 15 % drawdown limit and "not `baseline_better`".
3. Net figures from a `checked` sheet required, no gross read ever counting? **Yes.**
4. Result research-only (D6)? **Yes.**
5. `replay.holdout.auto_run`? **Yes**, and with `auto_register`, default **ON** under the owner's 2026-11-13 instruction (ADR 0282).
