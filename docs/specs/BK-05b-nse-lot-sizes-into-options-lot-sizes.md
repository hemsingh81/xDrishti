# BK-05b — NSE lot sizes into options.lot_sizes

- **Status:** Built on `feature/bk-05b` 2026-11-11 (approved by the owner 2026-11-11 in chat: "Yes, write + build"); full gate and reviewer pending
- **Phase / roadmap row:** BK follow-up — BK-05b (closes [BK-05](BK-05-broker-flag-and-fo-baskets.md) AC 7 and 8 as a dated table; REVIEW-2026-11-10 question 5 answered yes; ADR 0256 "Owner decision 2026-11-11")
- **Design refs:** ADR 0175 (P14-01 `options.lot_sizes`) · ADR 0256 (list baskets, NSE F&O files OFF) · ADR 0254 (fetch guard, owner acts) · docs/design/06 §2.3
- **Needs (VERIFIED in code at a793b745 unless marked):**
  - `options.lot_sizes(id, underlying_id fk market.instruments, effective_from, lot_size > 0, source varchar(20), source_url, confirmed, recorded_at)`, CHECK `source IN ('circular','secondary','scrip_master')`, CHECK `NOT confirmed OR (source='circular' AND source_url IS NOT NULL)`; runtime role has **insert only** (migration `AddOptionContracts`).
  - `LotSizeEntry` constants `circular`, `secondary`, `scrip_master`; `LotSizeLookup.InForce`: latest `effective_from` on or before the date, then confirmed first, then the highest id.
  - `OptionContractStore` writes a `scrip_master` row only when the observed lot differs from the newest `scrip_master` row (pattern to follow).
  - `LotSizeParser.Parse(text, fetchDate)` → `LotRow(Underlying, Symbol, LotSize, ExpiryLabel)` (nearest expiry column on or after the fetch date); `ListFileProcessor.LotsAsync` builds share rows, writes `lot_size_mismatch:<symbol>:master=..:nse=..`, `fo_universe_diff` and a `lots_summary` warning into the fetch record and **stores no lot**.
  - The F&O files are behind `Baskets:System:Lists:FetchEnabled` (false) and the BK-01 fetch guard.
- **Size:** 1.5 agent-days (migration for the source value 0.2, writer and its idempotency 0.6, index rows mapping 0.3, tests 0.4)
- **Safety:** yes (a table that P14 option sizing reads). NSE rows are **unconfirmed**, so on the same `effective_from` a confirmed `circular` row still wins (`LotSizeLookup` unchanged); the master lot is never overwritten; no order; the fetch stays OFF.

## Goal
When the owner turns the NSE lot file on, each fetch leaves a dated, insert-only trail of NSE's lot sizes in `options.lot_sizes` with where it came from, so a past date's lot can be read and the master can be cross-checked over time.

## Scope
**In:** a new `source` value `nse_mktlots`; writing NSE lots for every **share** row matched to the master and for **index** rows matched to an index instrument; provenance (`source_url` = the file URL, the fetch id in the fetch record); idempotency; the existing mismatch and universe warnings unchanged; tests; ADR 0256 note.
**Out:** `market.fo_lot_sizes` (never built); overwriting or updating any row; changing the master's lot; changing `LotSizeLookup` precedence; marking NSE rows confirmed; reading the later expiry columns as future-dated lots (see Open questions); turning the fetch on.

## Acceptance criteria
1. **Source value.** Given migration `AllowNseLotSource` (with `Down`), then the CHECK accepts `nse_mktlots`; the confirmed CHECK is unchanged, so a `nse_mktlots` row is always `confirmed = false`; `LotSizeEntry.NseMktLots = "nse_mktlots"`. `Down` refuses (fails) while any `nse_mktlots` row exists, never deletes rows.
2. **Rows written.** Given a fetched `fo_mktlots.csv` on 2026-11-12 IST with a share row `RELIANCE` lot 500 (nearest column `NOV-26`) and RELIANCE matched in the master, then one row `(underlying_id = RELIANCE, effective_from = 2026-11-12, lot_size 500, source 'nse_mktlots', source_url = the file URL, confirmed false)` is inserted in the same transaction as the fetch record; the fetch record's summary counts `lotsWritten`.
3. **Insert only, change only.** Given the next day's file with the same lot, then no row is written for that underlying (the newest `nse_mktlots` row already says 500); given a new lot 475, then a new row dated that fetch day; no `UPDATE` or `DELETE` statement on `options.lot_sizes` exists in the code (architecture test) and the runtime role still cannot run one (integration test).
4. **Same bytes.** Given the same file twice (same sha256), then the second fetch is `unchanged` and writes no lot row (BK-05 AC 5 path).
5. **Index rows.** Given an index row (`NIFTY`, `BANKNIFTY`), then it is written only if the symbol maps to exactly one index instrument by the master's canonical symbol (mapping table in code: `NIFTY`→`NSE:NIFTY`, others UNVERIFIED and checked against the stored master in the test fixture); an unmapped index row is counted `indexUnmatched` and written nowhere.
6. **Master untouched.** Given master lot 250 and NSE 275, then the NSE row 275 is written, the `lot_size_mismatch` warning is still written as today, and `market.instruments`'s lot field is unchanged (test reads it back).
7. **Precedence unchanged.** Given on one `effective_from` a confirmed `circular` row 65 and a `nse_mktlots` row 75, then `LotSizeLookup.InForce` returns 65 (confirmed); given only a `scrip_master` 75 and a later-recorded `nse_mktlots` 65 on the same date, the higher id wins as today, and the result's `Source` names which (existing behaviour, test added).
8. **Rejected file writes nothing.** Given a rejected file (bad header, bad lot, out-of-bounds underlying count, HTML body), then no lot row is written and the previous rows stand.
9. **Unmatched shares.** Given a share symbol not in the master, then no row; it is counted in the fetch record as today.
10. **OFF by default.** Given `FetchEnabled` false, then no request and no lot row (counting handler 0); nothing else in the app writes `nse_mktlots` rows.
11. **Failure atomic.** Given an exception after some lot rows are inserted and before commit, then no lot row and no fetch record survive (one transaction), and the source shows `failed`.

## Contract
- **Data:** migration `AllowNseLotSource` (drop/re-add `ck_lot_sizes_source` with the fourth value). No new table, no grant change.
- **Application:** `ListFileProcessor.LotsAsync` returns the lot rows to store in its plan (it already plans without writing); a `IOptionLotWriter.InsertChangedAsync(rows, today, sourceUrl)` port in Infrastructure (beside `OptionContractStore`, same "only when changed from the newest row of this source" SQL); the applier calls it in the fetch transaction.
- **API / UI:** none. (`GET /api/instruments/{id}` lot history stays out.)
- **Settings:** none new.

## Non-functional
About 200 share rows plus a few indices per day: one set-based insert, under 100 ms. Log `basket.list` unchanged plus `lotsWritten`; no symbols in logs.

## Test plan
- **Unit:** plan contents from the parser rows (share, index mapped, index unmatched).
- **Integration (Testcontainers):** AC 1 to 4, 6 to 11 on two consecutive fetch days; role cannot update/delete; migration up/down.
- **Architecture:** no update/delete on `options.lot_sizes`.

## Open questions
1. NSE's file also lists later expiry months (a lot change announced for DEC-26 appears early). Record them as future-dated `nse_mktlots` rows, or only the nearest column (as now)? Recommended: nearest only in this slice (a future-dated row could be read as in force if the announcement is withdrawn).

## UNVERIFIED
- Index symbols in `fo_mktlots.csv` beyond `NIFTY`/`BANKNIFTY` and their master canonical symbols.
- Whether any P14 reader filters `source` values explicitly (a filter would ignore the new rows; a grep at build time settles it).

## Build note 2026-11-11 (feature/bk-05b, ADR 0256 build note; no new ADR)
- **UNVERIFIED resolved by reading the code.** (a) No P14 reader filters `source`: `OptionReference.LotSizesAsync` selects every row of an underlying, `LotSizeLookup` orders them; the new rows are read like the others (grep of `src`). (b) Index tickers: `NIFTY`, `BANKNIFTY`, `FINNIFTY`, `MIDCPNIFTY` map to `NSE:{TICKER}` (the names `OptionUnderlyings.DefaultIndices`, `ExpiryRules` and the live index set already use); `NIFTYNXT50` has no canonical symbol anywhere in the code, so it is mapped to nothing and counted `indexUnmatched` (never guessed). Whether `fo_mktlots.csv` lists further indices stays UNVERIFIED (a ticker outside the map is treated as a skipped non-share row, as before).
- **Open question 1:** nearest expiry column only, as recommended.
- **Where:** `INseLotApplier` (Application port) and `NseLotApplier` (Infrastructure, beside `OptionContractStore`) run the insert and the fetch row in one EF transaction; `ListFileProcessor` plans the lots (shares by master id, indices through `IListMasterReader.ReadIndexIdsAsync`, exactly one live index per canonical symbol); `RunBasketFetchHandler` calls the applier. The lot SQL lives in a new file, so the architecture test's list of option-table SQL files gains `NseLotApplier.cs` (a deliberate addition, as in P14-04 and P14-08), and a new architecture test forbids UPDATE/DELETE/TRUNCATE of `options.lot_sizes` in any source file.
- **Deviation from the contract text:** the port is `INseLotApplier.ApplyAsync(NseLotApplication)` (lots, India date, file URL, source state, a function from the written count to the fetch record), not `IOptionLotWriter.InsertChangedAsync`, because the fetch record's `lotsWritten` and its outcome depend on the count and must commit with the rows. AC 3 uses "newest `nse_mktlots` row of the underlying by (effective_from, id)".
- **Evidence.** AC1 `AllowNseLotSource` + `OptionMigrationTests` (up, check, Down refuses and deletes nothing, Down clean, up again) and `OptionGrantsTests` (confirmed true refused 23514). AC2/3 `ListBasketsApiTests` (rows with date, URL, never confirmed; same lot no row; 475 a new row; `lotsWritten`) and `ListFileFetchTests`. AC3 no update path: `OptionArchiveArchitectureTests` + `OptionGrantsTests` (UPDATE/DELETE 42501). AC4 same bytes: unit and integration. AC5 `NseIndexSymbolsTests`, `ListFileFetchTests`, integration with a `NSE:NIFTY` index. AC6 integration (instrument lot 250 stays, mismatch warning stays). AC7 `LotSizeLookupTests`. AC8 rejected file unit and integration. AC9 unit (unmatched share has no row). AC10 unit and integration (setting off: 409, zero requests, no row). AC11 `FakeLotApplier` failure gives `failed`/`lot_write_failed` (unit) and a duplicate fetch id after the insert rolls both back (integration).
- `scripts/check.sh` was not run for this slice (the builder ran the targeted suites only); the reviewer and the full gate stay with the caller.

## Done when
- [ ] Every criterion has evidence; `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any update path and any precedence change)
- [x] Migration with `Down`; ADR 0256 build note; BK-05 build note updated; roadmap row updated
