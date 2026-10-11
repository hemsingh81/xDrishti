# P2-01a — Portfolio settings own the first import; refresh once a day; Portfolio screen only has Sync now

- **Status:** Approved (owner, 2026-10-11: four-point change to P2-01) · decision [ADR 0013](../adr/0013-portfolio-import-with-settings-and-daily-refresh.md)
- **Amends:** [P2-01](P2-01-holdings-import-with-dates-and-auto-refresh.md) design items 2 and 5 · **Defaults I chose (object to any):** daily time default 16:00 IST; Sync now is not limited to once a day; per-holding dates only at first import (later edits per lot in the holding drawer); first import date stored when the account is not connected.

## Goal
Account → Portfolio tab (and the wizard's portfolio step) is the one place to set bucket, include-in-totals, auto refresh (Off | Daily at time) and the first-import dates. Saving imports the holdings the first time. The Portfolio screen drops Import holdings and Auto refresh; Sync now stays.

## Backend
1. Migration (new; never edit applied): drop `refresh_interval_minutes`; `refresh_mode` limited to `off|daily` (existing `interval` rows become `off`); add `first_import_date date` (nullable → default 2025-01-01 when read).
2. `PUT /api/accounts/{id}/portfolio` takes `{defaultBucket?, includeInTotals, refreshMode (off|daily), refreshDailyTimeIst, firstImportDate?, firstImport?: {rows:[{key, acquiredOn?}]}}`; `refreshIntervalMinutes` removed. Validation: date not future, not before 1990-01-01.
   If `firstImport` is present, the account is connected, and it has no holdings: after saving the settings, import in the same request (rows missing a date use `firstImportDate`, flagged default-date); result counts are returned (`import: {imported, defaultDated, …}`); import failure → settings are still saved and the problem is returned as a `warning`/error code the UI shows (`portfolio.import_failed`, retry = Save again or Sync now). If an account already has holdings, `firstImport` is ignored (counts say `skipped`).
3. First sync: `SyncHoldings` for an account with no holdings imports all fetched rows dated `first_import_date` (default 2025-01-01, flagged default date), trigger recorded as `import`. Later syncs unchanged.
4. Remove `POST /api/accounts/{id}/holdings/import`, `ImportHoldings` public command surface (reuse internally), interval schedule code, interval tests. `POST /holdings/preview` stays (used by the settings screen), returns `suggestedDate` = first import date.
5. `GET /api/accounts/{id}/holdings/sync-status` and portfolio settings view return `refreshMode`, `refreshDailyTimeIst`, `firstImportDate`, `hasHoldings`; no interval fields.
7. Settling rule: an automatic first import (account without holdings) waits until the account was last changed more than 10 minutes ago (`portfolio.settling`, nothing recorded), so per-holding dates chosen in the save that switched the refresh on are kept. Any other pre-run refusal of an automatic run (not connected, no bucket) is recorded as one skipped run per India day. Changes of the first import date are audited (old -> new).
6. Tests updated/added: daily-only schedule (fake clock), migration mapping, save-with-import (connected, not connected, already has holdings, import failure keeps settings), first sync imports with the stored date, endpoint removal (404), contract regenerated.

## Frontend
1. Account → Portfolio tab and wizard SettingsStep: `AutoRefreshSettings` becomes Off / Daily-at-time; new *First import* block (date input default 1 Jan 2025; when connected and account has no holdings, a list from `holdings/preview` with a date per holding and a bulk date control, reusing the existing import table/bulk-date pieces); Save sends `firstImportDate` + `firstImport.rows`; result toast "Imported N holdings"; helper text explains later changes are automatic. Hidden/read-only note once the account has holdings.
2. Portfolio screen: remove Import holdings button/dialog and the Auto refresh control/dialog; keep Sync now, status line, confirm flow; empty state points to Account → Portfolio settings; delete dead code/tests.
3. Tests updated; `gen-api.sh`; `scripts/check.sh` green.

## Done when
- [ ] Criteria evidence · [ ] `scripts/check.sh` green; reviewer no blockers/majors · [ ] Deployed; owner-run import verified
