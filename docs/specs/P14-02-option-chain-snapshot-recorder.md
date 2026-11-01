# P14-02 — Option chain snapshot recorder: our own bid/ask, OI, IV and Greeks history, stored compressed, no trading use

- **Status:** Approved (owner, 2026-11-01: pre-approved, no review wanted). Open questions resolved with the recommended/safer answers: allow-list rows `POST api.dhan.co/v2/optionchain`, `/v2/optionchain/expirylist`, `/v2/charts/rollingoption` accepted (each enters the pin in the commit of the slice that uses it, ADR 0133); underlyings = 4 indices plus the top 30 F&O shares; lot-size and rule-date seeds stay confirmed=false until checked against circulars; stock chains for 30 stocks (not all 213); backfill = indices plus top 20 stocks at ATM±3 only after the probe; the backfill probe runs first; the chain cap 4 GB and backfill cap 3 GB with the shared 20 GB free floor; the VM disk enlargement (to 160 GiB) is the owner's act and every recorder stays off until its gates are turned on, so the build proceeds within the caps; Services-page switches and the Data-quality card are a later frontend slice; quality thresholds and the 6.5 % parity rate as drafted.
- **Phase / roadmap row:** P14 — slice P14-02 of [P14-00](P14-00-options-and-stat-arb-plan.md) (D1 "archive the chain ourselves from now", D13 start early).
- **Design refs:** docs/design/20 §C, 17 §1-2 (VM disk 120 GB), 16 · ADR 0133 (allow-list rule), 0097 (pinned list), 0135 and 0173 (recorder pattern: three gates, fresh-password switch, disk guard, no reader), 0091/0128 (fresh password), 0083 (no order). New ADR: **next free number at build time**.
- **Needs:** P14-01 (`options.contracts`, `Options:Underlyings`), P2-10 (Data account credentials). **Needed by:** P14-04 (coverage, parity), P14-07 (half-spread after ≥ 60 sessions), P14-10 (credit structures).
- **Size:** 3.5 agent-days (estimate; P14-00 said 3, +0.5 for the fresh-password switch pattern that came after it).
- **Safety:** **yes** — two new broker endpoints on the allow-list, the Data account's token on a new HTTPS client, continuous disk growth. Off by default three times. No order, no order-capable scope, no reader of the data on any decision path.

## Goal
From the day the owner switches it on, every 5 minutes of the session the chain of the chosen index expiries (and once a day the chosen stock chains) is stored strike by strike with bid, ask, last, OI, volume, IV and Greeks. The data is compressed, append-only and bounded by its own cap and the shared free floor. A failed request is a counted gap, never zeros.

## Verified facts
**Dhan (read 2026-11-01, <https://dhanhq.co/docs/v2/option-chain/>):**
- `POST https://api.dhan.co/v2/optionchain`, body `{"UnderlyingScrip": 13, "UnderlyingSeg": "IDX_I", "Expiry": "2024-10-31"}`.
- `POST https://api.dhan.co/v2/optionchain/expirylist`, body without `Expiry`, answer `data: ["YYYY-MM-DD", ...]`. The page's curl example sends `{}`; the build follows the field table.
- Headers `access-token`, `client-id`, JSON. No query on either.
- Answer: `data.last_price` (underlying) and `data.oc`, keyed by strike strings such as `"25650.000000"`, each with `ce`/`pe` holding `average_price`, `greeks{delta, theta, gamma, vega}`, `implied_volatility`, `last_price`, `oi`, `previous_close_price`, `previous_oi`, `previous_volume`, `security_id`, `top_ask_price`, `top_ask_quantity`, `top_bid_price`, `top_bid_quantity`, `volume`.
- Rate: "one unique request every 3 seconds". Error codes are not listed on that page.

**Annexure (<https://dhanhq.co/docs/v2/annexure/>):** `DH-901` invalid or expired token, `DH-902` not subscribed to Data APIs, `DH-904` rate limit, `DH-905` bad input, `DH-907` no data, `DH-908`/`DH-909` server and network. Data API limits are 5 a second and 100,000 a day (<https://dhanhq.co/docs/v2/>).

**Plan:** Dhan's product post of 2025-10-15 (<https://madefortrade.in/t/introducing-options-data-and-full-market-depth-on-dhanhq-data-apis/56937>) puts options data inside the ₹499 Data API subscription.

**Code (read 2026-11-01):**
- `BrokerEndpoints.Allowed` lists exact-path rows; `Describe()` prints them. `Domain/Planning/PlanPhaseCheck.PinnedAllowList` pins 11 lines, and neither file contains an option path.
- `DhanQuoteClient` is the pattern for a Data-account POST with `client-id`.
- `DepthDiskGuard.Check` (Application/Depth) is pure. The recorders' switch, service and heartbeat pattern is in ADR 0135/0173.

**UNVERIFIED until the pilot:**
- Whether "unique request" means per (underlying, expiry) or per user. **This spec assumes per user: one request every 3 s in total.**
- Units of `implied_volatility` (percent assumed) and of theta (per day assumed).
- Whether a strike with no quote sends zeros or omits fields.
- The answer after 15:30 (bid/ask probably zero).
- The `UnderlyingSeg` value for a stock (`NSE_EQ` assumed).
- Bank Nifty's `IDX_I` id (only Nifty's `13` is in the docs; the ids come from `market.instruments`).

## Scope
**In:**
- Allow-list rows and `PinnedAllowList` lines, in one commit, with their tests.
- `IOptionChainClient` + `DhanOptionChainClient`: own throttle of at least `Options:Chain:SpacingMs` (3000) between any two requests of the process; never logs URL, token or body.
- Pure `OptionChainParser` (strike string to paise, zero to null with a flag) and `ChainSlotPlanner` (which requests in which slot).
- `OptionChainRecorder` and supervisor; service `option-chain-recorder` in `xd-worker`. The build confirms the host that composes Data-account clients; otherwise `xd-feed`.
- `IOptionChainStore` (insert, size read, **no row read**).
- Migration `AddOptionChainRecording`.
- The owner's switch: `options.chain_recorder_switch`, `GET`/`PUT /api/options/chain-recorder`.
- Settings `Options:Chain:*`; fixtures from the documented shape (UNVERIFIED); tests; ADR.

**Out:** any reader of snapshots (P14-04 reads coverage only; research reads start in P14-07/08); the expired-options endpoint (P14-03); computing IV or Greeks; stocks every 5 minutes; BSE/MCX; a screen (the Services row shows state; a Services-page switch is an open question); deleting data.

## Acceptance criteria
1. **Allow-list (ADR 0133/0097).** Given the allow-list, then exactly these are allowed:
   - `POST https://api.dhan.co/v2/optionchain`
   - `POST https://api.dhan.co/v2/optionchain/expirylist`

   Each takes no query, not even `?`. They are added as two `Uri` constants in `BrokerEndpoints.Allowed`, with the class summary updated. `PlanPhaseCheck.PinnedAllowList` gains `POST api.dhan.co/v2/optionchain` and `POST api.dhan.co/v2/optionchain/expirylist` in ordinal order, in the same commit.

   These are refused, as new `BrokerEndpointsTests` cases: GET/PUT/DELETE on both, a query, a trailing slash, `/v2/optionchain/x`, `/v2/OptionChain`, another host or port, and `http`. `POST /v2/charts/rollingoption` and `/v2/charts/historical` stay refused. Every existing allow and refuse test passes unchanged.
2. **Parser.** Given fixture answers (marked UNVERIFIED), the parser:
   - turns each strike key into integer paise (half away from zero), and prices into paise;
   - turns a zero or missing `top_bid_price`/`top_ask_price` into null with flags `no_bid` (bit0) and `no_ask` (bit1), and a zero `last_price` into null with `no_trade` (bit2);
   - flags NaN or negative IV or Greeks as null with `bad_greeks` (bit3), and bid > ask as `crossed` (bit4).

   It keeps only strikes within `Options:Chain:StrikesEachSide` (30 for indices, 15 for stocks) of the strike nearest `last_price`, and never throws. A malformed body is one counted gap (`bad_body`).
3. **Slots.**
   - On trading days the recorder plans one slot every `Options:Chain:IntervalMinutes` (5) from 09:20 to 15:30 India time, which gives 75 slots.
   - Each slot asks each index expiry in `Options:Chain:IndexExpiries`. The default is NIFTY: the next 3 weekly expiries and the next monthly; BANKNIFTY: the next 2 monthly. Expiries come from the expiry list, asked once per underlying at 09:15.
   - The stock pass asks `Options:Chain:StockExpiries` (2) expiries of each listed stock once a day. It runs between index slots, starts 14:30 and ends by 15:25; any request not made by then is a gap `window_end`.
   - All requests share the one throttle: at least 3,000 ms apart.
   - A slot unfinished when the next starts is closed and its missing requests counted `slot_overrun`. There is no catch-up.
4. **Rows.** One row per (slot `ts`, underlying, expiry, strike, side), holding:
   - `rx_at`, the receive time;
   - `underlying_px`, `last_px`, `avg_px`, `prev_close_px`;
   - `bid_px`, `bid_qty`, `ask_px`, `ask_qty`;
   - `oi`, `prev_oi`, `volume`, `prev_volume`;
   - `iv`, `delta`, `gamma`, `theta`, `vega`;
   - `security_id` and `flags`.

   Inserts are idempotent: a re-run of a slot inserts nothing (`ON CONFLICT DO NOTHING` on the primary key).
5. **Request log.** Every planned request writes one `options.chain_requests` row: slot, underlying, expiry, `outcome`, HTTP status, Dhan code, rows, latency ms. The outcome is one of `ok`, `rate_limited`, `timeout`, `server_error`, `no_data`, `bad_body`, `token_refused`, `not_subscribed`, `slot_overrun`, `window_end`, `disk_stop`. A failed request writes **no** snapshot row; nothing writes a zero for a missing value.
6. **Failures.**
   - `DH-904` or HTTP 429 → `rate_limited`, and the spacing doubles for the rest of the slot. Three slots in a row with a rate limit pause the recorder for 15 minutes and raise the service-failure notice.
   - `DH-901`, 807 or 809 → `token_refused`; the recorder sends nothing until the token changes.
   - `DH-902` or 806 → `not_subscribed`, which ends the day.
   - `DH-907` → `no_data`.
   - Timeout (`Options:Chain:TimeoutSeconds` 10) → `timeout`; no retry inside the slot.

   One `options.chain_sessions` row is written per day when the day is over; an unclosed past day is closed `crashed` (ADR 0135 rule).
7. **Three gates, all default off.**
   - The deployment flag `Options:Chain:Enabled` (`XD_OPTIONS_CHAIN_ENABLED`, default false).
   - The owner's switch, the newest row of `options.chain_recorder_switch`; no row = OFF.
   - The service `option-chain-recorder`, registered paused, retries 0, no catch-up, notify on failure; "Run now" is refused.

   With any gate closed or unreadable, nothing reads credentials, builds a request or sends.

   `PUT /api/options/chain-recorder {enabled, reason, password}` follows P7-16 criterion 6 exactly:
   - a fresh password on every change, ON and OFF;
   - `StepUpCommand` with `Elevate: false`;
   - the actor from the session;
   - account event `OptionChainRecorderSwitched`, category `service`;
   - 401/423, and 400 `options.reason_invalid`.

   `GET` returns the switch, its history and the not-running reasons.
8. **Disk guard**, before each slot and every 5 minutes, with `DepthDiskGuard.Check`:
   - the stored size of `options.chain_snapshots` above `Options:Chain:MaxStoredGb` (4) ends the run `disk_budget`;
   - free space below `Options:MinFreeGb` (20, the same value as `Depth:MinFreeGb`) ends it `disk_free`;
   - a warning at 80 %;
   - it stays stopped until the condition is cleared and the owner resumes;
   - **the app never deletes data.**
9. **Migration.**
   - `options.chain_snapshots` is a 7-day-chunk hypertable compressed after 7 days, segment by `underlying_id, expiry`, order by `ts, strike_px, side`, with no retention policy.
   - `xd_app` may SELECT and INSERT but not UPDATE, DELETE or TRUNCATE on the four new tables (grants test).
   - `Down` drops them.
10. **No trading use** (architecture test):
    - `IOptionChainStore` has no row-read method.
    - No route returns snapshot rows. `GET /api/options/chain` of P14-00 belongs to P14-16.
    - Decision layers (Domain trading code, `Simulation`, `Strategies*`, `Indicators`, `Application/Planning|Learning|Portfolio|Execution`), the live feed and `XDrishti.Execution` reference no chain type.
    - The no-order tripwire is green.
    - With the recorder failing, every other service behaves exactly as with it off.
11. Logs `options.chain.request|gap|slot|disk_warning|disk_stop|token_refused|paused` carry counts, ids and outcome codes only, never URL, token, client id or body.
12. **Pilot (owner-run, five sessions, recorded in the ADR):**
    - Measure requests per slot, gaps by outcome, rows per day, bytes per row and the compression ratio.
    - Check the rate-limit reading: one run at 3 s spacing across two underlyings shows no `DH-904`.
    - Check the IV and theta units against one hand-computed Black-Scholes value (P14-05 fixture).
    - Check that bid/ask are non-null for ≥ 90 % of index strikes within ±10 of the money.

## Contract
- **API:** `GET`/`PUT /api/options/chain-recorder` (as `/api/ticks/recorder`; `ApiJsonContext`, `scripts/gen-api.sh`; problem codes `options.reason_invalid`, `auth.invalid_credentials`, `auth.locked`).
- **Data (`AddOptionChainRecording`, insert-only):**
  - `options.chain_snapshots`: `ts timestamptz, underlying_id uuid, expiry date, strike_px int4, side char(2), rx_at timestamptz, security_id int8, underlying_px int4, last_px int4 null, avg_px int4 null, prev_close_px int4 null, bid_px int4 null, bid_qty int4 null, ask_px int4 null, ask_qty int4 null, oi int8, prev_oi int8, volume int8, prev_volume int8, iv real null, delta real null, gamma real null, theta real null, vega real null, flags int2 check 0..31`. Primary key `(ts, underlying_id, expiry, strike_px, side)`.
  - `options.chain_requests`: the criterion 5 columns.
  - `options.chain_sessions`: `session_date pk, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows, stored_bytes, note`.
  - `options.chain_recorder_switch`: the `depth_recorder_switch` columns.
- **Settings (`Options:Chain:*`, ranges, `ValidateOnStart`, doc 16):**

  | Setting | Default | Range |
  |---|---|---|
  | `Enabled` | false | |
  | `IntervalMinutes` | 5 | 5-60 |
  | `SpacingMs` | 3000 | 3000-60000; never below 3000 |
  | `StrikesEachSide` | 30 | 5-50 |
  | `StockStrikesEachSide` | 15 | 3-30 |
  | `IndexExpiries` | the defaults above | |
  | `StockExpiries` | 2 | 1-3 |
  | `TimeoutSeconds` | 10 | |
  | `MaxStoredGb` | 4 | 1-100 |

  `Options:MinFreeGb` is 20 (5-500).

## Data volume and the disk budget
**Requests:**
- Index requests per slot: NIFTY 4 + BANKNIFTY 2 = 6, which take 18 s of each 5-minute slot.
- Index requests per day: 6 × 75 = 450.
- Stock pass: 30 stocks × 2 expiries = 60 requests, 3 minutes. All 213 F&O stocks would be 426 requests (21.3 min), which still fits 14:30 to 15:25.
- Total: about 515 a day, far under 100,000.

**Rows:**
- Indices: 6 × 75 × (61 strikes × 2 sides) = 54,900 a day.
- Stocks: 60 × 62 = 3,720 a day.
- Total: 58,620 a day; × 248 sessions = **14.5 M rows a year**.

**Bytes:** about 120 B of data + 24 B tuple header + about 40 B primary-key index ≈ **185 B raw**, so **2.7 GB a year raw**. Real-valued Greeks compress worse than integers, so the planning ratio is **5:1, UNVERIFIED** (range 3:1 to 8:1): **0.54 GB a year** (0.34 to 0.9). With 213 stocks: +4.8 GB raw, +1 GB compressed a year.

**Cap:** `MaxStoredGb` 4 is about 7 years at the planning ratio and 4.4 years at 3:1. One uncompressed week is 410 k rows, about 76 MB.

**Combined budget (VM disk 120 GB; per the session brief of 2026-11-01, 76 GB free and the database 5.8 GB, UNVERIFIED here):**

| Writer | Cap |
|---|---|
| depth | 40 GB |
| ticks | 12 GB |
| chain (this slice) | 4 GB |
| expired-options backfill (P14-03) | 3 GB |
| **Caps total** | **59 GB** |
| Shared free floor | 20 GB |
| **Free space needed with everything on** | **79 GiB** |

Not under any cap: the 5-year underlying backfill (+3 to 5 GB, ISS-19) and CF-04 (2 to 4 GB, up to 9). That leaves about 67 to 71 GB, so **all on is 8 to 12 GB short**.

Options:
- Enlarge the VM disk to 160 GiB (`podman machine set --disk-size`, machine stopped). Recommended.
- Or run fewer recorders at once:

  | Combination | Free needed |
  |---|---|
  | depth + options | 67 GiB |
  | ticks + options | 39 GiB |
  | options alone | 27 GiB |

## Non-functional
At most 1 request per 3 s (0.33/s, far below Dhan's 5/s); a request and its insert take ≤ 2 s at p95 (UNVERIFIED); memory under 50 MB extra. Tokens and client id are never logged.

## Test plan
- **Unit:** the parser covers the fixture shape, strike keys with decimals, zeros to null, NaN, crossed and the window cut. The slot planner covers 75 slots, expiries rolling over after an expiry day, the stock pass between slots and `window_end`. The throttle is tested on a fake clock (never below 3,000 ms across both endpoints). Outcome mapping covers each Dhan code.
- **Application with fakes:** supervisor states (each gate, paused, token, not subscribed, rate-limit pause, disk stop, crash recovery).
- **Infrastructure:** allow-list allow and refuse cases; the client against a local HTTP fixture server (headers present, no query, body shape).
- **Integration (Timescale):** migration up/down, hypertable and compression settings, idempotent slot re-insert, grants, constraints, `PinnedAllowList` with exactly the two new lines.
- **Architecture:** criteria 10 and 11.

## Decisions (researched)
1. **Two exact allow-list rows, no wildcard.** `/v2/optionchain` and `/v2/optionchain/expirylist` are separate exact rows, per ADR 0133 §2. Rejected: a prefix rule for `/v2/optionchain*`.
2. **One request every 3 s for the whole process.** The strictest reading of an ambiguous limit; it costs nothing, because 6 index requests fit 18 s of a 5-minute slot. The pilot may relax it in a later ADR, never this spec.
3. **Stocks in-session at 14:30, not at 15:45.** P14-00 put the stock job at 15:45. After the close, bid/ask are probably empty (UNVERIFIED), and the snapshot exists only for the spread, so the pass runs while the book is live.
4. **Store as sent, labelled; no recompute.** This follows P14-00 D3. Zeros become null with a flag, so a missing quote can never become a price.
5. **Own cap, shared floor** (ADR 0173 decision 6). The floor protects the machine; each cap shows which writer stopped.
6. **Three gates and a fresh password** (ADR 0135 decision 8). The `options.chain_recorder.enabled` setting of P14-00 is replaced by this pattern.

## Risks and honest limits
- A sleeping Mac or an expired daily token loses those slots for good (counted).
- The 5-minute grid misses intra-slot spreads.
- Vendor IV and Greeks are a black box (model and rate unknown).
- No backup, and this is the only copy: `xd-backup` must exclude `options.chain_snapshots` data like the depth and tick tables.

## Open questions (owner)
1. Approve the two allow-list rows and this spec.
2. Disk: accept the 4 GB cap and the shared 20 GB floor. Choose between enlarging the VM disk to 160 GiB (recommended) and not running depth, ticks and options together.
3. Stock chains for the P14-01 list of 30 (recommended) or all 213 F&O stocks (+1 GB a year).
4. Add the switch to the Services page Recorders section in a later small slice (recommended), or API only.

## Done when
- [ ] All acceptance criteria have evidence (12 after the pilot)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any path from chain data to a decision or order, any request faster than 3 s, and any token in a log)
- [ ] Migration `AddOptionChainRecording` with `Down`; allow-list, `Describe()`, `PinnedAllowList` and tests in one commit
- [ ] ADR written; docs 20 §C, 16, 17 (disk precondition, backups) updated; fixtures listed as UNVERIFIED in `docs/research/dhan-samples/README.md`
