# P8-15 — Plan notifications in the bell (read only)

- **Status:** Approved (owner: P8-00 questions answered as recommended; in-app only, read-only)
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-15 of [P8-00](P8-00-reports-and-ui-plan.md), 3 d
- **Design refs:** docs/design/13-reports-and-ui.md "Notifications and pop-ups" · [ADR 0030](../adr/0030-read-only-notices-retention-and-structured-failures.md) (notices are read only) · ADR 0018 §7 (derived notices) · ADR 0083/0086/0095/0096/0093 (plans, runs, review, pre-open, journal) · ADR 0101 · P8-00 Q6 (no push) and the safety section

## Goal
The bell tells the owner about the nightly plan without a scheduler, a push or a button that does anything: a plan waiting for review (with the minutes to its stored cutoff), a nightly run that failed closed (gate and reason), tickets that lapsed, tickets armed or invalidated at pre-open, and approved tickets whose fills are not matched yet. Each item goes away by itself when the stored state changes.

## Scope
**In:** `GET /api/plans/notices` (derived at read time from the stored plan record, the stored plan runs and the stored fills; nothing is written); the pure rules and the handler; the bell reads the endpoint like the other notice sources; each item opens `/plans?plan=<id>` (the Plans screen already honours `?plan=`; `/plans` for a run that failed closed, which has no plan); contract regenerated; ADR 0101; roadmap row.
**Out (not in this slice):** push, e-mail or any call that leaves the app (P8-00 Q6); a scheduler or a stored notice table (nothing is reconciled, so nothing is stored); a command button of any kind (the allow-list of bell commands is untouched); a pop-up (these are not attention items); a post-trade form; any write to a plan, ticket, approval or journal; a price-crossing alert (needs numeric levels, not built).

## Acceptance criteria
1. Given a plan in `published` or `inreview` with an undecided ticket and a cutoff in the future, when notices are read, then one `plan_awaiting_review` item says how many tickets are undecided and how many minutes remain (rounded up); a warning at 30 minutes or fewer, information above. Once every ticket is decided, or the cutoff has passed, the item is gone. *`PlanNoticeRulesTests`, `GetPlanNoticesHandlerTests`.*
2. Given the newest recorded attempt of the newest planned session is `failed_closed`, then one `plan_nightly_failed` error item names the gate and the reason (one line, at most 160 characters). A later published attempt, or a newer session that published, removes it. A `dry_run` or a `published` attempt never raises it. *`PlanNoticeRulesTests`, `GetPlanNoticesHandlerTests`.*
3. Given a plan of today or later with tickets in `lapsed`, then a `plan_tickets_lapsed` warning says how many lapsed and that lapsing is never approval; it is gone once the plan's session is over or the plan is `expired`. *`PlanNoticeRulesTests`.*
4. Given tickets in `armed` / `invalidated` for a plan of today or later, then `plan_tickets_armed` (information, "watched, no order") / `plan_tickets_invalidated` (warning) items say how many; they go when no ticket is in that state any more (triggered, expired, closed) or the session is over. *`PlanNoticeRulesTests`.*
5. Given approved-family tickets (approved, armed, triggered, closed) of a plan of today or earlier with a stored broker fill on the session day for the same account, normalised symbol and side, and no journal entry for that ticket and account, then one `plan_fills_unmatched` information item counts those tickets and says the journal matches them. It is gone when the entry exists. A ticket with no fill raises nothing. *`PlanNoticeRulesTests`, `GetPlanNoticesHandlerTests`.*
6. Only the plans of the last three India days (newest version per session, at most three sessions) and the newest run are read; older state never shows. *`GetPlanNoticesHandlerTests`.*
7. Items are ordered error, warning, information; then the newer session first; then by id. Empty store gives `{ "items": [] }`. *`PlanNoticeRulesTests`, `PlanNoticesApiTests`.*
8. Every item has a route and no action list; no symbol, size, price or strategy text is in any item. *`PlanNoticeRulesTests`.*
9. Session auth: anonymous is 401; only GET exists on the route; POST/PUT/DELETE are 404/405. The handler calls only read members (a statement-free read: the store fakes record no write). The no-order tripwire architecture tests stay green. *`PlanNoticesApiTests`, `Architecture.Tests`.*
10. The bell shows the items next to the other sources; a plan item counts as unread until read and Open leads to its route; an item that is no longer returned leaves the list and the badge; a resolved item of another source is not counted; a kind this version does not know is dropped; the bell stays usable when the endpoint fails. *`PlanNotices.test.tsx`.*
11. The first-load bundle guard still passes; before and after numbers are reported. *`npm run build`, bundle guard.*

## Contract
- **API:** `GET /api/plans/notices` → `PlanNotices { items: PlanNotice[] }`; `PlanNotice { id, kind, severity, text, route, planId?, at? }`. `id` is `plan:{review|lapsed|armed|invalidated|fills}:{planId}` or `plan:nightly:{yyyy-MM-dd}`; `kind` is `plan_awaiting_review`, `plan_nightly_failed`, `plan_tickets_lapsed`, `plan_tickets_armed`, `plan_tickets_invalidated`, `plan_fills_unmatched`; `severity` is error, warning or info; `route` is an in-app path. No error case beyond 401.
- **Data:** none; no migration. Reads through existing ports: `IPlanStore` (`ListAsync`, `ItemsAsync`, `ListRunsAsync`) and `IJournalStore` (`FillsAsync`, `EntriesAsync`).
- **UI:** the existing bell (`features/notifications`); one more source in `useNotifications`, a `derivePlanNotifications` mapping, six kinds. No new screen, no new route. The query lives in `shared/api` like the other notice sources, so the bell does not import the Plans feature.
- **Events/jobs:** none. The bell polls every 60 s and on window focus, as for the other sources.

## Non-functional
Per read: one plan list (20 rows), one run list (20 rows), the items of at most three plans, and for fills one fill query plus one entry read per plan with approved tickets; a light poll. No secret is read; there is no logging (a read of stored state). Bundle: no new eager code beyond a mapping function and a query definition in the lazy bell chunk and the shared API module.

## Test plan
Unit: `PlanNoticeRulesTests` (each item appears, disappears when the state changes, boundaries, ordering, no strategy text), `GetPlanNoticesHandlerTests` (fake ports; window; newest version only; only reads). Integration (real TimescaleDB): `PlanNoticesApiTests` (401, empty, a seeded plan and a failed run, resolves after a decision). Frontend (MSW): `PlanNotices.test.tsx`. Architecture: unchanged and green.

## Open questions
None (all answered as recommended in P8-00). Not verified by this slice: how the owner finds the wording on a real day; the fills rule matches by account, symbol, side and day only (the journal itself refuses to guess when two tickets fit).

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green
- [x] Contract regenerated (`scripts/gen-api.sh`); no migration
- [x] ADR 0101 and the roadmap row; `scripts/build-docs.sh`
