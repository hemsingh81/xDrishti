# 0015 — The first import date stays editable (re-dates the first import); no bucket choices in Accounts

- **Status:** Accepted (owner, 2026-10-12) — amends [ADR 0013](0013-portfolio-import-with-settings-and-daily-refresh.md) (date read-only after the first import) and [ADR 0011](0011-default-portfolio-bucket-per-account.md) (bucket chosen and added in Accounts)
- **Date:** 2026-10-12

## Context
After the first import the Accounts → Portfolio tab showed the first import date as plain text ("First import date: 2025-01-01"). The owner wants to change it with a calendar,
and wants the bucket feature (default-bucket choice, extra buckets) removed from the account section: the portfolio buckets are now shown on the Baskets page
([ADR 0014](0014-portfolio-buckets-appear-as-system-baskets.md)) and every account has its own automatic bucket.

## Decision
1. **The first import date is always editable** (calendar: not in the future, not before 1990-01-01). Saving a **changed** date on an account that already has holdings
   **re-dates the lots that still carry the first-import date**: lots with `source = import` and `uses_default_date = true` (not closed before the new date). Lots
   the owner dated by hand, lots found by later refreshes and manual lots are untouched. The lots stay flagged as default-dated. Returns and XIRR are computed on read, so
   they follow. The change is audited (old → new, number of lots re-dated, number skipped because the new date is after their close day). With no holdings the date is only stored.
2. The save response's `import` block gets `redated` (number of lots re-dated; 0 otherwise) and `redateSkipped`; the UI says "Re-dated N holdings to <date>."
3. **No bucket choices in Accounts:** the default-bucket select and the Buckets list / "Add bucket" are removed from the Portfolio tab and the wizard. Imports always go to the
   account's own default bucket (`defaultBucket` omitted/null). The buckets API (`GET/POST /api/portfolio-buckets`, `defaultBucket` on the settings) stays in the contract for now
   (not removed in this change); nothing in the UI creates custom buckets any more, so the Baskets page shows one portfolio basket per account.

## Consequences
- A new date later than the day a refresh found a newer holding puts the first-import lots after it in the oldest-first order (ADR 0012); lots already closed are not re-ordered. Pick a date before your later purchases.
- The count in the toast is lots, which equals holdings unless a holding was partly sold.
- Changing the date is a bulk edit of acquisition dates, so XIRR/returns change; it only touches lots still on the default date, and every change is in the audit log.
- Existing custom buckets (none were created by the owner) keep working.
