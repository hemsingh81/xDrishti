# P1-13 — Services framework and the Services screen

- **Status:** Approved (owner, 2026-10-13: "approved as written" — all five open questions answered with the recommended defaults) · decision [ADR 0018](../adr/0018-services-framework-and-editable-schedules.md) (Accepted)
- **Phase / roadmap row:** P1 — "Services screen with schedule configuration & dependency checks"; completes P1-12 (editable schedules) · after P1-12a, P1-14a
- **Design refs:** docs/design/04-accounts-access-and-operations.md §3 (services overview, §3.1 schedule configuration), 05-data-and-brokers.md §6, 17-deployment-and-operations.md; prototype `docs/prototype/assets/app-pages-3.js` (*Services*)

## Goal
The automatic jobs have one home: a Services screen where the owner sees what runs when, what the last run did and what runs next, edits a schedule with the dependency checks and a preview of the next runs, pauses a service or runs it now — with no restart and every change audited.

## Scope
**In:**
- Platform: `ServiceDefinition` registry, `ScheduleCalculator` (daily · weekly · after · window · interval · continuous, trading-days flag from the calendar), `ServiceScheduler` hosted service (catch-up, retries with back-off, timeout, one run at a time), schedule rules/checks over the whole set, run history, interrupted-run recovery.
- Two services moved onto it: **End-of-day data run** (`eod`: dhan → aggregate → quality, trigger `daily`) and **Instrument master refresh** (`instrument-master`). Their work, once-per-day, skipped-run and catch-up behaviour stay as ADR 0017 §2 / ADR 0010 describe.
- API for list, detail, runs, save, preview, pause/resume, restore default, run now, timeline, notices.
- Services screen: checks banner, timeline, table, Configure dialog (with preview and issues), History drawer, failure notice in the bell.
- One row for the per-account holdings sync (the schedule stays owned by the account, ADR 0013): no Run now / Configure / Pause / History, a link to Accounts, and (amendment 2026-10-13) a **Settings** dialog for each Portfolio account's automatic refresh (Off / Daily at HH:MM IST), saved through the accounts portfolio-settings call.

**Out (not in this slice):** a separate `xd-worker` container; services that do not exist yet (pre-open check, price alerts, nightly pipeline, AI briefing, CSV watcher, retrain, backup, live feed) — each registers its definition in its own story, and the checks for them apply from then on; moving the per-account holdings refresh into this editor (its dialog only edits the account's own setting through the accounts call); step-up for schedule edits; purging old runs; role-based access (single owner).

## Acceptance criteria
**Calculator and rules (pure, fake clock)**
1. Given a daily schedule 15:50 on trading days, when asked at 15:49 / 15:50 / 16:00 on a weekday, Saturday, an NSE holiday and a special session, then the next occurrence and "missed" answers match ADR 0017 §2 (the run time is never before the day's session end).
2. Weekly (weekday + time), `after` (next occurrence = when the predecessor finishes successfully), `window`, `interval` (every N minutes within a window, aligned to the window start, nothing after its end), `continuous` give the expected occurrences for a given date, including across midnight India time and days without a session.
3. Save is refused (`400 services.invalid`, each issue named with its field) for: a circular or continuous-target run-after chain, window start ≥ end, interval < 1 or > 120 min or longer than its window, no day chosen, a schedule type the service does not allow, retries outside 0–5, timeout outside 1–240, an unknown parameter or a parameter outside its bounds, an invalid time.
4. Warnings are returned with the schedule and do not block: end-of-day fetch before 15:35; a window that does not cover 09:15–15:30 for a service whose definition requires it; a service that `MustNotOverlap` market hours scheduled inside them; a run-after chain longer than three; two services at the same minute. Rules for a service that is not registered are not evaluated (tested with test definitions for pre-open, nightly, backup).
5. Checks use the **whole set**: saving the end-of-day time to 15:20 warns; moving it after a dependent `after` service's own time is reported on the dependent service.

**Scheduler**
6. An edit takes effect at the next wake-up without a restart: after a save the new next-run time is used (fake clock + in-memory store).
7. A service never runs twice at once: a due or manual start while it runs is recorded `skipped` ("already running") and starts nothing.
8. A failed attempt is retried up to `retries` times with the back-off 30 s, 2 min, 10 min, 30 min, 1 h; every attempt is a run row; success ends the sequence; exhausting the retries ends the run `failed` with the last message. A run past `timeout` is cancelled and recorded `failed: timed out`.
9. Catch-up: at start-up, when an occurrence is past with no run since and catch-up is on, exactly one run starts (`catch_up`); with catch-up off, none; a paused service never catches up, also not when resumed; at most one catch-up per service per start.
10. A run left `running` by a previous process is closed `failed: interrupted` at start-up, before the catch-up decision.
11. The two moved services keep their behaviour: the existing end-of-day and instrument-refresh tests (once per day, skipped run with reason when a job runs or the Data account cannot fetch, trading days only, special-session run time, catch-up after a restart) pass re-pointed at the scheduler; `Data:Eod:Enabled/TimeIst` and the refresh time seed a row once and are ignored after it exists (test: seed, edit, restart keeps the edit).
12. Pausing stops scheduled and catch-up runs; **Run now** still works on a paused service (trigger `manual`) — except when the service is already running.

**API**
13. `GET /api/services` returns every registered service: id, name, description, runsAs, enabled, schedule (+ words), execution settings, parameters (with schema), status (`healthy | running | failed | paused`), lastRun (trigger, status, time, message), nextRun (null when paused/never), issues (errors/warnings), revision; plus the per-account sync as an entry the registry does not own (`readOnly: true`, `manageUrl: "/accounts"`; its refresh is changed in the Settings dialog of its card or in Accounts).
14. `GET /api/services/{id}/runs?page&pageSize` newest first; `GET /api/services/timeline?date` returns market hours, review cutoff, whether the date is a trading day and every enabled service's occurrences that day as marks/bands with the chain they follow.
15. `PUT /api/services/{id}/schedule` (body: enabled, schedule, retries, timeoutMinutes, catchUp, notifyOnFailure, parameters, revision) saves; `409 services.revision_changed` on a stale revision; `POST …/schedule/preview` validates and returns issues + the next three occurrences without saving; `POST …/schedule/reset` writes the defaults; `POST …/pause`, `…/resume`; `POST …/run` → `202` with the run, `409 services.already_running`. Unknown id → `404 services.not_found`. All need a session and CSRF; none needs step-up. A read-only entry refuses writes (`409 services.read_only`).
16. Every save, pause, resume, reset and run request writes an audit event (who, service, what changed) through the existing audit store.
17. `GET /api/services/notices` (derived on read) lists `service_failed` for the latest failed run of each service with *notify on failure*; the bell shows them next to the data notices, cleared by the next successful run.

**Screen**
18. `/services` (nav item loses its "coming in P1" mark) follows the prototype: title row; checks banner (all good / errors / warnings, naming service and field); the timeline card; the services table with the columns, status chips (icon + word), last-run trigger chip, next run in IST, actions Run now · Configure · Pause/Resume · History. From `md` it fills the viewport under the top bar like the Data screens (the card grid scrolls inside); on a phone the blocks stack. The sync card has **Settings** (dialog: each Portfolio account's refresh Off / Daily at HH:MM IST, saved through the accounts call with the other portfolio settings sent back as stored; an account that would import its holdings on saving is read-only with a link) and a menu with Settings… and Manage in Accounts, and no Run now / Configure / Pause / History. See *Redesign 2026-10-13*.
19. Configure (dialog): type select limited to the service's allowed types, type-specific fields (time + day chips with "Trading days"/"Every day" shortcuts; weekday + time; run-after select; from/to; every N minutes), enabled, retries, timeout, catch-up, notify on failure, parameters; the **next three runs** and the issues update as the owner types (debounced preview call); Save is disabled while an error stands; warnings are listed but allow saving; Restore default; a stale revision shows "changed elsewhere — reload".
20. History drawer: runs with trigger chip, status (icon + word), start, duration, message; infinite scroll; empty state "No runs yet".
21. Run now shows the service as running (progress for the end-of-day run reuses the Data job progress) and a toast on the outcome; the Data screen's "Automatic update" line and next-run time read the same schedule.
22. Loading, error with Retry, empty ("No services") and a11y states for every query; status never by colour alone; keyboard-reachable timeline marks have text equivalents; tests by role/label/text.

## Contract
- **API (module `platform`):** as in 13–17. Problem codes: `services.not_found`, `services.invalid` (with `issues[]`), `services.revision_changed`, `services.already_running`, `services.read_only`. DTOs additive; `scripts/gen-api.sh` in the same change.
- **Data (schema `platform`, migration `AddServiceSchedules`):** `service_schedules(service_id text pk, enabled bool, schedule jsonb, retries smallint, timeout_minutes smallint, catch_up bool, notify_on_failure bool, parameters jsonb, updated_by, updated_at, xmin concurrency)`; `service_runs(id uuid pk, service_id, trigger text check scheduled|catch_up|after|manual, attempt smallint, started_at, finished_at null, status text check running|succeeded|failed|skipped, message null, data_job_id null)` index `(service_id, started_at desc)`, a unique partial index on `(service_id) where status='running'` (one at a time). Grants: `xd_app` SELECT/INSERT/UPDATE on both, no DELETE.
- **UI:** lazy route `/services`, feature `features/services` (api, components, hooks, lib) with `index.ts`; shared pieces (chips, drawer shell, infinite grid) reused; prototype *Services*.
- **Events/jobs:** `ServiceScheduler` wakes ≤ 1 min and at the next occurrence; services implement `IScheduledService` (`RunAsync(context, ct)` returning outcome + message); the data job path is unchanged and linked by `data_job_id`.

## Non-functional
- Scheduler tick is a few indexed reads (≤ 20 services); no per-tick scans of market tables. List/preview/timeline p95 < 150 ms.
- Initial frontend bundle stays ≤ 200 KB gzip (the feature is a lazy chunk).
- Logging: `service.started|finished|failed|skipped|retry|catch_up` with service id and run id (source-generated log messages, no secrets); no schedule parameter is a secret.
- Security: authenticated + CSRF; parameters validated against the definition schema, never executed or interpolated.

## Test plan
- Domain/application unit: `ScheduleCalculator` (table-driven per type, fake clock, calendar edge days: weekend, holiday, special session, midnight, DST-free India time), rules/checks (each error and warning, whole-set effects, test definitions for services not yet built), scheduler (once at a time, retries/back-off, timeout, catch-up rules, pause, recovery, edit without restart, seed once), the moved runners' existing tests re-pointed.
- Integration (TimescaleDB): migration + grants (no DELETE), unique running index, revision conflict, seed-once across restarts, API end to end with a fake service.
- Frontend (Vitest + MSW stateful `servicesFake`): list/status/chips, banner variants, timeline, Configure per type with preview and issues, save/stale revision, pause/resume, Run now, history infinite scroll, notices in the bell, loading/error/empty, phone layout, a11y.
- Architecture: scheduler depends on abstractions only; module dependency rules unchanged.
- Visual check against the prototype in the browser (light, phone dark).

## Redesign 2026-10-13
After the owner reviewed the deployed screen (a sparse table with four buttons per row): the table became a **card grid** (as many columns as fit, at least 340 px; one on a phone), each card with the service's icon chip, name and status chip, a two-line description, a prominent **Next run (IST)** with a countdown ("in 3 h 12 min", minute by minute) and **Last run (IST)** with trigger and reason, the schedule in words with chips (retries · timeout · catch-up), a left accent bar and tint for failed / running / paused, and the end-of-day progress while it runs. A **summary strip** shows the counts (services, healthy, running, paused, failed), the next run overall with a countdown and the last run overall. The **timeline** has a taller track with hour ticks, the market band, the review cutoff, a live **"Now HH:mm IST"** marker (today only, moves each minute; reads the new day's timeline after midnight India time), starts and bands as pills in stacked rows, and scrolls sideways on a phone. **Actions:** one primary button (Run now; Settings on the sync card) and an overflow menu "Actions for <service>" (Configure, Pause/Resume, History; Settings… and Manage in Accounts on the sync card). An **IST clock** (HH:mm:ss, `Asia/Kolkata` whatever the browser's zone, rolling digits, no animation with reduced motion, HH:mm only on a phone, one label a minute for assistive technology) is in the top bar. Behaviour, contract and acceptance criteria are otherwise unchanged.

## Open questions (owner) — answered 2026-10-13: all as written (scheduler stays in `xd-api`; `Data:Eod:*`/refresh time are first-start seeds; holdings-sync row (read-only at the time; Settings dialog added 2026-10-13); no step-up; back-off and notify defaults)
1. **Hosting:** keep the scheduler in `xd-api` for now (recommended; the worker split is a later deployment ADR) — or create `xd-worker` as the host in this slice (adds a container, a second DB connection set and deploy work)?
2. **Seeds:** `Data:Eod:*` and the instrument refresh time become first-start seeds only, then the screen is the single source — OK?
3. **Holdings sync:** show the per-account refresh as a row linking to Accounts (recommended; amended 2026-10-13: with a Settings dialog), or leave it off the screen entirely?
4. **Edits without step-up:** schedule changes need only the session and CSRF — OK, or should changing/pausing ask for the password like role changes?
5. **Retry back-off** 30 s · 2 min · 10 min · 30 min · 1 h and "notify on failure" default **on** for the data jobs — OK?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddServiceSchedules` added
- [ ] Design docs updated (04 §3 services table and 3.1, 05 §6, 17), ADR 0018 accepted; `scripts/build-docs.sh`
- [ ] Deployed; the owner changes the end-of-day time on the screen and sees the next run move
