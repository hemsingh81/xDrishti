# SR-02 — System replay: a no-selection baseline arm, a start-date sweep and the trade chart link

- **Status:** Built 2026-11-12, not deployed (approved as the coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended).
- **Phase / roadmap row:** SR — System replay, row SR-02 (GAP-07 of the 2026-11-12 due-diligence review)
- **Size:** about 4.5 agent-days (estimate, uncalibrated): **a** baseline arm and comparison 2 d · **b** start-date sweep 1.5 d · **c** replay-trade chart 1 d. The three parts are independent.
- **Needs:** SR-01 (built, ADR 0272), P8-11 trade chart (built), CF-06 trial ledger. Net figures need ISS-25, as in SR-01.
- **Design refs:** SR-01 spec ("Out": the baseline is noted for SR-02; part C deviation, line 236); ADR 0272, 0216 (paired day bootstrap), 0207.

## Goal
The replay answers three things:
- whether the system's choosing beat taking everything;
- whether the answer depends on the start date;
- what each replay trade looks like on the price chart.

## Facts (main at 30777841)
- **Selection and fill in a replay.** One session runs the live `SelectionPlanBuilder` over as-of ports (`Application/Replay/ReplaySessionRunner.cs:82-94`). Only `Primary` items are taken (:104-114). The candidates are `set.Found` from the ticket builder (:70-78).
- **The pair comparison.** `ReplayPairComparison.Compare` pairs daily returns and calls `ThreeArmLoopTest.Compare`, the CF-10c paired day bootstrap (`Domain/Replay/Comparison/ReplayPairComparison.cs:22-80`). It answers `TooLittleHistory` when there are fewer informative days than `LoopOptions.MinDays` (20) (:61-66). Equity is realised-only (`// shortcut`, :61). The bars are `HelpsAt` 0.95 and `HurtsBar` 0.95 (`Domain/Confidence/LoopOptions.cs:20-55`).
- **Runs and arms today.** `ReplayParams` has a mode (frozen or learning) and no arm (`Domain/Replay/ReplayParams.cs:16`). `ReplayPairRunner` runs one parameter set twice (`Application/Replay/Comparison/ReplayPairRunner.cs:11-26`).
- **One replay at a time.** A replay is active while queued, running or cancelling (`Domain/Replay/ReplayRunStates.cs:25`). A second start answers `replay.busy`.
- **Trials.** Each run writes `system_replay` rows to the trial ledger (`Application/Replay/Job/StartReplayHandler.cs:82`; `Application/Abstractions/Lab/ITestLedger.cs:31`).
- **Speed.** The measured engine speed is 0.88 s per session for 100 instruments (SR-01 part B evidence).
- **No chart link.** Replay trades have no link to the P8-11 chart (SR-01 spec line 236).
  - P8-11 reads `GET /api/reports/trades/{runKey}/{ordinal}` by sim run key (route `frontend/src/app/router.tsx:125`). It reads 1-minute bars by range through `IBarStore` (`Application/Reporting/GetTradeReplayHandler.cs:25`) and windows them with `Domain/Reporting/TradeReplayWindow.cs:11` (at most 5,000 bars).
  - P8-11 decision 3 rejected adding `from`/`to` to `GET /api/data/instruments/{id}/bars` (`Api/Endpoints/DataEndpoints.cs:36`).
  - `research.replay_trades` stores symbol, entry, stop, qty, exit time and gross P&L, but no exit price and no instrument id (`Infrastructure/Replay/ReplayStore.cs:25,301`).
- **Bar reads are clamped.** Replay bars are read only through `ClampedBars`, cut at the hold-out start less one day less the embargo (`Application/Replay/ClampedBars.cs:36-47`).

## Decisions (recommended, taken)
- **D1. The baseline arm** (`arm = baseline`, frozen mode only) takes every `set.Found` candidate of the session. It has no selection filters, no ranking, no smart rules, no cluster, sector or max-positions cap. It uses the same capital, profile, costs, scope, versions and cells as the selection arm.
  - **Equal risk.** Headroom H = `MaxTotalRiskPct` − open risk. Each of the n candidates risks r = min(`RiskPerTradePct`, H / n) of equity.
  - **Size limits.** Quantity is capped by `MaxCapitalPerTradePct` and by free cash; quantity < 1 is blocked as `capital`.
  - **Same fill path.** Fills and exits use the same `TicketReplayer` path, and a `data_missing` session takes nothing in either arm.
  - *Alternative:* a random-k baseline (the same number of trades as the selection arm, drawn by seed, 100 draws), which isolates ranking skill. Deferred: it costs 100 times the runtime.
- **D2. The comparison** reuses `ReplayPairComparison` unchanged in rule, generalised over the two arm names. The answers are `selection_better`, `baseline_better`, `no_measurable_difference` and `too_little_history`. It always shows n, the informative days and P. It inherits the realised-only shortcut, and says so.
- **D3. The sweep.** 2 to 12 start dates share one end date E (the SR-01 default end) and one parameter set.
  - **Queueing.** All runs are stored as `queued` in one transaction with a `sweep_id`, and the job claims them oldest first, so there is still one active replay.
  - **Trial count.** Each run is a CF-06 look (ledger rows written at the start). The screen says: "this sweep adds 12 replay looks".
  - **Start date rules.** Each start needs at least 126 sessions before E and frozen cell statistics as of the start. Otherwise the whole sweep answers 422, naming the bad dates.
  - **Spread.** The output is the min, median and max of CAGR and of max drawdown, the share of starts with CAGR > 0, and the share of starts beating Nifty 500.
  - *Alternative:* fixed-length windows (for example every 12 months). Rejected for now: with about 2 years of history that gives 1 to 2 windows.
- **D4. The chart** is a replay-trade reader, not a data endpoint change. `GET /api/replay/runs/{id}/trades/{tradeNo}/chart` returns the P8-11 `chart` block (bars, coherence) and markers.
  - **Bars.** It resolves symbol to instrument as of the entry session. Bars come through `ClampedBars` with `TradeReplayWindow`, so the window never reaches the hold-out: a window past the clamp is cut at the clamp and says so.
  - **Exit price.** It is derived as entry + gross P&L / (qty × side). This is exact for the gross figures the builder confirms. If not, a nullable `exit_price` is added for new runs.
  - The UI route `/simulation/replay/:runId/trades/:tradeNo` reuses `PriceChart`.

## Scope
**In:**
- Arm column, baseline step, arm comparison, the start request flag.
- Sweep table, sweep endpoints, sweep card.
- Chart endpoint, chart route.
- Tests and `gen-api`.

**Out:**
- Random-k baselines.
- Learning-mode baselines.
- Running two replays at once.
- Opening the hold-out (SR-03).
- Any evidence reader of replay tables (the ADR 0272 fence stays).
- Any order.

## Acceptance criteria
1. **Baseline sizing.** Equity is Rs 10,00,000, open risk 1.0 %, the standard profile (3 % total) and 8 candidates. Then H = 2 % and r = min(0.5 %, 0.25 %) = 0.25 %, which is Rs 2,500.
   - A candidate with entry 500 and stop 490 gets qty 250 (Rs 1,25,000, 12.5 % ≤ 25 %).
   - With 3 candidates, r = 0.5 %, which gives qty 500 and Rs 2,50,000 (25 %).
   - With H ≤ 0, nothing is taken, and every candidate is blocked as `total_risk`.
2. **Baseline takes all.** A fixture where selection takes 2 of 6 candidates: the baseline takes all 6. No `smart_rule`, `max_positions` or `cluster_cap` block appears in the baseline arm.
3. **Comparison.** A start with `baseline: true` creates a selection run and a baseline run with one `pairId`.
   - 250 paired days, 140 informative, P(selection better) = 0.97: `selection_better`.
   - P = 0.93 on both sides: `no_measurable_difference`.
   - 15 informative days: `too_little_history`, with n shown and P null.
4. **Sweep.** Sweep starts 2025-01-01, 2025-04-01 … with 12 CAGRs [−4.1, −1.2, 0.3, 2.0, 3.5, 4.4, 5.0, 6.1, 7.7, 9.0, 11.2, 14.6] % give min −4.1, median 4.7, max 14.6 and 10 of 12 positive (83 %), with the same for max drawdown.
   - A 13th date answers 422 `replay.invalid`.
   - A start inside 126 sessions of E answers 422 naming it.
   - A sweep while any replay is active answers 409 `replay.busy`, and so does a single replay started while a sweep has queued runs.
5. **Sweep order and cancel.** Runs execute one at a time in date order. A kill resumes as in SR-01 AC 13. Cancelling the sweep cancels the run in flight after its session and every queued run. The ledger rows stay (looks are never erased).
6. **Determinism.** Same inputs give the same result hash per run, and the arm is part of the input hash.
7. **Chart.** A replay trade with bars returns ≤ 5,000 bars from the session before entry to the session after exit, and `coherence.state=ok` when the first bar opens within a tick of the entry.
   - A trade whose window passes the clamp is cut, with a `limits` entry.
   - An unknown trade answers 404 `replay.not_found`.
   - A symbol with no instrument at the entry date gives `no_bars`.
8. **Fence.** `EvidenceReaderFenceArchitectureTests` and the replay write-list test stay green, with the sweep table added to the list. `ExecutionBoundaryArchitectureTests` stays green.

## Contract
- **API (owner-only, `gen-api`):**
  - `POST /api/replay/runs` gains `baseline?: bool` (default false).
  - `GET /api/replay/pairs/{pairId}` gains `selectionVsBaseline`.
  - `POST /api/replay/sweeps { starts[], to?, …SR-01 params }` answers 202 `{ sweepId, runIds[] }`.
  - `GET /api/replay/sweeps/{id}` answers the per-start rows (run id, state, CAGR, max DD, final equity, trades, beats benchmark), the spread, and `complete`.
  - `POST /api/replay/sweeps/{id}/cancel`.
  - `GET /api/replay/runs/{id}/trades/{tradeNo}/chart?timeframe&before&after`.
- **Data (migration `AddReplaySweeps`, schema `research`):**
  - `replay_sweeps(id, params jsonb, starts date[], created_at)`, insert-only.
  - `replay_runs.arm text NOT NULL DEFAULT 'selection' CHECK (arm IN ('selection','baseline'))`.
  - `replay_runs.sweep_id uuid NULL`.
- **UI:**
  - The System replay tab gains a "with no-selection baseline" toggle, a baseline line on the equity chart and an arm comparison card.
  - It gains a Sweep form (dates or "every quarter × n") and a sweep card with the per-start table and spread.
  - Trade rows open the chart route.
  - States: loading, empty, running, partial and cancelled.

## Test plan
- **Unit:** baseline sizing (AC 1); the arm comparison's four answers; the sweep spread and its date validation; the exit-price derivation.
- **Integration:** baseline pair end to end; sweep queue, resume and cancel; busy; the chart endpoint with the clamp.
- **Architecture:** fence and write list.
- **Frontend (MSW):** the new cards, the sweep states and the chart route.

## Risks and shortcuts
- **Sweep runtime.** 12 starts averaging 300 sessions over 100 instruments is about 3,600 × 0.88 s ≈ 53 min of engine time, which is fine.
- **Repeated looks.** 12 looks per sweep make over-reading easy. The look count and "data seen before" stay first on screen.
- **Biased baseline.** The baseline uses the same cell set as the selection arm (frozen, active cells), so it is "no choosing among the system's candidates", not "random stocks". The label says so.
- `// shortcut:` the comparison stays realised-only.

## Safety
**No.** This is research only:
- The ADR 0272 fence is kept, and no evidence reader can read replay tables.
- It writes only the replay tables and the trial ledger.
- It reads nothing past the hold-out clamp.
- It adds no switch and places no order.

## Backend as built (2026-11-12, ADR 0279)
- **Status:** backend of parts a, b and c built on `feature/sr-02`; frontend built (below); `scripts/check.sh` open.
- **a. Baseline.** `ReplayArm` on `ReplayParams` (left out of the stored text when `Selection`, so old hashes hold); `BaselineSizer` (pure, uses the live `PerAccountAllocator` with r = min(risk, headroom / n)); a branch in `ReplaySessionRunner`; label `baseline_no_selection`; `ReplayPairComparison.CompareArms` (seeded `sr02|`); `POST /api/replay/runs` takes `baseline` (frozen only) and queues a selection and a baseline run with one `pairId`; `GET /api/replay/pairs/{id}` answers `selectionVsBaseline` (`frozen`, `learning` and the old answer fields are then null). Runs and list rows carry `arm` and `sweepId`.
- **b. Sweep.** Migration `AddReplaySweeps` (Down included). `POST /api/replay/sweeps` (202 `{ runIds, pairId: null, trialNumber, sweepId }`), `GET /api/replay/sweeps/{id}`, `POST /api/replay/sweeps/{id}/cancel`. 422 names each bad start (fewer than 126 sessions, no cell statistics); 409 `replay.busy`. `ReplayFacts` now keeps one frozen snapshot per freeze date (the sweep prepares its runs on one instance).
- **c. Chart.** `GET /api/replay/runs/{id}/trades/{tradeNo}/chart?timeframe&before&after`: P8-11 `chart` block, `markers` (entry, stop, exit), `limits`, derived `exitPrice` (`exitPriceDerived: true`); bars unadjusted, as the replay read them.
- **Deviations.** (1) A symbol already open or repeated in a session is blocked as `capital` (`already_held`) in the baseline, so it is not doubled. (2) `exit_price` stays derived; no column was added. (3) The sweep is frozen and selection-arm only. (4) Sweep runs are one microsecond apart in `created_at` so the unchanged claim order is date order.
- **Tests.** Domain (`ReplaySweepTests`, `ReplayArmComparisonTests`), Application (`BaselineSizerTests`, `ReplayBaselineArmTests`, `ReplaySweepHandlerTests`, `ReplayTradeChartTests`, `ReplayArmPairReadTests`, `ReplayExitPriceTests`), Architecture lists (write list, fence), integration (`ReplayStoreTests` sweep case, grants and migration lists).

## Frontend as built (2026-11-12)
- **Breaking type change.** `ReplayPairView.frozen` and `.learning` are nullable; `pairInterval` polls a baseline pair by the arms of `selectionVsBaseline`.
- **a. Baseline.** A "With no-selection baseline" checkbox on the start form (frozen mode only; sends `baseline: true`). The equity chart draws the other arm of a pair as a dashed line. `ArmComparisonCard` (replaces the frozen-against-learning card for a baseline pair): hindsight labels, "not random stocks" label, the realised-only shortcut, paired days, informative days (n), P both ways, the answer in plain words, and the `reason` when `answer` is null. The run picker marks a baseline run.
- **b. Sweep.** A "New start-date sweep" form (every quarter x n, or explicit dates; shared end date, capital, risk profile, costs) posting `POST /api/replay/sweeps`; it says "this sweep adds N replay looks" before sending. `SweepCard` (`?sweep=<id>`, or the sweep of the chosen run): looks first, state, per-start table (state with sessions done, CAGR, max drawdown, final equity, trades, beats benchmark, link to the run), spread of the completed starts, a cancel button while running; polls every 3 s while running. 422 `replay.invalid` shows its per-start reasons, 409 `replay.busy` says a replay is running.
- **c. Chart.** Trade symbols link to the lazy route `/simulation/replay/:runId/trades/:tradeNo` (`ReplayTradeChartPage`): `PriceChart` with entry and stop levels and entry and exit marks, the "exit price is derived" note, the trade facts and the `limits`; timeframe tabs; 404 and no-bars states.
- **Deviations.** (1) The sweep form fixes the scope (auto), versions (today's active) and the two filter options (off), as the sweep is frozen and selection-arm only; only dates, end, capital, risk profile and costs are chosen. (2) No sweep list: a sweep opens from its address or from any of its runs. (3) The new route has no navigation entry (a detail route, like P8-11), and no e2e matrix row.
- **Tests.** `ReplayBaseline.test.tsx`, `ReplaySweep.test.tsx`, `ReplayTradeChart.test.tsx` (Vitest + MSW); fixtures `src/test/replaySr02Fixtures.ts`.

## Done when
- [ ] Every AC has evidence; `scripts/check.sh` green; reviewer: no blockers or majors.
- [ ] Migration added and `gen-api` run; ADR 0272 amended (baseline, sweep, chart); roadmap row updated.
