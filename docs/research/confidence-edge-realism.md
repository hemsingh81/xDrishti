# Confidence in edges: cost realism, prior evidence, anti-overfitting, scorecard

Research note, 2026-10-27. No code changed. "UNVERIFIED" = I did not reach a primary source; confirm before it goes into a spec.
Sizes are agent-days (estimates, not calibrated). Repo facts below were read from `docs/specs` and `backend/src` on this date.

## 0. Frame

- SEBI, individual intraday cash traders FY23: about 71 % lost money, average loss Rs 5,371; loss-makers spent costs equal to 57 % of their losses, profit-makers 19 % of profits; 80 % lost among those with over 500 trades a year. [Business Standard](https://www.business-standard.com/amp/markets/news/over-70-intra-day-traders-incur-losses-during-fy23-reveals-sebi-study-124072401110_1.html), [Motilal](https://www.motilaloswal.com/learning-centre/2024/7/sebi-study-reveals-key-insights-on-individual-intraday-traders-in-india). Equity F&O: over 93 % of individuals lost in FY22-24, about 91 % in FY25. [Moneylife](https://www.moneylife.in/article/93-percentage-of-individual-traders-lost-rs18-lakh-crore-in-equity-fo-in-past-3-years-sebi/75210.html), [Business Standard FY25](https://www.business-standard.com/amp/markets/news/net-losses-of-traders-in-fo-widens-in-fy25-sebi-study-125070701221_1.html).
- Implication: the base rate of "a retail rule has a tradable edge" is low (my prior: well under 10 %, UNVERIFIED as a number). The Lab must therefore be built to reject, and a pass must be expensive to earn.
- Repo today: history about 2.1 years (ISS-19), no hold-out in the Lab (P5-06 says "no hold-out" in its honest limits), three built-in strategies (`ORB`, `VWAP_PULLBACK`, `BASE_BREAKOUT`), a trial penalty `MultipleTesting.Factor(n) = 1 + 0.25*log2(n)` (`Domain/Lab/MultipleTesting.cs`), a sign-flip null, sensitivity sweep, Monte Carlo, by-year (P5-05), a 6-month hold-out for the ML harness only (P6-05).

## 1. NSE cost reality

### 1.1 Statutory and broker charges (as I could confirm, 2026-10)

| Item | Intraday equity | Delivery equity | Equity futures | Source / status |
|---|---|---|---|---|
| Brokerage (Dhan) | Rs 20 or 0.03 %, lower, per executed order (the doc 16 value) | Dhan delivery brokerage UNVERIFIED (doc 16 says 0) | Rs 20 flat UNVERIFIED | [Zerodha charges](https://www.zerodha.com/charges) shows the same Rs 20 / 0.03 % shape; check Dhan's page |
| STT | 0.025 % sell side | 0.1 % buy and sell | 0.05 % sell from 1 Apr 2026 (was 0.02 %); options sell 0.15 % (was 0.1 %) | [5paisa](https://www.5paisa.com/news/stt-hike-on-fo-to-take-effect-from-april-1-amid-rising-options-activity), [ICICI Direct](https://www.icicidirect.com/ilearn/futures-and-options/articles/stt-changes-in-budget-2026-what-f-o-traders-should-know); cash rates unchanged |
| Exchange transaction (NSE) | 0.00297 % (one snapshot) or 0.00322 % (another) of turnover | same | futures different rate, UNVERIFIED | [Finology summary of Zerodha](https://select.finology.in/broker/zerodha/calculator); snapshots disagree, treat 0.0035 % as a conservative figure |
| SEBI turnover fee | Rs 10 per crore | same | same | Zerodha page above |
| Stamp duty | 0.003 % buy side | 0.015 % buy side | 0.002 % buy side UNVERIFIED | Zerodha page above (intraday); delivery/F&O UNVERIFIED here |
| GST | 18 % of (brokerage + exchange + SEBI) | same | same | Zerodha page above; doc P3-03c already uses this base and marks it UNVERIFIED |
| DP charge | none | per scrip per sell day, about Rs 13.5 plus GST at big brokers; Dhan figure UNVERIFIED | none | check Dhan |

The repo's `ChargeRates`/`CostModel` (P3-03c, P3-03f) has a field for every line above, effective-dated, with a "not checked against bills" flag. So the structure is right; the gap is the numbers and the bill check, not the model.

Hand example, intraday round trip, Rs 1,00,000 per leg (Rs 2,00,000 turnover), 1 % stop so 1R = Rs 1,000:
brokerage 20 + 20 = 40; STT 0.025 % of 1,00,000 = 25; exchange 0.00297 % of 2,00,000 = 5.94; SEBI 0.20; stamp 0.003 % of 1,00,000 = 3.00; GST 18 % of (40 + 5.94 + 0.20) = 8.31. Total about Rs 82.45 = 8.2 bps of one leg = **0.082 R**. Same trade as delivery: STT 100 + 100, stamp 15, exchange 5.94, SEBI 0.20, GST about 1.1, DP about 16: about Rs 238 = 23.8 bps = **0.24 R**, before brokerage. Lesson: a 1 % stop with 0.08 R costs is fine; a 0.3 % stop makes the same fixed cost 0.27 R. Fixed Rs 20 per order and tick size dominate for tight stops and small size.

### 1.2 Slippage, spread, impact by liquidity tier

What I could cite: NSE's impact-cost definition and the Nifty 50 inclusion rule (impact cost at most 0.5 % for 90 % of observations) [NSE Indices](https://niftyindices.com/resources/index-concepts/impact-cost); Nifty-50 names run about 0.02 % for a Rs 50 lakh order in 2019 data [Freefincal](https://freefincal.com/impact-cost/); the NSE microstructure paper puts the median limit-book spread of all listed stocks at about 0.77 % of price [NSE paper 128](https://nsearchives.nseindia.com/content/research/comppaper128.pdf). I found **no** published intraday slippage study by tier for NSE; the table below is my planning assumption, UNVERIFIED, to be replaced by measurement from our own 1-minute data and later from paper fills.

| Tier (planning) | Example | Half-spread + impact for our size (Rs 1-5 lakh) | Tick as % of price |
|---|---|---|---|
| A: Nifty 50 / top liquid | RELIANCE, HDFCBANK | 1-3 bps | under 1 bp at Rs 1,000+ |
| B: Nifty Next 50 / midcap-100 | | 3-8 bps | 1-3 bps |
| C: smallcap, price under Rs 200 | | 10-30 bps, wide variance, gap risk | 2.5 bps at Rs 200 (0.05 tick), 10 bps at Rs 50 |

Capacity: for market orders keep order size under about 1 % of the 1-minute traded value and under 0.5 % of the 20-day median daily value per name, UNVERIFIED rule of thumb. At our scale (single owner) capacity is not the binding constraint in tier A and B; it is in C, so exclude tier C from any edge claim (a `min_turnover` filter already exists in the strategy anatomy but is "accepted and not evaluated until the liquidity data exist": that is a gap).

### 1.3 How the simulator's fill rule compares (P3-03b)

Rule: entry is a limit at trigger-bar close +0.1 % (max), filled in the next minute at its open if the open is within the limit, else at the limit if the minute trades back to it, else no trade (`LimitNotReached`); slippage = 1 tick + 5 % of bar range, stop exits +2 ticks, gap through a stop fills at the open; zero-volume minute holds no trade; modelled in `SlippageConfig` and stored per run.

Assessment:
- Fair on direction (never improves a fill, property-tested) and on gaps.
- Optimistic: fill is guaranteed at the next-minute **open** whenever it is within the limit, ignoring queue/latency (a real order from a Python/.NET client lands seconds later, after the print that triggered it); a breakout limit at close+0.1 % inside a fast move is the case where real fills are worst, and these are exactly the winners (adverse selection: the trades you miss are the ones that ran).
- 1 tick + 5 % of the bar range is a flat rule, not tied to tier; in tier A it is likely conservative, in tier C it is likely light (UNVERIFIED).
- Target exits: "no free price" (good). Stop slippage +2 ticks is light for tier B/C.
- Missing: no volume participation cap and no impact term.

### 1.4 Recommendation: conservative cost model and stress rule

1. Slippage per side = `max(1 tick, tier_bps) + 0.10 * bar_range`, plus stop exits +2 ticks and a "fill delay" variant: fill at the **second** minute open (or worst of next open and next-minute close) for breakout entries. Tier bps: A 3, B 8, C excluded.
2. Charges from the stored sheet, and every strategy result must carry "checked against a bill: yes/no".
3. **Stress rule: the strategy must still be net positive and above its gate at 1x costs+slippage, 1.5x, and 2x; the verdict uses the worst of the three for the "tradable" label.** Today P5-06 has only a `cost_sensitivity` warning (net expectancy under 0.15 R). Make it a hard gate.
4. Delay stress: re-run with the entry fill one minute later; expectancy loss over 30 % = fail.

Hand example: net expectancy at 1x = +0.20 R, costs+slippage = 0.12 R of that. At 2x: 0.20 - 0.12 = +0.08 R, passes. If 1x is +0.10 R and costs are 0.12 R, 2x gives -0.02 R: fail, correctly.

| Item | Repo gap | Change | Size |
|---|---|---|---|
| Verify charge numbers and Dhan DP/delivery brokerage against a real bill | sheet exists, numbers `?`/UNVERIFIED | owner provides 5 bills; reconcile with the P3-03f bill check; fill sheet | 0.5 |
| Tiered slippage + delay-fill variant | flat `SlippageConfig` | add tier by 20-day median turnover (data from stored bars), second-minute fill option | 2 |
| Stress 1x/1.5x/2x as verdict gate | warning only | `CostStress` multiplier in run options, three results in the result doc, gate | 1.5 |
| Turnover filter and capacity cap | `min_turnover` not evaluated | evaluate from stored bars; cap qty to % of minute value | 1.5 |

## 2. Which strategy families have evidence in Indian equities

Honest summary: Indian academic evidence is mostly monthly, long-horizon, pre-2020, and says nothing about costs or intraday. For the intraday rules our engine is built around, published out-of-sample evidence is essentially absent; I found only practitioner posts (UNVERIFIED, no costs, no hold-out).

| Family | Evidence found | Decay / caveat | Fit to our engine |
|---|---|---|---|
| Cross-sectional momentum (6-12 month) | Significant short-term momentum and long-term reversal 1997-2013 on NSE [IJFM](https://www.publishingindia.com/archive/ijfm/a-study-of-contrarian-and-momentum-profits-in-indian-stock-market); IIMA four-factor 1993-2013 including momentum, liquid-only [IIMA WP](https://web.iima.ac.in/~iffm/legacy/four-factors-India-90s-onwards-IIM-WP-Version-original-Sep13.pdf); NSE momentum indices rebalanced semiannually [Stockviz](https://stockviz.substack.com/p/if-you-cant-beat-em?open=false) | Crash risk at reversals; monthly data; post-2013 not checked here. Delivery STT 0.1 % per side is small against a 6-month hold | Needs 1D history over years: we have 2.1 years. Cannot validate; adopt as a low-turnover reference only |
| Short-term reversal (1 day to 1 week) | not found for India (UNVERIFIED) | US evidence says it lives in illiquid names and vanishes after costs | Poor: tier C only |
| ORB / intraday breakout, PDH/PDL | only practitioner backtests, none with costs or OOS (see search of [Marketcalls](https://www.marketcalls.in/amibroker/backtestable-open-range-breakout-orb-study-for-amibroker.html), [TradingQnA](https://tradingqna.com/t/orb-opening-range-breakout-2pm-banknifty-intraday-strategy/39756)) | Treat as no prior evidence; high retail crowding; SEBI intraday result above | Our main strategy has zero published prior. It must earn it entirely from our data |
| Gap fade/continuation | not found (UNVERIFIED) | | |
| 52-week high | not searched/found for India (UNVERIFIED) | US effect partly momentum | |
| Low volatility / betting against beta | Varma's blog: BAB dominates size/value/momentum in India [JRVarma](https://www.jrvarma.in/blog/Y2014-15/betting-against-beta.html) | Blog, not peer-reviewed; long-only low-vol is index-like | Swing/portfolio level, not a Lab entry rule |
| Quality | Summary of 1995-2021 study: quality alpha about 0.92 %/month, India stronger than developed [Quantpedia](https://quantpedia.com/performance-of-factor-strategies-in-india/) | Needs fundamentals data we do not have | Out of scope until a fundamentals feed exists |
| PEAD | Mixed: drift after earnings found for 2002-2017, esp. bad news [Nottingham](https://eprints.nottingham.ac.uk/26569); little drift 2014-2018 for 100 NSE firms [VUT](https://journals.vutbr.cz/index.php/trends/article/view/541) | Neither addresses costs; liquidity-dependent | Needs earnings calendar/surprise; our `not_event_day` filter is the opposite use |
| Pairs / stat-arb | not found for India cash equities (UNVERIFIED) | short-selling delivery not allowed, so intraday-only both legs; costs hit twice | Not supported by engine |

### 2.1 Recommended prior-informed menu

1. Keep a **core, low-turnover** sleeve as reference: monthly/weekly relative-strength on liquid names (tier A/B), delivery, rebalanced rarely. Use as the *benchmark the intraday ideas must beat*, and the cheapest sensible place for real capital.
2. Intraday ideas (`ORB`, `VWAP_PULLBACK`, `BASE_BREAKOUT`) are **hypotheses with no published prior**; they enter the pipeline as "exploratory", capped at paper-size-zero until they pass section 4.
3. Add as candidate rules only those with a one-paragraph mechanism: opening-range continuation on high relative volume with news (information arrival/underreaction), VWAP reclaim after a liquidity-driven flush (reversal of price-pressure), trend with volatility-contraction (momentum). Reject "indicator X crosses Y" without a mechanism.

### 2.2 "Economic rationale required" pre-registration gate

A strategy cannot be run beyond a quick test unless its record has: (a) who is on the other side and why they lose (forced seller, slow reaction, risk-transfer premium); (b) the expected sign of every parameter's effect; (c) the instrument tiers and sessions it applies to; (d) the expected gross edge in bps and the cost it must beat; (e) a falsifier ("if out-of-sample expectancy < X or the placebo scores within Y, retire"). Stored with a hash; parameters and universe frozen at that time.

| Item | Repo gap | Change | Size |
|---|---|---|---|
| Rationale + falsifier fields on strategy version | none; strategy anatomy has `params` only | required text fields + frozen hash at first full run; Lab refuses `full`/`robust` without them | 1.5 |
| Cohort/reference benchmark (buy-and-hold Nifty, equal-weight same-universe, and the delivery momentum reference) in report | P4-05/P6-05 baselines are ML/exits, not strategy-level | compute strategy R vs benchmark with same costs; show alpha net | 2 |

## 3. Anti-overfitting protocol for the Lab

What exists (read from specs/code): `full` test with walk-forward (P5-04); `robust` adds parameter sweep (up to 17 runs), Monte Carlo, by-year, and a **sign-flip** baseline which the spec itself calls lenient (it tests mean R against 0 given trade sizes; "random entries with a 2R target lose money before costs", so the real null is harder); multiple-testing factor `1 + 0.25*log2(N)` on gates; seeded determinism; P6 has a purged walk-forward harness with a 6-month hold-out that throws `validation.holdout_touched`, plus base-rate/cell-rate/logistic baselines. P3-09 pre-registers a seed for sample draws.

| # | Control | Repo gap | Application change | Hand example | Size |
|---|---|---|---|---|---|
| 1 | Pre-registration | none for Lab; seed precedent in P3-09 | freeze strategy text, universe, window, cost sheet, parameter grid hash before the first `full` run; later runs with a different hash count as new trials | 3 reg'd params x 5 values = 125 grid; registering "3 values each" = 27 | 1.5 (with 2.2) |
| 2 | Hold-out locked to the end | Lab has none; only ML harness | reserve the last 20 % of days (min 6 months) per strategy; one unlock per strategy version, logged; result visible only as pass/fail until unlock | 2.1 y data: 0.4 y hold-out leaves 1.7 y; 2 walk-forward folds only, so say "inconclusive" rather than pass. Need data: ISS-19 says backfill toward 5 years | 2 (code), data is the real cost |
| 3 | Trial counting | `MultipleTesting.Factor` takes a count the caller supplies; no ledger | persisted ledger per (strategy family, universe) counting every run, sweep point, exit candidate and edit; verdict uses ledger total, not the current run | after 64 trials factor = 1 + 0.25*6 = 2.5; a gate of 0.15 R becomes 0.375 R. Factor is heuristic, not a statistical claim; add Deflated Sharpe style check (Bailey and Lopez de Prado, UNVERIFIED here) | 2 |
| 4 | Random-entry null | sign-flip only, lenient | engine mode that, per real trade, draws an entry at a random minute in the same instrument, session, side, with the same stop/target/time rules and costs; 1000 seeds; real mean R must exceed the 95th percentile | if real = +0.12 R and 1000 random runs give mean +0.00, sd 0.04: 95th = +0.066, pass; if random gives 0.07, fail | 3 (P5-05 called this an engine change) |
| 5 | Shuffled-label null | exists for ML (baselines) not for rule strategies | for ML overlays: permute outcomes within fold and re-score; for rules: permute trade dates across instruments (breaks signal-return timing, keeps marginals) | | 1 |
| 6 | Placebo strategies | none | run 20 registered "nonsense but same shape" rules (mirror the trigger: enter the opposite side, shift trigger by +30 minutes, invert the filter) in the same family; if the real strategy is not in the top 5 % of placebo+real, no edge. Reverse-direction is the cheap test: a real breakout edge must not also profit as a fade | real +0.12 R, mirror -0.10 R: consistent. Real +0.12, mirror +0.08: the "edge" is mostly beta/drift, fail | 1.5 |
| 7 | Plateau, not peak | sensitivity sweep exists, one param at a time, no gate | gate: for each param the neighbours (+-1 step) must keep at least 70 % of the chosen value's net expectancy, and the median over the grid must be positive; choose the centre of the plateau, not the maximum | expectancy by OR length 5/10/15/20/30 min = 0.02/0.11/0.12/0.10/0.01: plateau 10-20, pick 15; if 0.01/0.02/0.15/0.02/0.01 it is a spike: fail | 1 |
| 8 | Decay monitor | none for live/paper | rolling 60-trade net R, CUSUM of deviation from OOS mean, and by-quarter; alert when 60-trade mean < OOS mean - 1.5 standard errors | OOS mean +0.12, sd 1.0, n=60: SE 0.13, alert below -0.08 | 1.5 |
| 9 | Kill rules | risk limits exist in doc 08, no strategy-level retirement | auto-demote to paper-size-zero when: 1.5x-cost stress turns negative on trailing 100 trades; drawdown > 2x the Monte Carlo 95th; or decay alert twice in a row; owner act to re-enable (auto option per standing instruction, default off) | | 1 |
| 10 | Data-snooping hygiene | members as of today (ISS-22 survivorship) | point-in-time universe; flag every result as survivorship-biased until fixed; require tier A/B filter | | per ISS-22 |

Total for section 3: about 14.5 agent-days, plus data backfill.

## 4. Edge scorecard (go/no-go before any strategy gets paper size)

Everything on hold-out or out-of-sample, net of the sheet's costs, at 1.5x costs unless a row says otherwise. Numbers are proposals (my judgment, not derived); the owner tunes them once.

| # | Criterion | Go threshold | Why |
|---|---|---|---|
| 1 | Rationale + falsifier registered before first full run | present, hash frozen | section 2.2 |
| 2 | Independent trades (OOS + hold-out) | at least 300, at least 30 per used cell | with SD 1 R, SE = 1/sqrt(300) = 0.058 R, so a +0.12 R mean is about 2 SE |
| 3 | Net expectancy at 1x costs | at least +0.15 R after the trial penalty factor (so raw +0.15 x factor) | echoes the existing 0.15 R warning, made a gate |
| 4 | Stress: 1.5x and 2x costs/slippage | 1.5x: at least +0.08 R, 2x: greater than 0 | section 1.4 |
| 5 | Delay stress (fill one minute later) | at least 70 % of expectancy kept | section 1.3 |
| 6 | t-stat (mean/SE) of OOS+hold-out | at least 2.0, and at least 2.0 x Factor-adjusted for trials | cheap proxy for deflated significance |
| 7 | Random-entry null | real mean above the 95th percentile of 1000 random-entry runs | section 3 #4 |
| 8 | Placebo / mirror | mirror side expectancy at most 0, and rank in top 5 % of placebos | #6 |
| 9 | Plateau | neighbours keep at least 70 %, grid median positive | #7 |
| 10 | Stability | at least 70 % of calendar quarters (hold-out included) positive-or-flat within -0.05 R; no year holds over 60 % of total R; no instrument over 15 % | existing `concentration` warning, made a gate |
| 11 | Drawdown | observed max DD within Monte Carlo 90th percentile; MC 95th DD under 20 R (or the account loss budget) | |
| 12 | Hold-out | used once, expectancy at least 50 % of the dev OOS and still positive | #2 in section 3 |
| 13 | Benchmark | beats the same-universe equal-weight / buy-and-hold on risk-adjusted basis net | section 2.2 |
| 14 | Capacity/tier | all trades in tier A/B; qty under 1 % of the minute value | section 1.2 |
| 15 | Paper confirm | 20-trade check (P3-09) passed, then paper with n at least 60 trades with mean within 1.5 SE of OOS | live-vs-sim fill gap measured |

Verdict: all of 1-12 and 14 must pass for paper size small (e.g., 0.25 % risk); 13 and 15 for scale-up. Any `unavailable` input counts as fail (matches the standing rule about `unavailable` never passing). Auto/manual approval setting as in CLAUDE.md; no setting allows orders.

Hand example (rows 2, 3, 6): 320 trades, mean +0.20 R gross-of-penalty, SD 1.0 R; SE = 1/sqrt(320) = 0.056; t = 3.6. After 64 trials Factor = 2.5: required t = 2.0 x 2.5 = 5.0 -> **fails** though it looks good; with 8 trials Factor = 1.75, required 3.5 -> passes barely. This is the intended behaviour: a result found by searching much needs much more evidence.

| Item | Repo gap | Change | Size |
|---|---|---|---|
| Scorecard object + gate evaluation in verdict (P5-06) | verdict has PASS/MARGINAL/FAIL from fewer gates | `EdgeScorecard` pure Domain, one row per criterion with value, threshold, status; report section | 2 |
| Paper-size authorisation tied to scorecard | none | scorecard result is a precondition for the owner act; auto option per standing rule, default OFF | 1 |

## 5. Plan and totals

Order by value per day: stress multipliers (1.5) -> trial ledger (2) -> plateau gate (1) -> random-entry null (3) -> placebo/mirror (1.5) -> scorecard (3) -> rationale gate + benchmark (3.5) -> tiered slippage/delay + liquidity filter (3.5) -> hold-out (2) -> decay/kill (2.5) -> bill check (0.5). **About 24 agent-days**, plus the real constraint: **history of 2.1 years cannot support a locked hold-out and 300 OOS trades per strategy**; backfill toward 5 years (ISS-19) is the highest-value non-code item.

Open questions for the owner: (1) numbers in the scorecard; (2) hold-out length versus data available; (3) Dhan delivery brokerage/DP and current NSE transaction rate from a recent contract note.
