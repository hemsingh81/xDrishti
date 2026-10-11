# 0010 — Instrument master: sources, scope and refresh

- **Status:** Accepted (owner, 2026-10-11)
- **Date:** 2026-10-11

## Context
Doc 06 defines master list → tracked instruments → baskets, and doc 05 §7 says the master is refreshed daily from Dhan. The owner supplied the file to use
(`https://images.dhan.co/api-data/api-scrip-master-detailed.csv`) and asked for a manual refresh button and a sector source. Profiling the real file (2026-10-11):
207,560 rows, 35 MB, updated daily ~08:20 IST. NSE "equity" segment = 9,991 rows but only 3,204 are shares (ES) and 405 ETFs — the rest is bonds/debt/T-bills;
NSE index rows 119 (incl. NIFTY, BANKNIFTY, INDIA VIX); NSE stock futures 635 real contracts on 213 underlyings (plus exchange test symbols such as `011NSETEST`);
BSE, MCX, currency, commodity and ~115k option rows are not needed by v1. The file has **no sector**; `ISIN`, `UNDERLYING_SYMBOL` (ticker), `SERIES`, `LOT_SIZE`, `TICK_SIZE`,
`ASM_GSM_FLAG`, `MTF_LEVERAGE`, `SM_FREEZE_QTY` and expiry are present. NSE publishes the Nifty Total Market constituent list
(`https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv`, 49 KB, 755 stocks, 22 industries, ISIN column; 751 join by ISIN).

## Decision
1. **Scope kept in the master:** NSE shares (`INSTRUMENT_TYPE` = ES, any series) and ETFs, NSE indices, NSE stock and index futures (`FUTSTK`, `FUTIDX`); exchange test symbols (`*NSETEST*`) excluded.
   Everything else (BSE, MCX, currency, commodity, options, debt) is skipped on import and can be added with its phase (options: P14). ≈ 4,400 rows.
2. **Canonical symbols:** share/ETF `NSE:{UNDERLYING_SYMBOL}` (e.g. `NSE:RELIANCE`); index `NSE:{SYMBOL_NAME}` upper-case with spaces as `_` (`NSE:NIFTY`, `NSE:NIFTY_MIDCAP_150`); future `NSE:{UNDERLYING}-{YYYY}-{MM}-FUT`.
   A collision is a data-quality issue reported in the refresh result, never silently resolved. Dhan ids are kept in `instrument_broker_ids` (`SECURITY_ID` + exchange segment).
3. **Flags derived, not guessed:** F&O eligible = an active stock future exists for the underlying; ASM/GSM from the file; sector/industry from the NSE list joined **by ISIN** (instruments without a match keep sector empty — shown as "—", editable later).
4. **Refresh:** a background job in `xd-api` (it owns the egress network): manual button (any signed-in session, no step-up — it only reads public files) and an automatic daily run (default 08:45 IST, switchable off in configuration), one run at a time (advisory lock),
   conditional (ETag / Last-Modified: an unchanged file is skipped unless the owner forces it), streamed parse, batched upsert, instruments no longer in the file are **retired** (`retired_at`), never deleted, and revive if they come back; every run is recorded
   (counts added / updated / retired / unchanged, sector matches, warnings, duration, error) and a failed run leaves the previous list untouched. The sector list failing is a warning, not a failure. The daily run moves into the service framework with P1-13.
5. **Network:** `images.dhan.co` and `nsearchives.nseindia.com` join the API's outbound allow-list (GET, https, exact paths, no redirects, size caps 100 MB / 5 MB, timeouts, descriptive User-Agent); both are public files, no credentials sent.

## Consequences
- A small, searchable master (≈ 4.4k rows) instead of 207k; options and other segments are a later, separate decision.
- Sector coverage is limited to the ~750 Nifty Total Market stocks until another file is added; the sector facet shows "Unclassified" for the rest.
- The master depends on two public NSE/Dhan files whose format can change; the importer validates the header and fails closed with a clear message.
- Data stays local; using the files follows the sources' terms for personal use (see doc 19).
