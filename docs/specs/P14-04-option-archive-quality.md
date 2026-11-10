# P14-04 — Option archive quality: coverage, parity, IV and Greek sanity, OI/volume jumps and rolling-series stitching, with honest `unavailable`

- **Status:** Built 2026-11-02 (ADR 0178). Approved (owner, 2026-11-01: pre-approved, no review wanted). Open questions resolved with the recommended/safer answers: allow-list rows `POST api.dhan.co/v2/optionchain`, `/v2/optionchain/expirylist`, `/v2/charts/rollingoption` accepted (each enters the pin in the commit of the slice that uses it, ADR 0133); underlyings = 4 indices plus the top 30 F&O shares; lot-size and rule-date seeds stay confirmed=false until checked against circulars; stock chains for 30 stocks (not all 213); backfill = indices plus top 20 stocks at ATM±3 only after the probe; the backfill probe runs first; the chain cap 4 GB and backfill cap 3 GB with the shared 20 GB free floor; the VM disk enlargement (to 160 GiB) is the owner's act and every recorder stays off until its gates are turned on, so the build proceeds within the caps; Services-page switches and the Data-quality card are a later frontend slice; quality thresholds and the 6.5 % parity rate as drafted.
- **Phase / roadmap row:** P14 — slice P14-04 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Archive quality ... the `unavailable` states").
- **Design refs:** docs/design/05 §10, §10b (data quality window), 20 §C · ADR 0017 (data quality checks), 0135/0173 (no reader on a decision path). New ADR: **next free number at build time** (only if the usable-session rule below is accepted as a design rule).
- **Needs:** P14-01 (contracts, strike step, epochs); P14-02 and/or P14-03. Each source is checked only when it has rows; the other is `not_recorded`. **Needed by:** P14-07 (the count of usable chain sessions, ≥ 60, for the spread model), P14-08 (rolling keys usable per session), P14-16 (coverage report).
- **Size:** 2 agent-days (estimate).
- **Safety:** no — reads our own tables only, no broker call, no setting that sends. It is the first reader of the option archive and returns aggregates only.

## Goal
After each recorded session and each backfill night, every underlying, expiry and session of the archive gets a stored verdict per check: `pass`, `warn`, `fail` or `unavailable`, with the reason. Later slices can then count usable sessions instead of trusting raw rows, and the owner can see where the archive has holes.

## Verified facts (read 2026-11-01)
**Code:** `market.quality_runs` / `market.quality_results` (P1-14a, ADR 0017) are keyed per instrument with a foreign key to `market.instruments` and checks as `jsonb`, so the option archive (per underlying, expiry and session) needs its own pair of tables in the same style. The data quality service and screen exist for bars.

**Inputs:** the tables of P14-01 to P14-03 (specs, not yet built). NSE index and stock options are European-style (UNVERIFIED for stocks: NSE moved stock options to European settlement in 2011, secondary knowledge), so put-call parity applies without an early-exercise term.

## Scope
**In:**
- Pure checks in `Domain/Options/Quality` (one class per check, no I/O).
- `OptionArchiveQualityJob`: service `options-quality`, daily at 16:15 India time on trading days and after each backfill run that wrote rows. On by default because it only reads and writes its own result rows.
- Tables `options.quality_runs`, `options.quality_results`; migration `AddOptionArchiveQuality`.
- `GET /api/options/coverage`.
- Settings `Options:Quality:*`; tests.

**Out:** any screen (P14-16 reports; a Data quality card is an open question); correcting, deleting or re-fetching data (the backfill re-fetch is an owner act in a later slice); computing IV (P14-05); cost or spread models (P14-07).

## Acceptance criteria
1. **Status words.** Every result is one of `pass`, `warn`, `fail` or `unavailable`, with `reason` from a closed list: `not_recorded`, `gap`, `no_two_sided_quote`, `no_underlying_bar`, `too_few_points`, `out_of_era`. **`unavailable` never counts as `pass`** in any count or roll-up.

   A session's status is the worst of its checks, with `unavailable` ranked below `fail` and above `warn`.
2. **Coverage (chain).** Per session and underlying, `ok` requests ÷ planned requests from `options.chain_requests`:
   - ≥ 95 % `pass`, ≥ 80 % `warn`, below that `fail`.
   - A day with no `options.chain_sessions` row is `unavailable/not_recorded`, never `fail`.

   Gaps are listed by outcome.
3. **Coverage (rolling).** Per fetch key and session, stored bars ÷ expected bars (75 at 5 minutes, 25 at 15 minutes):
   - ≥ 95 % `pass`, ≥ 80 % `warn`, below that `fail`.
   - A key whose fetch log says `empty`/`no_data` inside a known out-of-era period (P14-01 epochs, e.g. BANKNIFTY weekly after 2024-11-20) is `unavailable/out_of_era`; outside such a period it is `fail`.
4. **Put-call parity (chain).** For each snapshot and expiry:
   1. Take the strikes where both sides have bid and ask. Fewer than 5 such strikes is `unavailable/too_few_points`.
   2. Estimate the forward from the strike nearest the money: `F = K0 + (Cmid − Pmid) / DF`, where `DF = exp(−r·T)`, `r` = `Options:Quality:Rate` (0.065, UNVERIFIED, the owner may set it) and `T` = calendar days to expiry ÷ 365.
   3. At each strike compute `res = |(Cmid − Pmid) − DF·(F − K)|` and compare it with `tol = (Cask − Cbid + Pask − Pbid)/2 + 1 tick`.
   4. Share of strikes with `res ≤ tol`: ≥ 90 % `pass`, ≥ 70 % `warn`, else `fail`.

   Hand fixture: K0 = 100, Cmid 5.0, Pmid 4.0, DF 1 → F = 101. At K = 102, a call mid of 4.1 and a put mid of 5.1 give `res = |−1.0 − (−1.0)| = 0` (pass). A put mid of 6.0 gives `res = 0.9`, against `tol = 0.2` for spreads of 0.1 each plus a tick of 0.05 → beyond.
5. **IV and Greeks sanity (chain).** Per snapshot and expiry, among non-null values:
   - `0 < iv < Options:Quality:MaxIv` (300, percent assumed per P14-02).
   - Call delta in [0, 1] and put delta in [−1, 0].
   - Call and put delta both **non-increasing in strike**, with tolerance 0.02.
   - Gamma and vega ≥ 0.
   - Across expiries at the same `ts`, the at-the-money total variance `iv²·T` is non-decreasing, with tolerance 5 %.

   Results: any violation in more than 2 % of points is `warn`; more than 10 % is `fail`. Rows with flag `bad_greeks` count as violations. Fewer than 5 points is `unavailable`.
6. **OI and volume jumps (chain).** Between consecutive snapshots of one contract in one session:
   - Volume must not fall (cumulative): any fall is `fail`.
   - An OI change above `Options:Quality:OiJumpPct` (50 %) **and** above 10 lots (lot from P14-01; lot `unavailable` → percent only) is counted. More than 1 % of pairs is `warn`.

   Real block trades can trip this, so it is never `fail`.
7. **Rolling stitching (backfill).** Per key and session:
   - Missing bars inside the session are counted.
   - A strike move of more than 2 strike steps between consecutive bars (step from P14-01 contracts of that underlying and date; unknown → `unavailable`) is counted as a `strike_jump`.
   - `spot` is compared with the underlying's `market.bars_1m` close at the bar's end minute; a difference above 0.5 % is counted. No 1-minute bar there → `unavailable/no_underlying_bar` for that comparison only.
   - Thresholds: any strike jump `warn`, more than 1 % of bars `fail`; spot mismatch above 1 % of bars `warn`.
8. **Usable sessions.** A chain session for an underlying is **usable** when coverage is `pass` and parity is not `fail`. A rolling key-session is usable when coverage is `pass` and stitching is not `fail`.

   `GET /api/options/coverage?underlying&from&to` returns, per session:
   - each check's status, value, threshold and reason;
   - the usable flags;
   - the cumulative usable chain sessions per underlying (the number P14-07 compares with 60).

   It returns **no price, IV or Greek**. It answers 400 `options.invalid_query` for a range over 400 days or an unknown underlying, and an empty list (not an error) when nothing is recorded.
9. **Idempotent runs.** A re-run for the same session replaces nothing: it writes a new `quality_runs` row, and readers take the newest run per session (insert-only for `xd_app`, grants test). One session of NIFTY's chain (about 23 k rows) is checked in ≤ 2 s on the test container (UNVERIFIED budget).
10. **No trading use** (architecture test): the quality code and the endpoint are the only readers of `options.chain_snapshots`, `options.rolling_bars` and `options.fetch_log` in this slice; the decision layers and `XDrishti.Execution` reference neither them nor the result tables; the no-order tripwire is green.

## Contract
- **API:** `GET /api/options/coverage` → `{underlying, sessions: [{date, source, checks: [{code, status, value?, threshold?, reason?}], usable}], usableChainSessions}`; problem `options.invalid_query`; `ApiJsonContext`, `scripts/gen-api.sh`.
- **Data (`AddOptionArchiveQuality`, insert-only):**
  - `options.quality_runs(id identity, trigger in (daily, backfill, manual), started_at, ended_at, sessions, results, note)`.
  - `options.quality_results(run_id, session_date, underlying_id, source in (chain, rolling), expiry date null, fetch_key varchar(80) null, check_code varchar(40), status in (pass, warn, fail, unavailable), reason varchar(30) null, value numeric null, threshold numeric null, n int4, detail jsonb)`, indexed `(underlying_id, session_date DESC)`.
  - Size: about 34 underlyings × 6 checks × 248 sessions plus rolling keys, which is under 1 M rows a year and **under 0.3 GB a year**. No cap is needed; the free floor is not touched.
- **Settings (`Options:Quality:*`):**

  | Setting | Default | Range |
  |---|---|---|
  | `Rate` | 0.065 | 0-0.2 |
  | `MaxIv` | 300 | |
  | `OiJumpPct` | 50 | |
  | Thresholds | as above, each with a range | |

  Service `options-quality`: 16:15 India time on trading days, retries 1, catch-up 1 day.

## Non-functional
It reads by `(underlying_id, ts)` ranges only (the hypertables' segment order). Logs `options.quality.run|session|done` carry counts only.

## Test plan
- **Unit:** each check on hand fixtures: the parity example above, a delta-order violation, a calendar-variance violation, a volume fall, an OI jump below 10 lots, a strike jump of 3 steps, out-of-era empty windows, fewer than 5 points. The roll-up order (`fail` > `unavailable` > `warn` > `pass`) and that `unavailable` is never counted as `pass`.
- **Integration (Timescale):** a synthetic recorded day and a synthetic backfill window round-trip to results; re-run idempotency; grants; the endpoint (each state, the 400, empty).
- **Architecture:** criterion 10.

## Decisions (researched)
1. **Own result tables, the bar quality pattern.** `market.quality_results` is per instrument with a foreign key; option checks are per underlying, expiry and session.
2. **Forward from the money, not from the spot.** It avoids needing dividends and rates exactly; the residual tests the chain's internal consistency, which is what the spread model needs.
3. **Delta order instead of an "IV monotone" check.** IV is not monotone across strikes (the smile), so P14-00's "IV monotone" is read as delta order plus calendar total variance, both of which no-arbitrage requires.
4. **OI jumps warn and never fail**, because real block trades exist; a volume fall fails, because cumulative volume cannot fall.
5. **`usable` defined here**, so P14-07's "≥ 60 sessions" counts quality sessions, not rows.

## Risks and honest limits
- The rate `r` and the European-style assumption for stocks are UNVERIFIED. A wrong `r` biases parity slightly at long expiries, and weekly expiries are short.
- Vendor Greeks may come from another model, so a delta-order violation can be the vendor's, not ours. Either way it is reported, not repaired.
- Out-of-era rules depend on the P14-01 epoch values (confirmed or not).

## Open questions (owner)
1. The pass/warn/fail thresholds above as a starting set (recommended), or other values.
2. A read-only "Options archive" card on the Data quality screen in this phase (0.5 d), or wait for P14-16 (recommended).
3. The rate `r` for parity: 6.5 % fixed (recommended), or a value you set per year.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to check that `unavailable` never counts as pass and no price leaves the endpoint)
- [ ] Migration `AddOptionArchiveQuality` with `Down`; contract regenerated
- [ ] Doc 05 §10 and doc 20 §C updated; an ADR only if the usable-session rule is accepted as a design rule
