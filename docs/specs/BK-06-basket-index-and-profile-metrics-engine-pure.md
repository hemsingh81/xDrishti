# BK-06 — Basket index and profile metrics engine (pure)

- **Status:** Draft (written 2026-10-25; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 follow-up — basket performance insight, kernel slice of [BK-07](BK-07-basket-profile-endpoints-and-screen.md); research note `docs/research/basket-leverage-uses.md` §2 (m1b to m16)
- **Design refs:** docs/design/06-instruments-and-baskets.md §3, §4 · docs/design/13-reports-and-ui.md §1c · ISS-22 (survivorship) · ADR 0102 (basket benchmarks) · ADR 0105 · P8-07 · P8-09
- **Needs:** P8-07 built (`BuyAndHold`, `DailyClose`, `InstrumentSeries`, `Metric`, `MetricState` in `Domain/Reporting`, VERIFIED present). Nothing from BK-01..BK-05 is needed to compile or test: the engine takes membership and bars as arguments. BK-01..BK-05 (system baskets with dated membership) only decide how much real history it will be fed.
- **Size:** 4 agent-days (index and risk kernels 1.5, breadth/dispersion/correlation 1, contribution/capacity/turnover/regime 1, tests with hand-worked tables and properties 0.5)
- **Safety:** no. Pure functions, no I/O, no clock, no setting, no write, nothing leaves the app.

## Goal
Given a basket's membership by date and its members' daily bars, one pure engine returns the basket's own performance profile (metrics m1b to m16 of the research note) with the honest state of every figure, so the endpoint and screen of BK-07, the correlation and breadth consumers of BK-08, and the relative-strength features of BK-09 all read one set of formulas.

## Scope
**In:** pure `Domain/Reporting/BasketAnalytics` (one static class per metric family, each under 250 lines, one public type per file): `EqualWeightIndex` (m1b), `RiskMetrics` (m3 to m7), `RelativeStrength` (m8), `BasketBreadth` (m9), `Dispersion` (m10), `PairwiseCorrelation` (m11, m12), `MemberTurnover` (m13), `Capacity` (m14), `Contribution` (m15), `RegimeSplit` (m16); input and output records; options record; tests.
**Out:** any read, port, endpoint, DTO, cache or screen (BK-07); storing breadth or correlation per day (BK-08); clustering (BK-08); the rule or index baskets themselves (BK-01..05); strategy-family metrics m17 to m23 (P8-09 has m17 to m19 and the "not additive" note of m22; the hold-out m23 and the Jaccard of m22 are in BK-07); dividends and total return; capitalisation weights; any rupee profit figure.

## Overlap with built work (stated, not duplicated)
| Metric | Where it lives | This slice |
|---|---|---|
| m1 buy-and-hold price return | P8-07 `BuyAndHold` (built) | **Reused as is**: the profile calls it; nothing re-implemented. |
| m2 excess vs Nifty 50 | P8-07 returns both numbers; its Decision 5 forbids a subtracted "excess" figure beside the trading result | Computed here only as a difference of two **passive** series over the same dates (see Decision 6). |
| m17 to m19, m22 note | P8-09 (built: comparison table, `what-works?lens=basket,strategy`, pivots, non-additive note) | Not touched. |
| pair correlation (Pearson) | `CorrelationTable.FromReturns`, `ReturnSeries.FromCloses` in `Domain/Selection/SmartRules` (P7-06, VERIFIED present; the research note's "no correlation code" is out of date) | `PairwiseCorrelation` takes already computed return series and reuses the same Pearson rule, so the gate, the fill and the profile cannot disagree on a number. |
| breadth of the owner's tracked set | P3-01e (Draft; no `BreadthCalculator` found in the repo: UNVERIFIED that it is unbuilt, absent from `backend/src`) | `BasketBreadth` is the one pure kernel; P3-01e's tracked-set breadth is the same function called with the tracked set as members. |

## Definitions
Notation: window [t0, t1]; members M(d) = membership as of d (a function the caller supplies; a constant function when only one snapshot exists); daily close c_i(d); r_i(d) = c_i(d)/c_i(d-1) - 1 on India trading days; benchmark r_m(d) of the chosen index. Weights for the day-d return are fixed from M(d-1) (decided at the previous close; no look-ahead). Sample statistics use n-1. Annualisation factor 252. Risk-free rate ignored and said so.

| # | Metric | Formula (exact) | Minimum for a number |
|---|---|---|---|
| m1b | Daily-rebalanced equal-weight index | I(d) = I(d-1) × (1 + mean of r_i(d) over N_d), rebased I(t0) = 100; N_d = members in M(d-1) with closes on d-1 and d, not excluded for that day | N_d ≥ 3 on at least 90 % of days and ≥ 3 on the last day, else `insufficient` (n = days with N_d ≥ 3) |
| m3 | Annualised volatility, windows 20/60/252 | sqrt(252) × stdev(r_B(d)) over the last w daily index returns | w + 1 index points, else `insufficient` for that window |
| m4 | Maximum drawdown and duration | DD(d) = I(d) / max(I(s), s ≤ d) - 1; MDD = min DD; duration = longest run of days from a peak to the next new peak, and the days since the last peak if open | ≥ 20 index points |
| m5 | Beta to the index, 60 and 252 days, and the 60-day rolling series | Cov(r_B, r_m) / Var(r_m) over the last w common days | w common days; Var(r_m) > 0 |
| m6 | Alpha, tracking error, information ratio | alpha = 252 × (mean r_B - beta × mean r_m); TE = sqrt(252) × stdev(a), a = r_B - r_m; IR = 252 × mean(a) / TE | same window as m5; TE > 0 |
| m7 | Up and down capture | UpCap = mean(r_B on r_m > 0) / mean(r_m on r_m > 0); DownCap likewise on r_m < 0 | ≥ 10 up days and ≥ 10 down days, else `insufficient` for that side |
| m8 | Relative strength line and return ranks | RS(d) = I(d) / I_m(d) rebased 100; RS return over 21/63/126/252 sessions = RS(t1)/RS(t1 - w) - 1; rank among the baskets passed in (1 = strongest), ties share the lower rank | w + 1 points; a rank needs ≥ 2 baskets with a value |
| m9 | Breadth inside the basket | b_k = share of voters with close > SMA_k, k = 20/50/200; A/D share = adv / (adv + dec); new-high share = members at the 252-session high / voters; a voter has ≥ k bars up to d | voters ≥ `MinVoters` (default 5), else `insufficient` (n, min) |
| m10 | Dispersion | stdev of r_i(d) across members at day d; the 20-day mean of it; the IQR | voters ≥ 5 |
| m11 | Average pairwise correlation | rho_bar = mean of Pearson rho_ij over all pairs, 60 sessions of returns, pair needs ≥ 40 common sessions | ≥ 5 members with ≥ 10 pairs, else `insufficient`; > 200 members: the 200 most liquid by the ADV the caller passes, said in the result (`sampled`) |
| m12 | Effective number of bets | N_eff = 1 + (N - 1) × (1 - rho_bar), clamped to [1, N] | as m11 |
| m13 | Member turnover | (adds + removes inside the window) / average N | ≥ 2 membership dates, else `unavailable` ("membership history starts on <date>") |
| m14 | Capacity | ADV20 = median of the last 20 daily traded values (close × volume); basket median and 10th percentile (linear interpolation); per-member capacity = `CapacityPct` (default 1 %) × ADV20; basket capacity = sum; share ASM/GSM, share F&O | ≥ 20 sessions per member counted; a member with fewer is listed, not guessed |
| m15 | Contribution | share_i = (R_i / N_inc) / R_B with R_i the buy-and-hold return of member i and R_B their mean; top and bottom 5; share of return from the top 3 | `BuyAndHold` figure is a value and R_B ≠ 0; if R_B is within 1e-9 of 0 then `not_evaluable` ("return is zero; shares undefined") |
| m16 | Regime-conditioned performance | days grouped by the regime code of the previous session (1 to 9, or the 3 × 3 trend × vol); per group: n days, mean daily return, annualised vol, hit rate = share of days r_B > r_m | each group ≥ 30 days else `insufficient` for that group (n, min), never hidden |

## Acceptance criteria
1. **m1b by hand.** On day 1 members A, B, C return +2 %, +4 %, -1 %; day 2 -1 %, 0 %, +2 %; day 3 +3 %, -2 %, +1 %; then the daily index returns are +1.6667 %, +0.3333 %, +0.6667 % and I = 100, 101.6667, 102.0056, 102.6856 (each within 1e-4). Permuting the members changes nothing.
2. **Day-level exclusion.** Variant of criterion 1: C's day-2 return is -50 % (close 200 then 100); then C is excluded for d2 only (|r| above the discontinuity threshold 0.25, the P8-07 setting value passed in), N_2 = 2, the d2 return is mean(-1 %, 0 %) = -0.5 %, and the result lists "1 instrument-day left out: C, 2026-.., discontinuity". It is never a repaired price.
3. **Volatility by hand.** Given the m1b returns of criterion 1 and a test window of 3 (the options allow windows below 20 for tests only), then sample stdev = 0.0069390 and annualised vol = 0.11015 (within 1e-4). With the production windows and 30 index points the 60-day and 252-day figures are `insufficient` with n = 30 and min = 61 / 253, and the 20-day figure is a value.
4. **Drawdown by hand.** Given an index 100, 110, 99, 104.5 then DD = 0, 0, -10 %, -5 %; MDD = -10 %; the drawdown is open, 2 days since the peak. Given 100, 110, 99, 121 then the peak resets and duration is 2 days, closed.
5. **Beta, alpha, capture by hand.** Given the m1b returns of criterion 1 and index returns +1.0 %, -0.5 %, +1.0 % then beta = 0.5556, alpha_ann = 1.5400, tracking error = 0.10017, information ratio = 9.784, UpCap = 1.1667, DownCap = `insufficient` (1 down day, min 10) (each within 1e-3). A constant index (Var = 0) gives `not_evaluable` for beta, alpha and the ratios, never infinity.
6. **Contribution ties to P8-07.** Given first and last closes A 100 to 110, B 50 to 45, C 200 to 250 (the P8-07 table) then R_B = +8.3333 % and the shares are A +0.4000, B -0.4000, C +1.0000, summing to 1.0000; the share from the top 3 is 100 %. Given returns summing to zero then `not_evaluable`.
7. **Correlation by hand.** Given three members with pair correlations 0.6, 0.3 and 0.0 then rho_bar = 0.3 and N_eff = 2.4 (test passes MinMembers 3; N_eff = 1 + (N-1)(1 - rho_bar) is an equal-correlation heuristic, not a measured count of bets). Given 4 members then `insufficient` (n 4, min 5). Given two perfectly correlated series the pair rho is 1.0000 and equals `CorrelationTable.FromReturns` on the same returns (a test pins the two together).
8. **Breadth by hand.** Given 10 members of which 8 have 20 bars and 5 of those close above their SMA20, then b20 = 0.625 with voters 8, excluded 2 named "short history"; 30 advancers, 20 decliners, 5 unchanged gives A/D share 0.6; 2 of 8 at a 252-session high gives 0.25 (the high needs 252 bars: a voter without them is excluded from that figure only and counted). Given 4 voters the figure is `insufficient` (n 4, min 5).
9. **Capacity by hand.** Given ADV20 of 50, 20 and 10 crore then the median is 20 crore, the 10th percentile is 12 crore (position 0.2 between 10 and 20), per-member capacities 0.5, 0.2, 0.1 crore at 1 % and the basket capacity 0.8 crore.
10. **Turnover by hand.** Given 2 adds and 1 remove in the window and an average N of 10 then turnover = 0.30; a basket with one membership date gives `unavailable` with the date from which history exists.
11. **Regime.** Given 45 days in regime 5 and 12 days in regime 7 then regime 5 is a value row with n 45 and regime 7 is `insufficient` (n 12, min 30), shown, not dropped; a day whose previous session has no regime code is counted in `unknown` and in no group.
12. **No look-ahead (property).** For random series, truncating every series after day k, or changing any value after k, leaves every figure computed for days up to k byte-identical. Appending a member that starts after k changes nothing up to k.
13. **Honest states (property).** Over random inputs including empty, one-day, all-equal and NaN closes, every output figure is a number only when its state is `value`; no NaN or infinity ever escapes; a non-positive or non-finite close is dropped with a reason (as `ReturnSeries`) and counted.
14. **Determinism and speed.** The same inputs give identical results (a hash test); the full profile for 50 members × 300 days runs in under 150 ms on the CI machine and 200 members × 300 days (sampled correlation) under 1 s.

## Contract
- **Domain (pure):** `BasketAnalytics.Profile(BasketProfileInput, BasketProfileOptions) → BasketProfile`. `BasketProfileInput(Members: IReadOnlyList<MemberSeries>, MembershipAt: Func<DateOnly, IReadOnlySet<string>>, Benchmark: IReadOnlyList<DailyClose>, Regimes: IReadOnlyDictionary<DateOnly, int?>, Window)`; `MemberSeries(Symbol, Closes: IReadOnlyList<DailyClose>, Volumes, IsFno, IsAsmGsm)`. Result types reuse `Metric` / `MetricState` (P8-05) and the `InstrumentSeries` shape (P8-07). Options: `MinVoters` 5, `MinDaysPerRegime` 30, `MinMembersForReturn` 3, `DiscontinuityThreshold` 0.25, `CapacityPct` 0.01, `CorrelationWindow` 60, `MinCommonSessions` 40, `MaxCorrelationMembers` 200. Defaults come from the caller's settings (BK-07 binds them); the domain holds none as a hidden constant.
- **API / data / UI / events:** none.
- **Architecture:** `Domain/Reporting` stays pure; no order-named member; no I/O types; no new project.

## Non-functional
Allocation-light (arrays, no LINQ in the inner loops); 50 × 300 under 150 ms. No logging in the domain.

## Test plan
- **Unit (hand-worked tables):** criteria 1 to 11 with the numbers above, each asserted to the stated tolerance, plus the m1b example extended by one day.
- **Properties (FsCheck-style, as the repo's other pure kernels):** truncation (12), honest states (13), permutation, scaling every close by a constant leaves returns unchanged, a member with all-equal closes contributes zero return.
- **Cross-checks:** `PairwiseCorrelation` equals `CorrelationTable.FromReturns`; the contribution shares tie to `BuyAndHold`.
- **Architecture:** `Domain/Reporting` has no dependency outside Domain; no new Infrastructure type.

## Decisions (researched)
1. **Daily-rebalanced equal weight for the index, buy-and-hold kept for the headline.** Question: which series do volatility, drawdown and beta run on? Chosen: m1b, because buy-and-hold weights drift, so its daily returns mix a changing portfolio and its drawdown depends on which member ran away; the rebalanced series has one meaning at each date. Rejected: using the P8-07 buy-and-hold curve for risk metrics. Source: research note §2 (m1b row); P8-07 Decision 1 (the headline stays the hold figure a person can check by hand).
2. **Weights from the previous close's membership.** Chosen: the day-d return uses M(d-1) so the basket is what one could have held, not what turned out to be in it. Reason: look-ahead is the commonest basket error (research note §4 "No look-ahead membership"). Honest limit: a basket with one membership snapshot is constant in time and still survivorship biased (ISS-22), said on every result.
3. **Plain Pearson average correlation, no shrinkage in v1.** Chosen: the mean of pairwise Pearson over 60 sessions, with a 40-session pair minimum, reusing the P7-06 kernel. Reason: an average over many pairs has low variance even when each pair is noisy, and one correlation rule across the gate, the fill and the profile is worth more than a better estimator used in one place. Rejected: Ledoit-Wolf shrinkage toward constant correlation (the research note names it; the primary paper was not opened, UNVERIFIED, and it changes the number the plan already uses). Source: P7-06 `CorrelationTable`; research note §5 ("Ledoit-Wolf not opened").
4. **Effective number of bets by the simple formula.** Chosen: N_eff = 1 + (N-1)(1 - rho_bar). Rejected: the exponential of the PCA eigenvalue entropy (needs an eigen decomposition for a number a person cannot check by hand; revisit if BK-08 clusters show the simple formula misleading).
5. **Plain beta, no Dimson lag in v1.** Chosen: one-factor OLS beta. Reason: an equal-weight basket of liquid names has little non-synchronous trading; the lag-1 sum beta needs a two-regressor fit. Named limit: for low-liquidity baskets (capacity tier low) beta is biased downward; the screen prints the median ADV beside it. Source: Dimson beta is the research note's suggestion, secondary and UNVERIFIED.
6. **"Excess vs the index" is allowed here and not in the trading comparison.** Question: P8-07 Decision 5 forbids a third unexplained figure. Chosen: in the profile both sides are passive price returns over the same dates, so the difference is like for like and is shown beside both inputs, worded "difference to the index", never "alpha" or "outperformance" (alpha only as the regression intercept m6). Rejected: omitting it (the owner asked for performance insight; the subtraction in the browser would be unreviewed).
7. **Day-level discontinuity exclusion for the index, instrument-level for the hold figure.** Chosen: a one-day move above the P8-07 threshold removes that instrument-day from the daily index and counts it; P8-07 removes the whole instrument from the hold. The two answer different questions; the result prints both counts so they can be reconciled. Source: ADR 0102 (exclude, count, name; never repair).
8. **Minimum samples are stored settings with these defaults.** 30 days per regime row, 5 voters, 3 members for a return, 10 up and 10 down days for capture. Reason: the same 30 as the rest of P8; fewer would let a handful of days rank a regime.
9. **Annualisation 252, risk-free ignored.** Reason: no risk-free series is stored; stating the omission beats inventing a rate. Alpha is therefore "versus the index, rate ignored".

## Risks and honest limits
- Membership look-ahead and survivorship persist for any basket whose history starts at our first snapshot (index baskets) or whose members are today's tracked set; every result carries `membershipBasis` (`dated` or `single_snapshot`) and the first date with dated history.
- Daily bars are as stored; corporate-action restatement (data module) may differ from what was knowable; the threshold exclusion catches large unadjusted jumps only.
- Two to three years of history (ISS-19) means a 252-day window has few independent observations; those rows are `insufficient` rather than approximate.
- Regime rows describe a handful of market episodes; the screen must not read them as causes.
- `ADV20` from the daily bars' volume as stored; auction and block volume are included (the median resists spikes, not a structural block-heavy name).

## UNVERIFIED
- Whether `market.context_daily` carries the regime code for every day of the window (P3-01c/d are built per the docs; the loaded range was not queried): m16 degrades to `unavailable`.
- That the gate and fill really use `CorrelationTable.FromReturns` unchanged in production (read in code, not run).
- Primary papers for Ledoit-Wolf, Dimson (named in the research note, not opened).
- BK-01..BK-05 membership contract (assumed: `GET /api/baskets/{id}/members?asOf=`, `market.basket_versions` with `params_hash`, and the dated rows); re-check at Approval.

## Alignment with BK-01..BK-05 (read 2026-10-25)
BK-00..BK-05 now exist (Draft). What this slice relies on, as they state it: `kind` in `manual|portfolio|index|list|derived` and `system` (BK-01); `GET /api/baskets/{id}/members?asOf=` with `membersKnownSince` (BK-01); derived baskets stored as immutable dated versions in `market.basket_versions` (`as_of`, `effective_from`, `params_hash`, member rows with `valid_from`/`valid_to`) written by a job that is OFF by default (BK-03); list baskets for F&O, ASM/GSM and MTF (BK-05). Wherever this spec says `rule_version` it means the basket's version id plus `params_hash` (null for manual and index baskets, whose history starts at the first fetch). Index baskets are point in time only from the first fetch; the screens say so. Re-check names and ids at Approval; nothing here edits BK-00..BK-05.

## Open questions
1. Default benchmark: Nifty 50 (the owner's portfolio benchmark setting, as P8-07) or Nifty 500 for broad baskets? Recommended: keep the one app-wide benchmark.
2. Is "difference to the index" acceptable wording (Decision 6)?

## Done when
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors (told to look for a number shown for a non-value state and for look-ahead)
- [ ] No contract change, no migration; ADR (next free number at build time) on the daily-rebalanced index and the previous-close membership rule
- [ ] `scripts/build-docs.sh`

## Review fixes 2026-10-25
- Criterion 5: information ratio is 9.784 and tracking error 0.10017 (the active-return mean is 0.0038889, not 0.0037778).
- Criteria 1 and 2 reworded (day-by-day returns; criterion 2 is a variant of 1). Criterion 7 notes the test passes `MinMembers` 3 and that N_eff is an equal-correlation heuristic.
- `rule_version` and `IBasketQueries` replaced in the body by `params_hash`, `market.basket_versions`, `members?asOf` (BK-01..05).
- Size unchanged: 4 agent-days.
