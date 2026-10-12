# P2-01b — Editable first import date (re-dates the first import); remove the bucket feature from Accounts

- **Status:** Approved (owner, 2026-10-12: "Remove Bucket feature from account section" and "'First import date: 2025-01-01' this should be editable as calendar") · decision [ADR 0015](../adr/0015-first-import-date-editable-and-no-bucket-ui-in-accounts.md)
- **Amends:** [P2-01a](P2-01a-portfolio-import-on-save-and-daily-refresh.md) (date read-only once holdings exist; bucket select) · **Default I chose (object to any):** editing the date re-dates the lots still on the default date.

## Backend
1. `PUT /api/accounts/{id}/portfolio`: when `firstImportDate` is present and differs from the stored one and the account has holdings → in the same unit of work re-date every lot with `source = import` and `uses_default_date = true` to the new date, skipping closed lots whose `closed_on` is before the new date; store the date; audit `FirstImportDateChanged` ("First import date changed from A to B; N lots re-dated, M skipped"). Without holdings only the date is stored (as now). Validation as now (not future, ≥ 1990-01-01). Idempotent: the same date again changes nothing. `FirstImportView` gets `redated` and `redateSkipped` (integers, 0 when nothing happened); status stays `skipped` (no Dhan call) with those counts.
2. Lots changed by the owner (`uses_default_date = false`), refresh-found lots and manual lots are never touched.
3. Buckets: the API stays as it is (omitted `defaultBucket` keeps the account's own bucket). No removal in this change.
4. Tests: domain (lot re-date rules incl. closed lots, flag kept), application (changed/unchanged/no holdings/skipped counts/audit), integration (PUT re-dates only default-dated import lots, returns counts, audit, XIRR input dates move, concurrency), contract regenerated.

## Frontend
1. Accounts → Portfolio tab and wizard: remove the default-bucket select, the Buckets list and the Add bucket form (and their queries/tests). The row holds **First import date** (a calendar date field, editable at all times, max today, min 1990-01-01) and "Include in portfolio totals"; helper line says what saving does ("Changing it re-dates the holdings still on the default date.").
2. Save shows "Re-dated N holdings to <date>." (and a note when some were skipped); other statuses as before.
3. Tests updated; `scripts/check.sh` green.

## Done when
- [x] Backend criteria evidence (1, 2, 3, 4: domain, application and integration tests; contract regenerated) · [x] Frontend criteria evidence (1, 2, 3: `AccountSettings.test.tsx`, `FirstImport.test.tsx`, `AddAccountWizard.test.tsx`, `portfolioForm.test.ts`; typecheck, ESLint, Prettier, Vitest, build green) · [ ] `scripts/check.sh` green; reviewer · [ ] Deployed and verified
