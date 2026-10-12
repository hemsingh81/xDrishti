# P1-05 — Tracked instruments: choose what the system works on

- **Status:** Approved (owner, 2026-10-11: "allow me to select what instrument I want to configure in our system")
- **Phase / roadmap row:** P1 — Data platform & instruments · story **P1-05** · depends on [P1-03](P1-03-instrument-master-import-search-and-refresh.md)
- **Design refs:** docs/design/06 §2

## Goal
From the master list the owner picks the instruments to work with (**track** them), singly or in bulk, sees which are tracked, and can untrack them. Only tracked instruments will later get history, scans and analytics.

## Design (defaults — object to any)
1. **Table `market.tracked_instruments`** (the module schema is `market`, not `catalog`) (instrument_id PK/FK, active, active_since, deactivated_at, data_from, data_to, last_update, backfill_requested_at, note, tracked_by); tracking a retired instrument is refused; an instrument that **retires after being tracked** stays tracked and shows a warning ("no longer in Dhan's list").
2. **API (session, no step-up):** `POST /api/instruments/tracked {instrumentIds[]}` (idempotent bulk track, max 500 per call), `DELETE /api/instruments/tracked {instrumentIds[]}` (untrack = `active=false`, history kept, re-tracking reactivates and keeps `data_from`), `POST /api/instruments/tracked/resolve {symbols[]}` → matched ids + unmatched list (paste / CSV list: tickers, canonical symbols or ISINs; never guessed), `GET /api/instruments?tracked=true|false` and a `tracked` block (active, since, dataFrom/To, lastUpdate) on every instrument; audit events (tracked/untracked with counts). Tracking sets `backfill_requested_at` — **the backfill job itself belongs to P1-12 / P1-06**; until bars exist coverage shows "—" and the status "history not collected yet".
3. **UI (Instruments page):** tabs **Master list · Tracked**; row checkboxes with select-all-on-page and "select all N results"; bulk bar (Track · Untrack · Add to basket — enabled with P1-15); single-row Track toggle; **Paste symbols** dialog (textarea or CSV file → resolve → preview matched/unmatched → confirm); Tracked tab shows coverage columns (from/to, last update, quality "—" until data), baskets chips (after P1-15), warning chip for retired-but-tracked; counts in the page header ("312 tracked"). Honest states: "History collection starts with the EOD service (P1-12)".

## Acceptance criteria
1. Bulk track/untrack is idempotent and atomic per call, audited, limited to 500 ids; untracked history fields survive re-tracking; retired instruments cannot be newly tracked (400 with reason) but tracked ones survive retirement with a warning flag.
2. Resolve returns matched/unmatched for tickers, canonical symbols and ISINs (case-insensitive, trimmed, duplicates collapsed); unmatched are listed, never guessed; CSV upload parsed client-side (≤ 5,000 symbols).
3. Instruments list/search supports `tracked` filter and shows tracked status; Tracked tab lists only active tracked instruments with the extra columns; counts correct.
4. UI: selection model works with keyboard and across pages as specified, bulk bar states, paste dialog preview, loading/empty/error, a11y, responsive; bundle ≤ 200 KB gzip.
5. Tests: unit/integration/frontend per above; `scripts/gen-api.sh`, `scripts/check.sh` green; reviewer no blockers/majors; deployed and verified in the browser; docs (06 §2) updated.

## Effort
3 pts. **Done when:** [ ] criteria evidenced (backend 1-3 and the audit/limits evidenced by integration tests, 2026-10-12; UI criterion 4 evidenced by the frontend tests, 2026-10-12, browser check open) · [ ] check.sh green + reviewer · [x] contract regenerated, migration (`AddTrackedInstruments`, grants in the migration) · [ ] deployed and verified

**Status 2026-10-12 — backend and frontend done; check.sh, reviewer and the browser check are open.** Shipped: `market.tracked_instruments`; `POST`/`DELETE /api/instruments/tracked` (max 500, idempotent, atomic, audited with counts), `POST /api/instruments/tracked/resolve` (max 5,000), `tracked=true|false` filter, a `tracked` block on every instrument, `tracked`/`trackedRetired` in the facets; the reusable `InstrumentTracking` service for baskets (P1-15). Shapes: docs/design/06 §2.3.

**Frontend (2026-10-12):** `frontend/src/features/instruments` — tabs Master list · Tracked (`?tab=tracked`), "N tracked" in the header and on the tab, row checkboxes (page, shift-range, "Select all N results" read in pages of 100, selection kept across pages and reset when tab/filters change), bulk bar (Track · Untrack with confirmation · Add to basket disabled "Available with Baskets" until P1-15 passes `onAddToBasket`), row and drawer Track toggle, Paste symbols dialog (text or CSV read in the browser, ≤ 5,000, look up → preview → confirm), Tracked-tab coverage columns with the retired warning chip; calls of more than 500 ids go out in chunks of 500. The baskets chips column plugs into `trackedExtraColumns` in `components/instrumentColumns.tsx`.
