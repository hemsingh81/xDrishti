# SR-01 — Whole-system replay from a chosen date

- **Status:** Approved (owner, 2026-11-11, in chat: "Approve all", every open question as recommended). Written 2026-11-11 after the owner asked for it in chat.
- **Phase / roadmap row:** SR — System replay, row SR-01 (new; see docs/design/18-roadmap.md)
- **Size:** about 13.5 agent-days in five parts (an estimate, not calibrated) · **Needs:** built P7 pipeline, P3-04a ports, CF-08 `TicketReplayer` path, P10-02 `PaperLedger`, AU-11 fence, CF-06 hold-out and trial ledger; better with CF-04 B/C (five years of history)
- **Decisions:** ADR 0272 (to be written at build: the replay is research, fenced from evidence, hindsight-labelled)
- **Design refs:** docs/design/09-learning-engine.md, 10-trade-selection.md, 11-strategy-lab.md, 13-reports-and-ui.md, 19-risks-and-compliance.md; ADR 0041 (run labels say "portfolio limits: not simulated"), 0064, 0077, 0086, 0207, 0217, 0243, 0251, 0266

## Goal
The owner picks a start date and sees how the **whole system** would have run from that day on. For each session that means the plan it would have made, what it took and what it blocked and why, the fills, and a portfolio equity curve against Nifty 500. The system can be frozen at the start date or keep learning as it goes, and a paired run says whether learning helped.

## Why
Every result today is per strategy and per trade. The simulator never calls selection, the portfolio filler, sizing or the caps, so no number answers "would the system have made money, after costs, as a portfolio?" The P7 nightly pipeline is built from pure pieces, and the facts it needs can mostly be read as of a past date. Reusing both closes the gap without a second engine.

## Facts this spec is built on (main at b22b90f8)
- `RunNightlyPlanHandler` takes `RunNightlyPlanCommand(DateOnly? Date, …)`, but it refuses a session that already has a plan (ADR 0086 §3) and writes to `IPlanStore`. Its gates (Calendar, Eod, PortfolioSync, FreshData, Quality, SelectionInputs) read only the newest records. **The replay does not call this handler.** It calls the pieces below and never touches the planning tables.
- These are pure and reusable as they are: `CandidateSelector.Select`, `SmartRulesEngine.Apply`, `CorrelationClusters.Compute`, `PortfolioFiller.Fill`, `SizeScaler.Apply` (internal static, BK-10), `PlanWiring.Wire`, `TicketBuilder.Build`, `PaperLedger.Compute` and `RealisedSeries`.
- These can already be read as of a date:
  - cell stats: `ICellStatsStore.ListLatestBeforeAsync`, plus the P6-01 calculator, which reads only trades exited on or before the as-of date;
  - cell state: `CellStateAsOf.Of(changelog, cut)`;
  - basket members: `IBasketQueries.MembersAsync(id, cutoff, asOf)`, where system baskets are clamped to `knownSince`;
  - market context: `IContextFigures.AsOfAsync` / `AsOfManyAsync`;
  - basket context: `LatestBeforeAsync`;
  - correlations and sizing bars: day bars up to the session.
- These are read only as of now today:
  - strategy versions (`GetVersionAsync(id, null)`; an as-of read can be derived from `StoredVersion.CreatedAt`);
  - the model champion (derivable from `DesignationsAsync`);
  - auto scope and the planner universe (`MembersAsync(id, null, null)`);
  - basket, sizing and event settings (`CurrentAsync`; the tables are insert-only, so an as-of read is derivable);
  - accounts, risk profiles, holdings and the ramp (`StoredPlanContextSource`, which says so in a "shortcut" comment).
- `TicketReplayer.ReplayAsync` is the one place that builds a simulation query for a ticket (`PaperCaptureArchitectureTests`). CF-08 shadow grading uses it, and costs come from `ReplayCosts`. **ISS-25 is still Open**: the charge sheet is not filled in, so real runs are gross.
- The ML influence gate refuses every model (`NoMlInfluenceEvidence`, ADR 0217). The Planning, Selection and Autonomy namespaces depend on no score source (`MlInfluenceArchitectureTests`).
- The AU-12 job pattern is `SimAllRunner` hosted by `SimAllService` (a BackgroundService). Chunks are the queue (`FOR UPDATE SKIP LOCKED`, redo of stale chunks), and `YieldAsync` steps aside for low disk, market hours, data jobs and `ISimAllBackfillGate`. There is no Hangfire.
- No spec or ADR proposes a portfolio-level replay. The "SR" prefix is free, and ADR 0272 is the next free number.

## Scope
**In:**
- A replay engine that steps one session at a time.
- An as-of fact context for each session.
- A replay book and equity computed with `PaperLedger`.
- Two learning modes and a paired comparison.
- A resumable job, its tables, API and screen.
- Fences and architecture tests.

**Out (not in this slice):**
- Any order, broker call or reference to `XDrishti.Execution`.
- Writing to planning, paper, shadow, cell-stats, model or settings tables.
- ML model influence, which stays behind the ADR 0217/0220 gate exactly as in the live system, so today no model influences a replay.
- Opening the hold-out.
- Options, pairs or basis expressions; cash cells only (ADR 0193).
- A "no selection" baseline run (all candidates, equal weight), which would show whether selection adds value; noted for SR-02.
- Running several replays at once.

## How one session is replayed
For each session S from the start date D to the end date E, strictly in order (each session needs the book left by the one before):

1. **As-of context.** Every fact is read through one `IReplayFacts` port that takes `cut = close of S`:
   - **Universe:** the chosen baskets with members as of S. For any date before `knownSince` the earliest known membership is used instead, and the session carries the label `survivorship` (ISS-22).
   - **Bars:** stored bars up to S only.
   - **Strategy versions:** chosen per run (see Q1).
   - **Cell stats and cell state:**
     - `frozen`: as of D, for the whole run.
     - `learning`: recomputed in memory as of S with the P6-01 calculator, on the cadence of the scheduled cell-stats job. Nothing is stored to `learning.cell_stats`.
   - **Market gate, regime, correlations:** as of S.
   - **Settings:** the snapshot taken at job start, recorded with the run (see Q2).
2. **Candidates.** `TicketBuilder.Build` runs over each universe member, the same way `StoredTicketSource` does, but with the as-of reads.
3. **Selection and fill.** `CandidateSelector`, `SmartRulesEngine`, `CorrelationClusters`, `SizeScaler` and `PortfolioFiller` run against the replay book and replay equity.
   - The replay account has a capital and risk profile chosen at the start. Defaults: Rs 10,00,000 and the seeded `standard` profile (0.5 % risk, 25 % max capital), as in Q4 of the plan questions.
   - The live gates are skipped. A session whose bars are incomplete for more than the configured share of the universe is marked `data_missing`. It takes no new trades, but open positions still exit.
4. **Fills.** Each taken plan item is replayed on the next session through `TicketReplayer`, with the run's `CostChoice`. Positions held across sessions keep their slot and risk until their exit, and the exit is booked on its exit session.
5. **Book.** `PaperLedger.Compute` gives equity, open risk and the realised series after S. The session record stores:
   - the candidates;
   - the ranked list;
   - what was taken;
   - what was blocked, each with one reason from a closed list: `gate`, `smart_rule`, `max_positions`, `total_risk`, `cluster_cap`, `capital`, `data_missing`, `no_version`;
   - the fills;
   - equity at the close.

## Acceptance criteria
1. **Start.** Given a start date D, an end date E (default: the last closed session before the hold-out block start minus its embargo), a scope (baskets and/or stocks), a capital, a risk profile, a cost choice, a version rule and one or both learning modes, when the owner starts a replay, then a job is stored with an input hash and the settings snapshot, and it answers 202 with its id.
2. **Bad input.** D after E, D before the first stored bar, an empty scope, or a capital ≤ 0 answers 422 `replay.invalid`. A second replay while one is running answers 409 `replay.busy`.
3. **Hold-out.** An E inside or after the hold-out start (minus the embargo) answers 409 `replay.holdout`, saying the last allowed date. No session inside the hold-out is ever read: an architecture test pins that `IReplayFacts` reaches bars only through a reader clamped to the hold-out start.
4. **Point in time.** For every fact in the as-of context, a test with a fixture holding values before and after S proves that the replay of S reads only values known at S's close. This covers bars, members, cell stats in both modes, cell state, market context, correlations and strategy versions under the `asOf` rule. A look-ahead property test (as in P3-08) runs a replay with the bars after S mutated and asserts that session S's plan and fills are unchanged.
5. **Same pieces as live.** For a session where the live system stored a plan, a replay with `versions=start`, `frozen` at S, and a book and settings equal to the live ones gives the same taken items and the same block reasons as the stored plan. This is a golden test on fixture data. Any difference is a defect, not a tolerance.
6. **Portfolio limits are real.**
   - A fixture where the filler must refuse proves that `max_positions`, `total_risk` and `cluster_cap` blocks show up in the session record with their reasons.
   - An open swing position holds its slot on later sessions until its exit.
7. **Equity.** Equity, drawdown and the realised series come only from `PaperLedger.Compute` over the replay trades. A test reconciles the final equity with capital plus the sum of net trade P&L.
8. **Costs.**
   - With no filled-in charge sheet, the run is gross. Every response and the screen label it "before costs (charge sheet not filled, ISS-25)".
   - With a sheet, net and gross are both stored.
9. **Learning modes.** With both modes chosen, two linked runs share D, E, scope and settings.
   - The comparison uses `PairedDayBootstrap` (CF-10c) over the daily returns and answers one of: `learning_better`, `frozen_better`, `no_measurable_difference`, `too_little_history` (fewer than the bootstrap's minimum days).
   - It always shows its n and its probability.
10. **Hindsight label.** Every run states plainly which parts are hindsight:
    - strategies written after D (`versions=start`);
    - settings from now;
    - survivorship in membership;
    - "data seen before" (the strategies were designed on this history).
    The label is on the API response and is the first line of the screen, never hidden behind a click.
11. **Fence.** Replay results are research only.
    - No reader of evidence (cell stats sources, Lab verdicts, readiness scorecard, CF-12 decay, model training) can read replay tables. This is extended in `EvidenceReaderFenceArchitectureTests`.
    - The replay writes to no planning, paper, shadow, learning, model or settings table. An architecture test lists the tables it may write.
    - Planning code does not name replay types.
12. **Trials.** Each started run is recorded in the CF-06 trial ledger as a look of kind `system_replay`, with its input hash. The run screen shows how many replays have been run over the same range ("the 7th replay of this range").
13. **Resumable.**
    - The job stores a cursor after each session.
    - Killing the process mid-run and starting again resumes at the next session and gives byte-identical session records (same input hash, same result hash).
    - The job yields under the same rules as `SimAllRunner.YieldAsync`, and does not run while a simulate-everything chunk, a bar-history job or a recovery pass is running.
14. **Read while running.** The run status, equity so far and each finished session can be read while the job runs, with `complete: false`, sessions done of total, and the as-of session.
15. **Cancel.** A cancel stops the run after the current session. The run keeps its finished sessions and reads `cancelled`.
16. **Deterministic.** Two runs with the same input hash give the same result hash.
17. **No orders.** `ExecutionBoundaryArchitectureTests` stays green, and the replay project references no broker adapter.

## Contract
- **API** (new module `replay`, all owner-only like `/api/reports`; `scripts/gen-api.sh` in the same change):
  - `POST /api/replay/runs` takes `{ from, to?, scope: { basketIds[], instrumentIds[] }, capital, riskProfileId?, costs, versions: "start"|"asOf", modes: ["frozen","learning"] }` and answers 202 `{ runIds[], pairId? }`.
    - Refusals: 422 `replay.invalid`, 409 `replay.busy`, 409 `replay.holdout`.
  - `GET /api/replay/runs` lists runs: id, created, range, mode, state, `complete`, final equity, max drawdown, trial number.
  - `GET /api/replay/runs/{id}` gives the summary: settings snapshot, input and result hashes, hindsight labels, completeness, CAGR, max drawdown, Sharpe of daily returns, exposure, trades, hit rate, mean R, contribution per strategy, and the costs label.
  - `GET /api/replay/runs/{id}/equity?from&to` gives the daily equity, drawdown and benchmark (Nifty 500, rebased to the capital) and a cash line.
  - `GET /api/replay/runs/{id}/sessions?from&to` gives one row per session: taken, blocked by reason, P&L, equity.
  - `GET /api/replay/runs/{id}/sessions/{date}` gives that session's full record: candidates, ranked list, taken, blocked with reasons, fills, open book.
  - `GET /api/replay/runs/{id}/trades` lists the trades, each linking to the existing P8-11 trade replay where the trade has bars.
  - `GET /api/replay/pairs/{pairId}` gives the frozen-versus-learning comparison of AC 9.
  - `POST /api/replay/runs/{id}/cancel` answers 202.
- **Data** (migration `AddSystemReplay`, schema `research`, insert-only except the job state):
  - `replay_runs`: id, pair_id, params jsonb, settings_snapshot jsonb, input_hash, result_hash, state, cursor_session, created_at, finished_at.
  - `replay_sessions`: run_id, session, record jsonb, equity, drawdown; primary key (run_id, session).
  - `replay_trades`: run_id, trade fields as in `paper` trades, entry_session, exit_session, net and gross P&L, R.
  - The tables are not hypertables. A five-year run over 500 stocks is about 1,250 sessions per run. Retention: kept until the owner deletes a run (no delete API in this slice).
- **UI:** a new **System replay** tab on the Simulation screen (route `/simulation/replay`), in the AU-15 style.
  - A start form: date range picker, scope picker (reusing the auto-scope picker), capital, profile, costs, versions rule, modes.
  - A progress card.
  - The results:
    - the hindsight banner first;
    - KPI tiles;
    - an equity curve against Nifty 500 with a drawdown strip;
    - a session table; clicking a day opens that day's plan with its blocked reasons;
    - a trade list;
    - the paired comparison card when both modes ran.
  - States: loading, empty ("no replay yet"), running (partial), cancelled, failed (with the stored reason).
  - There is no prototype screen. It follows the existing Simulation screen.
- **Events/jobs:**
  - `ReplayService` (BackgroundService), recovery class `Convergent` (AU-01).
  - One session per step.
  - Idempotent by (run_id, session).
  - A failed session stores its reason and stops the run as `failed`. It never skips silently.
  - The service has an Internal settings section descriptor (AU-08) holding the incomplete-data share (default 0.2) and the yield rules.

## Non-functional
- **Speed:** measured, not asserted in the gate (wall-clock asserts are `Explicit`). The target is one year over a 100-stock scope finishing within one night (8 h) on the owner's Mac.
  - Bars for each instrument are loaded once per run as a rolling window, not 400 days for every session.
  - If the target is missed, the builder reports the measured time per session and stops for the owner. The target is not loosened silently.
- **Security:** owner-only endpoints, no secrets, nothing published beyond 127.0.0.1.
- **Observability:** log events in a new block (the next free event ids after the BK-12b range). The job writes start, each 50 sessions, yield, resume, finish, fail and cancel.

## Safety
**No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only. The replay changes no live state: no plan, no paper ledger, no shadow grade, no cell statistic, no model designation, no setting.

## Build parts
| Part | What | Size |
|---|---|---|
| A | `IReplayFacts` as-of port and its readers (versions as of a date, settings snapshot, members as of a date, clamped bars), the pure session step, and the AC 4–7 tests | 4.5 d |
| B | Migration, store, `ReplayService` job with cursor, yield and cancel, trial-ledger record, and architecture fences (AC 3, 11–13, 15–17) | 3 d |
| C | API and `gen-api` (AC 1, 2, 14) | 2 d |
| D | Frozen versus learning pairing and the comparison (AC 9) | 1.5 d |
| E | System replay screen | 2.5 d |

Parts A and B are on the decision path and need the Opus reviewer. Parts C to E are ordinary.

## Test plan
- **Unit:**
  - the session step on fixtures (taken, blocked by each reason, swing slot held);
  - as-of readers on before/after fixtures;
  - equity reconciliation;
  - the paired comparison's four answers.
- **Property:**
  - look-ahead (mutating bars after S changes nothing at S);
  - determinism (same input hash, same result hash).
- **Golden:** a replay of a fixture session equals the stored live plan (AC 5).
- **Integration (API + DB):**
  - start, read while running, resume after kill, cancel;
  - the hold-out refusal and the busy refusal.
- **Architecture:**
  - the evidence fence;
  - the write-table list;
  - the hold-out reader clamp;
  - no Execution reference;
  - Planning does not name Replay;
  - `ProductionNames` lists the new project if one is added (prefer a namespace inside Application, Domain and Infrastructure over a new project).
- **Frontend (MSW):** every state of the tab, the hindsight banner always first, the day drill-down, the pair card.

## Open questions (each with a recommendation)
All eight answered by the owner on 2026-11-11 as recommended ("Approve all"); the recommendation of each is now the decision. Items marked UNVERIFIED are for the builder to confirm, not for the owner.

1. **Strategy versions.** All stored strategies were written in Oct–Nov 2026, so "versions as of each session" gives no candidates before then.
   - *Recommended:* default `versions=start` (today's active versions, run backwards, labelled hindsight).
   - Keep `asOf` as an option, so that later replays starting after a version existed are strict.
2. **Settings.** Use one snapshot of the current settings for the whole run, or derive settings as of each session from the insert-only histories?
   - *Recommended:* one snapshot, labelled. The history is one month old, and per-session settings add many readers for little gain.
   - Revisit in SR-02 once the settings history covers the replay range.
3. **Learning cadence.** In `learning` mode, recompute cell stats every session, or on the scheduled job's cadence?
   - *Recommended:* the scheduled job's cadence, so the replay learns the way the live system does.
   - **Verified at build (part A, ADR 0272):** no scheduled cell-statistics job exists in code (only `xd learn cells`); the design's weekly learning service (Saturday) is the cadence, so the replay uses weekly. The P6-01 calculator is pure and runs in memory without its store (reading the source runs of the frozen snapshot).
4. **Cell lifecycle in `learning` mode.** Run the lifecycle rules in memory, or keep the stored changelog as of S?
   - *Recommended:* the rules in memory, if they are pure; otherwise the stored changelog, labelled "lifecycle as recorded". **Verified at build (part A, ADR 0272):** the lifecycle is a table of legal moves that need owner approval and out-of-sample facts, so it is not pure in the needed sense; the changelog as of S is used, labelled.
   - Before Oct 2026 the changelog is empty, so "as recorded" means the initial state.
5. **Compounding.** Size from replay equity (compounding) or from the fixed starting capital?
   - *Recommended:* replay equity, as the live system sizes from the paper book.
6. **Trial counting.** Count each replay as a CF-06 look of kind `system_replay`?
   - *Recommended:* yes. Repeated replays over the same range are the easiest way to fool yourself, so the screen shows the count.
7. **Charge sheet.** Allow net figures only after ISS-25 is closed?
   - *Recommended:* yes. Until then the run is gross and labelled.
   - Filling in the sheet is the owner's act, and the most useful single step before trusting any profit figure.
8. **Scope default.** Default the scope to the auto scope as of D?
   - *Recommended:* yes. This needs `AutoScopeResolver` to accept an as-of date, which is the same change as REVIEW-2026-11-10 question 3, so build it once in part A.

## Decisions taken at build (part A review, ADR 0272)
- Two per-run parameters, both off by default: `filtersWithoutHistory` (event, F&O-ban and surveillance filters not required; label `filters_without_history`) and `includeResearchCells` (research-state cells may be candidates; label `research_cells`). Part C adds them to the start request.
- A start with no frozen cell statistics as of D answers 422 `replay.invalid`.
- The rolling-window bar load is part B. The latest auto-scope definition is accepted under `settings_now`; part B stores the scope hash.

## Evidence notes (part B, 2026-11-12; ADR 0272, "Part B decisions")
- **AC 3 (hold-out).** Part A's clamp stands; part B adds that the rolling window never loads past it (`ClampedBarsWindowTests`) and that a start into the hold-out stores nothing (`StartReplayHandlerTests`, `replay.holdout`).
- **AC 11 (fence).** `EvidenceReaderFenceArchitectureTests.No_evidence_reader_names_the_replay_tables_or_the_replay_store` (no evidence reader, no file outside the store, its ports, the job and the registrations names the replay tables or ports); `ReplayArchitectureTests.The_replay_may_write_only_its_three_tables_and_only_the_one_store_names_them` and `The_job_names_no_execution_project_broker_or_order_and_writes_only_through_the_replay_store_and_the_trial_ledger`; Planning-names-no-Replay and `ExecutionBoundaryArchitectureTests` stay as before.
- **AC 12 (trials).** `StartReplayHandlerTests`: one `system_replay` ledger row per strategy version with the input hash as its run key; the trial number counts the runs over the same range (`TrialNumber` on the run read). `ReplayStoreTests` proves the ledger accepts the kind.
- **AC 13 (resumable).** `ReplayRunnerTests.A_run_killed_after_three_sessions_resumes...` (byte-identical records, same result hash, stored sessions not written again); `The_job_does_not_claim_a_run_while_a_yield_rule_says_wait...` for low disk, market hours, a bar-history job or recovery pass, a simulate-everything chunk and an incomplete backfill; `A_run_in_flight_waits_between_sessions...`.
- **AC 14 (read while running).** `IReplayReads` (run with sessions done of total, as-of session, `Complete`; sessions by range; one session; trades; summary from `PaperLedger`), tested in `ReplayStoreTests`. The endpoints are part C.
- **AC 15 (cancel).** `A_cancel_stops_the_run_after_the_current_session...` and `A_cancel_of_a_queued_run_never_starts_it`; `ReplayStoreTests` for the states.
- **AC 16 (deterministic).** `Two_runs_with_the_same_input_hash_give_the_same_result_hash`; `The_input_hash_is_stable_for_the_same_inputs_and_moves_with_each_input`.
- **AC 17 (no orders).** The job and the store are in the scan for execution, broker and order names.
- **Speed (non-functional).** The bars are loaded once per run as a rolling window. Measured with the explicit `ReplayBenchmarks` (one year, 100 instruments, in-memory stores, Release): 0.88 s per session, 227.9 s for 259 sessions, live managed memory 609 MiB and peak working set 968 MiB for 100 instruments (bounded chunked window; a 1 GiB container is at its edge, see the ADR); the database read of the bars and a held trade's simulation are not in the figure (ADR 0272). The 8-hour target is not threatened by the engine; the figure on the owner's database is still to be measured.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors (Opus for parts A and B)
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddSystemReplay` added
- [ ] ADR 0272 written; docs/design/11-strategy-lab.md and 13-reports-and-ui.md updated; roadmap row updated
