# BK-04 — System baskets on the Baskets screen

- **Status:** Draft (written 2026-10-25 under the owner's delegation; decisions below researched; owner's Approved needed before build)
- **Phase / roadmap row:** BK — system baskets, slice BK-04 of [BK-00](BK-00-system-baskets-plan.md)
- **Design refs:** docs/design/06-instruments-and-baskets.md §3.1, §3.4 · [ADR 0014](../adr/0014-portfolio-buckets-appear-as-system-baskets.md) · [P1-15a](P1-15a-portfolio-baskets-the-account-buckets-on-the-baskets-page-protected.md) · ISS-22 · frontend rules `frontend/CLAUDE.md` · prototype `docs/prototype/` (Baskets page, match its look)
- **Needs:** [BK-01](BK-01-index-basket-catalogue-fetcher-and-versioned-membership.md) (API fields `kind`, `system`, `membersKnownSince`, `source`), [BK-03](BK-03-derived-baskets-refresh-job-and-scores.md) for derived cards and versions (without it only index cards appear); [BK-05](BK-05-broker-flag-and-fo-baskets.md) for list cards (cards simply do not appear until their kind exists). Built: the Baskets feature in `frontend/src/features/baskets` (page, members grid, history drawer, copy-out).
- **Size:** 3 agent-days (cards and groups 1, provenance/as-of/status and version drawer 1, tests, a11y, responsive 1)
- **Safety:** no. Read-only screen; the only actions are existing copy-out and the two owner-act buttons that call BK-01/03 endpoints (disabled unless their setting is ON).

## Goal
The Baskets screen shows the system baskets as read-only cards with what they are, where they came from, which date their members are as of, and whether their source is fresh, so nobody mistakes a system list for history or for a recommendation.

## Scope
**In:** a "System" group after "Portfolio" and before "Your baskets" (order: portfolio, system, yours), cards with a `kind` chip and a lock; members grid read-only with scores/rank for derived; the provenance line; the as-of label; source status (`ok`, `stale`, `failed`, `rejected`, `never`); history/version drawer for system baskets (dated membership exists, unlike ADR 0014 portfolio ones); the "what this basket is NOT" text; filter chips by kind; copy-out to a manual basket; the benchmark hint line (BK-00 Decision 1); tests.
**Out:** creating, editing, archiving or reordering system baskets; the fetch/derived settings screens (Settings); weights; charts; any use of the basket as a scan scope (BK-06+).

## Acceptance criteria
1. Given the API returns system baskets, then they appear in a "System" group between "Portfolio" and "Your baskets"; each card shows the kind chip (`index`, `derived`, `list`), a lock, name, member count and colour; no drag handle, edit, archive or reorder controls, no drop highlight (as P1-15a).
2. Given an index basket whose `membersKnownSince` is 2026-10-23 and whose latest members are fetched 2026-10-25, then the card line reads exactly "Members as of 25 Oct 2026 · known since 23 Oct 2026; earlier dates use these members (ISS-22)" (date format as the app's existing India-date formatter).
3. Given a derived basket whose latest version has `asOf` 2026-10-23 and `effectiveFrom` 2026-10-26, then the line reads "Members as of 23 Oct 2026 (bars to that close) · effective from 26 Oct 2026 · {definition name}", and a "why" column shows score and rank (rank 1..k, score with 2 decimals; hand-worked: score 0.0213 prints 0.02, ATR% 2.1 % prints 2.10 %).
4. Given a source with state `stale` (no successful check for 3 days), then the card shows an amber "Stale: last checked {date}, last updated {date}" status and keeps showing the last good members; given `rejected` or `failed`, a red status with the reason text from the API and the last good members; given `never` (fetch off), a neutral "Not fetched yet: fetching is off" and no members; colour is never the only cue (icon + text).
5. Given the source is `ok`, then the status reads "Up to date: checked {date}" (green icon + text).
6. Given any system basket, then the members grid is read-only; a member row offers only **Copy to...** (menu, keyboard and drag, with Undo as for portfolio baskets); "Move" is disabled with the tooltip "System baskets cannot be changed"; a stray 409 `baskets.system` shows the stored problem text.
7. Given `unmatched` > 0, then the card shows "{N} members not in your instrument list" and the grid lists them greyed with "no data" for data-dependent columns (not hidden).
8. Given a system basket, then the history control is shown (unlike portfolio baskets): index baskets list membership changes with dates (added/removed, count and symbols from the members endpoint, not from audit), derived baskets list versions (`asOf`, `effectiveFrom`, member count, params hash short form); an as-of date input works for system baskets and sends `asOf`; before `membersKnownSince` the page shows the "earlier dates use these members" notice rather than an empty grid.
9. Given a derived basket, then its description shows "What this basket is NOT: ..." from the definition, and the in-sample warning "Ranked on past bars; do not test a strategy on the same window it was ranked on".
10. Given the kind filter chips (All, Index, Derived, List, Yours), then selecting one filters the cards, is kept in the address (`?kind=`), and survives a reload.
11. Given the benchmark in force is Nifty 50 and a Nifty 500 series is tracked, then one neutral line in the System group header says "Broad baskets compare better against Nifty 500: change in Portfolio" (hidden otherwise); it never changes a setting.
12. Given the owner-act buttons "Fetch now" and "Recompute now", then each is shown only to the owner session, disabled with the tooltip "Fetching is off in Settings" when the respective setting is OFF (the API answers 409 `baskets.fetch_disabled` / `baskets.derived_disabled`, and the client also handles that), and opens the existing step-up flow when enabled.
13. Given loading, empty and error states, then: skeleton cards while loading; "No system baskets yet: turn on fetching in Settings (off by default)" when none; an inline retry on error. Phone width (375): one column, no horizontal scroll, the status line wraps.
14. Given accessibility, then cards are in a list with the group heading, status is announced (`role=status`), the chip text is read, focus order is chip, name, status, grid; keyboard copy-out works; contrast meets the app theme.

## Contract
- **API:** uses `GET /api/baskets` (BK-01 fields), `GET /api/baskets/{id}/members?asOf=`, `GET /api/baskets/{id}/source`, `GET /api/baskets/{id}/versions[/{versionId}]` (BK-03), the two owner-act POSTs. No new endpoint here. Generated client types regenerated with `scripts/gen-api.sh` (should be a no-op beyond BK-01/03).
- **Data / events:** none.
- **UI:** route stays `/baskets`; query `?kind=`, `?basket=`, `?asOf=`; components `SystemGroup`, `SystemCard`, `SourceStatus`, `AsOfLabel`, `VersionDrawer` in `features/baskets/components`, reusing the members grid and `AddToBasket`; TanStack Query hooks per `frontend/CLAUDE.md`; MSW handlers for tests; labels in the existing copy style.

## Non-functional
First paint of 30 system cards under 100 ms after data (no per-card request: the list carries counts and source status; members load on selection). Members grid virtualised for 500+ rows as in the existing grid. No new dependency. Bundle impact under 10 KB gzip.

## Test plan
- **Frontend (Vitest + MSW):** group order; chip per kind; exact label strings of AC 2 and 3; each source state (ok, stale, rejected, failed, never) text and icon; unmatched list; read-only (no controls rendered); copy-out by menu/keyboard with Undo; stray 409; version drawer; `asOf` before known-since notice; filter in the URL; disabled buttons with tooltips; empty/error/loading; phone width; axe checks.
- **Backend:** none new (BK-01/03 tests).
- **Architecture:** none.

## Decisions (researched)
1. **Group order portfolio, system, yours.** Reason: the owner asked (P1-15a) for portfolio first; system baskets are reference lists and belong above the owner's editable ones to avoid mixing; the same lock+chip pattern keeps the screen one idea. Rejected: system baskets last (long manual lists push them out of sight); a separate page (a second place to look; ADR 0014 rationale). Source: P1-15a design 3, ADR 0014 §5.
2. **Two different dates on the card ("members as of" and "known since").** Reason: for index baskets the as-of date is the fetch date while history is forward-only (ISS-22); one date would imply history. Rejected: a single "updated" date. Source: research §3.4 and §3.3 UI note.
3. **Show the last good members even when stale or rejected.** Reason: fail-closed policy; the status says what is wrong. Rejected: hiding members on failure. Source: research §3.3.
4. **History drawer shown for system baskets** (ADR 0014 hid it only because portfolio baskets have no dated membership). Source: research §3.3 UI; ADR 0014 §6.
5. **"What it is NOT" and the in-sample warning are on screen, not only in docs.** Reason: derived baskets look authoritative. Source: research §3.4 last bullet, §2 failure modes.
6. **Benchmark hint is text only.** Reason: BK-00 Decision 1; a UI must not change a global setting. Rejected: a toggle here.
7. **Owner-act buttons live here but are disabled while the setting is OFF**; the API is the authority. Source: BK-00 Decision 5.

## Risks and honest limits
- A card with an old "known since" date may be read as history; the exact label text is tested.
- 30+ cards can crowd the screen; the filter chips and a collapsed-by-default derived group are the answer; a search box is out of scope.
- Symbols in the version drawer come from the members endpoint at that `asOf`; for index baskets before `membersKnownSince` the drawer says so.

## Open questions
Collapse the System group by default on first visit? (suggest expanded). Show a score column for index baskets (none exists)? (no).

## UNVERIFIED
The Baskets prototype's exact card layout (prototype page not opened; match at build); the existing history drawer's props; the formatter name for India dates; whether a Nifty 500 index series is tracked (hint logic depends on `IPortfolioBenchmark` data).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green (typecheck, ESLint, Prettier, Vitest, build); reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`) if types changed
- [ ] Docs 06 §3.1 updated; plan row; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **"Members as of" source:** the date comes from `source.lastOkAt` (last successful check, including 304 `unchanged`) for index and list baskets, so a stable list does not look stale; "known since" is `membersKnownSince`; the date of the last actual change is shown in the drawer. For derived baskets it is the latest version's `asOf`.
- **One formatter per unit:** ratio 0.0213 prints `0.02` (2 decimals, used for ER, beta, VR, rho); percent 0.021 prints `2.10 %` (ATR%, volatility, momentum); rupees 100,000,000 prints `Rs 10.00 Cr` (ADTV, crore with 2 decimals). The score unit comes from the definition (`unit: ratio|percent|rupees`) in the version response; AC3 is read with these.
- **Settings state:** the client learns `fetchEnabled` / `derivedEnabled` from `GET /api/baskets` (BK-01 added them); the "no new endpoint, gen-api no-op" claim is withdrawn: types change with BK-01.
- **AC11 fixture:** MSW fixtures for "Nifty 500 tracked" and "untracked"; the hint shows only in the first.
- **Size:** 4 agent-days.
