# AU-12 — Simulate-everything job, resumable and yielding

- **Status:** Approved (owner, 2026-11-09, in chat)
- **Phase / roadmap row:** AU — Automation and simplicity programme, row AU-12 (plan: [AU-00](AU-00-automation-and-simplicity-plan.md))
- **Size:** 3 agent-days (an estimate, not calibrated) · **Needs:** AU-11
- **Decisions:** [ADR 0243](../adr/0243-simulate-everything-is-training-data-fenced-from-the-hold-out-and-from-scorecard-evidence.md)
- **Design refs:** docs/design/11 §3; deploy/CLAUDE.md

## Goal
One job runs every active strategy version over every tracked stock across all usable history, and survives restarts.

## Scope
**In:** `simulate-all` as a registered service (manual start, paused schedule), chunks by strategy version and instrument, resume, pause, cancel, progress, a disk guard and a market-hours yield.

**Out (not in this slice):** Training (AU-13); reports (AU-14).

## Acceptance criteria
1. Starting creates one job with one chunk per (strategy version, instrument); a second start while one runs answers 409.
2. A chunk is identified by its run key, so re-running a finished chunk writes nothing.
3. A restart resumes at the first unfinished chunk, and a chunk left running is redone.
4. A failed chunk is recorded with its reason and the job continues; the job ends `completed_with_failures` when any failed.
5. During market hours (09:00 to 15:45 IST on trading days) the job pauses, unless `Simulate:RunInMarketHours` (default false, Internal).
6. Free disk under `Simulate:MinFreeGb` (10 to 500, default 40, on top of the existing floor) pauses the job with a notice.
7. Concurrency is `Simulate:Workers` (1 to 16, default 2, chosen for the 10 GiB VM) and a test shows the limit is held.
8. Progress reports chunks done, failed, remaining and the date range covered.

## Contract
- **API:** `POST /api/simulate-all` 202; `GET /api/simulate-all/current`; `POST .../pause|resume|cancel`. Authenticated, CSRF, audited.
- **Data:** Migration `AddSimAllJobs`: `research.sim_all_jobs`, `research.sim_all_chunks` unique on `(job_id, strategy_id, version, instrument_id)`.
- **UI:** Start, progress and pause controls on the report screen (AU-15).
- **Events/jobs:** Runs under the services framework on `xd-worker` or the existing Lab dispatcher, whichever the build finds has capacity.

## Non-functional
Throughput and storage per chunk are UNVERIFIED: the first 1 % of the job is measured and an estimate of time and disk is shown before the rest continues.

## Safety
The job writes `training` runs only. **No orders.** Nothing in this slice places, modifies or cancels an order, adds a broker order endpoint, or references `XDrishti.Execution`; the P12 live path stays separate and owner-only.

## Test plan
Unit for chunking and resume with fakes; integration with a small seeded universe. No full-history run in tests.

## Open questions
None.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) and migration added, where this slice has them
- [ ] Design docs/ADR updated if behaviour changed
