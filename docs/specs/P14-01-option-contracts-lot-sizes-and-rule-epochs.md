# P14-01 — Option contracts, lot-size history and rule epochs: the reference data every later option slice joins on

- **Status:** Built 2026-11-01 (ADR 0175). Approved (owner, 2026-11-01: pre-approved, no review wanted). Open questions resolved with the recommended/safer answers: allow-list rows `POST api.dhan.co/v2/optionchain`, `/v2/optionchain/expirylist`, `/v2/charts/rollingoption` accepted (each enters the pin in the commit of the slice that uses it, ADR 0133); underlyings = 4 indices plus the top 30 F&O shares; lot-size and rule-date seeds stay confirmed=false until checked against circulars; stock chains for 30 stocks (not all 213); backfill = indices plus top 20 stocks at ATM±3 only after the probe; the backfill probe runs first; the chain cap 4 GB and backfill cap 3 GB with the shared 20 GB free floor; the VM disk enlargement (to 160 GiB) is the owner's act and every recorder stays off until its gates are turned on, so the build proceeds within the caps; Services-page switches and the Data-quality card are a later frontend slice; quality thresholds and the 6.5 % parity rate as drafted.
- **Phase / roadmap row:** P14 — slice P14-01 of [P14-00](P14-00-options-and-stat-arb-plan.md) (slice table row 1; D13: start right after P7, before P13, in free capacity).
- **Design refs:** docs/design/06 §2.1 (master list; "options come with their phase, P14"), 05 §7, 20 §C · ADR 0010 (master scope), 0002 (configuration in the database), 0045 (insert-only research rows). New ADR: **next free number at build time** (design change of doc 06 §2.1).
- **Needs:** P1-03 (master import, built). **Needed by:** P14-02 (underlying ids, expiry calendar), P14-03 (underlying ids, strike grid), P14-04 (expected expiries, strike step), P14-07 and P14-08 (lot in force on a date, rule epochs).
- **Size:** 2 agent-days (estimate).
- **Safety:** no — no new broker endpoint (the scrip master is the file already fetched from `images.dhan.co`, `MasterDataEndpoints.DhanScripMaster`, not a broker allow-list row), no token, no order; under 20 MB a year. `market.instruments` and `InstrumentScope` are unchanged.

## Goal
From the next master refresh on, the option contracts of a configured list of underlyings are kept (live and expired, never deleted) in their own table, with a dated lot-size history and a table of the dated market rule changes, so later slices can say which contract, which lot and which rules were in force on any date, or say `unavailable`.

## Verified facts (read 2026-11-01)
**Code:** `DhanMasterParser` reads `api-scrip-master-detailed.csv` as a stream and already lists the columns `INSTRUMENT`, `UNDERLYING_SECURITY_ID`, `UNDERLYING_SYMBOL`, `LOT_SIZE`, `SM_EXPIRY_DATE`, `STRIKE_PRICE`, `OPTION_TYPE`, `TICK_SIZE`, `EXPIRY_FLAG`; `InstrumentScope.Classify` keeps shares, ETFs, indices and `FUTSTK`/`FUTIDX` and drops everything else (options included), and `MightBeInScope` skips option rows before they are read. `market.instruments` holds today's `lot_size` only. `TradeSkips` recognises `OPTIDX|OPTSTK` names only to skip them. No `options` schema exists.
**Dhan:** <https://dhanhq.co/docs/v2/annexure/> instrument values `OPTIDX` (index options) and `OPTSTK` (stock options), segment `NSE_FNO` = 2, `IDX_I` = 0, `NSE_EQ` = 1.
**Rules (secondary sources, all UNVERIFIED against the circulars; listed in P14-00 "Rules that shape the design"):** 2024-11-20 one weekly index expiry per exchange and contract value ≥ ₹15 lakh; 2025-02-01 upfront premium; 2025-09-01 Nifty expiry on Tuesday; 2025-12-30 lots Nifty 75→65, Bank Nifty 35→30; 2026-04-01 STT futures 0.05 %, option premium 0.15 %.
**UNVERIFIED until the build opens the file:** the `OPTION_TYPE` values (`CE`/`PE` assumed), the unit of `STRIKE_PRICE` (rupees assumed), the `EXPIRY_FLAG` values (`W`/`M` assumed) and the real row count for the list.

## Scope
**In:** a pure `OptionScope` classifier (beside `InstrumentScope`, which does not change); the master import also writes the option rows of the configured underlyings to `options.contracts` (new rows inserted, `last_seen_on` advanced, never deleted); `options.lot_sizes` (seed rows plus one `scrip_master` row each time the master's lot for an underlying changes); `options.market_epochs` (seeded); a pure `LotSizeLookup` and `EpochLookup`; setting `Options:Underlyings`; migration `AddOptionContracts`; counts in `market.master_refreshes` warnings text; tests; ADR.
**Out:** any Dhan call (P14-02/03); option rows in `market.instruments`, search or baskets; prices, Greeks, chain data; costs (P14-07); any API route or screen (P14-16); BSE, MCX, currency options.

## Acceptance criteria
1. Given a master fixture with shares, futures, `OPTIDX` and `OPTSTK` rows, then `market.instruments` receives exactly the rows it received before this slice (same counts by type; `InstrumentScope` tests unchanged) and `options.contracts` receives only `NSE`/`D` rows with instrument `OPTIDX` or `OPTSTK` whose underlying is in `Options:Underlyings`; others are counted as `skipped_not_listed`.
2. Each contract row holds `security_id` (Dhan id, unique), `underlying_id` (the index or share in `market.instruments`; a listed underlying not found there is counted `unmapped_underlying` and its rows skipped), `instrument` (`OPTIDX|OPTSTK`), `expiry`, `strike_px` (integer paise, half away from zero), `side` (`CE|PE`), `expiry_kind` (`week|month`, from `EXPIRY_FLAG`, else `unavailable` stored as null), `lot_size_seen`, `tick_px`, `first_seen_on`, `last_seen_on`. A row with an unparsable expiry, strike, side or lot is counted `invalid` and skipped, never stored with a zero.
3. A second import of the same file inserts nothing and changes nothing (idempotent on `security_id`); a later file advances `last_seen_on` only; a contract missing from the file (expired) keeps its row. `xd_app` may INSERT and UPDATE only the column `last_seen_on` of `options.contracts` (column grant) and INSERT only on the other two tables (grants test).
4. When the master's lot for a listed underlying differs from the newest `scrip_master` row for it, one `options.lot_sizes` row is written with `effective_from` = the refresh's India date, `source = scrip_master`, `confirmed = false`; an unchanged lot writes nothing.
5. `LotSizeLookup(underlying, date)` returns the row with the latest `effective_from <= date` and its `confirmed` flag, or `unavailable` when none exists; given Nifty seed rows 75 (from 2024-11-20) and 65 (from 2025-12-30), then 2025-12-29 → 75 and 2025-12-30 → 65.
6. Seed rows (`lot_sizes` and `market_epochs`) are `confirmed = true` only for a value the build agent read in the circular itself, with its URL in `source_url`; every other seed is `confirmed = false` with the secondary URL; the ADR lists which.
7. `options.market_epochs(code, effective_from, description, source_url, confirmed)` is seeded with at least `weekly_index_one_per_exchange` 2024-11-20, `upfront_premium` 2025-02-01, `nifty_expiry_tuesday` 2025-09-01, `index_lot_revision` 2025-12-30, `stt_fo_raise` 2026-04-01; `EpochLookup(date)` returns the epochs in force; unique on (`code`, `effective_from`).
8. `Options:Underlyings` (default: `NIFTY`, `BANKNIFTY`, `FINNIFTY`, `MIDCPNIFTY` and the 30 F&O shares with the highest 20-session mean turnover among tracked shares at the first import, written into the setting by the owner, not recomputed silently) is validated at start: unknown symbols are a start warning, not a crash; an empty list writes no contract.
9. No trading use: `Domain` decision code, `Simulation`, `Strategies*`, `Indicators`, `Application/Planning|Learning|Portfolio` reference no `options` type (architecture test); no route reads the tables in this slice.
10. The import stays inside today's limits: the stream is still read once; the master run time grows by less than 20 % on the fixture-scaled benchmark (UNVERIFIED budget: the run takes about a minute today).

## Contract
- **API/UI:** none.
- **Data (`AddOptionContracts`, schema `options`, `GrantSchemaUsageToAppRole`):** `contracts(security_id int8 pk, underlying_id uuid fk market.instruments, instrument varchar(6), expiry date, strike_px int4 check > 0, side char(2) check in ('CE','PE'), expiry_kind varchar(5) null, lot_size_seen int4 check > 0, tick_px int4, first_seen_on date, last_seen_on date)`, index `(underlying_id, expiry, strike_px, side)` unique; `lot_sizes(id identity, underlying_id, effective_from date, lot_size int4 > 0, source varchar(20) in (circular, secondary, scrip_master), source_url varchar(500) null, confirmed bool, recorded_at timestamptz db-stamped)`; `market_epochs(code varchar(60), effective_from date, description varchar(500), source_url, confirmed bool, pk (code, effective_from))`. All rows kept; `Down` drops the schema.
- **Settings:** `Options:Underlyings` (list of canonical symbols; default above).

## Non-functional
Rows: live contracts for the list about 10 k (P14-00 estimate; the build counts the real file); expired ones accrue about 40 to 60 k a year (Nifty weeklies about 150 strikes × 2 sides × 52 plus monthlies; UNVERIFIED) × about 250 B with index = **under 20 MB a year**, no cap needed. Log counts only.

## Test plan
Unit: `OptionScope` (each instrument type, BSE, test symbols, unlisted underlying), row mapping (paise rounding at .5, bad strike, bad side, `NA` lot), `LotSizeLookup` boundaries, `EpochLookup`. Integration (Timescale): migration up/down, grants (column grant on `last_seen_on`), idempotent re-import, lot change row, expired contract kept, `market.instruments` unchanged on the fixture. Architecture: criterion 9.

## Is the archive worth it
**What it enables.** The option family of P14 has two data sources and nothing else: Dhan's expired-options history (P14-03) and our own chain snapshots (P14-02). P14-01 is the key both join on.
- **P14-03 backfill → P14-08, P14-09, P14-11.** Without option prices the expression simulator (P14-08) cannot run, so the option verdict (P14-09) and IV rank (P14-11) are NOT_EVALUABLE. The backfill reaches **5 years back from the day it runs**, so every month of delay loses the oldest month for good.
- **P14-02 recorder → P14-07, P14-10 and P14-04.** Dhan keeps **no bid/ask history and no Greeks beyond IV** (P14-00 D1), so the half-spread in the cost model (P14-07) can come only from our snapshots. It needs ≥ 60 sessions. Credit spreads and condors (P14-10) pay four half-spreads, so their result depends most on it. A missed day can never be fetched again.
- **Earliest evaluable dates.** The spread model is ready 60 NSE sessions after switch-on: switched on 2026-11-09, that is about **2027-02-05** (holiday calendar UNVERIFIED). The option verdict can run once P14-03 and the 5-year underlying backfill (ISS-19, D9) are both in, which takes days, not months. Until the spread model is ready, the verdict uses a flagged fixed spread. The **latest rule epoch** (from 2025-09-01) holds only about a year, so it reads mostly INCONCLUSIVE whatever the archive does (P14-00 risk 1).

**Cost.** The Data API plan the owner already pays for covers option chain and expired options data: Dhan's product announcement (2025-10-15) says the new options data is included in the ₹499 a month subscription. That is not stated on the docs page, so it stays UNVERIFIED until the first call answers without `DH-902`/806. The disk caps proposed in P14-02 and P14-03 are 4 + 3 GB, and the rate is one request per 3 s at most. Every setting is OFF and nothing is sent until the owner switches it on.

**If the owner skips it.**
- *Skip P14-02 only:* every spread is an assumption, forever. P14-07 labels it, and P14-10 credit structures should then be NOT_EVALUABLE (recommended).
- *Skip P14-03 only:* there is no option verdict at all; P14-05 to P14-11 can still be built on fixtures.
- *Skip all of P14-01 to P14-04:* the option families stay disabled (`expression` stays `cash`), which is exactly the kill criterion's default. `PAIRS` and `BASIS` (P14-12 to P14-15) are unaffected.

**Recommendation:** P14-01 and P14-02 now, because their data cannot be recovered later. P14-03 any time soon, at the latest before P14-08.

## Open questions (owner)
1. The underlying list: four indices plus the top 30 F&O shares by turnover (recommended), or a different list or count.
2. Confirm the lot-size and epoch seed values against the circulars, or accept `confirmed = false` until you do. Lookups return the value and say it is unconfirmed.
3. Keep expired contracts forever (recommended, under 20 MB a year).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to check that `market.instruments` and search are unchanged)
- [ ] Migration `AddOptionContracts` with `Down`; no allow-list change
- [ ] ADR written; doc 06 §2.1 and doc 20 §C updated; the real contract count recorded in the ADR
