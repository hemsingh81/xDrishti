# 0013 — Holdings: first import happens when Portfolio settings are saved; refresh is once a day

- **Status:** Accepted (owner, 2026-10-11) — amends [ADR 0012](0012-holdings-acquisition-dates-and-returns-model.md) decisions 1 and 3
- **Date:** 2026-10-11

## Context
ADR 0012 gave the Portfolio screen an Import dialog and an Auto refresh control with Off / every N minutes / daily. The owner simplified it: the account's
Portfolio settings own everything about getting holdings in; the Portfolio screen only shows them and has **Sync now**.

## Decision
1. **Auto refresh** is *Off* or *Daily at HH:MM IST* (default Off, time 16:00, after market close). Interval mode and `refresh_interval_minutes` are removed.
   Daily keeps ADR 0012's rules: one run per India day after the time, one catch-up if missed, a not-connected account records at most one skipped run per day.
2. **First import is part of saving the account's Portfolio settings.** The settings carry a *first import date* (default 1 Jan 2025). When the account is connected and has no
   holdings in the system, *Save* stores the settings and **imports every holding Dhan lists in the same request, without any per-holding list**: each lot is dated with the
   effective first import date (request, else stored, else 2025-01-01) and flagged "default date". Per-holding dates are edited later on the Portfolio screen (lot dates);
   `firstImport.rows` stays an optional API detail whose dates override the default for their key. A save that changes nothing on an account that has holdings answers `skipped`
   without calling Dhan. The answer always carries `import` (imported / skipped / deferred / failed). When the account is not connected (or the role is off), the date is stored,
   the answer is `deferred`, and the **first sync** (manual or daily) performs the import the same way.
3. **Afterwards the refresh handles everything**: new holding → lot dated today (IST); quantity increase → lot dated today; decrease/missing → FIFO close (ADR 0012 rules unchanged).
   Saving the settings again never imports again (the account already has holdings); changing the first import date afterwards has no effect on existing holdings.
4. **Portfolio screen** has no Import dialog and no Auto refresh control. **Sync now** runs the same sync as the daily refresh, for that day (new holdings dated today); it is a manual
   refresh, not limited to once a day. The standalone `POST /api/accounts/{id}/holdings/import` endpoint is removed; the import is internal to saving the settings and to the first sync.

## Consequences
- One place (account → Portfolio) decides how holdings come in; the Portfolio screen is read-only plus Sync now.
- An account that never gets connected before saving simply imports on its first sync.
- The daily refresh never performs the first import (account without holdings) within 10 minutes of the account's last change (`UpdatedAt`): per-holding dates chosen in the same save are not overwritten by an import the minute after. Nothing is recorded while waiting; the next tick after that imports. A pre-run refusal of an automatic run (not connected, no bucket) is recorded as one skipped run per India day. Changes of the first import date are audited (old -> new).
