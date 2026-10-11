# P1-03 — Instrument master: import from Dhan, NSE sectors, search, manual + daily refresh

- **Status:** Approved (owner, 2026-10-11: use Dhan's detailed scrip master; keep NSE equity + indices + futures; manual refresh button + daily automatic refresh; find a sector source now)
- **Phase / roadmap row:** P1 — Data platform & instruments · story **P1-03** (+ the master-list half of P1-17) · decision [ADR 0010](../adr/0010-instrument-master-sources-and-scope.md)
- **Design refs:** docs/design/06 §1–2, 05 §7; prototype `docs/prototype/` → Instruments

## Goal
The owner opens **Instruments**, sees the master list (NSE shares, ETFs, indices, stock/index futures) with search and filters, can refresh it from Dhan with one click (and it refreshes itself every morning), and sees exactly what changed.

## Scope
**In:** schema `market` (instruments, broker ids, refresh runs); importer for Dhan's `api-scrip-master-detailed.csv` and NSE's Nifty Total Market list; manual and daily refresh with progress; search/facets API; Instruments page (master list view, refresh panel); tests with a committed small fixture; docs.
**Out (next stories):** tracking (P1-05), baskets (P1-15), history/backfill (P1-12), index membership and rule-based baskets, options/other segments.

## Data (profiled 2026-10-11 — see ADR 0010)
Dhan file: 207,560 rows / 33 columns / 35 MB; kept rows ≈ 4,400: `EXCH_ID=NSE` with `SEGMENT=E & INSTRUMENT_TYPE ∈ {ES, ETF}`, `SEGMENT=I & INSTRUMENT=INDEX`, `SEGMENT=D & INSTRUMENT ∈ {FUTSTK, FUTIDX}`; drop `*NSETEST*`. Example: `SECURITY_ID 2885, ISIN INE002A01018, UNDERLYING_SYMBOL RELIANCE, SYMBOL_NAME RELIANCE INDUSTRIES LTD, DISPLAY_NAME Reliance Industries, INSTRUMENT_TYPE ES, SERIES EQ, LOT_SIZE 1, TICK_SIZE 10, ASM_GSM_FLAG N, MTF_LEVERAGE 4.545, SM_FREEZE_QTY 81195`. Futures: `UNDERLYING_SYMBOL 360ONE, SYMBOL_NAME 360ONE-Oct2026-FUT, LOT_SIZE 500, SM_EXPIRY_DATE 2026-10-27`.

## Design (defaults — object to any)
1. **Tables (`market` schema, migration, `GrantSchemaUsageToAppRole`):**
   `instruments` (id uuid v7, canonical_symbol unique, exchange, segment, type `share|etf|index|future`, name, display_name, isin, series, lot_size, tick_size, expiry_date, underlying_symbol, sector, sector_source, fno_eligible, asm_gsm_flag/category, mtf_leverage, freeze_qty, first_seen_at, last_seen_at, retired_at),
   `instrument_broker_ids` (instrument_id, broker `dhan`, broker_id `SECURITY_ID`, segment; unique broker+broker_id+segment),
   `master_refreshes` (id, trigger `manual|daily|forced`, started_at, finished_at, status `running|succeeded|unchanged|failed`, source etag/last_modified (Dhan + NSE), counts added/updated/retired/revived/unchanged/skipped_by_scope, sector_matched/sector_unmatched, warnings jsonb, error, requested_by).
   Indexes: pg_trgm GIN on canonical_symbol/name/display_name, btree on type, sector, fno, retired.
2. **Importer** (Application port `IInstrumentSource` + parser; Infrastructure HTTP clients on the egress network): streaming CSV parse (validate header names; unknown/missing required column → fail closed with a clear message), scope filter, canonical symbols per ADR 0010, F&O eligibility from active stock futures, sector via NSE list by ISIN (warning only if unavailable), batched upsert (COPY into a temp table + merge) in one transaction so a failed run changes nothing, retire/revive, recorded run. Futures past expiry are retired.
3. **Refresh API (session required, no step-up):** `POST /api/instruments/refresh {force?: bool}` → 202 `{refreshId}` (409 `instruments.refresh_running` if one is running); `GET /api/instruments/refresh/status` → current/last run (progress phase: downloading / parsing / saving / enriching, rows, counts, warnings, error); `GET /api/instruments/refresh/history?take=`. Conditional download via ETag/Last-Modified → status `unchanged` (no work) unless `force`. Daily automatic run: hosted service in `xd-api`, default 08:45 IST, `Instruments:AutoRefresh:Enabled|TimeIst` options, advisory lock so two instances never overlap, skips when unchanged, audited like manual runs.
4. **Search API:** `GET /api/instruments?q=&type=&sector=&fno=&asmGsm=&series=&includeRetired=&sort=&page=&pageSize=` (server-side, pageSize ≤ 100, stable ordering, trigram/ILIKE on symbol, name, display name, ISIN), `GET /api/instruments/facets` (counts by type, sector incl. "Unclassified", F&O, ASM/GSM, series), `GET /api/instruments/{id}`. Read-only, `AsNoTracking`, projected DTOs.
5. **Outbound access:** `images.dhan.co` (exact path `/api-data/api-scrip-master-detailed.csv`) and `nsearchives.nseindia.com` (exact path `/content/indices/ind_niftytotalmarket_list.csv`): GET only, https, no redirects, no cookies/credentials, size caps 100 MB / 5 MB, 120 s / 30 s timeouts, no request logging of URLs beyond host+status, descriptive User-Agent, added to the existing allow-list/guard.
6. **UI (Instruments page, prototype UX, P1-19/20/21 look):** header with icon chip; **refresh panel** (last refreshed time and result, "Refresh list" button + "Force refresh" in a menu, live progress phase and row counts while running, result summary "+12 added · 3 updated · 5 retired · 4,381 total", warnings and errors in plain words, auto-refresh status "Daily at 08:45 IST"); search box with debounce, facet filters (type, sector, F&O, ASM/GSM, series, show retired), results table (paginated or virtualised; columns symbol, name, type chip, series, sector, lot, tick, F&O chip, ASM/GSM chip, retired chip), row detail drawer (all fields, Dhan id, first/last seen); loading/empty ("No master list yet — refresh it to get started")/error states; keyboard accessible; 360 px → 4K; lazy route; bundle ≤ 200 KB gzip initial.

## Acceptance criteria
1. A refresh with the real Dhan file imports ≈ 4,400 kept instruments (counts by type reported), excludes debt/bonds/BSE/options/test symbols, and every kept row has a unique canonical symbol (collisions listed, none silently merged).
2. Re-running with an unchanged file reports `unchanged` and does no work; `force` re-imports and reports 0 added/0 retired when nothing changed; a changed fixture reports the right added/updated/retired/revived counts; a failed or malformed download leaves the previous list intact and records the error; two simultaneous requests → one runs, the other gets 409.
3. F&O eligibility is true exactly for shares with an active stock future (213 underlyings in the real file); sectors are filled by ISIN from the NSE list (≈ 751 matches), others "Unclassified"; a sector-list failure only warns.
4. Search/facets behave per design (partial symbol/name, ISIN, filters combine, pagination, retired hidden by default) and respond < 200 ms on the full list.
5. Manual refresh from the UI works end to end against the running stack (owner-run with the real URLs); the daily service is covered by tests (time maths in IST, switch off, lock, unchanged skip) using a fake clock.
6. No credential or token is ever sent to these hosts; allow-list tests prove only the two paths are reachable; responses/logs contain no secrets (none involved) and no raw file content.
7. Contract regenerated (`scripts/gen-api.sh`); migration added (never editing applied ones); docs updated (06 §2 scope + canonical symbols, 05 §7/§8, 16 `Instruments:*` options, 17 egress hosts); `scripts/check.sh` green; reviewer no blockers/majors; deployed and the master list verified in the browser.

## Test plan
Committed fixture `backend/tests/.../Fixtures/dhan-master-sample.csv` (~80 real rows: shares EQ/BE/SM, ETF, index, futures, test symbol, debt, BSE, option) + `nse-sector-sample.csv`; unit (parser/header validation, scope, canonical symbols, collisions, F&O derivation, sector join, diff counts), integration with a stub HTTP handler (conditional GET/ETag, force, failure, lock, schedule), search/facet queries on real PostgreSQL, access matrix (401), allow-list; frontend (MSW): refresh panel states, search/filter, empty/error, drawer, accessibility basics.

## Effort
5 pts (backend ≈ 3, frontend ≈ 2).

## Done when
- [ ] All acceptance criteria have evidence · [ ] `scripts/check.sh` green; reviewer: no blockers/majors · [ ] Contract regenerated · [ ] Deployed and verified in the browser · [ ] Docs updated (06, 05, 16, 17, ADR 0010)
