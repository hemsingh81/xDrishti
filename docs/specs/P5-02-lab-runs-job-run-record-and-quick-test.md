# P5-02 — Lab runs: the job, the run record and the quick test

- **Status:** Approved (owner, 2026-10-22, "approve all": the questions stand as recommended). **Built 2026-10-22 ([ADR 0058](../adr/0058-lab-runs-the-quick-test-the-run-identity-and-the-restart-rule.md)); three deviations are recorded there: no Hangfire (the table is the queue), the job runs in `xd-api` (the export library is the API's alone), settings from `StrategyLab:*` configuration. Criterion 12 (the 1 to 2 minutes on the owner's data) is not measured.** Written 2026-10-22 from `main` at f4294e31. It depends on [P5-01](P5-01-lab-api-validate-and-strategy-library.md) (also Draft; stored versions, hash) and through it on P3-02c/d (future). Names taken from those are **UNVERIFIED against code**. `RunSimulationHandler`, the Hangfire server, the hub and the basket reader were **not re-read** for this draft; each statement about them is from the roadmap or an ADR title and is marked UNVERIFIED.
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 2 of the plan [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §3 (pipeline steps 1 to 3, "Runs as Hangfire jobs in `xd-worker`; progress streamed via SignalR"), §6 (`lab.lab_runs`, the run record is the reproducibility record) · 03-target-architecture.md §4, §6, the real-time row · 16-configuration.md `strategy_lab` (`quick_test`, `max_parallel_runs`) · [ADR 0037](../adr/0037-simulation-conventions-and-the-two-stage-seam.md), [ADR 0041](../adr/0041-the-simulate-verb-the-export-library-and-the-pipeline-in-the-host.md), [ADR 0045](../adr/0045-the-result-store-sim-runs-and-sim-trades.md), [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md) (hub).

## Goal
The owner starts a quick test of a stored strategy version on a basket (or a list of symbols) from the API; a background job validates, compiles, replays the last year on up to 30 symbols, reports progress live, and stores a Lab run that points at the simulation run(s) it produced. The result is trades and counts, not a verdict (that is P5-03 to P5-06).

## Scope
**In:**
1. `lab.lab_runs` (state, inputs, config snapshot hash, engine version, the run keys produced, stage log, timings, error) and its migration.
2. `LabRunner` (application service) with the stages **validate**, **compile**, **quick test**, and the `ILabProgress` port (D1 of P5-00).
3. `POST /api/lab/runs`, `GET /api/lab/runs`, `GET /api/lab/runs/{id}`, `POST /api/lab/runs/{id}/cancel`.
4. The Hangfire job in `xd-worker` (queue `lab`, concurrency `max_parallel_runs`), restart behaviour, idempotency by the run identity of P5-00 D2.
5. Progress and completion on the SignalR hub `/hubs/events` as new client events, and the cross-process path from worker to hub.
6. The universe: a basket id (the owner's baskets) or an explicit symbol list; the quick-test sample rule when the universe is over 30 symbols.
7. The quick test runs through `RunSimulationHandler` with `--store` semantics (the run is kept in `research.sim_runs`), costs per the stored sheet if one exists and gross labelled so otherwise (as P3-03f).

**Out (not in this slice):** the full test, walk-forward, robustness (P5-04, P5-05); the verdict, the gates, the ledger (P5-03); the report (P5-06); the page (P5-08b) and CLI (P5-07); the exit comparison (P5-04); promotion; plugins. A run's `verdict` field exists and is `null`.

**Size.** About 3 to 3.5 agent-days (P5-00; not calibrated). Itemised, UNVERIFIED: table, migration, grants 0.5; `LabRunner` and stages 0.8; API 0.5; Hangfire job, queue, restart and cancel 0.7; progress across worker and hub 0.6; universe and sampling 0.2; tests and docs 0.5. Cut order: cancel, then the runs list; never idempotency or restart handling.

## What exists (UNVERIFIED unless an ADR says it)
`RunSimulationHandler` is host-agnostic and the `simulate` verb calls it (ADR 0041, "moves behind the Lab API in P5"); the result store exists with `--store` (ADR 0045); comparison and selection exist (ADR 0053/0054) but are not used in this slice. The Worker project references Hangfire (a grep found it in the csproj); a running job, a queue configuration and the Hangfire dashboard exposure are UNVERIFIED. The hub `/hubs/events` has `pricesChanged` and `feedStatus` (ADR 0024); how another process pushes to it is UNVERIFIED (doc 03 says Postgres `LISTEN/NOTIFY` for events). Baskets exist in the API (`BasketsEndpoints`) and as a universe in the `simulate` verb (ADR 0041).

## Decisions
- **D1. Stages and states.** States: `queued`, `running`, `succeeded`, `failed`, `cancelled`, `interrupted`. Stages are recorded in order with start, end, counts and the problems they found. A failed validate or compile ends the run `failed` with the located problems and no simulation.
- **D2. Quick test window.** The last `quick_test.years` (1) of stored data ending at the last stored session, and `quick_test.symbols` (30) instruments. With more members than 30 the sample is the first 30 by a seeded shuffle of the sorted instrument ids (seed in the run record), so it is the same twice; with fewer, all. The window and the sample are inputs to the run identity.
- **D3. Run identity and idempotency.** The key is the SHA-256 of a fixed text of: strategy hash, universe (sorted ids and label), window, method `quick`, config snapshot hash (the `strategy_lab` and `fills` keys that matter), engine version. A second post with the same key returns the existing run (`200`, not a new job); a post while that run is `queued` or `running` returns it too. UNVERIFIED: the simulation run key already contains most of this; the Lab key wraps it.
- **D4. Restart.** On worker start any `running` row of this host becomes `interrupted` with the stage reached; it is never resumed silently; the owner starts it again (a new run row, same key, because the key is only unique among non-interrupted rows).
- **D5. Progress events.** `labRunProgress { runId, state, stage, done, total, message }` throttled to at most 4 per second per run, and `labRunFinished { runId, state }`. The API serves the same data by `GET` so a missed event costs nothing.
- **D6. Every Lab run is gross or costed on its face.** The run record carries the cost basis line of P3-03f and the data range per instrument (ISS-19), and a line that a quick test is exploration, not a verdict.

## Acceptance criteria
1. Given a stored valid version and a basket of 12 fixture symbols, when `POST /api/lab/runs` is called, then `202` with a run id, state `queued`, and the run ends `succeeded` with the produced simulation run key(s) and counts.
2. Given the same input twice, then the second call returns the first run and no second job exists (D3); given a changed strategy version, a new run.
3. Given a built-in strategy and the same input as `simulate`, then the Lab run's simulation run key and `trades.csv` equal the verb's, trade for trade (P5-00 criterion 3).
4. Given a version that fails compile (a located problem), then the run is `failed` at stage compile with the problems and writes no `sim_runs` row.
5. Given a basket of 80 symbols, then exactly 30 are used, chosen by the seeded rule, listed in the run record; the same input gives the same 30.
6. Given a running job, when cancelled, then it stops at the next safe point (between instruments), the state is `cancelled`, partial `sim_runs` are not written (the run is stored whole or not at all), and no job remains.
7. Given a worker restart during a run, then the row becomes `interrupted` and no result is presented as complete.
8. Given a subscriber to the hub, then it receives progress events for its run in order, at most 4 per second, ending with `labRunFinished`; with the hub disconnected the `GET` still returns the right state.
9. Given `max_parallel_runs: 2` and three posts, then two run and one stays `queued` until a slot frees.
10. No secret, no strategy text and no trade appears in a log line (a byte scan of the test logs); `xd_app` can INSERT runs and UPDATE only the state columns of `lab.lab_runs` and has no DELETE (grants test).
11. The architecture tests stay green: the Lab module reaches the simulation only through the handler's public port (ADR 0039 pattern; UNVERIFIED which port).
12. A quick test of one strategy over 30 symbols and one year completes within the 1 to 2 minutes of doc 11 §3 on the owner's machine (UNVERIFIED; measured and written down, not a gate if it misses: the figure goes to the ADR).

## Contract
- **API:**
  - `POST /api/lab/runs` body `{ strategyId, version, universe: { basketId } | { symbols: [..] }, method: "quick", window?: { from, to } }` → `202 { id, state, key }` (or `200` with an existing run); `422 lab.invalid_input`, `404 lab.not_found`, `409 lab.no_data` (no stored bars in the window).
  - `GET /api/lab/runs?strategyId=&state=&limit=` and `GET /api/lab/runs/{id}` → `{ id, key, state, stage, strategy: { id, version, hash }, universe, window, method, simRunKeys, counts, problems, startedAt, finishedAt, labels: [..], verdict: null }`; `POST /api/lab/runs/{id}/cancel` → `202` or `409 lab.not_cancellable`.
  - Hub events D5.
- **Data:** migration `AddLabRuns`: `lab.lab_runs (id, key, strategy_id, version, strategy_hash, universe jsonb, window_from, window_to, method, config_hash, engine_version, state, stage, sim_run_keys text[], counts jsonb, problems jsonb, error, created_at, started_at, finished_at)` (UNVERIFIED column list); index on `(key)` and `(strategy_id, created_at)`; a partial unique index on `key` where `state in ('queued','running','succeeded')`. `sim_run_keys` is a soft reference to `research.sim_runs` (no foreign key across schemas, UNVERIFIED policy).
- **UI:** none (P5-08b); the shapes above are its contract.
- **Events/jobs:** Hangfire job `LabRunJob(runId)` on queue `lab`; retries off (a failed run is a result, not a retry); log event ids new (UNVERIFIED which are free): `lab.run.queued|started|stage|finished|failed|cancelled|interrupted`.

## Non-functional
Quick test within the 1 to 2 minutes of doc 11 §3 (AC 12). The API never waits for a run. A job holds at most the memory of one instrument's replay at a time (as the verb, UNVERIFIED). Deterministic by seed. Reads only stored bars and the database; no network from the job. The progress throttle bounds hub traffic.

## Test plan
Unit: the run identity text, the sampling rule, the state machine (legal transitions only), the throttle. Integration (Testcontainers): post, job, finish on fixture bars; idempotency; cancel; restart (the host restarted under test); parallel limit; migration up/down; grants. Equality test against the verb (AC 3) on an F1-shaped fixture day. Hub: a test client receives the events. Edge cases: empty basket; a symbol with no bars; a window with no sessions; a basket changed after the post (the run stores the resolved list); a strategy with BUY and SELL; cancel at the last instant; two posts racing.

## Open questions (for the owner; each with a recommendation)
1. **Sample rule.** A seeded shuffle of the first 30, or the 30 most liquid? Recommended: seeded shuffle now (liquidity is a universe-layer concept, doc 10, not built); say so in the run record.
2. **Window.** The last 1 year, or the first year of the data? Recommended: the last year, labelled; once the hold-out question (P5-00 Q4) is answered the quick test must stay out of the hold-out.
3. **Stored by default.** Quick tests always `--store`? Recommended: yes (the Lab run points at a stored run; storage is insert-only and idempotent); the cost is rows per exploration.
4. **Cost basis.** Gross until a stored sheet exists, costed after? Recommended: yes, as P3-03f, with the line on the record.
5. **Cancel.** In this slice or later? Recommended: in; a 10-minute job with no cancel is a trap.
6. **Cross-process progress.** Postgres `LISTEN/NOTIFY` (doc 03) or the worker polling the table and the API relaying? Recommended: `LISTEN/NOTIFY`; confirm it exists (UNVERIFIED) before the build, otherwise polling at 1 s.

## Done when
- [ ] The owner has answered questions 1 to 6 and the status is Approved; P5-01 is Built
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddLabRuns` added with `Down`
- [ ] ADR (UNVERIFIED next number) for the Lab runner, run identity and restart rule; docs 03, 11 §3 and §6, 16 and the roadmap updated; `scripts/build-docs.sh` run
