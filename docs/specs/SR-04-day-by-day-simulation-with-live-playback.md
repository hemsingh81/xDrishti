# SR-04 — Day-by-day simulation with live playback

- **Status:** Approved (owner, 2026-11-14, in chat: answers Q1–Q6 below and open questions 1–2; 3–4 as recommended)
- **Phase / roadmap row:** SR — System replay, row SR-04 (new)
- **Needs:** SR-01 (built: `ReplayDriver.PrepareAsync` / `StepAsync`, `ReplaySessionRunner`, `ReplayBook`, `PaperLedger`), SR-02, the stored 1m bars
- **Decisions:** ADR 0293 (to be written at build)
- **Design refs:** docs/design/11-strategy-lab.md, 13-reports-and-ui.md, 09-learning-engine.md (hold-out); spec SR-01; reference UX: XDhan Simulate tab (owner's repo `hemsingh81/XDhan`, `docs/CLAUDE/simulation-environment-trade-lab-simulate.md`)

## Goal
The owner picks stocks and/or a basket and a date range (for example 2 Feb → today). The simulation runs **one trading day at a time**. Each day it learns only from data up to the day before, picks the trades, plays the day minute by minute on a clock the owner can watch, pause and speed up, closes the day and moves to the next. It can also run every remaining day unattended. It never touches real state.

## Owner answers (2026-11-14)
1. Data: only what is stored (1m bars of 313 instruments, 2024-04-01 → 2026-11-13); no owner input of any kind (see DROP-OWNER-INPUTS).
2. Loop: the full XDhan loop (learn as of the previous trading day → pick → play → settle → advance).
3. Playback: both animated (pause, 2X–10X) and Instant / unattended auto-run.
4. Isolation: fully isolated, **no push** to real learning, strategy, plan or paper state.
5. Money: the paper-account risk rules; capital is a sim setting (default ₹10,00,000); gross costs (DROP-OWNER-INPUTS Q2: gross forever); equity carries over day to day.
6. End: stops at the last complete stored session; **Continue** extends the run when newer sessions are stored.

## What SR-01 already does, and the gap
SR-01 already replays the whole system session by session with an as-of context, `learning` mode (cell stats recomputed as of S), selection, portfolio limits, sizing from replay equity and `PaperLedger` equity, as a resumable background job with a results screen. It is **not** XDhan-like in four ways, which are this slice:
1. It runs a whole range as one job; there is no "current day" the owner steps through.
2. There is no minute clock: fills and exits are computed, but nothing plays the day.
3. There is no live board (open positions, stops, running P&L at the clock time).
4. It cannot be continued past its end date.

## Design (smallest change)
- **One engine.** A simulation is a stored session cursor over an SR-01 `ReplayPrepared`. "Run day" calls `ReplayDriver.StepAsync` for the cursor day: the existing as-of learning and selection give the day's picks and trades. No second engine, no copy of selection code.
- **Playback is a view of the settled day.** The day is settled first (the same call Instant uses). Animated playback then streams the stored 1m bars of the day's instruments and reveals each entry, stop move and exit at its own minute (`EntryAt`, exit time). So animated and Instant give the **same result by construction**, with no look-ahead in the engine. The clock only controls what the screen shows, never what is computed.
- **Advance** moves the cursor to the next session of the trading calendar (holidays and weekends skipped) and stores a day snapshot (picks, blocked with reasons, trades, equity at close).
- **Auto-run** repeats run day → settle → advance until the end, using the same commands as the manual buttons; pausing it and stepping a day by hand, then resuming, carries on from the stored cursor.
- **Isolation.** It writes only its own tables in the `research` schema; the SR-01 fence and architecture tests (no planning, paper, cell-stats, model or settings writes; no `XDrishti.Execution` reference) cover it. No push endpoint exists.

## Scope
**In:** session cursor, run-day / play / instant / advance / auto-run / continue / cancel, day snapshots, the live board and clock UI, the scope picker (stocks and/or baskets), the money settings.
**Out:** any push to real state; orders or broker calls; ML model influence (stays behind the ADR 0217 gate, so today "learning" means cell stats and cell state, as in SR-01); running several simulations at once; options, pairs, basis.

## Acceptance criteria
1. **Start.** Given a scope (stocks and/or baskets), a start date, an end date (default: the last complete stored session, limited per Q1), capital (default from the setting) and a learning mode (default `learning`), when the owner starts a simulation, then it is stored with its input hash and settings snapshot, the cursor is on the first session on or after the start date, and it answers 201 with its id.
2. **Bad input.** Start after end, start before the first stored bar plus the warm-up, empty scope, capital ≤ 0 → 422 `sim.invalid`; a second active simulation → 409 `sim.busy`.
3. **Day = SR-01 session.** For any day, the picks, block reasons, trades and closing equity equal those of an SR-01 replay with the same inputs at that session (golden test).
4. **No look-ahead.** Learning and picks for day S read only data up to the close of the previous session; the SR-01 mutation property test (bars after S changed → day S unchanged) runs on the cursor path.
5. **Animated = Instant.** A day played animated, at any speed, with pauses, ends with exactly the same trades and equity as the same day settled Instant (parity test).
6. **Clock.** Playback starts at 09:15 of the day at the default speed (1 minute of market = 10 s); pause, resume, 2X, 5X, 10X and Max work; at each clock minute the live board shows open positions, entry, current stop, last price and running P&L, using only bars up to that minute.
7. **Advance.** Advance after a settled day stores the day snapshot (immutable) and moves the cursor to the next trading session; an advance before the day is settled → 409 `sim.not_settled`.
8. **Auto-run.** Auto-run settles and advances every day to the end without the owner; pause stops after the current day; manual steps in between are allowed and auto-run resumes from the stored cursor.
9. **Equity carries over.** Positions held overnight keep their slot and risk; equity, drawdown and the realised series come only from `PaperLedger.Compute`; final equity = capital + sum of trade P&L (reconciliation test).
10. **Stop and Continue.** At the end date the simulation reads `complete`. When sessions newer than its end are stored, **Continue** extends the end to the new last complete session and the cursor resumes; nothing before the old end is recomputed (hash of old days unchanged).
11. **Survives restart.** After an app restart, the simulation resumes from its stored cursor; a day half-settled at the restart is settled again from its start, with the same result.
12. **Isolation.** No write reaches planning, paper, shadow, cell-stats, model or settings tables (architecture + integration test); there is no push endpoint.
13. **Labels.** The screen states the hindsight parts (as SR-01 AC 10) and the cost basis.
14. **No orders.** `ExecutionBoundaryArchitectureTests` stays green.

## Contract
- **API** (`/api/sim/days`, problem details codes above):
  - `POST /api/sim/days` start · `GET /api/sim/days/{id}` status (cursor, phase `idle | settling | settled | complete | cancelled | error`, equity, days done of total)
  - `POST /{id}/settle` (settle the cursor day; Instant) · `POST /{id}/advance` · `POST /{id}/auto` `{ on: bool }` · `POST /{id}/continue` · `POST /{id}/cancel`
  - `GET /{id}/days/{date}` day snapshot · `GET /{id}/days/{date}/tape?from=&to=` the day's 1m bars of the instruments in play plus trade events, for playback (paged by minute)
- **Data** (migration `AddSimDays`): `research.sim_days_runs` (id, input hash, settings snapshot, scope, capital, mode, start, end, cursor, phase, auto, created/updated), `research.sim_days_snapshots` (run id, session, picks, blocked, trades, equity close, result hash; insert-only). Not hypertables (small).
- **Settings:** `Sim:DefaultCapital` = 1,000,000; `Sim:DefaultSpeedSecondsPerMinute` = 10; `Sim:AutoContinue` = OFF (manual Continue by default; ON lets a new stored session extend a complete run automatically — no risk widened, no evidence lost; see Q3).
- **UI:** Simulation tab → "Day by day" screen, matching the XDhan flow and the prototype style: start form (scope picker for stocks and baskets, dates, capital, mode); a header with the cursor day, phase and equity; the floating clock with pause and speed; the live board; the day's picks and blocked list; buttons Run day / Instant / Advance / Auto / Continue; the day history table and equity curve (reusing SR-01 `ReplayCharts`, `ReplaySessionTable`, `SessionDrawer`). States: loading, empty, settling, complete, error.
- **Jobs:** one hosted runner for settle and auto-run, using the SR-01 job pattern (`YieldAsync` steps aside for market hours and data jobs); idempotent per (run, session).

## Non-functional
Settle one day for 50 instruments in < 5 s (p95) on the owner's Mac; the tape endpoint returns one minute page in < 200 ms; playback keeps 10X smooth with ≤ 1 request per second. Log events for start, settle, advance, continue, cancel. Published ports stay on 127.0.0.1.

## Test plan
Unit: cursor and calendar stepping, phase machine, tape slicing to the clock minute. Integration (API + DB): start/validate, settle = SR-01 golden, advance, auto-run, continue, restart resume, isolation. Property: look-ahead mutation on the cursor path; animated = instant parity. Frontend (MSW): start form, clock controls, live board at a minute, auto-run pause/resume, complete and continue. Architecture: no writes outside `research`, no Execution reference.

## Open questions (each with a recommendation)
1. **The hold-out block — ANSWERED 2026-11-14: (a), seal it and use block 1's strategy look.** Hold-out block 1 runs from **2026-04-10** to today. SR-01 refuses any session inside it, so "2 Feb → today" would stop at 2026-04-09. Options:
   - (a) **Recommended:** the simulation may run inside the hold-out but is **sealed**: it is isolated (no push), the run is recorded as a look of kind `system_replay` in the CF-06 trial ledger, and running it inside the block **uses block 1's strategy look** (or the block is released with forfeit) — taken **automatically** on the first run inside the block (recorded as auto, with the setting snapshot; DROP-OWNER-INPUTS Q1). After that look, simulating to today is allowed. Without it, the end is clamped to 2026-04-09 and the screen says why.
   - (b) Clamp always to the hold-out start (keeps the hold-out untouched; 2 Feb → 9 Apr only).
   - (c) Ignore the hold-out for this simulation (fastest, but every later hold-out result is no longer clean evidence).
2. **Strategy versions — ANSWERED 2026-11-14: versions current at run start, for any start date, hindsight-labelled.** All stored strategies were written Oct–Nov 2026. Recommendation: as SR-01 Q1 — use the versions current at the simulation's start (hindsight-labelled), not "versions as of each day" (which would trade nothing before October).
3. **Auto-continue default — as recommended.** Recommendation: OFF (manual Continue), as answer 6; the setting can switch it ON.
4. **Learning mode choice — as recommended.** Recommendation: default `learning` (XDhan loop); offer `frozen` as an option and the SR-01 paired comparison as a link, not new work.

## Build parts
- A (backend): tables + migration, cursor/phase service over `ReplayDriver.StepAsync`, settle/advance/auto/continue/cancel endpoints, restart resume, tests. ~3 d
- B (backend): tape endpoint and the trade-event stream per minute; parity and look-ahead tests. ~1.5 d
- C (frontend): Day-by-day screen, clock, live board, controls, history; MSW tests. ~3 d

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR 0293 written; design docs (11, 13, roadmap) updated
