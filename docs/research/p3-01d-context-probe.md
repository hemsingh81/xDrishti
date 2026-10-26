# P3-01d probe: what the allowed chart endpoint gives for NIFTY 50 and INDIA VIX

- **Status:** pending. The probe needs the owner's connected Dhan account (a live token), so it is an owner-environment step; the build agent cannot run it.
- **Outcome:** O1 to O5 of the spec (D8) are **not yet decided**. Until the owner has run the steps below, regime features read `unavailable`, never a default.
- **Why a probe at all:** the system derives the daily context from 1-minute bars fetched through the one allow-listed chart endpoint (`POST /v2/charts/intraday`, ADR 0033, ADR 0133). Whether Dhan serves 1-minute history of `IDX_I` instruments, how far back, and how full the days are, is only knowable on the owner's account.

## Steps (owner)
1. Connect the Dhan Data account and **track NIFTY 50 and INDIA VIX** on the Instruments screen (the data job reports `index_not_tracked` / `vix_not_tracked` until then; `scripts/context.sh coverage` prints the fix).
2. Set `Data:Dhan:InitialDaysIndex` to 1800 for the first full fetch (compose variable `XD_DATA_DHAN_INITIAL_DAYS_INDEX`) (the default is 1825, the maximum; 1800 keeps a week of margin inside Dhan's documented "last 5 years"). The setting applies only to instruments of type `index` that have no bars yet.
3. Run `Update data` (steps dhan and aggregate). The end-of-day job does the same every afternoon.
4. Run `scripts/context.sh coverage` (add `scripts/context.sh refresh` to write any day that became final; it is idempotent). It prints the resolved instruments, the first and last day, the days stored, the first readable date of `index_trend` / `regime` / `vix_pct`, the VIX days and the span of their window, the first date on which the regime has both parts, and the skipped days with reasons and bars.
5. From the same data, record by hand: Dhan `securityId` of each index (the spec's 13 and 21 are UNVERIFIED), bars per day (min / median / max) and days under 360 bars, the largest gap in sessions, price sanity (NIFTY above 5,000; VIX between 5 and 100), and the last 1-minute close against a published daily close for ten dates.
6. Write the numbers below with the date and state the outcome O1 to O5. A failure is quoted by its Dhan code only (`DH-9xx`), never the answer text.

## Results
| Item | NIFTY 50 | INDIA VIX |
|---|---|---|
| Resolved instrument and `securityId` | pending | pending |
| First bar / last bar | pending | pending |
| Days with bars | pending | pending |
| Bars per day (min / median / max) | pending | pending |
| Days under 360 bars | pending | pending |
| Largest gap (sessions) | pending | pending |
| Price sanity | pending | pending |
| Last 1-minute close vs published daily close (10 dates) | pending | pending |

**Outcome:** pending. Decided fallbacks, from the spec: O2 (VIX missing) gets a null VIX and `regime_vol` from the index alone; O3 (neither served) needs the owner's decision on the daily-history endpoint (a security act, separate slice) or a CSV loader; O4 (shallow) builds and the coverage prints when each feature becomes readable; O5 (many thin days) revisits `Data:Context:MinDayFraction` with the numbers.
