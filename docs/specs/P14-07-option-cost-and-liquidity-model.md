# P14-07 — Option cost and liquidity model: effective-dated option and futures charge lines on the owner's sheet, a half-spread model from usable chain sessions, rupee liquidity filters and the lot-feasibility count, each with its label

- **Status:** Built 2026-11-03 (ADR 0194). Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-07 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Option cost and liquidity model"); phase acceptance criterion 3.
- **Design refs:** docs/design/08 §8 (costs always on), 16 §2 (`costs`), 20 §C · P14-00 D7 (fractional lots in research, feasibility counted apart), D8 (costs effective-dated), O.1, O.2, O.5(d) · ADR 0042/0044 (the charge sheet, its canonical text and hashes; "nothing in `src/` holds a rate"), 0176 consequence ("P14-07 reads half-spreads only after 60 sessions and only through a reader slice of its own"), 0178 (usable chain sessions). New ADR: **next free number at build time**.
- **Needs:** P14-04 (built: `usableChainSessions`, `SessionUsability`), P14-01 (built: `LotSizeLookup`, `EpochLookup`), P14-05 (half-spread in premium terms), P3-03f (built: `CostSheet`, `cost-sheet load|show|check`). **Owner acts (defaults if missing):** the option and futures charge lines (ISS-25; default: gross, labelled); 60 usable chain sessions per underlying (default before then: an owner-set assumption, else `spread_unavailable`). **Needed by:** P14-08, P14-10, P14-15.
- **Size:** 2.5 agent-days (estimate).
- **Safety:** **yes** in the plan's sense — it asks the owner for his rates and assumptions (never invented in `src/`), and it adds a second reader of `options.chain_snapshots`. No network, no order, no setting that sends.

## Goal
For any option leg the research code can ask "what does this cost on this date": the owner's effective-dated charges when he has given them (else gross, said so), a half-spread measured from our own usable chain sessions when there are 60 (else his stated assumption, labelled, else `unavailable`), whether the strike was liquid enough at that moment, and whether one lot fits the trade's rupee risk. Every answer carries the label that says which.

## Verified facts (read 2026-11-03 in `backend/src`)
- `CostSheet` (Domain/Research): 1 to 12 effective-dated `ChargeSchedule`s; each holds `GstPct`, `Rounding` and `ChargeRates` for **two** segments only (`CostSegment.IntradayEquity`, `Delivery`); `RatesOf(segment)` throws on another member. `ChargeRates` has ten explicit fields (brokerage percent/cap/min, STT buy/sell percent, exchange percent, SEBI per crore, stamp buy percent, other percent, DP per sell); a zero is written 0, never null. `CostModel.Leg(sheet, segment, legSide, turnover, date)` returns `LegCharges`. `RatesSha256` hashes the canonical rates JSON.
- No option or futures segment, no spread model, no liquidity filter exists. `options.chain_snapshots` has `bid_px`, `ask_px` (paise, null with flags `no_bid`/`no_ask`), `bid_qty`, `ask_qty`, `oi`, `volume`; its only readers are the chain store and `OptionArchiveReader` (pinned per table by `OptionQualityArchitectureTests`, ADR 0178 decision 8).
- `options.rolling_bars` carries `volume` and `oi` (nullable), `spot_px`, `close_px` — so historical liquidity can be judged from OI and volume, but **not** the spread (no bid/ask in that data).
- `options.lot_sizes` holds seeds for the indices (unconfirmed) and one `scrip_master` row per change **from the first master import on 2026-11-01**; historical stock lots are therefore `unavailable` before that date unless a seed is added.

## Scope
**In:**
- Domain: `CostSegment` gains `Options` and `Futures`; `ChargeSchedule` gains **optional** `Options` and `Futures` rates (absent → `unavailable` for that segment, never zero); `CostSheetJson` reads and writes them only when present, so the canonical text and `RatesSha256` of every existing sheet are unchanged.
- Domain (pure): `OptionLegCost` (charges on premium turnover + half-spread), `HalfSpreadModel` (buckets, medians, fallbacks), `LiquidityFilter`, `LotFeasibility`.
- Application: `BuildSpreadModelHandler` and verb `xd options spread-model build [--underlying]`; port `IChainSpreadReader` (bounded reads of usable sessions' quotes); `ISpreadModelStore`.
- Migration `AddOptionSpreadModels`: `research.option_spread_models`, `research.option_spread_buckets`.
- `cost-sheet load|show` accept and print the two new segments; `check` stays equity-only (the importers skip derivative rows, ISS-05).
- Settings `Options:Costs:*`, `Options:Liquidity:*`; tests; ADR; doc 08 §8, doc 16 §2.

**Out:** margin (no margin API, D5); exercise and assignment costs (positions exit before expiry, D6); a spread for dates before our first snapshot other than the labelled model; changing `cost-sheet check` for derivative bills; any screen.

## The rules
- **Charges.** An option leg's turnover is premium × units; futures turnover is price × units. `CostModel.Leg(sheet, Options|Futures, side, turnover, date)` uses the schedule in force on the leg's own India date. A sheet without the segment → `costs_unavailable` and the trade is **gross**, labelled `costs: none (no option lines)`; never a zero line. The owner's figures are entered with `cost-sheet load`; the secondary rates of P14-00 (STT on premium 0.1 % → 0.15 % from 2026-04-01, futures 0.02 % → 0.05 %) are examples in the doc text only, never seeded.
- **Half-spread, measured.** For an underlying with at least `Options:Costs:MinUsableSessions` (60) usable chain sessions (P14-04 definition: coverage `pass` and parity `pass` or `warn`), the model is the median of `(ask − bid)/2 ÷ mid` over two-sided, uncrossed quotes of those sessions, per bucket: moneyness by strike steps from the snapshot's ATM (`0`, `1-2`, `3-5`, `6-10`), days to expiry (`0-2`, `3-7`, `8-30`, `31+`), time of day (`09:20-10:00`, `10:00-14:30`, `14:30-15:30`). A bucket with fewer than `MinQuotesPerBucket` (200) quotes uses the bucket with the time-of-day split merged, then the moneyness split merged; still short → `unavailable` for that bucket. The model is a stored, content-addressed snapshot (`model_id` = SHA-256 of the inputs: underlying, sessions used, settings), labelled `spread: measured (n sessions, from … to …)`.
- **Half-spread, assumed.** Before 60 usable sessions, or for an `unavailable` bucket, the half-spread is the owner's `Options:Costs:AssumedHalfSpreadPct` for that underlying if set (labelled `spread: assumed x % by owner`), else `unavailable` (`spread_unavailable`). Nothing defaults to a number.
- **Applying a recent spread to the past.** The backfilled bars (P14-03) reach 5 years back; our snapshots start when the recorder is switched on. A measured spread used for an earlier date is labelled `spread_from_recent_archive` and counted per trade; P14-09 prints the share.
- **Liquidity filter** (rolling bars or chain rows at the entry bar): open interest × spot ≥ `Options:Liquidity:MinOiNotionalCr` (₹5 crore) **and** the entry bar's volume × spot ≥ `MinBarTurnoverLakh` (₹25 lakh) (both UNVERIFIED starting values, settings). A null OI or volume → `liquidity_unknown`, never a pass. Rupee thresholds avoid needing a historical lot.
- **Lot feasibility** (D7): research sizes in fractional units; feasibility is counted apart. `loss_per_lot = lot × |Δ| × |entry − stop|` (Δ from P14-05 on vendor IV at entry) against the cash trade's rupee risk `qty × |entry − stop|`: above it → `not_sizable`; lot `unavailable` from `LotSizeLookup` → `lot_unknown`. Example (P14-00 O.2 corrected): Nifty lot 65, Δ 0.5, stop 80 points → loss ₹2,600 a lot > ₹1,000 risk → `not_sizable`. Lots unconfirmed carry the label `lot: unconfirmed`.

## Acceptance criteria
1. **Existing sheets unchanged.** Every stored and fixture sheet without option lines has byte-identical canonical text and `RatesSha256` before and after (golden); every existing cost test is green unmodified.
2. **Option charges.** A TEST sheet with an option schedule from 2025-04-01 and another from 2026-04-01 charges the same buy-sell pair of premium legs on 2026-03-31 and 2026-04-01 with the rates of each date (hand table in the test, paise exact); a sheet without option lines returns `costs_unavailable` and the trade label `gross`.
3. **Spread model.** On a synthetic archive of 60 usable sessions with known quotes, each bucket's median equals the hand value; a bucket of 150 quotes merges as stated; 59 usable sessions → no model (`sessions 59 of 60`); unusable sessions are never read (a fake reader records the dates asked).
4. **Assumption and unavailable.** With no model and no setting the half-spread is `unavailable`; with `AssumedHalfSpreadPct` 0.5 for NIFTY it is 0.5 % with the owner label; a measured model used on a date before its first session is labelled `spread_from_recent_archive`.
5. **Liquidity.** Hand cases: OI 1,000 units × spot 22,000 = ₹2.2 crore → filtered; OI 5,000 units → ₹11 crore passes; a null OI → `liquidity_unknown`; never a pass on a null.
6. **Lot feasibility.** The Nifty example gives `not_sizable`; the same trade on 2025-12-29 and 2025-12-30 uses lot 75 and 65 (phase criterion 3); a stock date before its first stored lot gives `lot_unknown`, and R in fractional units is still computed.
7. **Reader discipline.** `IChainSpreadReader` reads only `(underlying_id, ts)` ranges of usable sessions and returns bid, ask and the bucket keys only (no IV, Greeks, OI); the per-table reader pins of ADR 0178 are changed deliberately to add exactly this one file; no decision path references it (architecture test).
8. **Migration.** `research.option_spread_models(model_id char(64) PK, underlying_id, sessions int, first_session, last_session, settings jsonb, built_at)` and `research.option_spread_buckets(model_id, moneyness, dte, tod, n int, half_spread_pct double precision, PK (model_id, moneyness, dte, tod))`, insert-only for `xd_app`, nothing for `xd_exec` or the read roles (grants tests); a rebuild with the same inputs writes nothing; `Down` drops both.
9. **No invented rate.** A test greps `src/` for numeric literals in the option and futures segment code paths and the settings defaults: `AssumedHalfSpreadPct` defaults to empty; no STT or brokerage figure appears outside tests.

## Contract
- **Verb:** `xd options spread-model build [--underlying SYMBOL]` (exit 0 when built or nothing to build, with the reason; 2 for a bad line).
- **Data:** `AddOptionSpreadModels` (criterion 8).
- **Cost sheet JSON:** optional `options` and `futures` objects per schedule with the ten existing fields.
- **Settings:** `Options:Costs:MinUsableSessions` 60 (20-250), `MinQuotesPerBucket` 200 (50-5,000), `AssumedHalfSpreadPct` {} (per canonical symbol, 0.01-20); `Options:Liquidity:MinOiNotionalCr` 5 (0-1,000), `MinBarTurnoverLakh` 25 (0-10,000).
- **API, UI:** none (P14-16 reports the labels).

## Non-functional
Building one underlying's model from 250 sessions ≤ 2 minutes (bounded reads, one session at a time; UNVERIFIED); a cost or spread lookup ≤ 5 µs from the loaded model.

## Test plan
- **Unit:** charges hand table (criterion 2), bucket medians and merging, the liquidity and lot hand cases, label texts.
- **Integration (Timescale):** sheet round trip with and without the new segments, the spread-model build on a seeded archive, grants, idempotent rebuild.
- **Architecture:** criteria 7 and 9.

## Decisions (researched)
1. **Optional segments on the existing sheet.** One sheet, one bill check, one canonical text; optional keys keep every existing hash. Rejected: a second "derivatives sheet" (two effective-date chains to keep in step).
2. **Percent-of-mid half-spreads in buckets.** Spreads scale with premium; moneyness, days to expiry and time of day are where they differ most. Medians, because a few crossed or stale quotes would move a mean.
3. **Rupee liquidity thresholds.** They need no historical lot, which we do not hold for stocks before 2026-11-01.
4. **Feasibility apart from the unit** (P14-00 D7): the primary comparison uses fractional units; `not_sizable` is a reported share and a secondary, sizable-only comparison (P14-09). This resolves the plan's O.1, which listed `not_sizable` as unpaired.

## Risks and honest limits
- The spread is measured in 2026-27 and applied to 2021-26 (labelled); spreads in earlier years, especially in Bank Nifty weeklies, were different and probably wider for stocks.
- Vendor bid/ask are top-of-book snapshots every 5 minutes; a fill of more than the top quantity costs more (not modelled; P14-09 prints the median order size against `bid_qty`/`ask_qty`).
- Without the owner's option lines every option result is gross while the cash side may be net: P14-09 compares like with like (both gross or both net) and says which.

## Open questions (owner)
1. Load the option and futures charge lines (Dhan's sheet with effective dates) with `cost-sheet load`? **Default: gross, labelled.**
2. Set `AssumedHalfSpreadPct` per underlying for the time before 60 usable sessions? **Default: none, so the option verdict is NOT_EVALUABLE (`spread_unavailable`) until the archive has 60 usable sessions.**
3. The liquidity thresholds (₹5 crore OI notional, ₹25 lakh bar turnover)? **Default: as drafted.**

## Done when
- [x] All acceptance criteria have evidence (test map in the build report; criterion 1 has the recorded deviation in ADR 0194 decision 2b)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any invented rate, any unavailable spread or cost read as zero, any changed hash of an existing sheet, any unusable session read)
- [x] Migration `AddOptionSpreadModels` with `Down`; no API change
- [x] ADR written (0194, Proposed); docs 08 §8, 16 §2, 20 §C updated
