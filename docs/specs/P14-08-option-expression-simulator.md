# P14-08 — Option expression simulator: re-mark each stored cash trade as a single long option on the same entry, with a fixed strike rebuilt from the rolling offsets, an exit mapped from the cash exit, an exit before the expiry session, and every unpaired trade counted by reason

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-08 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Option expression simulator"); phase acceptance criteria 2, 3 and 4.
- **Design refs:** docs/design/20 §C · 08 (exits) · P14-00 O.1, O.2, D3, D6, D7 · ADR 0053 (same entries: one row per entry under every candidate), 0038 (the worse outcome inside a bar), 0043 (fills), 0045 (insert-only research rows, open-at-end excluded), 0058 (run identity), 0177 consequence ("P14-08 and P14-11 read bars only through a reader slice of their own"), 0178 (usable rolling key-sessions; bar timestamp meaning UNVERIFIED). New ADR: **next free number at build time**.
- **Needs:** P14-03 (built; **data after the owner's probe and switch**), P14-04 (built: usable key-sessions), P14-05 (Δ, the fallback price), P14-06 (`Expression`, profiles), P14-07 (costs, spread, liquidity, lot feasibility). **Can be built and tested without any recorded data** (synthetic rolling bars and a synthetic cash run); with none, every trade is unpaired `no_series` and the run says so. **Needed by:** P14-09, P14-10, P14-16.
- **Size:** 4 agent-days (estimate; the plan said 3.5; +0.5 for the expiry calendar, which the plan did not list and the rolling bars do not carry).
- **Safety:** no — research re-marking of stored trades. It writes only its own insert-only rows, adds one bounded reader of `options.rolling_bars`, and nothing here selects, plans or orders. The simulator engine (`sim-9`) is not changed.

## Goal
For a stored cash run and a pre-registered option profile, one verb produces a paired set: for every cash trade either the option version of the same trade (entry at the same minute, a fixed strike and expiry, exits mapped from the cash exits, net rupees and `R_equiv` on the cash trade's rupee risk) or an unpaired row with its reason. Prices come from vendor bars; a model price is used only as a labelled, counted fallback.

## Verified facts (read 2026-11-03 in `backend/src`)
- `options.rolling_bars(ts, underlying_id, expiry_flag 'W'|'M', expiry_code 1-9, atm_offset −10..10, side 'CE'|'PE', interval_minutes, strike_px, spot_px, open/high/low/close_px, volume null, oi null, iv real null, flags 0-1 suspect)`; PK `(ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes)`. **There is no expiry-date column**: a series is "the nearest (code 1) weekly", not a contract. `IRollingBarStore` has no read method; only `RollingBarStore` and `OptionArchiveReader` name the table (ADR 0177, 0178 pins).
- The backfill plan holds NIFTY (5 min, WEEK 1 and 2, MONTH 1, ±10), BANKNIFTY (5 min, MONTH 1 and 2, WEEK 1 until 2024-11-20, ±10) and the first 20 frozen shares (15 min, **MONTH 1 only**, ±3) (ADR 0177 decision 3).
- `options.contracts` holds contracts **from the first master import (2026-11-01)** on, live and expired; it says nothing about 2021-2026 expiries. `options.market_epochs` seeds `nifty_expiry_tuesday` 2025-09-01 and `weekly_index_one_per_exchange` 2024-11-20 (unconfirmed). `ITradingCalendar` exists (Application/Abstractions/Market/Calendar).
- `SimTrade` carries `InstrumentId`, `Side`, `TradeDate`, entry and exit epochs and prices, `StopPrice`, `TargetPrice`, `ExitReason`, `RExact`, `RFilled` and the cost basis; stored runs are in the research result store (`AddResearchResultStore`). Simulator SELL is intraday only (`swing_sell_unsupported`).
- `market.bars_1m` holds the tracked instruments (214, about 2.1 years). **Whether any stored run trades an index (NIFTY, BANKNIFTY) is UNVERIFIED**; if none does, the 5-minute index archive pairs with nothing and the index profiles are NOT_EVALUABLE `no_paired_trades` (see the plan's review fixes).

## Scope
**In:**
- Domain (pure): `ExpiryCalendar` (expiry dates per underlying and flag from contracts, else a derived weekday rule per epoch and the trading calendar), `HeldStrikeSeries` (rebuild a fixed-strike premium series from the offsets), `OptionRemark` (the pairing, fills, exits, units, `R_equiv`, labels), `OptionProfileParams` (the `option_buy` parameter list and its canonical text for `ExpressionProfile`).
- Application: `RemarkRunHandler`, verb `xd options remark --run <run-key> --profile <hash>`; port `IRollingBarReader` (bounded: underlying, flag, code, side, interval, ts range; returns bars of all offsets); readers of the cash run, the calendar, lots, epochs, the spread model, the cost sheet.
- Migration `AddExpressionTrades`: `research.expression_runs`, `research.expression_trades`.
- Tests, ADR, doc 20 §C.

**Out:** spreads and condors (P14-10); pairs and basis; the verdict (P14-09); any change to the simulator or a strategy; a screen; `future` as an expression (no futures bars exist: `future` profiles are refused `no_futures_bars` until a futures bar source is specified).

## The rules
- **Which trades.** Every trade of the stored cash run whose instrument is an underlying with rolling series is a candidate; all other trades get one row `unpaired/no_series` (counted, never dropped). Open-at-end cash trades are excluded as in ADR 0045 and counted.
- **Side.** A BUY becomes a call (`CE`), an intraday SELL a put (`PE`). Long options only (D5).
- **Strike rule** (profile parameter `strike`): `atm` = the strike of offset 0 at the entry bar; `itm1` = offset −1 for a call, +1 for a put. That strike `K` is then **fixed** for the life of the trade.
- **Expiry rule** (`expiry`): `nearest_safe` = the nearest series (code 1) unless the trade's planned last session (the trade date for intraday; the date + `max_hold_sessions` for swing) is on or after the last session before that expiry, then code 2. Indices: NIFTY `W`; BANKNIFTY `W` until 2024-11-20, else `M`. Shares: `M` code 1 only (the backfill holds no code 2), so a hold reaching the expiry session is `unpaired/no_series_for_expiry`.
- **Expiry dates (`ExpiryCalendar`).** An expiry date comes from `options.contracts` when it holds that underlying, flag and month (label `expiry: contract`); otherwise from a **derived rule** (label `expiry: derived`, unconfirmed): per epoch, the weekday of the weekly expiry and of the monthly (the last such weekday of the month), moved to the previous trading day when that day is a holiday. The weekday table is seeded as UNVERIFIED data in the ADR (from secondary sources: NIFTY Thursday until 2025-08-31 and Tuesday from 2025-09-01 (epoch `nifty_expiry_tuesday`); BANKNIFTY monthly the last Thursday, then the last Tuesday from 2025-09-01; BANKNIFTY weeklies on Wednesday in their last era; shares the last Thursday, then the last Tuesday) and checked by the build agent against the NSE circulars it opens. A date the rule cannot give → `unpaired/expiry_unknown`.
- **Following the held strike.** At each option bar the premium of `K` is the bar of the offset whose `strike_px` equals `K` (same flag, code, side, interval). Bars outside the ±N window are `unavailable` — never a zero, never the last price (phase criterion 2). If a bar needed for an exit or a stop check is unavailable, the premium is a **Black-76 fallback** (P14-05) on the latest vendor IV of that strike no older than `Options:Remark:MaxIvAgeMinutes` (60) and the underlying's 1-minute close, labelled `bs_fallback` and counted; without such an IV the trade is `unpaired/strike_left_window` (counted; this biases toward short trades, P14-00 risk 2, and the share is printed).
- **Usable data.** Only rolling key-sessions that P14-04 marks usable are read; a trade touching an unusable key-session is `unpaired/archive_unusable`; a bar flagged `suspect` is treated as unavailable.
- **Fills.** Entry at the open of the first option bar whose start is at or after the cash entry minute; exit the same way at the cash exit minute; a session square-off at the close of the last bar of the session. The bar start convention is UNVERIFIED (ADR 0178); the build reads it from the probe result and a test pins the convention used.
- **Exits.** The cash exit (stop, target, trail, session exit, `MAX_HOLD`, `event_exit`) maps to the option exit at the same minute. Optional premium stop (`premium_stop_pct`, default none): a long option exits when the bar low reaches `entry × (1 − p)`, at that price, or at the bar open if it opened below. **When the premium stop and the cash stop fall in the same option bar, the worse outcome is taken** (ADR 0038; phase criterion 4). **Expiry exit (D6):** a position still open at the close of the last session before its expiry session leaves at that close (`expiry_exit`, counted).
- **Units and `R_equiv`** (O.2, D7). `risk = qty × |entry − stop|` of the cash trade (rupees). `Δ` from P14-05 at the entry bar on the vendor IV of that bar (label `delta: bs_on_vendor_iv`; IV null → `unpaired/no_iv`). `units = risk ÷ (|Δ| × |entry − stop|)`, fractional. Net rupees = `(exit − entry premium) × units − charges(P14-07) − 2 × half-spread × units`. `R_equiv = net rupees ÷ risk`. The cash side's `R_equiv` is the cash trade's R on the same cost basis (both gross or both net; a mismatch is refused `basis_mismatch`). Hand example (O.2): cash entry 1,000, stop 990, qty 100 → risk ₹1,000; premium 40, Δ 0.5 → 200 units; exit at the cash target 1,020 with premium 50 → +₹2,000 gross, `R_equiv` 2.0 before costs.
- **Liquidity and lots** (P14-07): a trade failing the liquidity filter is `unpaired/illiquid` or `unpaired/liquidity_unknown`; lot feasibility is a column (`sizable`, `not_sizable`, `lot_unknown`), not a pairing fate.
- **Run identity.** The expression run id = SHA-256 of `expr-remark/1`, the cash run key, the profile hash, the re-mark version `expr-1`, the archive watermark (the highest `options.fetch_log.id` read), the quality run ids used, the spread model id, the cost sheet `RatesSha256` (or `gross`), and the hashes of the lot and epoch rows read. Same inputs → same id → nothing written again.

## Acceptance criteria
1. **Same entries.** On a synthetic cash run of 50 trades (30 on an underlying with series, 20 without), the expression run has exactly 50 rows: each paired row's entry minute equals the cash entry minute's option bar; 20 rows `no_series`; no trade is dropped (a count check of both).
2. **Strike rebuild.** Synthetic series where spot rises 3 strike steps during a trade: the premium is read from offsets 0, −1, −2, −3 in turn by the stored `strike_px`; when `K` leaves ±N, the bars are unavailable, the fallback is used with its label when an IV of age ≤ 60 minutes exists, else `strike_left_window` (phase criterion 2).
3. **Worse outcome.** A bar where the premium stop and the cash stop both fall gives the lower premium (phase criterion 4); a gap below the premium stop exits at the bar open.
4. **Expiry exit.** A swing trade reaching the session before expiry leaves at that close; an expiry on a holiday Thursday moves to Wednesday by the derived rule; a contract-backed expiry wins over the derived one; an unknown expiry is `expiry_unknown`.
5. **Units and R.** The O.2 hand example gives 200 units and `R_equiv` 2.0 gross; with a TEST option sheet and a 1 % half-spread it gives the hand net value (paise exact in the test); a cash trade gross and an option net is refused `basis_mismatch`.
6. **Lots.** The same trade on 2025-12-29 and 2025-12-30 records lot 75 and 65 and the feasibility column (phase criterion 3).
7. **Honest fates.** Every unpaired reason of the closed list (`no_series`, `no_series_for_expiry`, `expiry_unknown`, `strike_left_window`, `archive_unusable`, `no_iv`, `illiquid`, `liquidity_unknown`, `basis_mismatch`, `open_at_end`) is reached by a fixture; counts per reason are in the run row; no unpaired row carries a premium or an R.
8. **No look-ahead (property).** For 200 seeded runs, changing any bar after a trade's exit minute (or after the decision for the strike and expiry choice) changes nothing in that trade's row; the fallback IV is never later than the bar it prices.
9. **Determinism.** Same inputs in any order → the same rows and run id; a second run writes nothing.
10. **Reader and decision discipline.** `IRollingBarReader` reads bounded ranges only; the per-table pins of ADR 0177/0178 gain exactly this one file; no decision path, `XDrishti.Execution` or `XDrishti.Simulation` references the re-mark code (architecture test); `SimulationEngine.Version` stays `sim-9` and every golden is unchanged.
11. **Migration.** `research.expression_runs(run_id char(64) PK, cash_run_key, profile_hash char(64), remark_version, inputs jsonb, trades int, paired int, unpaired jsonb, bs_fallback_share double precision null, labels text[], created_at)` and `research.expression_trades(run_id, trade_key, fate varchar(24), side, expiry_flag, expiry_code, expiry date null, expiry_source, strike_px int null, entry_ts, exit_ts, entry_premium_px int null, exit_premium_px int null, units double precision null, gross_inr numeric null, net_inr numeric null, r_equiv double precision null, cash_r double precision null, exit_reason, lot int null, sizable varchar(12), labels text[], PK (run_id, trade_key))` with checks (`fate = 'paired'` ⇔ premiums and R present); insert-only for `xd_app`; nothing for `xd_exec` or the read roles (grants tests); `Down` drops both.

## Contract
- **Verb:** `xd options remark --run <run-key> --profile <hash>` (prints counts by fate and the labels; exit 0; 2 for a bad line; 1 for an unknown run or profile).
- **Data:** `AddExpressionTrades` (criterion 11).
- **Settings:** `Options:Remark:MaxIvAgeMinutes` 60 (5-390).
- **API, UI:** none (P14-16).

## Non-functional
10,000 paired trades ≤ 5 minutes (P14-00 budget, UNVERIFIED): the reader loads one (underlying, series, day range) at a time and the core is allocation-light. No bar values in logs; logs `expression.remark.start|done` carry counts.

## Test plan
- **Unit:** each rule above on hand series; `ExpiryCalendar` across the 2024-11-20 and 2025-09-01 epochs and a holiday; units and `R_equiv` hand values.
- **Property (seeded, ≥ 200 runs):** criteria 8 and 9; every trade has exactly one row.
- **Integration (Timescale):** a seeded archive and cash run through the verb; grants; idempotent re-run.
- **Architecture:** criterion 10.

## Decisions (researched)
1. **A fixed strike rebuilt from the offsets.** The rolling series is "ATM+k", not a contract; holding a position means holding one strike. Reading `strike_px` per bar is exact where the window covers it and honest (`unavailable`) where it does not.
2. **An expiry calendar as data with a derived fallback.** The bars carry no expiry date and `options.contracts` starts in 2026-11; without dates neither the time to expiry nor D6's exit can be computed. Labelled and unconfirmed, like the lots.
3. **Open of the next option bar.** No look-ahead and no favourable timing: the option fill is never earlier than the cash fill. The cost is a delay of up to one bar (5 or 15 minutes), which P14-09 prints as a limit.
4. **Fallback only on an observed IV.** A model price is the vendor's volatility moved along the model, never a volatility we chose; its share is capped by P14-09 (O.5e).

## Risks and honest limits
- Stock series are 15-minute bars at ±3 and code 1 only: many stock trades will be `strike_left_window` or `no_series_for_expiry`, and the remainder is biased toward short trades.
- Δ comes from Black-76 on vendor IV (a model Greek); a wrong IV unit would scale every unit count (pinned by the P14-02 pilot and the P14-03 probe).
- If no stored run trades an index, the index archive (most of the backfill) pairs with nothing; an index signal strategy would have to be specified and run first (not in this plan).

## Open questions (owner)
1. Accept the derived expiry weekday table as unconfirmed data until checked against circulars? **Default: yes, labelled `expiry: derived`.**
2. A premium stop in the primary profile? **Default: none (the cash stop is the stop); premium stops are secondary profiles, each counted in N.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any dropped trade, any premium read across the window edge, any fill before the cash fill, any fallback without an observed IV, any change to `sim-9`)
- [ ] Migration `AddExpressionTrades` with `Down`
- [ ] ADR written (with the expiry weekday table and its sources); doc 20 §C updated
