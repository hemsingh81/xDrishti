# P15-11 — Screens: results on plan tickets and Today, a Filings tab under Data, the event-filter switch card, and the P15 verdicts with their evidence

- **Status:** Draft (written 2026-11-02 under the owner's delegation; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P15 — slice P15-11 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Screens"); first on the cut line.
- **Design refs:** docs/design/13 (reports and UI), 10 (selection), 15 (pre-trade checks) · P8-00 D3 (honest states, evidence block), P8-01 (analytics kit: `EvidenceBlock`, `MetricTile`, `DataTable`), P8-19 (owner-act forms with password) · ADR 0182 (the switch). Prototype: **no screen in `docs/prototype/` shows filings, a results calendar or these verdicts** (search 2026-11-02); the nearest patterns are the Data tabs, the plan ticket checks and the Learning evidence cards, which this slice follows.
- **Needs:** read endpoints that exist (`/api/filings/coverage`, `/api/filings`, `/api/filings/{id}`, `/api/calendar/results`, `GET/PUT /api/selection/event-filter`), P15-03 (`/api/filings/{id}/facts`), P15-08 (`/api/filings/extractions/coverage`), P15-10 (`/api/research/extensions`), P8-08 and the P8 kit (built). Each part works with the endpoints that exist when it is built; a missing endpoint is a hidden section, never a fake value.
- **Size:** 2 agent-days (estimate).
- **Safety:** no for the read parts. The switch card is an **owner act** (it sends `PUT /api/selection/event-filter` with a fresh password); it adds no new authority (the endpoint, its checks and its audit are P15-06's) and nothing in this slice sends an order.

## Goal
The owner sees, where he already works, when each company reports and what the system does about it: the days to results on each plan ticket and on Today, the coverage of his filings and where it is missing, the event filter's switch with its reasons, and every P15 verdict with its evidence. `unavailable` is shown as a word with its reason, never as a blank or a zero.

## Verified facts (read 2026-11-02)
**Code:** the frontend has `features/data` (tabs: overview, coverage, calendar, corporate actions, …), `features/plans` (tickets and checks), `features/today` (`TodayPage`), `features/learning`, `features/reports`, `features/owner-acts` and `shared/analytics` (`EvidenceBlock`, `MetricTile`, `DataTable`). No frontend code calls a filings, calendar-results or event-filter endpoint today (search of `frontend/src`: only the generated `schema.d.ts`). The plan pre-trade check "results within 3 days" already shows `unavailable` on tickets (P7-05).

## Scope
**In:**
- **Plan tickets:** an "Events" line per ticket from the plan basis and checks: `results in 3 sessions (Wed 12 Aug, intimated 3 Aug)`, or `unavailable — no upcoming meeting known` / `— no calendar entry`; and, when the event filter is ON, `excluded by the event filter (N=2)` or `held: event unavailable`.
- **Today tile** "Results this week": tracked instruments with a visible meeting whose event session is in the next 5 sessions (from `/api/calendar/results`), plus the count without an entry (link to the Filings tab).
- **Data → Filings tab** (`/data?tab=filings`): coverage per kind and quarter (ok, unavailable by reason, superseded, out of order, instruments without a filing); the results calendar list with filters; per filing metadata and, for results, the parsed facts with their states and sanity flags; the extraction coverage.
- **Event-filter card** (in Settings → Selection, or the owner-acts area by the P8-19 pattern): the effective switch, N, approval mode, history (latest 50, `decided_by owner|auto` shown distinctly), and the reasons it is not acting; the change form asks for N, approval, a reason and the password.
- **Verdicts:** a "Fundamentals and events" section in Learning with one card per family (`filter:results`, `ext:ev-1`, `ext:fu-1`, `ext:llm-1`, `pead`) showing the verdict word, the evidence block, the missing quantities, and for `filter:results` the shares skipped, taken and unavailable by reason, including the share lost to `no_upcoming_entry`.
- MSW handlers and tests; `scripts/gen-api.sh` types only (no API change).

**Out:** any new endpoint; editing filings or calendar rows; labelling announcements (CLI, P15-07); showing announcement text or the model's reason in bulk (only counts; a single filing's metadata); turning features into scoring; charts beyond the kit.

## Acceptance criteria
1. **Every state.** Each section renders loading, empty ("no filings stored yet"), error (problem details title), `unavailable` with its reason word, and data, through the P8-01 components (MSW test per state). No section prints `0` or `—` for an unavailable value.
2. **Tickets.** Hand cases from the P15-06 window table render the right text for days 0-4, `no_entry`, `only_cancelled`, `no_upcoming_entry`, filter OFF and ON (excluded, held, optional caveat).
3. **Today tile.** With a fixture of 3 meetings in the next 5 sessions and 27 instruments without an entry, the tile lists 3 rows by event session and says "27 without a calendar entry"; with none, "no results meetings known for the next 5 sessions" plus the count.
4. **Filings tab.** Coverage table matches the endpoint figures; a results filing shows its facts per span and basis with `unavailable` reasons and the parser version (labelled "not checked against real filings" for `xbrl-1-synthetic`); no file content, path or announcement text is requested or shown.
5. **Switch card.** The form is disabled without a password and a 1-300 character reason; a 401/423 shows the auth message, 400 `selection.invalid_setting` marks the field, 409 `selection.stale` reloads; a successful change shows the new history row. `auto` rows are labelled "applied automatically from verdict <ref>", never "approved by owner". The password is never stored in state beyond the request.
6. **Verdict cards.** NOT_EVALUABLE shows the missing quantities as text (`seasons 8 of 20`); KEEP and DROP show the bound, margin and N; a hold-out not yet read is stated; the `no_upcoming_entry` share is on the F card.
7. **Accessibility and size.** Keyboard reachable, labelled controls, contrast per the P8-16 pass; the bundle guard of P8-01 passes (new code lazy-loaded with its route).

## Contract
- **UI:** `/data?tab=filings`, the Today tile, ticket line, the switch card, the Learning section. No API or data change.

## Non-functional
Each view's first render ≤ 300 ms after data on the owner's machine; the calendar list virtualised above 500 rows.

## Test plan
Vitest + MSW per component and state; one route test per new view; the switch form's error paths; the bundle guard.

## Decisions (researched)
1. **Follow existing screens, not a new page family.** No prototype exists for these views; the Data tabs, ticket checks and Learning cards are where the owner already looks.
2. **The switch card uses the P8-19 owner-act form.** One way to ask for a password across the app.

## Risks and honest limits
- Without a prototype the layout is the agent's; the owner reviews it at Approval of the build.
- Most cells will say `unavailable` until the owner's files cover the window; that is the intended message.

## Open questions (owner)
1. Where should the switch card live: Settings → Selection or the owner-acts area? **Default: Settings → Selection, linked from owner-acts.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any unavailable shown as a number, any text of a filing shown, any `auto` decision labelled as the owner's)
- [ ] Doc 13 (screens) updated; no ADR unless a behaviour changes
