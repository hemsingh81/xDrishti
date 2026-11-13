# 0290 — The context inbox: NIFTY 50 and INDIA VIX daily history from owner-dropped NSE CSVs (P3-01d fallback D)

- **Status:** Accepted (owner, 2026-11-14: build it now as the fallback; the Dhan route stays first; no allow-list change).
- **Date:** 2026-11-14.
- **Relation to other ADRs:** Builds on [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) (the context table), 0179 (the filings folder-drop, whose "local files only, no network" shape this copies) and 0282 (default ON where no evidence is lost and no risk is widened). Spec: P3-01d, fallback D (D8 outcome O2, D12).

## Context
If Dhan serves no or too little history for NIFTY 50 or INDIA VIX (outcomes O2/O3 of the probe), the regime and `vix_pct` features stay unavailable. Fallback C (a Dhan daily-chart endpoint) needs an allow-list change, which the owner has not made. The owner can download both histories from NSE by hand.

## Decision
1. **Folder.** `Data:Context:InboxPath` (default `data/inbox/context`, `/data/inbox/context` in the container). Top-level regular `*.csv` files only; no symlink, no sub-folder, at most 8 MiB, strict UTF-8 (BOM allowed). A handled file is moved, with a UTC time prefix and never overwriting, to `done/` (read, at least one usable row) or `failed/` (unreadable, no header, empty, or no usable row). Nothing is deleted.
2. **Reader.** Pure `ContextCsv.Parse`: NIFTY 50 (niftyindices.com: `Index Name?, Date, Open, High, Low, Close`) and INDIA VIX (nseindia.com: `Date, Open, High, Low, Close, Prev. Close, Change, % Change`; also any file named `*vix*`). Tolerant of BOM, header case and spacing, quoted numbers with thousands commas, trailing spaces, dates `dd-MMM-yyyy`, `dd MMM yyyy`, `dd-MM-yyyy`, `dd/MM/yyyy`, `yyyy-MM-dd`. A row that fails is refused with a closed code (`bad_date`, `bad_number`, `bad_range`, `short_row`, `duplicate_date`, `wrong_index`), counted and logged by code, never guessed or repaired. Index rows need low > 0, low <= close <= high; a VIX close is 0 to 200.
3. **Final days only.** A row is used only for a trading-calendar session at or before the last closed session (`not_a_session`, `not_final` otherwise), as the derivation does.
4. **Precedence (the rule).** The table is insert-only and a stored day is never touched, so the first writer wins. In the upkeep the bars derivation runs first, the CSV load after it: a CSV day fills only a day that is missing. An index day is stored in `market.context_daily` with `source = 'nse_csv'`, `index_bars = 1` (a daily row, not minutes), `vix_bars` null, `index_instrument_id` the empty id (no instrument is needed). A Dhan-derived day is never overwritten by a CSV. Because `context_daily` cannot be updated (only the breadth columns can), a VIX close from a CSV goes to the new insert-only table `market.context_vix_csv` (date, close, source, written_at) and `ContextDayStore.ListAsync` joins it only where the day has no VIX of its own: a bars-derived or earlier VIX always wins, and the order in which the two files arrive does not matter. This also fills days that were stored without a VIX after the grace (the D10 limit).
5. **Idempotent.** The same file again writes nothing (`ON CONFLICT DO NOTHING`); the answer counts rows as already stored.
6. **Schedule and switch.** It runs inside `ContextUpkeep`, after every data job that aggregated (and so on the daily end-of-day run), and by hand with `scripts/context.sh import`. `Data:Context:InboxEnabled` defaults to true (ADR 0282: it reads local files only, loses no evidence, widens no risk). A failure is a warning of the job, never an error.
7. **Coverage.** `GET /api/data/context` carries `csv` (index days and VIX closes from CSV, first and last day); the CLI prints it.
8. **Provenance and terms.** The `source` label shows where a day came from. Whether the owner may use the NSE downloads this way is the owner's reading of the NSE terms of use (doc 19 section 5); nothing here fetches them.

## Consequences
- The regime and `vix_pct` become readable from CSV history without any allow-list change.
- A CSV day is the official daily figure, the Dhan-derived day is built from minutes; the two can differ slightly. Because the first writer wins, a CSV day loaded before the bars catch up stays a CSV day.
- One new table and one migration (`AddContextVixCsv`), insert-only for `xd_app`.
- The container needs the folder mounted read-write (`deploy/compose.yaml`); `deploy/xd-up.sh` creates it 0700.
