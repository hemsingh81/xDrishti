# P14-03 — Expired-options backfill: five years of rolling ATM-offset option bars from Dhan, resumable, off by default

- **Status:** Approved (owner, 2026-11-01: pre-approved, no review wanted). Open questions resolved with the recommended/safer answers: allow-list rows `POST api.dhan.co/v2/optionchain`, `/v2/optionchain/expirylist`, `/v2/charts/rollingoption` accepted (each enters the pin in the commit of the slice that uses it, ADR 0133); underlyings = 4 indices plus the top 30 F&O shares; lot-size and rule-date seeds stay confirmed=false until checked against circulars; stock chains for 30 stocks (not all 213); backfill = indices plus top 20 stocks at ATM±3 only after the probe; the backfill probe runs first; the chain cap 4 GB and backfill cap 3 GB with the shared 20 GB free floor; the VM disk enlargement (to 160 GiB) is the owner's act and every recorder stays off until its gates are turned on, so the build proceeds within the caps; Services-page switches and the Data-quality card are a later frontend slice; quality thresholds and the 6.5 % parity rate as drafted.
- **Phase / roadmap row:** P14 — slice P14-03 of [P14-00](P14-00-options-and-stat-arb-plan.md) (D2: 5-minute index bars, 15-minute stock bars; ATM±10 / ±3).
- **Design refs:** docs/design/20 §C ("expired options data API for backtests"), 05 §7, 17 · ADR 0133 (allow-list rule), 0097, 0016 §7 (the chart client and its throttle), 0135/0173 (switch, guard), 0083. New ADR: **next free number at build time**.
- **Needs:** P14-01 (`Options:Underlyings`, contracts for the strike grid). **Needed by:** P14-04 (stitching checks), P14-08 (option prices for re-marking), P14-11 (5-year IV).
- **Size:** 3 agent-days (estimate).
- **Safety:** **yes** — a new broker endpoint on the allow-list, the Data token, about 32,000 requests and up to 3 GB of disk. Off by default three times. No order, no reader on a decision path.

## Goal
On the owner's say, the system fetches up to five years of expired-option bars from Dhan for the configured underlyings and strike offsets. Each 30-day window is fetched once, in a resumable job outside market hours, at one request a second. The bars land in an insert-only compressed table, with a fetch log that says what came back, what was empty and what failed.

## Verified facts
**Dhan (read 2026-11-01, <https://dhanhq.co/docs/v2/expired-options-data/>):**
- `POST https://api.dhan.co/v2/charts/rollingoption`, no query.
- Body fields:
  - `exchangeSegment` (`NSE_FNO`), `securityId` (the underlying's id), `instrument` (`OPTIDX`/`OPTSTK`).
  - `interval` 1, 5, 15, 25 or 60.
  - `expiryFlag` `WEEK`/`MONTH`, `expiryCode` (values not listed on the page).
  - `strike`: `ATM`, then `ATM±1..10` for index options near expiry and `ATM±1..3` for all other contracts.
  - `drvOptionType` `CALL`/`PUT`.
  - `requiredData`: any of `open, high, low, close, iv, volume, strike, oi, spot`.
  - `fromDate`/`toDate` (`YYYY-MM-DD` in the example; `toDate` non-inclusive).
- At most 30 days a call, up to 5 years back.
- Answer: `data.ce` / `data.pe` (one null), each with parallel arrays `iv, oi, strike, spot, open, high, low, close, volume, timestamp` (epoch).
- No bid/ask. The page states no rate limit. Data API limits are 5/s and 100,000 a day (<https://dhanhq.co/docs/v2/>).

**Plan:** Dhan's post of 2025-10-15 says this data is in the ₹499 Data API subscription.

**Code (read 2026-11-01):**
- `BrokerEndpointsTests` lists `POST https://api.dhan.co/v2/charts/rollingoption` as a **refused** case. `PinnedAllowList` has no such line.
- `DhanIntradayChartClient` and `RequestThrottle` (1 a second) are the pattern for a resumable Dhan chart fetch.

**UNVERIFIED until the probe (criterion 9):**
- The `expiryCode` meaning (1 = nearest assumed, 0 possibly current).
- Whether "near expiry" limits ±10 to code 1 only.
- The `timestamp` zone.
- `iv` units.
- What a window before an underlying's weekly era returns (empty or `DH-907`).
- Whether `strike` is the absolute strike per bar (assumed).

## Scope
**In:**
- Allow-list row, `PinnedAllowList` line and tests, in one commit.
- `IRollingOptionClient` + `DhanRollingOptionClient` (Data credentials, own `RequestThrottle` at `Options:Backfill:RequestsPerSecond` 1).
- Pure `BackfillPlanner`: the set of (underlying, expiry flag, code, offset, side, window) fetch keys.
- Pure `RollingBarParser` (arrays to rows, prices in paise, length check).
- `OptionBackfillJob`, a resumable service `options-backfill`.
- `options.fetch_log`; `options.rolling_bars`.
- The owner's switch, `GET`/`PUT /api/options/backfill`.
- Settings `Options:Backfill:*`; fixtures; tests; ADR.

**Out:** bid/ask (does not exist); 1-minute bars (D2); any absolute-strike contract history; re-fetching a window already `ok` (only an owner reset, a later slice); reading the bars (P14-04 checks, P14-08 uses); index live data; deleting data.

## Acceptance criteria
1. **Allow-list.** `POST https://api.dhan.co/v2/charts/rollingoption` with no query is allowed: a `Uri` constant added to `BrokerEndpoints.Allowed` and the class summary.
   - The `rollingoption` case moves from the refused list of `BrokerEndpointsTests` to the allowed list.
   - New refused cases: GET/PUT/DELETE, a query, `?`, a trailing slash, `/v2/charts/RollingOption`, `/v2/charts/rollingoption/1`, another host or port, `http`.
   - `/v2/charts/historical` and `/v2/charts` stay refused.
   - `PlanPhaseCheck.PinnedAllowList` gains `POST api.dhan.co/v2/charts/rollingoption` in ordinal order **in the same commit**.
2. **Planner.** Given the defaults, the planner yields exactly these fetch keys, in a fixed order (underlying, flag, code, offset, side, window):
   - Indices at 5 minutes, ATM−10..ATM+10:
     - NIFTY: `WEEK` code 1, `WEEK` code 2, `MONTH` code 1.
     - BANKNIFTY: `MONTH` codes 1 and 2, and `WEEK` code 1 limited to windows ending before 2024-11-20.
   - Stocks at 15 minutes: the top 20 of `Options:Underlyings` by turnover, `MONTH` code 1, ATM−3..ATM+3.
   - Sides `CALL` and `PUT`.
   - Windows: 30-day windows going back from the last full month to `Options:Backfill:Years` (5) before it.

   That is **6 × 21 × 2 × 61 = 15,372 index keys** (fewer by the BANKNIFTY weekly cut) and **20 × 7 × 2 × 61 = 17,080 stock keys**, as computed in P14-00.
3. **Fetch log.** Each key is one `options.fetch_log` row per attempt: key, attempt, started/ended, outcome, HTTP status, Dhan code, bars, latency. The outcome is one of `ok`, `empty`, `no_data`, `rate_limited`, `timeout`, `server_error`, `bad_body`, `token_refused`, `not_subscribed`, `disk_stop`.
   - The job **resumes** at the first key with no `ok`/`empty`/`no_data` attempt; a key with three failed attempts is left `given_up` and listed.
   - A restart never re-fetches a done key (idempotent).
4. **Parser.**
   - Arrays of different lengths are `bad_body`.
   - The `ce` or `pe` side that matches the request is used; the other must be null.
   - Prices and strikes become integer paise; a zero or negative close, or a strike that is not a multiple of the P14-01 strike step, gets flag `suspect` (bit0) and is kept. Never zero-filled.
   - A bar outside 09:15 to 15:30 India time is dropped and counted.
   - Rows insert with `ON CONFLICT DO NOTHING` on `(ts, underlying_id, expiry_flag, expiry_code, offset, side, interval_minutes)`.
5. **Rate.**
   - At most `Options:Backfill:RequestsPerSecond` (1, range 0.2-2) on a fake clock, and at most `Options:Backfill:MaxRequestsPerDay` (40,000) per India day.
   - It runs only in the service window 16:30 to 08:30 India time, every day: never in market hours, so it never competes with the live, chart and chain calls.
   - `DH-904`/429 pauses 60 s and halves the rate for the rest of the night.
   - `DH-901`/807/809 stops until the token changes; `DH-902`/806 ends the run `not_subscribed`.
6. **Three gates, default off.**
   - `Options:Backfill:Enabled` (`XD_OPTIONS_BACKFILL_ENABLED`, default false).
   - The newest row of `options.backfill_switch` (no row = OFF).
   - The service `options-backfill` (registered paused, retries 0, notify on failure).

   With any gate closed or unreadable, nothing reads credentials or sends.

   `PUT /api/options/backfill {enabled, reason, password}`: fresh password on every change, ON and OFF, `StepUpCommand` with `Elevate: false`, actor from the session, account event `OptionBackfillSwitched`, 400 `options.reason_invalid`.

   `GET` returns the switch, history, not-running reasons and progress: keys done, empty, given up and left, with an estimated finish in hours at the current rate. It returns no bar.
7. **Disk guard** before each window and every 5 minutes (`DepthDiskGuard.Check`):
   - The stored size of `options.rolling_bars` above `Options:Backfill:MaxStoredGb` (3) stops the job `disk_budget`.
   - Free space below `Options:MinFreeGb` (20) stops it `disk_free`.
   - A warning at 80 %. The job resumes only after the owner resumes the service. **Nothing is deleted.**
8. **Migration and no reader.**
   - `options.rolling_bars` is a 30-day-chunk hypertable, compressed after 7 days of chunk age (old chunks compress at the first policy run), segment by `underlying_id, expiry_flag, expiry_code, side`, order by `offset, ts`, no retention.
   - `xd_app` may SELECT and INSERT only, on all three new tables (grants test). `Down` drops them.
   - `IRollingBarStore` has no row-read method in this slice.
   - The decision layers, the live feed and `XDrishti.Execution` reference no backfill type (architecture test). The no-order tripwire is green.
9. **Probe (owner-run, before the full run; recorded in the ADR).**
   - Fetch one window for each of NIFTY WEEK 1 at ATM+10, NIFTY WEEK 2 at ATM+10, BANKNIFTY WEEK 1 in 2024-06, and one stock at ATM+3.
   - Record: whether code 2 accepts ±10, the timestamp zone, the IV unit, empty versus `DH-907` for the out-of-era window, and bars per window.
   - The planner's offset limits are set from this result before the full run.

## Contract
- **API:** `GET`/`PUT /api/options/backfill` (`ApiJsonContext`, `scripts/gen-api.sh`; problem codes as P14-02).
- **Data (`AddOptionBackfill`, insert-only):**
  - `options.rolling_bars(ts timestamptz, underlying_id uuid, expiry_flag char(1) in ('W','M'), expiry_code int2, offset int2 between -10 and 10, side char(2), interval_minutes int2, strike_px int4, spot_px int4, open_px int4, high_px int4, low_px int4, close_px int4, volume int8, oi int8, iv real null, flags int2)`.
  - `options.fetch_log` (criterion 3 columns).
  - `options.backfill_switch` (the switch columns).
- **Settings (`Options:Backfill:*`):**

  | Setting | Default | Range |
  |---|---|---|
  | `Enabled` | false | |
  | `RequestsPerSecond` | 1 | 0.2-2 |
  | `MaxRequestsPerDay` | 40,000 | 1,000-90,000 |
  | `Years` | 5 | 1-5 |
  | `StockCount` | 20 | 0-30 |
  | `IndexOffsets` | 10 | 0-10 |
  | `StockOffsets` | 3 | 0-3 |
  | `MaxStoredGb` | 3 | 1-50 |

  Service window 16:30-08:30 India time.

## Data volume and the disk budget
**Requests:** 15,372 + 17,080 = **32,452**. At 1 a second that is **9.0 hours**: one or two nights of the 16-hour window, and under 40,000 a day.

**Rows (bars per 30-day window: about 21 sessions):**

| Series | Bars per window | Requests | Rows |
|---|---|---|---|
| Indices, 5-minute | 21 × 75 = 1,575 | 15,372 | 24.2 M |
| Stocks, 15-minute | 21 × 25 = 525 | 17,080 | 9.0 M |
| **Total** | | | **about 33 M** (P14-00: 32 M) |

**Bytes:** about 80 B of data + 24 B header + about 40 B key index ≈ **145 B raw**, so **4.8 GB raw**. At a planning ratio of **5:1 (UNVERIFIED**, integer-heavy columns, range 3:1 to 10:1) that is **about 1.0 GB** (0.5 to 1.6).

**Cap:** `MaxStoredGb` 3 holds the worst-plausible ratio with room. A night's uncompressed growth is at most about 4.8 GB raw if the whole run lands in one night, so the guard may stop at the cap until the compression policy runs. That is resumable and expected; the ADR records it.

**Combined budget:** caps 40 (depth) + 12 (ticks) + 4 (chain, P14-02) + 3 (this) = 59 GB, plus the 20 GB floor = **79 GiB free needed with everything on**. Today there are about 76 GB free (session brief, UNVERIFIED here) before the 5-year underlying backfill (+3 to 5 GB) and CF-04 (2 to 4 GB). So: enlarge the VM disk to 160 GiB (recommended) or do not run all four together. The backfill alone needs 23 GiB free.

## Non-functional
Ingest is bounded by the throttle (about 1,000 bars a second at most); one window is parsed and inserted in ≤ 1 s at p95 (UNVERIFIED). Logs `options.backfill.window|gap|given_up|disk_stop|token_refused|done` carry key ids and counts only.

## Test plan
- **Unit:** planner key count and order, including the BANKNIFTY weekly cut at 2024-11-20 and windows at year and month ends. Parser: lengths, null side, paise, `suspect`, out-of-hours bars. Outcome mapping. Throttle and daily cap on a fake clock.
- **Application with fakes:** resume after a crash at key k (no re-fetch), given-up after 3, each gate, the window boundary at 08:30 and 16:30, disk stop.
- **Infrastructure:** allow-list cases; the client against a local fixture server.
- **Integration (Timescale):** migration, compression settings, idempotent insert, grants, `PinnedAllowList` with the one new line.
- **Architecture:** no reader, no decision-layer reference.

## Decisions (researched)
1. **Nights only, one a second.** Dhan's 5/s is shared with the chart backfills (CF-04, ISS-19) and the day's calls. One a second finishes in 9 hours, so speed buys nothing.
2. **Fetch log as the resume point**, not timestamps in the bar table. It records empty and failed keys too, so coverage is honest (P14-04 reads it).
3. **Rolling offsets as Dhan returns them.** The absolute strike per bar is stored and P14-08 rebuilds positions from it; a strike leaving the window is `unavailable` there (P14-00 criterion 2).
4. **Own cap, shared floor, three gates, fresh password**, as P14-02.

## Risks and honest limits
- "5 years back" slides: a month of delay loses the oldest month for good.
- Rolling series lose a held strike when spot moves more than the offset window, which biases toward short trades; P14-04 counts it.
- Index weekly history crosses the 2024-11-20 and 2025-09-01 rule changes; the epochs of P14-01 label them.
- No bid/ask: the spread comes from P14-02 only.

## Open questions (owner)
1. Approve the allow-list row and this spec.
2. Disk: accept the 3 GB cap (the combined budget is as in P14-02).
3. Stocks: the top 20 at ATM±3 monthly (recommended), fewer, or none (indices only: 15,372 requests, about 0.7 GB).
4. Run the probe (criterion 9) before the full run (recommended), or run at once.

## Done when
- [ ] All acceptance criteria have evidence (9 after the probe)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to check the rate, the window, resume idempotency and that nothing reads the bars)
- [ ] Migration `AddOptionBackfill` with `Down`; allow-list, `Describe()`, `PinnedAllowList` and tests in one commit
- [ ] ADR written; docs 20 §C, 16, 17 updated; fixtures listed as UNVERIFIED
