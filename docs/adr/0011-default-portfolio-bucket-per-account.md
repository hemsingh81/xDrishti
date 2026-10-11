# 0011 — One default portfolio bucket per Portfolio account; acquisition dates derived from Dhan

- **Status:** Accepted (owner, 2026-10-11) — amends [ADR 0007](0007-accounts-credential-references-and-step-up.md) (seeded buckets)
- **Date:** 2026-10-11

## Context
P1-16 seeded three portfolio buckets (*Trading*, *MyLongTerm*, *MyChoice*) and made the owner pick one per account, and asked for an "import trade history from"
date. The owner has no use for presets and cannot know that date. XIRR and returns need the date and size of every cash flow, and what Dhan serves is: holdings
(quantity, average cost, **no purchase date**), trades (`/v2/trades/{from}/{to}/{page}`, dated) and the ledger (`/v2/ledger`, dated cash movements).

## Decision
1. **A default bucket per account, no presets.** An account with the Portfolio role has exactly one default bucket named `"{account name} · Portfolio"`, linked by
   `portfolio_buckets.default_for_account_id` (unique, FK to `broker_accounts`, restrict: accounts are archived, never deleted). Created in the same unit of work as the account or the role switch (a
   collision of the unique link means it exists: the loser retries once and finds it); renamed with the account; archived (`archived_at`) with the account and renamed "… (archived)" so its name is free again (holdings and history stay); kept but hidden from
   `GET /api/portfolio-buckets` while the role is off; a name collision gets a numeric suffix. No bucket is seeded any more.
2. **The chosen import bucket is a reference to a custom bucket or nothing.** `portfolio_settings.default_bucket` (a name) becomes `default_bucket_id` (FK to the
   bucket id); `NULL` means the account's own default bucket, so renaming the default bucket never touches a reference. Custom buckets stay (`POST /api/portfolio-buckets`).
3. **No manual history date.** `portfolio_settings.history_from` and the API property `historyFrom` are removed. Acquisition dates are derived by the sync (P2-01) from trades
   and the ledger, stored once found, and holdings Dhan cannot date are flagged *purchase date unknown* — never guessed ([doc 12](../design/12-portfolio-monitoring.md) §3).
4. **Migration `AddDefaultPortfolioBuckets`** creates the default bucket of every active Portfolio account, re-points accounts that used a preset to their own bucket
   (audited with actor `migration` for active accounts that have the Portfolio role), deletes the three presets once unreferenced and drops the old columns.

## Consequences
- The owner never chooses between presets or types a date; every Portfolio account has a bucket from the moment the role is on.
- Bucket names can be longer than the 40 characters of custom names (up to 100) and contain "·"; the character rules apply to custom buckets only.
- P2-01 must implement the date derivation and the unknown-date state; until then the Portfolio tab only explains where dates will come from.
- The migration is not reversible without loss (default buckets are deleted on the way down; the presets are re-created, references to presets are not restored).
