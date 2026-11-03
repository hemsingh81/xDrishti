# P14-14 — Basis data and carry model: end-of-day futures from F&O bhavcopy files the owner drops into a local folder, the observed expiry dates as a by-product, and a pure carry model that names what it does not know

- **Status:** Draft (written 2026-11-03; the owner's Approved is needed before an agent builds it)
- **Phase / roadmap row:** P14 — slice P14-14 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Basis data and carry model (EOD futures source decided and its terms read first)"); on the cut line (`BASIS` is cut first).
- **Design refs:** docs/design/05 §7, 20 §C (`BASIS`) · P14-00 D12 (basis from end-of-day files), protocol S item 5 · ADR 0179 (a local inbox folder and a read-only adapter for owner-dropped files; no fetcher), 0133 (an allow-list change is the owner's act in the commit of the slice that uses it — **this slice needs none**), 0184 (untrusted file parsing) · [CF-04](CF-04-history-backfill-to-five-years-daily-bhavcopy-and-corporate-actions.md) Part A (the cash bhavcopy and its terms ADR). New ADR: **next free number at build time** (the source decision and the file formats).
- **Needs:** P1 (built: instruments, calendar, bars). **Owner acts (defaults if missing):** read NSE's terms for the F&O bhavcopy and record them (as CF-04 Part A for the cash file) — until then the folder is not read (`terms_not_recorded`); drop the daily F&O bhavcopy CSV files of the window into `data/inbox/fo-bhavcopy/` — until then every basis is `unavailable/no_futures`. **Needed by:** P14-15; optionally P14-08 (observed expiry dates replace the derived rule where present).
- **Size:** 2.5 agent-days (estimate).
- **Safety:** no — **no network and no allow-list change**: the owner fetches the files himself and the adapter only reads a local folder (ADR 0179 pattern). The files are untrusted input, so the parser is bounded and refuses anything unexpected. No order, no margin, no decision reader.

## Goal
From files the owner drops, the system holds five years of end-of-day settlement prices of stock and index futures with their expiry dates and open interest, and a pure model gives, for any date and contract, the fair futures price from spot, rate, days to expiry and known dividends, the observed excess basis, and the reason when any of those is missing.

## Verified facts (read 2026-11-03)
- **Code:** no futures bar loader exists; the master import keeps only **live** futures (`CatalogBuilder` skips expired `Future` rows), so historical futures prices and expiries are not in the database. `market.corporate_actions` holds `split` and `bonus` only (`CorporateActionKind`): **no dividend ex-dates exist** for any instrument. `market.bars_1m` gives the spot close (about 2.1 years). The allow-list has no daily-history endpoint (`/v2/charts/historical` stays refused, ADR 0177).
- **Files (UNVERIFIED until the owner's first files are read):** NSE publishes a daily F&O bhavcopy; the older format (`fo<DD><MON><YYYY>bhav.csv`) is believed to carry `INSTRUMENT` (`FUTSTK`, `FUTIDX`, `OPTSTK`, `OPTIDX`), `SYMBOL`, `EXPIRY_DT`, `STRIKE_PR`, `OPTION_TYP`, `OPEN`, `HIGH`, `LOW`, `CLOSE`, `SETTLE_PR`, `CONTRACTS`, `VAL_INLAKH`, `OPEN_INT`, `CHG_IN_OI`, `TIMESTAMP`, and the newer common format (from about mid-2024) fields such as `TradDt`, `FinInstrmTp`, `TckrSymb`, `XpryDt`, `StrkPric`, `OptnTp`, `ClsPric`, `SttlmPric`, `OpnIntrst`. The parser is written against the owner's real files, never against this list.
- NSE's terms of use for these files are not recorded anywhere in the repository (CF-04 records the cash file's terms as an owner act).

## Scope
**In:**
- Infrastructure: `FoBhavcopyFolder` (read-only adapter over `data/inbox/fo-bhavcopy/`; `.csv` only, ≤ 50 MB, ≤ 500,000 lines; a file is identified by its SHA-256; nothing is moved or deleted).
- Domain (pure): `FoBhavcopyParser` (two format versions, `fo-1` and `fo-2`; futures rows kept; option rows read only for their expiry dates), `CarryModel` (fair price, excess, annualised excess, with closed reasons), `ExpirySet` (distinct expiry per underlying, flag and date).
- Application: `IngestFoBhavcopyHandler` (verb `xd basis ingest`), `IFoEodStore`.
- Migration `AddFuturesEod`: `market.futures_eod`, `market.fo_files`, `options.expiries_observed`.
- Setting `Basis:Rate` (0.065, labelled assumed, owner-set), `Basis:TermsRecorded` (false; the owner sets it when the terms ADR exists).
- Tests with synthetic files (fake symbols) and, when dropped, the owner's real files; ADR; doc 05 §7, doc 20 §C.

**Out:** any download (the owner's act, possibly CF-04's fetcher later); option prices from the bhavcopy (too many rows; the archive has them); intraday futures; margins; dividend data (none exists; see the carry rule); the verdict (P14-15).

## The rules
- **Ingest.** With `Basis:TermsRecorded` false, the verb refuses (`terms_not_recorded`) and reads nothing. Each new file (by SHA-256) is parsed in one transaction: futures rows go to `market.futures_eod`; distinct option expiries go to `options.expiries_observed` (source `bhavcopy`, the file hash). A symbol that maps to no instrument is counted `unmapped`; a row with an unparsable number or date is counted `invalid` and skipped (never a zero); a file whose header matches neither format is `rejected/unknown_format`; one with the same trade date as a stored file but a different hash is `rejected/conflicting_file` (listed for the owner). A second ingest of the same file writes nothing.
- **Spot.** The underlying's last 1-minute close of the session at or before 15:30 India time; none → `no_spot`. Indices use their index bars when tracked, else `no_spot`.
- **Carry model.** `T` = calendar days from the trade date to the expiry date ÷ 365; `r` = `Basis:Rate` (labelled `rate: assumed` until the owner sets it). Dividends: **unknown** for every instrument (no ex-date data) — the model returns `fair_ex_div = S·e^(rT)` labelled `dividends: not held` and, separately, a dividend-bounded fair `S·(1 − y·T)·e^(rT)` for a yield bound `y` given by the caller (P14-15 uses it only to bound a DROP). Excess `= F_settle − fair`; percent of spot; annualised `excess ÷ S × 365 ÷ days`. `T ≤ 0` → `expired`; settlement null or ≤ 0 → `no_futures`.
- **Expiry dates.** `options.expiries_observed` rows are exchange data (`confirmed = true`, source `bhavcopy`, file hash); P14-08's `ExpiryCalendar` prefers them over its derived rule when present (that slice reads this table only if built after it; otherwise a later one-line change).

## Hand table
| Case | S | F | days | r | Fair (ex-div) | Excess | Excess % | Annualised |
|---|---|---|---|---|---|---|---|---|
| P14-00 S.5 | 1,000 | 1,006 | 30 | 0.07 | 1,005.770007 | 0.229993 | 0.023 % | 0.280 % |
| Dividend ₹5 ex in 10 days (if it were known) | 1,000 | 1,006 | 30 | 0.07 | (1,000 − 4.990420)·e^(0.07·30/365) = 1,000.750792 | 5.249208 | 0.525 % | — |

The second row shows why unknown dividends matter: a ₹5 dividend changes the excess by 5 points, twenty times the first row's excess. Against a round trip of at least about 0.25 % (P14-00 S.5, UNVERIFIED), the first row is no trade.

## Acceptance criteria
1. **No network, no allow-list change.** An architecture test: the basis code references no HTTP client, socket or broker type; `BrokerEndpoints.Allowed` and `PlanPhaseCheck.PinnedAllowList` are unchanged (the existing guard test).
2. **Terms gate.** With `TermsRecorded` false the verb reads no file (a fake folder records zero opens) and prints `terms_not_recorded`.
3. **Parser.** Synthetic files in both formats (fake symbols) give the stated futures rows and expiry sets; a header of neither format, a file over the limits, a non-UTF-8 file, a row with `-` or empty price, a negative settlement, a duplicate row and a conflicting file each give the stated outcome; nothing is stored as zero.
4. **Idempotent ingest.** A second run writes nothing; a new file adds only its rows; `market.fo_files` lists every file with its outcome and counts.
5. **Carry hand table.** Both rows within 1e-6; `expired`, `no_spot`, `no_futures` reached; the label `dividends: not held` on every value.
6. **Expiry set.** A synthetic month with weekly and monthly option expiries gives the stated `options.expiries_observed` rows (`confirmed = true`).
7. **Migration.** `market.futures_eod(trade_date, instrument_id (the underlying), expiry, settle_px int, close_px int null, oi bigint null, contracts bigint null, file_sha char(64), PK (trade_date, instrument_id, expiry))`, `market.fo_files(sha256 PK, trade_date, format, outcome, rows, invalid, unmapped, ingested_at)`, `options.expiries_observed(underlying_id, expiry_flag char(1) null, expiry date, source, file_sha, PK (underlying_id, expiry))`; insert-only for `xd_app`; nothing for `xd_exec` or the read roles (grants tests); `Down` drops them.
8. **Logs.** `basis.ingest.file|done` carry the file hash, outcome and counts only; never a path or a row.

## Contract
- **Verb:** `xd basis ingest` (exit 0 with counts; 1 `terms_not_recorded`; 2 bad line).
- **Data:** `AddFuturesEod` (criterion 7). About 650 futures rows a day, 0.8 M over 5 years (P14-00), under 100 MB.
- **Settings:** `Basis:Rate` 0.065 (0-0.2), `Basis:TermsRecorded` false, `Basis:InboxPath` `data/inbox/fo-bhavcopy`.
- **API, UI:** none.

## Non-functional
One daily file parsed in ≤ 2 s and ≤ 100 MB memory; five years (about 1,250 files) in ≤ 1 hour (UNVERIFIED).

## Test plan
- **Unit:** parser cases, carry hand table, expiry set.
- **Integration (Timescale):** ingest of a synthetic folder, idempotency, grants, the terms gate.
- **Architecture:** criterion 1.

## Decisions (researched)
1. **Owner-dropped files, not a fetcher.** The terms are unread and the plan forbids new endpoints here; the inbox pattern (ADR 0179) costs nothing and keeps the network closed. Rejected: a Dhan daily-history endpoint (an allow-list act, and it serves live contracts only, D12); NSE page scraping (D14).
2. **Futures rows only, option expiries as a by-product.** Option rows are most of each file; their expiry dates are the one thing the archive lacks (P14-08).
3. **Dividends named as unknown, not assumed zero.** The excess before dividends is a different number from the excess; P14-15 can only bound with it.

## Risks and honest limits
- The file formats and terms are UNVERIFIED until the owner's first files; the parser version changes if NSE changed the format inside the window.
- Without dividend ex-dates the stock basis is biased (a dividend inside the window lowers the futures price); index dividend yields are equally unknown.
- Settlement prices are end-of-day; a real cash-and-carry trade fills at intraday prices on both legs.

## Open questions (owner)
1. Read and record NSE's terms for the F&O bhavcopy, and drop the files for the window? **Default: not done; every basis is `unavailable` and P14-15 is NOT_EVALUABLE.**
2. Or cut `BASIS` now (P14-00: "cut first")? **Recommended: cut P14-14 and P14-15 unless the owner wants the observed expiry dates for P14-08.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any network path, any zero for a missing price, any file moved or deleted, any dividend assumed)
- [ ] Migration `AddFuturesEod` with `Down`
- [ ] ADR written; docs 05 §7, 20 §C updated
