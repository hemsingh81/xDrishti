# 0106 — The report engine gains excursions, entry hour and sector additively, and MAE/MFE is served as counts in bins

- **Status:** Proposed (spec [P8-10](../specs/P8-10-catalogue-reports-the-stored-data-can-serve.md) part a, built 2026-10-25). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-25.
- **Relation to other ADRs:** [0100](0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md) (endpoints), [0104](0104-the-reports-screen-is-a-lazy-read-only-route-whose-filters-live-in-the-address-and-whose-figures-all-pass-through-the-kit.md) (screen).

## Decision
1. **`ReportTrade` gets optional trailing fields** `MaeR`, `MfeR`, `EntryMinuteIst`, `ExitReason`, `Sector` (null defaults, so P8-05 tests compile and pass unchanged). `ReportDimension` gets `Sector` and `Hour`, appended. The wire whitelist grows with the enum.
2. **Hour** is the India-time hour of the entry fill, two digits, for runs labelled `intraday` only; any other run, or an unrecorded minute, is "not recorded" (never hour 0). The reader projects `entry_epoch_seconds`; the engine applies the style rule.
3. **Sector** is the instrument master's current sector (`IHoldingClassifications.SectorsAsync`, one bulk read), attached by the loader only when the lens names `sector`. It is "as of today", not at the time of the trade; an instrument without one is "not recorded" (shown Unclassified). Nothing is cached across requests (no cache exists in the loader); the read is one query per request.
4. **MAE/MFE is `GET /api/reports/mae-mfe`**: per group a grid of MAE (0.25 R bins to 2 R, then "2 R or more") by result R (0.5 R bins, -5 to +5 R, with a tail column each end), an MFE histogram (0.5 R bins to 5 R, then "5 R or more"), trades that reached +1, +2, +3 R, and the nearest-rank 90th percentile of the MAE of winners (insufficient below the minimum number of winners). Counts only: no scatter, nothing sampled. Trades without a recorded excursion are left out and `excursionN` says how many are in. A group below the minimum has only the reason.
5. **Screens** (lazy `reports` chunk): strategies, side, calendar, MAE/MFE, R distribution, instrument scorecard, symbol and sector. Exit comparison, Monte Carlo and the pointer entries are the second part of the slice.

## Consequences
- Result tail columns are inside `Counts`; `BelowCount` and `AboveCount` repeat their totals.
- The reader now selects four more columns per trade; the cost is the same single pass.
