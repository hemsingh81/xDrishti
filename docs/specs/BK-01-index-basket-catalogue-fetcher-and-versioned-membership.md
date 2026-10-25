# BK-01 — Index basket catalogue, fetcher and versioned membership

- **Status:** Draft (written 2026-10-25 under the owner's delegation; decisions below researched; owner's Approved needed before build)
- **Phase / roadmap row:** BK — system baskets, slice BK-01 of [BK-00](BK-00-system-baskets-plan.md)
- **Design refs:** docs/design/06-instruments-and-baskets.md §2.1, §3.1, §3.4 · [ADR 0014](../adr/0014-portfolio-buckets-appear-as-system-baskets.md) · ISS-22 · research [system-baskets-nse](../research/system-baskets-nse.md) §1, §3
- **Needs:** built P1-15 / P1-15a; instrument master (ISIN, symbol, retired flag); services framework. Nothing else.
- **Size:** 4 agent-days (migration and domain 1, catalogue and parser 1, fetch/diff/apply and job 1.5, tests and ADR 0.5)
- **Safety:** yes. A network fetch of NSE files. `Baskets:System:Fetch:Enabled` defaults **OFF**; with it OFF the job makes zero HTTP calls and the catalogue is only a table. The owner reads the NSE terms and records an ADR before turning it on. No order, no trading config touched.

## Goal
Official NSE index constituent lists (Nifty 50, 100, 500, sector indices and more) become read-only system baskets whose membership is stored with dates from the first fetch on, refreshed by one generic job, failing closed and never claiming history it does not have.

## Scope
**In:** schema (`kind`, `system` on baskets; `basket_sources`; `basket_fetches`; `provenance`, `detected_at`, `source_version_id` on members); the catalogue of about 26 index files as data (seeded, disabled); a generic CSV fetcher (conditional GET, validation, ISIN-then-symbol matching, diff, one-transaction apply); the `system-baskets` scheduled service for index sources; protection (`baskets.system` 409) for kinds `index`; API for list/source status; a manual "fetch now" owner act; tests and ADR.
**Out:** derived baskets (BK-02/03), lists from Dhan or the F&O files (BK-05), the screen (BK-04), parsing press-release PDFs, index weights, any history before the first fetch, auto-tracking of unmatched members (the owner's call).

## Acceptance criteria
1. Given `Baskets:System:Fetch:Enabled` OFF (default), when the service runs or API starts, then no outbound HTTP request is made (counting handler = 0) and the run records `skipped: disabled`.
2. Given a fixture `ind_nifty50list.csv` with header `Company Name,Industry,Symbol,Series,ISIN Code` and 50 rows, all 50 matching master ISINs, on the first fetch (IST date 2026-10-23), then 50 member rows exist with `valid_from = 2026-10-23`, `valid_to` null, `provenance = fetched`, `detected_at` the fetch instant; the fetch row is `applied`, `added` 50, `removed` 0; the basket is `kind = index`, `system = true`.
3. Given the same bytes fetched again (same sha256) or a 304 answer, then the fetch row is `unchanged`, no member row is written.
4. Given a review-week file with 2 symbols gone and 2 new ones (50 rows still) fetched on 2026-10-01 (inside the September review window, 20 Sep to 7 Oct, so the change bound is lifted), then the 2 removed rows get `valid_to = 2026-10-01`, the 2 new rows `valid_from = 2026-10-01`, and `members(asOf = 2026-09-30)` returns the old 50, `members(asOf = 2026-10-01)` the new 50 (valid_to is exclusive of the day it ends: a row with `valid_to = D` is not a member on D, a row with `valid_from = D` is).
5. Given the same 2-out-2-in file fetched on 2026-11-10 (outside every review window), when the allowed change is `max(3, ceil(0.06 x expected))` = 3 for 50, then it is applied (the change size is `max(removed, added)` = 2, within the bound of 3); a file with 4 removed and 4 added is `rejected` with reason `too_many_changes`, nothing applied, previous membership still served.
6. Given a file that would remove more than 50 % of current members (30 of 50), then `rejected` with `tripwire`, unless the owner forces it (an owner act with step-up, recorded with the setting snapshot).
7. Given a 404 whose body is HTML, a `Content-Type` that is not `text/csv`, a wrong header, a row count outside `[expected_min, expected_max]` (Nifty 50 bound 50..50; 49 rows fails), or more than 20 % of rows with no master match (11 of 50), then `rejected`, no change, the previous membership stays visible and the source shows the reason.
8. Given a row whose ISIN is in the master but whose symbol differs (a rename), then it matches by ISIN; given an ISIN not in the master and a symbol that is, then it matches by symbol with a warning; given neither, it counts as unmatched (listed in the fetch record, not as a member), and the basket shows "N members not in your instrument list" with N.
9. Given 403, 429 or a timeout, then retries at 5, 30 and 120 minutes (3 tries), then the fetch is `failed`; after 3 consecutive days without a successful check the source status is `stale`; 403/429 backs the source off for 24 hours.
10. Given any change route (PUT, DELETE, members add/remove/order, transfer with it as target or as `move` source) on an `index` basket, then 409 `baskets.system`; `copy` out of it to a manual basket is allowed; archive is impossible.
11. Given `asOf` earlier than the basket's first `valid_from`, then the API still answers with the first known membership and sets `membersKnownSince` (first `valid_from`) so clients can print "membership known since {date}; earlier dates use these members (ISS-22)". It never returns an empty list for a date before the first fetch.
12. Given the audit log, then a membership change writes `BasketMembershipChanged` with counts only (no symbols).

## Contract
- **Data / migration** `AddSystemBaskets` (name at build; `xd_owner` migration, `GrantSchemaUsageToAppRole` already held by `market`):
  - `market.baskets` + `kind text not null default 'manual'` check `in ('manual','index','list','derived')` (portfolio stays computed), `system boolean not null default false`, check `(kind = 'manual') = (not system)`.
  - `market.basket_sources(basket_id pk/fk, source_url, file_name, host_alt, format_version, cadence, expected_min, expected_max, enabled default false, license_note, state ('ok','stale','rejected','failed','never'), last_ok_at, last_checked_at, etag, last_modified, sha256)`.
  - `market.basket_fetches(id, basket_id, fetched_at, http_status, etag, last_modified, sha256, rows, outcome ('unchanged','applied','rejected','failed','skipped'), added, removed, unmatched, warnings jsonb, error text)`; insert-only for `xd_app`.
  - `market.basket_members` + `provenance text not null default 'manual'` (check in `fetched, imported, recomputed, derived, manual`), `detected_at timestamptz null`, `source_fetch_id uuid null`. The unique open-row index stays. Existing rows keep `manual`.
  - Seed: the catalogue rows (name, file, host, bounds) as migration data or a seed step, all `enabled = false`.
- **Catalogue** (file names are data, never derived): Nifty 50 `ind_nifty50list.csv` (50..50), Next 50, 100, 200, 500 (bound 500..500; widen only if a trailing-line quirk is evidenced at ingest), Midcap 150, Smallcap 250, Microcap 250 `ind_niftymicrocap250_list.csv`, Bank (14), Financial Services, IT (10), Pharma (20), Auto (15), FMCG (15), Metal (15), Energy (40), Realty (10), PSU Bank (12), Oil & Gas (15), Healthcare (20), Consumer Durables (14), Media (10), Nifty200 Momentum 30, Nifty100 Quality 30, Nifty100 Low Volatility 30, Nifty200 Alpha 30. Base A `https://nsearchives.nseindia.com/content/indices/`, base B `https://niftyindices.com/IndexConstituent/`, host per file as recorded in the research table. Alpha 50 / High Beta 50: not seeded (link unknown, UNVERIFIED).
- **Application ports:** `IBasketSourceFetcher` (conditional GET), `IBasketMembershipApplier` (diff and apply in one transaction), `IBasketSourceStore`. Pure Domain `MembershipDiff.Compute(current, incoming) -> (Added, Removed)` and `FetchGuard.Check(...)` (header, bounds, unmatched fraction, change bound, tripwire). Handlers internal sealed, registered explicitly.
- **API** (session, tag `Baskets`):
  - `GET /api/baskets` gains `kind` in `manual|portfolio|index|list|derived`, `system`, `membersKnownSince`, `source { state, lastOkAt, lastCheckedAt, unmatched }` for system baskets.
  - `GET /api/baskets/{id}/source` → source status and the last 20 fetch outcomes (no raw file).
  - `POST /api/baskets/system/fetch` (owner act, step-up; body `{ basketIds? , force? }`) → 409 `baskets.fetch_disabled` when the setting is OFF; otherwise runs now, returns outcomes. `force` only overrides the tripwire.
  - Problem codes: `baskets.system` 409, `baskets.fetch_disabled` 409, `baskets.not_found` 404, `baskets.invalid` 400.
- **Settings** (`Baskets:System:Fetch`, `[Range]`, `ValidateOnStart`): `Enabled` false; `PreferredHost` `A`; `UserAgent` descriptive name plus owner contact (empty = refuse to run); `RunAt` 07:30 IST; `MaxChangeFraction` 0.06 with floor 3; `TripwireFraction` 0.5; `MaxUnmatchedFraction` 0.2; `BackoffMinutes` [5,30,120]; `StaleAfterDays` 3; `ReviewWindows` (months 3,6,9,12; from day 20 to day 7 of the next month; polled twice a day).
- **Events / jobs:** service `system-baskets` (`IScheduledService`, Daily 07:30 IST, `CatchUpReach.Latest`), one run at a time (advisory lock + running row as the master job does). Idempotent by sha256. Index files: one conditional GET per file per day at most, `If-None-Match`/`If-Modified-Since`. Never calls `www.nseindia.com/api/*`.
- **UI:** none here (BK-04).

## Non-functional
Steady state about 26 conditional GETs per day, mostly 304 (about 1 CPU-second). Apply of one list under 200 ms for 500 rows (one transaction). Log events `basket.fetch` (basket, outcome, rows, added, removed, unmatched, ms; no content), block id next free at build. Fixed descriptive User-Agent; no secrets; session protected; the URLs are catalogue data, not user input (no SSRF from the API).

## Test plan
- **Unit (Domain):** `MembershipDiff` (2 out/2 in; identical; all new); `FetchGuard` table over each rejection reason and its boundary (49 vs 50 rows; unmatched 10 of 50 passes, 11 fails; removed 25 of 50 passes, 26 fails the 50 % tripwire; changes 3 passes, 4 fails outside review window; inside the window the change bound is lifted).
- **Application (fakes):** disabled makes zero calls; ISIN-then-symbol matching; duplicate symbol row; BOM and blank lines; retired instrument whose membership closes; `asOf` boundary day (valid_to exclusive); first-fetch label.
- **Integration (Testcontainers):** fixtures for clean, extra column, HTML 404, truncated, duplicate symbol, rename with same ISIN; one transaction (kill after insert, nothing partial); lock test (two concurrent runs, one runs); `xd_app` cannot update/delete `basket_fetches`; all refused routes return 409; copy-out allowed; 401 without a session; contract snapshot.
- **Architecture:** Domain pure; no order-named member; no Application type references HttpClient.

## Decisions (researched)
1. **One generic fetcher over a catalogue table, no per-index code.** Reason: all verified files share `Company Name,Industry,Symbol,Series,ISIN Code`; file names are irregular (`list` vs `_list`, mixed case), so names live in data. Rejected: deriving file names from index names. Source: research §1.1, §1.2 (VERIFIED by fetch 2026-10-25).
2. **Effective date = IST date of the first fetch that shows the change; `detected_at` kept separately.** Reason: NSE's own effective date is not in the file; this is conservative (a stock added "last trading day of March" appears next morning). Owner may enter the NSE date later (`imported`). Rejected: guessing the NSE date from the review calendar. Source: research §3.2.
3. **Match by ISIN first, then symbol.** Reason: symbols change on renames, ISIN is stable except some corporate actions. Rejected: symbol only. Source: research §3.2.
4. **Fail closed, keep the last good, never half-apply** (guards in AC 5 to 7). Reason: same discipline as the master job's tripwire. Rejected: apply what parses. Source: research §3.3, doc 06 §2.2.
5. **Conditional GET once per file per day; host A (`nsearchives`) preferred, B as fallback.** Reason: static CDN returned 200 to `curl` with a browser UA; `ETag`/`Last-Modified` strong validators verified. Non-browser UA acceptance UNVERIFIED, so the UA is a setting and a failure shows as `failed` rather than being worked around. Rejected: cookie-gated JSON APIs; spoofing a browser UA by default. Source: research §1.1, §1.5.
6. **No history is invented.** Membership is forward-only; before the first `valid_from` the first known membership is served with `membersKnownSince`. Rejected: empty list before first fetch (breaks reports); back-fill. Source: research §3.1, §3.4, ISS-22.
7. **Skip the 'Equal Weight' variants** (same members as the parent); Alpha 50 / High Beta 50 only after a one-time manual capture of the link. Source: research §1.2.
8. **Local copy only, no public exposure, no weights, files not in the repo.** Reason: licence terms unread (UNVERIFIED). Source: research §1.5.

## Risks and honest limits
- Licence terms unread; the OFF default and ADR gate are the control.
- Announced future changes are invisible (the file shows only now); a change appears with up to one day of delay.
- File format or host changes: caught by header and bounds checks, shown as `rejected`.
- Strategy indices rebalance quarterly; the review windows cover Mar/Jun/Sep/Dec.
- Index membership is forward-only: a run before the first fetch uses "members as of the first known date".

## Open questions
1. Owner: permission to fetch (read terms, ADR). 2. Which of the 26 files to enable first (suggest Nifty 50, 100, Midcap 150, Bank = the index ones in the pre-registered eight). 3. Contact string for the User-Agent.

## UNVERIFIED
NSE/NSE Indices terms of use and redistribution rights; acceptance of a non-browser User-Agent by the CDN; exact announcement lead times; locations of Alpha 50, High Beta 50 and Nifty200 Quality files; `master_refreshes` as a pattern to mirror; existing ADR numbering at build; whether `market.baskets` has other writers that assume no `kind`.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR (fetch permission and terms, owner act) and doc 06 §3.1/§3.4 updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **Source reference:** `basket_members.source_ref uuid null` + `source_kind text null` (`fetch`) replaces `source_fetch_id`; BK-03/05 reuse it. The migration also adds `note text null` (already on members per the entity config; confirm) for BK-05 categories.
- **AC 13:** the reorder route (`PUT /api/baskets/order` with an index basket id) and the per-basket member order route are refused 409 `baskets.system`; `PUT /api/baskets/order` lists manual baskets only.
- **AC 14 (startup):** with `Fetch:Enabled` false, the NSE `HttpClient` is not registered in DI (a host test resolves the service provider and finds no named NSE client); the catalogue is seeded by the migration with no network; enabling it needs a restart-free setting change that is an **owner act with step-up** (toggling either direction), audited with the setting snapshot.
- **AC 15 (force):** `POST /api/baskets/system/fetch` with `force: true` and no valid step-up returns 401/403 and applies nothing; with step-up, force overrides only the tripwire (not header, bounds or unmatched checks); the fetch row records `forced = true`, the actor and the setting snapshot.
- **Nifty 500 bound** is 500..500 (the 502-line count was header + 500 + a trailing line; widen only if evidenced at ingest).
- **AC 9 retry:** 4 attempts total: immediately, then after 5, 30 and 120 minutes; `basket_sources` gains `next_attempt_at timestamptz null` and `fail_count int not null default 0` (reset on success); 403/429 sets `next_attempt_at` +24 h.
- **Retired or untracked but still listed:** an instrument retired in the master that NSE still lists stays a member, flagged `unmatched = retired` with its retired date in the fetch record and the members response; membership is never closed silently by the master's retirement, only by a fetch that no longer lists it.
- **Contract:** `GET /api/baskets` adds `fetchEnabled` and `derivedEnabled` (booleans, owner-visible) so clients know the setting state without a new endpoint.
- Size stays 4 d (the additions are small).
