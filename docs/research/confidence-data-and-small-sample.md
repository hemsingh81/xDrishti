# Confidence with little data: more honest history, and small-sample statistics

Research, 2026-10-27. No code changed. Repo facts were read from code and specs (paths cited). Web facts carry a URL; anything not opened on the source page is marked UNVERIFIED.

## 0. The problem in numbers

- ~2.1 years of 1-minute history (214 CSVs, 2024-04..2026-04; ISS-19, `docs/design/18-roadmap.md` line 274). Doc 16 assumes `train_years: 3`; walk-forward folds start at 9 months and test 3 months (`SelectionOptions`).
- The cell key is `(strategy, tf, side, style, exit, regime)` with `min_trades` 30 (`QualificationGates.Default`, P6-01). P6-00 already admits "most cells will stay below 30" and a regime split multiplies by up to 9.
- Almost nothing in the engine is wrong; the evidence is thin. Two levers: more history that is honest (section 1), and statistics that say "unknown" correctly and still extract signal from small n (section 2).

---

## 1. More honest history for NSE

### 1.1 Sources

| Source | What it gives | Depth / limits | Cost | Terms | Helps which gap |
|---|---|---|---|---|---|
| **Dhan daily** `POST /v2/charts/historical` | Daily OHLCV (+OI for F&O) | "back upto the date of its inception"; `toDate` non-inclusive; no per-request span stated | free with Data API | Dhan API terms (not opened) | Daily-resolution signals (252-day), regime context for index and VIX, cross-check of CSV days |
| **Dhan intraday** `POST /v2/charts/intraday` | 1/5/15/25/60 min | "last 5 years", **90 days per request**, "active instruments" only | free with Data API | same | Moving 2.1y toward ~5y for intraday strategies (ISS-19) |
| **NSE bhavcopy / archives** | Daily EOD per security; legacy `sec_bhavdata_full` carries delivery quantity and % (UNVERIFIED for the current UDiFF layout); UDiFF "CM-UDiFF Common Bhavcopy Final" is the format since 2024-07-06 per a user post (UNVERIFIED) | Daily back many years (exact start UNVERIFIED; a forum says pre-2024-07 files sit under older "CM - Bhavcopy" reports) | free to download | NSE Data policy restricts **redistribution** and derived-index use; personal internal research is the low-risk zone but the bhavcopy-specific terms were NOT opened (UNVERIFIED) | Daily history of 15+ years incl. delisted names that were listed on the day; delivery % as a feature |
| **NSE corporate actions** (the corporate-announcements page, CSV) | splits, bonus, dividends, ex-dates | years | free | as above, UNVERIFIED | Adjust pre-CSV history correctly; today actions are only "detected and confirmed by owner" (doc 05 4.2) |
| **niftyindices.com** reports/archives | Index OHLC, constituent lists, inclusion/exclusion history | Forum: inclusion/exclusion file stops ~2020-07; archives start 2008 or 2013 (conflicting) | free | UNVERIFIED | Index context (Nifty 50/500) and an approximate point-in-time membership, not a full one |
| **Vendors** (Bloomberg, FactSet, Quandl NSE, similar) | point-in-time constituents, delisted prices | long | paid, not priced here | licence | The only clean survivorship fix; probably not worth it for a single owner |

URLs: Dhan docs https://dhanhq.co/docs/v2/historical-data/ (opened); Dhan FAQ on the daily endpoint and on 5-year/active-only intraday https://dhan.co/support/platforms/dhanhq-api/why-am-i-not-receiving-the-latest-day-s-data-from-the-daily-historical-data-api/ (search snippet only); NSE forms and UDiFF https://www.nseindia.com/static/resources/forms-formats-members (search snippet only); NSE Data policy https://www.nseindia.com/market-data/nse-data-policy and https://nsearchives.nseindia.com/web/sites/default/files/inline-files/NSE_Data_Sharing%26Usage_Policy.pdf (search snippet only, not opened); index membership threads https://tradingqna.com/t/historical-constituents-of-various-nse-indices/176515 (forum, unconfirmed). The nseindia.com terms page and `all-reports` page timed out when fetched, so bhavcopy start date and the delivery columns remain UNVERIFIED.

### 1.2 What each source can and cannot fix

1. **Intraday ceiling is ~5 years, whatever we do.** Dhan's 5 years is the hard cap for the 1-minute strategies. From today (2026-10) that reaches ~2021-10; the CSVs start 2024-04..12, so the backfill adds ~2.5 to 3 years before the CSV start. That roughly doubles to 2.3x the history (2.1y becomes ~5y). It does not give 15 years.
2. **Bhavcopy gives daily only.** Intraday strategies cannot be replayed on daily bars. So bhavcopy helps: regime labels over 10+ years (a daily function: `ContextDay`/`Regime` in `XDrishti.Indicators`), 252-day signals, delivery %, cross-checking Dhan daily, and building a delisted-inclusive universe. It does not help intraday cell n.
3. **Dhan active-only is survivorship bias baked in.** Delisted/merged names are not served (the FAQ says "active instruments"). ISS-22 already says members as of today. A 5-year backfill widens the survivorship window (more years in which losers vanished). Mitigation: bhavcopy lists securities that traded each day, so a daily-universe-as-of-date is buildable from it; intraday for the dead ones is simply not obtainable, and the Lab must say so per run.
4. **Adjustment mismatch (design hazard, repo-verified).** Doc 05 says bars are stored raw, the owner's CSVs are already adjusted, and `csvAdjustedThrough` guards against double adjustment (4.2). A Dhan backfill before the CSV start is raw: it must be adjusted with the corporate-action table, and the seam between adjusted CSV and raw Dhan bars must be tested (factor at the join). Whether Dhan's own historical candles are split-adjusted is UNVERIFIED (the P0-13 trial lists it as an open question, `docs/research/p0-13-dhan-trial.md`).
5. **Licensing.** Personal analysis of downloaded files is the safe reading; redistribution (the docs site, any shared export) and index derivation need NSE Data approval. The Reports export (P8-20) must not carry raw third-party bars. UNVERIFIED until the policy PDF is read in full.

### 1.3 Backfill sizing, ~500 instruments

| Item | Arithmetic | Result |
|---|---|---|
| Daily, Dhan, to listing | 500 requests (span not limited per doc; chunk by 5 years to be safe: ~2,000) at 4 req/s (`Data:Dhan:RequestsPerSecond`) | 2 to 8 minutes; ~0.5 to 3 M rows total |
| 1-min, 5y, Dhan | 5y ~ 1,250 sessions x 375 bars = 470 k rows/instrument; 90-day chunks: ~21 requests/instrument | 500 x 21 = 10,500 requests ~ 45 min at 4 req/s; 235 M rows total |
| Gap vs what we hold (2.1y) | ~2.9y missing before CSV start: ~270 k rows/instrument | ~135 M new rows, ~14 x 10^3 requests/500 = about 7,500 requests ~ 31 min |
| Storage | doc 05 sizing: 200 instr x 8y ~ 150 M rows ~ 5-10 GB compressed | +135 M rows ~ +4 to 9 GB compressed (estimate) |
| Calendar | one-off, resumable per (instrument, chunk) | one night; the existing gap-backfill (doc 05 6.1) is the pattern |

**What ISS-19's 5-year backfill needs:** (a) allow-list: `/v2/charts/historical` is already described as allow-listed for historical candles (P0-13 line 20), but the roadmap Q7 asks whether it may be added for index/VIX, so confirm what is actually allowed; (b) chunker (90-day windows, newest first, idempotent upsert, `IIntradayBarSource` already exists); (c) corporate-action adjustment at the seam (4 above); (d) a trading-calendar check for 2021-2024 (holidays, Muhurat, special Saturdays) so the quality gate does not flag legitimate gaps; (e) universe-as-of-date for the pre-2024 years; (f) a labelled hold-out (Q7: the last months are not tuned on); (g) the printed limit changes from "2.1 years" to the real depth per instrument.

### 1.4 Recommended order (cheapest honest first)

1. Daily bhavcopy + corporate actions for 10+ years -> regime history and 252-day signals. No Dhan limit, lowest risk.
2. Dhan 1-min backfill to ~5y for the active universe, with the seam test.
3. Index membership history from niftyindices inclusion/exclusion files where they exist; accept an approximate universe and say so.

---

## 2. Statistical methods for small n

Repo audit (read, not assumed):

- `BetaShrinkage` + `CellStatsCalculator` (`Domain/Learning`): win rate shrunk to strategy then system (strength 20), confidence = 5% quantile, recency 0.5^(d/180). **No shrinkage of expectancy (R) itself; no effective n.** Credible interval "treats trades as independent" (P6-01 limits).
- `MultipleTesting.Factor = 1 + 0.25*log2(N)` (documented "not a statistical claim"): a heuristic multiplier on the gates.
- `MonteCarlo` is an **iid bootstrap** in exit order, spec says clustered losses are understated; `SignFlipBaseline` is a sign-flip null (lenient by its own note).
- Walk-forward: `ExitSelector` has expanding train, 3-month tests, 5-session **embargo** and **purge** (`Embargoed`/`Purged` counts) and `TripleBarrierLabel.ResolvedEpochSeconds`; ML side has `WalkForwardValidator`, `HoldOut`, `CusumDetector`, `IsotonicCalibrator`. Not combinatorial.
- `QualificationGates` fixed thresholds: 30 OOS trades, 0.15 R, PF 1.2, 60% positive folds.
- grep for sharpe/deflat/sprt/autocorr/effective n/power in `Domain` and `Application`: **no hits** (only embargo/purge).

### 2.1 Hierarchical / empirical-Bayes pooling (parent-child)

- **Have:** win rate only (Beta-Binomial, two levels, fixed strength 20, the parent includes the cell's own trades).
- **Gap:** expectancy and profit factor are not shrunk; strength is a constant, not estimated; the parent includes the child (stated limit); no regime-level parent (regime cell -> same cell with regime `*`).
- **Change:** (a) a pure `NormalShrinkage` for mean R: posterior = parent + B (cellMean - parent), B = tau^2 / (tau^2 + sigma^2/n_eff); (b) estimate tau^2 and the Beta strength by method of moments across sibling cells (empirical Bayes) instead of constant 20; (c) leave-one-out parent (exclude the cell's own trades); (d) chain regime cell -> `*` cell -> strategy -> system. Surface "shrunk mean R" and a lower credible bound next to win rate; the evidence label uses the shrunk bound, not raw n.
- **Example:** cell 10 trades, mean +0.40 R, sigma 1.2 R (variance 1.44), parent mean +0.10 R, between-cell variance tau^2 0.04: B = 0.04 / (0.04 + 1.44/10) = 0.04/0.184 = 0.217; shrunk = 0.10 + 0.217 x 0.30 = **0.165 R**. With 40 trades B = 0.04/(0.04+0.036) = 0.526 -> 0.258 R. Win-rate analogue already built: 6 wins of 10, parent 0.5, strength 20: (10+6)/(20+10) = 0.533.
- **Size:** 3 agent-days (domain 1.5, wiring and snapshot columns with a migration 1, tests/docs 0.5).
- Refs: Efron and Morris (1975/77) on shrinkage; hierarchical Bayes standard texts (Gelman, BDA3).

### 2.2 Cross-sectional pooling across instruments

- **Have:** cells already pool all instruments of a strategy/tf/side (the n is trades, not instruments). Instrument-level cells do not exist.
- **Gap:** pooled trades from the same day across correlated stocks are treated as independent (the main source of overstated n, see 2.3). No instrument-or-sector random effect.
- **Change:** a cluster key on each trade (session date, and sector or basket) feeding 2.3's effective n; optional instrument-level cell shrunk toward the cell pool (same function as 2.1, level = instrument).
- **Example:** 50 stocks x 1 trade on one gap-down day, average pairwise correlation 0.4: n_eff = 50 / (1 + 49 x 0.4) = 50/20.6 = **2.4**, not 50.
- **Size:** 2 agent-days after 2.3 (cluster key must be stored on `SimTrade` or derivable: entry date and basket exist).

### 2.3 Effective sample size (overlap, autocorrelation, clustering)

- **Have:** none. n = count of trades everywhere; `min_trades` 30, gate 30 OOS.
- **Change:** a pure `EffectiveSampleSize` (Kish design effect for clusters: n_eff = n / (1 + (m - 1) rho) with rho from a one-way ANOVA on the date or basket cluster; for time order, n_eff = n (1 - r1)/(1 + r1)). Use n_eff in: credible-interval width, `min_trades` checks (new label "n=34, effective 11"), gate 1, and the evidence level. Overlapping holding periods (swing) get the same treatment via the overlap graph.
- **Example (autocorr):** n = 100, lag-1 autocorrelation of R = 0.3: n_eff = 100 x 0.7/1.3 = **53.8**. Example (cluster): 30 trades, 6 on each of 5 dates, within-date rho 0.5: design effect 1 + 5 x 0.5 = 3.5, n_eff = 30/3.5 = **8.6**.
- **Size:** 2.5 agent-days (calculator 1, thread into cell stats + gates + labels 1, tests/ADR 0.5). Highest leverage, because it tightens honesty everywhere at once.
- Refs: Kish design effect (https://en.wikipedia.org/wiki/Design_effect, background only, not a primary source).

### 2.4 Block bootstrap (replace the iid Monte Carlo)

- **Have:** iid bootstrap (`MonteCarlo`), seeded (`SeededRandom`).
- **Gap:** acknowledged in the spec and the report; drawdown and p-values are optimistic under clustering.
- **Change:** `StationaryBootstrap` (Politis-Romano 1994, geometric block lengths, mean block L ~ n^(1/3) as a default, overridable); resample by trading day blocks, not by trade. Keep the old method behind a flag for comparison. Report both CI widths.
- **Example:** n = 100 -> L ~ 4.6 trades (n^(1/3)). With clustered losses a 95% CI for mean R that is +/-0.12 under iid may widen to ~+/-0.18 (illustrative, not measured).
- **Size:** 1.5 agent-days.
- Ref: Politis and Romano 1994 https://gnosis.library.ucy.ac.cy/handle/7/57533 ; arch implementation https://arch.readthedocs.io/en/latest/bootstrap/generated/arch.bootstrap.StationaryBootstrap.html

### 2.5 Deflated Sharpe ratio

- **Have:** a heuristic factor `1 + 0.25 log2(N)` on gates, N = variants.
- **Gap:** no Sharpe/expectancy t-stat, no skew/kurtosis, no trials-based expected maximum. The heuristic is calibration-free.
- **Change:** `DeflatedSharpe(srObserved, nTrials, trialSrVariance, skew, kurt, nEff)`; the number of trials N already exists (`variantCount`). Add as a 7th evidence line, shown beside, not replacing, the current gates until validated by backtest on known-null (sign-flipped) data.
- **Example:** 16 variants, SR standard deviation across trials 0.5 (per-year units): expected max SR_0 = sigma x [(1-g) Phi^-1(1 - 1/N) + g Phi^-1(1 - 1/(N e))], g = 0.5772: Phi^-1(0.9375) = 1.534, Phi^-1(1 - 1/43.5) = Phi^-1(0.977) = 1.99; 0.4228 x 1.534 + 0.5772 x 1.99 = 0.649 + 1.149 = 1.80; x 0.5 = **0.90**. A candidate with SR 0.85 is below the luck bar of 0.90 and fails; one with 1.3 is then tested vs 0.90 with the PSR normal approximation.
- **Size:** 2 agent-days.
- Ref: Bailey and Lopez de Prado, JPM 2014 https://papers.ssrn.com/abstract=2460551

### 2.6 Probability of backtest overfitting (CSCV)

- **Have:** none (only one winner per cell chosen by walk-forward; `SweepPlan` stores candidate runs).
- **Gap:** needs the matrix of period returns for all N variants; the Lab ledger already holds variants, but not their aligned per-period returns.
- **Change:** store per-variant daily P&L (or per-fold R) series at run time (jsonb in `lab.lab_results`); `Cscv.Pbo(matrix, S)`: split into S even blocks, for each of C(S, S/2) train/test splits find the in-sample best variant and its out-of-sample rank; PBO = share with rank below median. Show PBO in the verdict for sweeps; refuse PASS when PBO > 0.5 (tunable).
- **Example:** S = 8 blocks -> C(8,4) = **70** splits. If in 28 of those the in-sample winner lands in the bottom half OOS, PBO = 28/70 = 0.40.
- **Size:** 3 agent-days (domain 1.5, store series 1, tests 0.5). Needs enough variants (>= ~10) to mean anything.
- Ref: Bailey et al. https://papers.ssrn.com/abstract=2326253

### 2.7 Combinatorial purged CV with embargo

- **Have:** expanding-window walk-forward with embargo and purge in `ExitSelector` (one path, test windows from month 9); `WalkForwardValidator` for the model.
- **Gap:** a single path; 2.1 years gives only ~4 test windows. No recombination of paths.
- **Change:** `CombinatorialPurgedCv`: N group blocks, k test groups, purge by label end (`ResolvedEpochSeconds`), embargo after each test block; each combination yields a test set; paths are reassembled so every observation appears in exactly one path per split set.
- **Example:** N = 6, k = 2: C(6,2) = **15** splits, giving 5 distinct backtest paths (k/N x C = 2/6 x 15), each using all data once, instead of 1 path.
- **Size:** 3 agent-days (folds + purge reuse 1.5, integration into `OosPool` 1, tests 0.5). Real value only after the 5-year backfill; before it, gain is small.
- Ref: Lopez de Prado, *Advances in Financial Machine Learning* ch. 7 (book, not opened).

### 2.8 Sequential test (SPRT) for lifecycle decisions

- **Have:** `CusumDetector` and `CellLifecycle` (state moves at fixed thresholds); the paper-trade count is a fixed 60 in the journey.
- **Gap:** fixed-n tests waste trades; SPRT stops early.
- **Change:** `Sprt` on win-rate (Bernoulli) or mean R (normal): bounds A = ln((1-beta)/alpha), B = ln(beta/(1-alpha)). Use it for promote/demote proposals in `CellLifecycle` and the paper-trading phase check, still owner-approved.
- **Example:** H0 win rate 0.50, H1 0.60, alpha 0.05, beta 0.20: upper bound ln(0.8/0.05) = ln16 = 2.77, lower ln(0.2/0.95) = -1.56. LLR per win ln(1.2) = +0.182, per loss ln(0.8/0.5... ) = ln(0.4/0.5) = -0.223. After 15 wins and 5 losses: 15 x 0.182 - 5 x 0.223 = 2.73 - 1.12 = 1.61: undecided; 20 wins/5 losses = 3.64 - 1.12 = 2.52, still undecided; 21/5 = 2.70, next win crosses 2.77. Requires independent trials (use n_eff).
- **Size:** 1.5 agent-days.
- Ref: Wald 1945 (SPRT; not opened).

### 2.9 Minimum track-record length (MinTRL) and power analysis

- **Have:** neither; the gate `MinOosTrades` 30 is arbitrary.
- **Change:** (a) `PowerAnalysis.TradesNeeded(sigmaR, deltaR, alpha, power)`: n = ((z_alpha + z_beta) sigma/delta)^2, and the inverse (minimum detectable expectancy for the n we have). Print on every "not enough evidence" row: "needs ~395 trades to see 0.15 R, you have 34 (effective 11); smallest detectable now: 0.55 R". (b) `MinTrl` from Bailey and Lopez de Prado: n >= 1 + [1 - skew SR + (kurt-1)/4 SR^2] (z/(SR - SR*))^2. Replace the fixed 30 with max(30, computed) behind a setting.
- **Examples:** sigma = 1.2 R, delta = 0.15 R, one-sided alpha 0.05 (z 1.645), power 80% (z 0.84): n = (2.485 x 1.2/0.15)^2 = 19.88^2 = **395 trades**. For n = 34: delta = 2.485 x 1.2/sqrt(34) = **0.51 R**. MinTRL, normal returns, per-trade SR 0.10, SR* 0, 95%: 1 + 1.005 x (1.645/0.10)^2 = 1 + 1.005 x 270.6 = **273 trades**.
- **Why it matters:** with about 2 years, a 0.15 R edge is mathematically undetectable in nearly every cell; the honest output is "unknown", and this makes the number visible instead of a bare "not enough evidence".
- **Size:** 1.5 agent-days. Very cheap, high clarity.
- Ref: Journal of Risk, PSR/MinTRL https://ideas.repec.org/a/rsk/journ4/2223785.html

---

## 3. Ranked: eight highest-leverage changes

| # | Change | Why | Days |
|---|---|---|---|
| 1 | **Effective sample size** (2.3) thread through cell stats, gates and labels | Corrects the biggest overstatement: n counts overlapping/clustered trades | 2.5 |
| 2 | **Power analysis and MinTRL on every thin row** (2.9) | Turns "not enough evidence" into numbers; zero risk | 1.5 |
| 3 | **Dhan 1-min backfill to ~5y with seam test, calendar, adjustment** (1.3) | Doubles history; unlocks 2.7 and the walk-forward `train_years` | ~5 (UNVERIFIED sizing; mostly adjustment and calendar work) |
| 4 | **Empirical-Bayes shrinkage of expectancy, leave-one-out parent, estimated strength** (2.1) | Makes small cells usable without hiding them | 3 |
| 5 | **Daily bhavcopy + corporate actions, 10+ years, for regime context and 252-day signals** (1.4 step 1) | Regime history without intraday limits; licensing needs the policy read first | 4 (UNVERIFIED) |
| 6 | **Deflated Sharpe next to the log2 heuristic** (2.5) | Replaces an uncalibrated tightening with a defensible one | 2 |
| 7 | **Stationary block bootstrap** (2.4) | Fixes a known, documented optimism in the robustness block | 1.5 |
| 8 | **PBO via CSCV for sweeps** (2.6) | Direct overfitting estimate where variants are many | 3 |

Next in line: SPRT for lifecycle/paper-trading (1.5 d), CPCV (3 d, worth it only after #3), cluster-level cross-sectional pooling (2 d after #1).

## 4. Limits of this note

Web facts about NSE bhavcopy depth, delivery columns, corporate-action downloads, index membership depth and the NSE terms were not confirmed on the primary pages (fetches timed out or search snippets only). Dhan's depth (5 years intraday, 90 days per request, daily to inception) was read from the official page. Statistical formulas are standard and hand-checked above; the DSR and MinTRL constants should be verified against the papers before coding. Agent-day sizes are estimates for this codebase's pace.
