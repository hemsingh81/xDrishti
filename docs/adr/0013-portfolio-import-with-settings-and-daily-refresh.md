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
   holdings in the system, the settings screen lists the holdings found at Dhan with a date per holding (pre-filled with the first import date, one bulk date control);
   *Save* stores the settings and imports with those dates in the same request. When the account is not connected (or the owner skips the list), the first import date is stored
   and the **first sync** (manual or daily) performs the import, dating every holding with it (flagged "default date", editable per lot).
3. **Afterwards the refresh handles everything**: new holding → lot dated today (IST); quantity increase → lot dated today; decrease/missing → FIFO close (ADR 0012 rules unchanged).
   Saving the settings again never imports again (the account already has holdings); changing the first import date afterwards has no effect on existing holdings.
4. **Portfolio screen** has no Import dialog and no Auto refresh control. **Sync now** runs the same sync as the daily refresh, for that day (new holdings dated today); it is a manual
   refresh, not limited to once a day. The standalone `POST /api/accounts/{id}/holdings/import` endpoint is removed; the import is internal to saving the settings and to the first sync.

## Consequences
- One place (account → Portfolio) decides how holdings come in; the Portfolio screen is read-only plus Sync now.
- An account that never gets connected before saving simply imports on its first sync.
- The daily refresh never performs the first import (account without holdings) within 10 minutes of the account's last change (`UpdatedAt`): per-holding dates chosen in the same save are not overwritten by an import the minute after. Nothing is recorded while waiting; the next tick after that imports. A pre-run refusal of an automatic run (not connected, no bucket) is recorded as one skipped run per India day. Changes of the first import date are audited (old -> new).
