# BK-02 — Derived baskets engine (pure)

- **Status:** Draft (written 2026-10-25 under the owner's delegation; decisions below researched; owner's Approved needed before build)
- **Phase / roadmap row:** BK — system baskets, slice BK-02 of [BK-00](BK-00-system-baskets-plan.md)
- **Design refs:** docs/design/06-instruments-and-baskets.md §3.1 (rule-based baskets) · ISS-22 · research [system-baskets-nse](../research/system-baskets-nse.md) §2 · [basket-leverage-uses](../research/basket-leverage-uses.md) U1, §4 · ADR 0102 ("every exclusion named")
- **Needs:** nothing built beyond the Domain project and daily bars as values (the engine takes arrays). Can run in parallel with BK-01 (no shared files).
- **Size:** 4 agent-days (shared kit and ranking 1, the 14 definitions 2, property and boundary tests 0.5, ADR 0.5)
- **Safety:** no. Pure functions: no I/O, no clock, no network, no order. All thresholds are versioned parameters with defaults, never tuned on results.

## Goal
Fourteen derived "behaviour" baskets (most volatile, most liquid, momentum, trending and the rest) are computed by one pure engine from daily (and, for three, intraday) bars as of a date, using only data before the decision, with every excluded instrument named and every parameter versioned.

## Scope
**In:** `Domain/Baskets/Derived`: indicator kit (returns, true range, Wilder ATR, realised volatility, median traded value, efficiency ratio, ADX, beta, variance ratio, autocorrelation, correlation distance, clustering), the selection forms (top-k with buffer, threshold, quantile), the 14 definitions as data plus evaluators, `DerivedParams` (versioned, hashed), `DerivedResult(Members, Excluded, Scores)`; ADR.
**Out:** reading bars, writing versions, scheduling (BK-03); the screen (BK-04); fundamentals-based baskets (Quality, Value: data not stored); spread/depth baskets (no depth stored); optimising any threshold.

## Common rules
- **Inputs:** `InstrumentBars(Symbol, Daily[], Intraday[]?, Flags)` already adjusted for corporate actions by the caller; an instrument with an unresolved corporate-action suggestion in the window is excluded `corporate_action_unresolved`. Universe `U(D)` is passed in (BK-03 builds it from instruments with bars on D, including later-retired ones).
- **Point in time:** `as_of` = last bar date used; `effective_from` = next session. The engine reads only bars with date <= `as_of` (intraday: timestamp < the formation time). Appending a later bar never changes the result for an earlier `as_of` (property test).
- **Named exclusions:** `insufficient_history` (fewer than the basket's `min_bars`), `corporate_action_unresolved`, `bad_data` (non-finite, zero close), `not_in_universe_filter` (e.g. ASM, ban, series) — counted and listed with symbol, never silent.
- **Selection forms:** top-k, quantile, threshold. **Buffer:** incumbents keep their place while rank <= 1.5 x k (setting `BufferFactor` 1.5), up to k, then free slots are filled in rank order from non-members; a **minimum dwell** of 5 sessions for slow baskets blocks removal of a member added fewer than 5 sessions ago unless it fails the hard filter (named exclusion).
- **Ties:** equal scores are ordered by symbol ascending (deterministic, byte-identical output for the same input).
- **Constants:** 252 trading days a year (`sqrt(252)` = 15.8745).

## The 14 definitions (defaults are stored parameters)
| # | Basket | Formula | Window / min bars | Threshold / size | Cadence |
|---|---|---|---|---|---|
| 1 | Most volatile | `ATR%_14 = Wilder ATR_14 / C`; secondary report `rv_20 = stdev(ret,20) x sqrt(252)` | 20 d / 60 | top 50 | daily |
| 2 | Most moving | `mean(abs ret, 5)`; variant `mean((H-L)/C, 5)` | 5 d (+20 for z) / 25 | top 30 | daily |
| 3 | Most liquid | `ADTV_20 = median(C x V, 20)` in rupees | 20 d / 60 | top 100 (or ADTV >= Rs 20 crore) | daily |
| 4 | Gappers | `gap = O_D / C_{D-1} - 1`; in if `abs gap >= max(2 %, 1.5 x ATR%_14)` and first-5m volume >= its 20-day median | 14 d / 40 | all passing, cap 30 | formed 09:20 IST, frozen |
| 5 | High beta | `beta = cov(r_i, r_NIFTY) / var(r_NIFTY)`, 250 daily returns vs Nifty 50 | 250 d / 200 | top 50 with beta > 1 | weekly |
| 6 | Momentum 12-1 | `C_{t-21} / C_{t-252} - 1`; 6-1 variant `C_{t-21} / C_{t-126} - 1`; vol-adjusted variant divides by `rv_252` | 252 d / 252 (6-1: 126) | top 30 | monthly |
| 7 | Mean-reverting | `VR(5) = var(ret5) / (5 x var(ret1))` over 250 d; lag-1 autocorrelation; in if `VR < 0.85`, `rho1 < -0.05`, `ER_20 < 0.3`; rank by VR ascending | 250 d / 200 | bottom 50 | weekly |
| 8 | Trending | `ER_n = abs(C_t - C_{t-n}) / sum abs(C_i - C_{i-1})`, n 20; ADX_14 | 20-60 d / 60 | `ER_20 > 0.5` and `ADX_14 > 25`, top 50 by ER | daily |
| 9 | Near 52-week high | `C_{D-1} / max(H, 252) >= 0.97`, `ADTV >= floor`, `C > SMA_200` | 252 d / 252 | all passing, cap 50 | daily |
| 10 | Low volatility | `rv_252` ascending within ADTV top 200 | 252 d / 252 | bottom 30 | monthly |
| 11 | High relative volume | `RVOL = V_{D-1} / median(V, 20)`; intraday variant: cumulative volume to time-of-day vs the median of the same slot over 20 d | 20 d / 40 | `RVOL >= 2` and price >= Rs 50 | daily / intraday |
| 12 | Opening-range wide | `OR% = (max H - min L over 09:15-09:30) / O`; wide if `OR% >= 1.5 x median(OR%, 20 d)` and `OR% >= 0.8 %` | 20 d of 1m / 15 | top 30 | frozen 09:30 IST |
| 13 | Intraday-tradable (proxy) | `ADTV_20 >= Rs 10 crore`, zero-volume 1m bar fraction <= 5 %, median 1m `(H-L)/C >= 3 x tick / C`, not ASM/GSM, not in ban, series EQ | 20 d of 1m / 15 | all passing | daily |
| 14 | Return-correlation clusters | Spearman correlation of winsorised daily returns (1 % / 99 %), market-neutral (subtract Nifty return), distance `d = sqrt(0.5 x (1 - rho))`, average-linkage hierarchical clustering cut to k clusters, name by majority NSE industry; stability = adjusted Rand index vs the previous version | 120 d / 100 | k 10 to 15, min size 5 | monthly |
Each basket's description carries a one-line "what this basket is NOT" (for example "not NSE's Momentum 30; our own 12-1 rule").

## Acceptance criteria (hand-worked)
1. **Wilder ATR (#1):** given `ATR_13 = 2.0`, today's `TR = 3.4`, n = 14, then `ATR_14 = (2.0 x 13 + 3.4) / 14 = 29.4 / 14 = 2.1`; with `C = 100`, `ATR% = 2.1 %`. Given `max(H-L, abs(H-C_prev), abs(L-C_prev))` with H 104, L 100, C_prev 98, then TR = max(4, 6, 2) = 6.
2. **Realised volatility (#1, #10):** given a daily return stdev of 1 %, then `rv = 1 % x 15.8745 = 15.8745 %`.
3. **Median liquidity (#3):** given 19 sessions at Rs 10 crore and one block-day at Rs 200 crore, then the median is Rs 10 crore (the mean would be 19.5); with 20 values the median is the mean of the 10th and 11th sorted values.
4. **Gapper (#4):** given `C_{D-1} = 100`, `O_D = 103`, `ATR%_14 = 1.4 %`, then the bar is `max(2 %, 1.5 x 1.4 %) = max(2 %, 2.1 %) = 2.1 %`; gap 3 % passes, gap 2.05 % fails, gap 2.1 % passes (inclusive). An ex-dividend or split gap (adjusted away) does not appear.
5. **Beta (#5):** given Nifty returns [+1 %, -1 %, +2 %, -2 %] and stock returns [+1.5 %, -1.5 %, +3 %, -3 %], then `beta = cov/var = 1.5`; the stock is in only if it ranks in the top 50 with beta > 1; beta exactly 1.0 is out.
6. **Momentum (#6):** given `C_{t-252} = 100`, `C_{t-21} = 130`, `C_{t-126} = 110`, then 12-1 = +30 %, 6-1 = 130/110 - 1 = +18.1818 %; an instrument with 251 bars is `insufficient_history` for 12-1 (named), still eligible for the 6-1 variant.
7. **Variance ratio (#7):** given `var(ret5) = 0.0004` and `var(ret1) = 0.0001`, then `VR = 0.0004 / (5 x 0.0001) = 0.8 < 0.85` passes that leg; the basket needs all three legs (`rho1 = -0.04` fails).
8. **Efficiency ratio (#8):** given closes 100, 102, 99, 103, 100, 110 over 5 steps: net = 10, path = 2 + 3 + 4 + 3 + 10 = 22, `ER = 10 / 22 = 0.4545 <= 0.5`, so out; closes rising 100 to 110 in steps of 2 give ER = 1.0.
9. **52-week high (#9):** `C_{D-1} = 97.0`, 252-day high 100.0 gives 0.97, in (inclusive); 96.99 is out; a stock below its SMA_200 is out whatever the ratio.
10. **Relative volume (#11):** `V_{D-1} = 3,000,000`, median 1,200,000 gives `RVOL = 2.5`, in; a price of Rs 49 is out; `RVOL = 2.0` exactly is in.
11. **Opening range (#12):** O 1000, high 1010, low 995 over 09:15-09:30 gives `OR% = 15 / 1000 = 1.5 %`; with the 20-day median 0.8 %, `1.5 x 0.8 = 1.2 %`, so 1.5 % >= 1.2 % and >= 0.8 %: wide. With the median 1.2 %, threshold 1.8 %: not wide.
12. **Zero-volume bars (#13):** a 375-bar session allows at most `floor(0.05 x 375) = 18` zero-volume 1m bars; 18 passes, 19 fails.
13. **Correlation distance (#14):** `rho = 1` gives d = 0, `rho = 0.5` gives `sqrt(0.5 x 0.5) = 0.5`, `rho = 0` gives 0.7071, `rho = -1` gives 1; a cluster of fewer than 5 is merged into the nearest or listed `small_cluster`; the same inputs always give the same cluster labels (sorted by symbol).
14. **Buffer (k = 30, buffer 1.5 so rank <= 45 kept):** incumbents at ranks 1 to 27, 44, 46, 50 give 27 + the rank-44 one = 28 kept; ranks 46 and 50 drop; two free slots are filled by the best non-members (ranks 28 and 29), total 30. A new instrument at rank 31 does not displace an incumbent at rank 44.
15. **Minimum dwell:** a member added 3 sessions ago whose rank is 60 (outside the buffer) stays until 5 sessions have passed, unless it fails a hard filter (then it leaves and the reason is named).
16. **No look-ahead:** for any basket, appending a bar dated after `as_of` (or an intraday bar at or after the formation time) leaves the result byte-identical (property test over all 14).
17. **Determinism and hash:** the same inputs and parameters give identical members, scores and `params_hash`; changing any parameter changes the hash (hash of canonical JSON, sorted keys).
18. **Named exclusions:** an instrument with 59 bars for #1 (needs 60) appears in `Excluded` with `insufficient_history` and the counts add up: members + below-threshold + excluded = universe size.
19. **Parameter validity:** a threshold outside its range (buffer < 1, k < 1, window < 2) is rejected at construction with a message; none of the engine's types accepts an order-like argument.

## Contract
- **API / data / migration:** none (BK-03 stores). **UI:** none.
- **Domain (pure, `Domain/Baskets/Derived`):** `DerivedDefinition` (id 1..14, name, description, "what it is NOT", window, `min_bars`, cadence, form), `DerivedParams` (dictionary of named numbers + `Version` + `Hash`), `IDerivedEvaluator.Evaluate(universe, asOf, params, previous?) -> DerivedResult { Members[], Scores[(Symbol, Score, Rank)], Excluded[(Symbol, Reason)], Cluster?[] }`, `RankSelector` (top-k, buffer, dwell), `IndicatorKit` (static, no state).
- **Settings (defaults live in code, overridable later via BK-03 versioned params; not appsettings):** per-basket numbers above; `BufferFactor` 1.5; `MinDwellSessions` 5.
- **Events/jobs:** none.

## Non-functional
Evaluating the 14 baskets over 500 instruments x 260 daily bars under 2 s on a laptop core (allocation-free indicator loops; arrays in, arrays out); clustering 500 x 120 returns under 1.5 s. No logging in Domain.

## Test plan
- **Unit:** one test class per definition with the hand-worked numbers above; boundary pairs (inclusive and one tick below) for every threshold; empty and one-instrument universes; ties by symbol; non-finite input.
- **Property (FsCheck-style or generated):** no look-ahead (append future bar), permutation of input order gives the same output, scores are finite, members <= k, buffer never exceeds k, members + below + excluded = universe.
- **Golden:** a 30-instrument synthetic fixture with known ranks for #1, #3, #6 checked against a spreadsheet computed outside the code (values stored in the test).
- **Architecture:** `Domain/Baskets/Derived` has no I/O or clock reference; no order-named member.

## Decisions (researched)
1. **Top-k with a 1.5 x k buffer and a 5-session dwell for slow baskets.** Reason: cuts churn; modelled on NSE's own buffer rule for Alpha 50 (existing constituent kept while rank within 100 for a 50 index, methodology PDF read in the research). Rejected: plain top-k (flip-flop at the boundary); no buffer for slow baskets. Source: research §2 header, §1.2 Alpha 50 notes.
2. **ADTV uses the median, not the mean.** Reason: block deals and index-rebalance days spike turnover. Rejected: mean. Source: research §2 table row 3.
3. **Momentum skips the last 21 sessions and also offers 6-1.** Reason: the classic 12-1 skip of the last month; the stored history is about 2.1 years (ISS-19), so 12-1 is valid for only the last year of any replay and 6-1 covers shorter histories. Rejected: 12-month raw return (short-term reversal contamination); only 12-1 (idle for most of the history). Source: research §2 row 6 and notes; NSE Momentum 30 press release (secondary, snippet only; UNVERIFIED in full).
4. **Beta window 250 daily returns against Nifty 50.** Reason: NSE's High Beta 50 methodology uses 1-year trailing prices (VERIFIED in the Sept 2019 PDF). Which benchmark NSE's Alpha 50 uses is UNVERIFIED. Rejected: 60-day beta (noisier). Source: research §1.2.
5. **Mean-reverting needs three legs (VR, rho1, ER).** Reason: Hurst/VR estimates at n = 250 are noisy (about +/-0.07 around 0.5 for R/S), so two agreeing statistics plus a low efficiency ratio reduce false members. Rejected: Hurst alone. Source: research §2 notes 7.
6. **Correlation clusters are market-neutral, average linkage, k 10 to 15, with a stability score.** Reason: raw correlation is dominated by the market factor; clusters are unstable across windows, so the adjusted Rand index vs the last version is stored. Rejected: raw correlation; fixed k of 5. Source: research §2 notes 14; the HRP distance `sqrt(0.5(1-rho))` (Lopez de Prado, via the Wikipedia page listed in the leverage note; the original paper not opened, UNVERIFIED).
7. **Intraday baskets (#4, #12, #11 variant) are formed once and frozen** at 09:20 / 09:30 IST using only bars before that time. Reason: the basket is unknowable earlier; a simulation must form it from bars up to the formation time. Rejected: end-of-day membership applied backwards. Source: research §2 notes 4, 12.
8. **No Quality, Value or spread baskets.** Reason: fundamentals, depth and free-float market cap are not stored. Rejected: faking with proxies beyond #13 (named as a proxy). Source: research §2 "Data not stored".
9. **Parameters are defaults stored as versioned data, never tuned on a strategy's P&L.** Source: research §2 "Overfitting", leverage note §4.8.

## Risks and honest limits
- In-sample circularity: ranking on the window a strategy is later tested on (volatility, trend, mean-reversion) overstates it; BK-04 shows the warning, BK-06+ must use `as_of` versions.
- Adjustments are restated retroactively, so a recomputed old date differs slightly from what was knowable then (ranks are scale-free; flagged `recomputed`).
- Short history: 12-1 and 52-week rules have only about 1.1 valid years of replay.
- Universe is the owner's tracked set unless Nifty 500 is tracked (BK-00 question 3).
- #13 is a proxy (no bid/ask); #12 depends on 1m coverage.

## Open questions
Accept the default thresholds as pre-registered (BK-00 question 6)? Which of the 14 to enable first (suggest #3, #1, #10, #6 for the eight)?

## UNVERIFIED
NSE Momentum 30 rule in full (press release snippet only); the exact benchmark of NSE Alpha 50; ISS-19 history length (as stated in the research, roadmap row not opened); corporate-action "unresolved suggestion" API (doc 05 §4.2, not opened); the 20 crore and 10 crore floors are judgement numbers.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] ADR (derived baskets are pure, versioned, point-in-time); doc 06 §3.1 updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **D vs as_of (look-ahead):** `as_of` is the last complete daily bar used (call it C_asof). A basket "used on D" takes `as_of = D-1`. #9 and #11 read `C_{as_of}` and `V_{as_of}` (the text `C_{D-1}` means the same). #4 is the only definition reading `O_D`, valid only intraday at formation time (09:20 IST, bars before it); it is not computable from daily bars with `as_of = D-1` alone, and the engine refuses it without an intraday input.
- **Input type:** `InstrumentBars` gains `Benchmark[]` (Nifty 50 daily closes, needed by #5 and #14) and `Flags` as point-in-time per date (`AsmGsm`, `InBan`, `Series`), so #13 and the filters use the flag known at `as_of`, not today's.
- **AC14 amended:** when more than k incumbents have rank <= 1.5k, keep the best k incumbents by rank (the rest drop).
- **Dwell:** the 5-session dwell never blocks removal of an instrument that leaves the universe or is retired.
- **#14 market-neutral:** each stock's daily return minus the Nifty 50 return of the same day (residual = r_i - r_N, no regression), then Spearman on winsorised residuals.
- **Added worked numbers:** #2: returns +2 %, -1 %, +3 %, -2 %, +1 % give `mean(abs) = 9/5 = 1.8 %`. #10: `rv_252` 1.0 % daily is 15.8745 % annual vs 1.5 % daily 23.8118 %, so the first ranks lower (in the low-vol basket first). #8 ADX: with +DI 30 and -DI 10, `DX = 100 x 20/40 = 50`; ADX_14 is the Wilder mean of 14 DX values (all 50 gives 50 > 25, in); +DI 20 and -DI 15 gives DX 14.29 (out). #14: residuals perfectly correlated (rho 1, d 0) vs rho 0.5 (d 0.5): with three stocks A,B (rho 0.9: d = sqrt(0.05) = 0.2236) and C (rho 0.1 to both: d = sqrt(0.45) = 0.6708), average linkage joins A,B at 0.2236 first; C joins them at 0.6708.
- **Budget:** clustering 500x120 under 1.5 s is checked by a benchmark test (BenchmarkDotNet or a timed test on a CI-stable bound of 3 s); not an assertion in unit tests.
- **Size:** 5 agent-days (the #14 clustering and ADX add a day); #14 can be split to a BK-02b if the owner prefers (then 4 d and 1 d).
