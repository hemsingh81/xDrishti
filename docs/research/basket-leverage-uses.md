# Baskets as leverage for the system: uses, performance insight, safe "more leverage", ranking

Research note, 2026-10-25. No code. Status: Draft for the owner; nothing here is a spec or a decision.
Sources are cited by URL; a claim from a search snippet that I did not open on the primary page is marked **UNVERIFIED**.
Internal references: docs/design 06, 07, 08, 09, 10, 13, 14, 15; ADR 0014, 0102, 0105; specs P3-01e, P6-00, P7-00, P8-07, P8-09, P9-00, P13-00, P14-00, JOURNEY.

## 0. What we already have (the base everything plugs into)

| Asset | State (from the repo) |
|---|---|
| Baskets | Manual + portfolio system baskets built (P1-15/15a). Dated membership (`valid_from`/`valid_to`, `asOf`) = point-in-time membership exists. **Rule-based baskets (index, volatility, momentum, liquidity) are designed (doc 06 section 3.1) but NOT built.** Max 2,000 members. |
| Instrument data | Sector (NSE Nifty Total Market industry), F&O flag, ASM/GSM flag, lot size, MTF flag field, 1-minute bars, daily bars, `market.context_daily` (index, VIX, regime 1..9, breadth of the owner's tracked set, P3-01c/d/e). |
| Sim results | `SimTrade` schema 5 (net R, MAE/MFE, exit reason, decision time), ledger runs, Lab report `by_regime` once context exists. |
| Cells | `(strategy, timeframe, side, style, exit, regime)`; Bayesian shrinkage to parent group, 250 d / 60 d windows (P6-01, P6-02). |
| Features | 56-name catalogue incl. market-context features; `sector_rs` explicitly **not planned** (P3-01e out-of-scope). |
| Selection | Doc 10: per-basket cap ("max 2 from MyChoice"), per-sector, pair correlation, net long/short caps; 60-day pair correlation is "new, small, pure" (P7-00). Market gate: normal / reduced / no-trade from VIX + breadth + events (P7-06). |
| Reports | P8-06 endpoints: `groups?lens=basket`, `pivot`, `equity?lens=basket`, `what-works`; P8-07 buy-and-hold equal-weight price-return per basket + index with named exclusions (ADR 0102); P8-09 comparison (2..6 baskets), pivot builder, masking below 30 trades (ADR 0105). |
| AI | P9 tools: `get_report`, `get_basket`, `get_cell`, `get_plan`; read-only; model never decides a trade. |
| Safety | No order until P12; every switch default OFF; auto-approval setting OFF; owner act with password (CLAUDE.md standing rules, P7-00, P7-12/ADR 0091). |

Key honesty limits already stated in the repo and that bind this note: membership as of today (ISS-22 survivorship), breadth is "breadth of your N tracked shares", data 2024..2026 is one market history, most cells will be under `min_trades` 30.

## 1. Catalogue of uses

Common conventions: sizes in agent-days; "Plug-in" names our extension points. Every use ships as **evidence + advisory**, never an order, and every threshold is a stored setting default OFF/neutral.

### U1. Index and behaviour baskets as rule-based dynamic baskets (the prerequisite)
- **Mechanism:** a saved rule re-evaluated nightly yields a dated membership: index members (Nifty 50/100/200/Midcap 150...), F&O-eligible, liquidity tier (median turnover 20 d), volatility tier (ATR% or 60 d realised vol tercile), momentum tier (below), sector. Dated `valid_from/valid_to` rows make every historical basket point-in-time **from the day we start recording**.
- **Evidence:** index methodology are published rules (e.g. Nifty200 Momentum 30: top 30 of Nifty 200 by normalised 6- and 12-month return, volatility-adjusted, capped weights, semi-annual rebalance; one Dec 2025 rebalance replaced 19 names, showing turnover) https://www.niftyindices.com/Factsheet/Factsheet_Nifty200_Momentum30.pdf , https://www.business-standard.com/amp/markets/news/nifty200-momentum30-a-flip-of-index-switch-jolts-16k-cr-across-the-grid-125121400388_1.html
- **Have:** manual baskets, dated membership, sector, F&O, daily bars, `ind_niftytotalmarket_list.csv` import.
- **Needs:** a rule engine (pure `BasketRule` evaluated over daily bars as of date D, using only data <= D), a nightly job that writes membership rows (insert-only, with the rule version), NSE index constituent files for official indices (public CSVs, current members only: history not available, so index baskets are point-in-time **only from our first snapshot**; say so on screen).
- **Plug-in:** basket `kind: rule`; Baskets screen; every consumer below reads `members(asOf)`.
- **Benefit:** unlocks U2..U12; no edge by itself.
- **Overfit risk:** low for official indices and for rules fixed in advance; high if rules are tuned on results (section 4).
- **Size:** 5 agent-days (rule model 1.5, evaluator + nightly job 2, screen/rule editor 1, tests/ADR 0.5).
- **Phase:** P7.5 / early P8 extension (needs only built pieces). Recommended first.

### U2. Basket as strategy universe in the DSL and the Lab
- **Mechanism:** a strategy's scan scope is a basket (already: doc 07 `basket: Best stocks`). Testing one strategy on liquidity-tier, volatility-tier and sector baskets shows where its edge lives (universe selection matters: ORB-type breakouts need range and liquidity; mean reversion needs liquid large caps).
- **Evidence:** industry/sector effects are a large part of stock-level momentum, so universes defined by industry change results materially: Moskowitz & Grinblatt 1999 https://ideas.repec.org/a/bla/jfinan/v54y1999i4p1249-1290.html . Practitioner point that factor/index family definitions drive results: MSCI https://www.msci.com/research-and-insights/quick-take/sector-momentum
- **Have:** P3-04a runs a manual basket "members now"; the ledger family concept.
- **Needs:** run label stores `basket_id`, `rule_version`, `members_as_of`; for rule baskets, run with **membership per day** (sim reads `members(asOf=day)`), which removes look-ahead only for the period we have recorded memberships, otherwise label "members as of today".
- **Plug-in:** `simulate --basket`, Lab page basket picker, ledger family `universe:<basket>`; **each (strategy x basket) tested counts toward N for the multiple-testing factor already used in P14** (`Factor(N)`).
- **Benefit:** medium; mainly kills strategy-universe mismatches and gives a defensible "this strategy only on liquid F&O names" rule.
- **Overfit:** medium-high (strategies x baskets grid). Mitigate by pre-registering a small fixed basket set (about 8) and counting every cell in N.
- **Size:** 3 agent-days (point-in-time membership read in sim 2, labels/ledger 1).
- **Phase:** P8-P10 window, after U1.

### U3. Basket as a cell-key dimension in learning (P6)
- **Mechanism:** add `universe` (basket id or behaviour tier) to the cell so statistics answer "ORB, 15m, BUY, intraday, regime 5, **in high-liquidity F&O**". Shrinks toward the parent cell without the dimension.
- **Evidence:** conditioning is the point of P6-01 shrinkage; the danger is cell explosion (P6-00 already warns regime x 9). Multiple-testing literature: Harvey & Liu haircut https://people.duke.edu/~charvey/Research/Published_Papers/P120_Backtesting.PDF ; CME summary https://www.cmegroup.com/content/dam/cmegroup/education/files/backtesting.pdf
- **Have:** `CellId` extensible (P14-06 already adds an optional seventh part for expression, stored keys unchanged).
- **Needs:** reuse that pattern: optional part `universe` with default `*`. Use only **behaviour tiers (3 values)** not 30 baskets; trade-to-tier assignment must use the tier **as of the decision date**.
- **Plug-in:** `CellId.TryParse` 6/7/8 parts; `research.cell_stats` column; selection looks up the narrowest cell with n >= min_trades, else the parent (existing fallback).
- **Benefit:** medium; real gain mostly through U4/U5.
- **Overfit:** high if more than one tier dimension; keep `*` default and require the child cell's credible lower bound to beat the parent's.
- **Size:** 3 agent-days (key + stats + tagger 2, report/pivot support 1).
- **Phase:** after P6-01/02 and U1; before P7-01 consumes cells, or as a P7.5 follow-up.

### U4. Cross-sectional momentum / relative strength rank inside a basket (feature + ranking signal)
- **Mechanism:** rank members by 6-1 and 12-1 month return (skip the latest month), volatility-adjusted (return / daily vol, as the NSE index does). Use the rank as (a) a feature, (b) a swing-universe filter (top tercile), (c) the missing `sector_rs`/`RS_ROTATION` input.
- **Evidence:** Jegadeesh-Titman style momentum, Indian evidence on NSE 1997-2013 positive but with strong negative May and short-term reversal effects (secondary summaries, **UNVERIFIED**) https://www.addletonacademicpublishers.com/contents-emfm/248-volume-9-3-2014/2268-evidence-and-sources-of-momentum-profits-a-study-on-indian-stock-market ; time-series momentum reported stronger than cross-sectional in India https://sgbeta.inflibnet.ac.in/items/3cabc26f-85b5-4924-977d-4e6aa4e177f0/full ; Asness-Moskowitz-Pedersen "Value and Momentum Everywhere" https://pages.stern.nyu.edu/~lpederse/papers/ValMomEverywhere.pdf ; Nifty200 Momentum 30 methodology (above). Caveat: Indian momentum funds are long-only because of short-sale limits (SLB depth), so a long/short momentum factor is not tradable by us; evaluate long-only vs the basket (UNVERIFIED working paper).
- **Have:** daily bars for 214+ tracked shares, features `roc`, distance from N-day high.
- **Needs:** feature `mom_rank(basket, lookback, skip)` and `rs_vs_index(n)` computed at daily close as of the previous session, percent rank 0..1 within the basket's members as of that date; store in the P6-04 side table.
- **Plug-in:** DSL `feature mom_rank(...)` (lag rule P3-02f applies); P6-04 feature; P7 rank tie-break; `HIGH_52W_MOMENTUM` and deferred `RS_ROTATION`.
- **Benefit:** medium-high on swing strategies; the most literature-backed signal here.
- **Overfit:** medium (few parameters: lookback 126/252, skip 21). Fix them from the literature, do not grid-search.
- **Size:** 3 agent-days (indicator kernels 1.5, DSL/feature wiring 1, tests incl. truncation property 0.5).
- **Phase:** P8-P9 window (feature), consumed by P7 re-run.

### U5. Sector / industry rotation and sector relative strength
- **Mechanism:** sector index (equal-weight of sector members, our own, from `instruments.sector`) return rank over 1/3/6 months; "strongest stocks in strongest sectors" (`RS_ROTATION`); sector strength as a gate for swing BUY.
- **Evidence:** industry momentum persists up to a year (Moskowitz-Grinblatt, above); MSCI sector momentum outperformed developed-market broad indexes 2000-2021 but changes fast in stress https://www.msci.com/research-and-insights/quick-take/sector-momentum ; **sceptic**: conventional business-cycle sector rotation fades after costs and mistimed cycles (Molchanov & Stangl 2023, via https://mro.massey.ac.nz/items/ccadcb8d-2658-456b-9aab-4ba840fba845/full ). Arnott et al.: industry momentum is a by-product of factor momentum (**UNVERIFIED**, working paper).
- **Have:** sector for shares, daily bars; Nifty sector indices could also be fetched as index instruments (119 index rows in the master).
- **Needs:** pure `SectorStrength` (equal-weight member return, rank), minimum members per sector (>= 5) else `insufficient`, feature `sector_rs(n)`.
- **Plug-in:** DSL feature `sector_rs`, P3-01e noted it as own slice; P7 filter/rank; U12 reports.
- **Benefit:** medium for swing; low for intraday.
- **Overfit:** medium. Report as a filter on existing swing strategies, not as a new strategy first.
- **Size:** 3 agent-days.
- **Phase:** with U4.

### U6. Basket breadth, dispersion and correlation as regime and gate inputs
- **Mechanism:** per basket: % above 20/50/200 DMA, advance/decline, new-high minus new-low share, cross-sectional return dispersion, average pairwise correlation. High correlation + low dispersion = "one-factor day", where stock-picking strategies lose edge and correlation caps must tighten; high dispersion favours stock-specific setups and pairs.
- **Evidence:** breadth thrust (Zweig, EMA10 of A/(A+D) from <0.40 to >0.615 within 10 sessions) is a rare bullish signal; evidence is practitioner and small-sample https://articles.stockcharts.com/article/articles-arthurhill-2019-01-perspective---40-years-of-the-zweig-breadth-thrust---weight-of-the-evidence/ ; participation >50% as "broadening" is a heuristic, not a tested threshold (same family of sources). Treat as **descriptive context first**, signals only after our own OOS test.
- **Have:** P3-01e breadth of the whole tracked set (point-in-time, `breadth_state`).
- **Needs:** generalise `BreadthCalculator` to take a basket's members as of D (it already takes "the owner's tracked shares"); add dispersion and avg correlation; store per (basket, date) in a new `market.basket_context_daily` (insert-once, same rule as P3-01e).
- **Plug-in:** P3-01c features `basket_breadth`, `basket_disp`, `basket_corr`; market gate P7-06 (use Nifty 50 / liquid-100 basket instead of the whole tracked set: less noisy and less "owner's own set" bias); regime tags P6-02; reports U12.
- **Benefit:** medium (gate quality; better "why did the strategy fail that week").
- **Overfit:** medium; use few fixed definitions, no thresholds tuned on PnL.
- **Size:** 4 agent-days.
- **Phase:** P8 window (fits P3-01e follow-up), before the P7 gate is relied on.

### U7. Volatility-targeted sizing with basket-level risk budgets (advisory)
- **Mechanism:** size by target risk, not by fixed rupee: ticket risk x (target vol / forecast vol of the basket or instrument), capped. Plus a **risk budget per basket** (share of the day's total open risk that one basket may use) and an equal-risk-contribution check across baskets.
- **Evidence:** volatility-managed portfolios raise factor Sharpe ratios (Moreira & Muir, J. Finance 2017) https://www.nber.org/papers/22208 ; managing momentum risk nearly doubled momentum Sharpe (Barroso & Santa-Clara, JFE 2015) https://ciencia.ucp.pt/en/publications/momentum-has-its-moments/ ; **but** out-of-sample/after-cost versions often fail to beat unmanaged (Cederburg et al. 2020; Barroso-Detzel on costs), https://www.lehigh.edu/~xuy219/research/COWY.pdf . Risk parity families: ERC and Hierarchical Risk Parity (Lopez de Prado 2016, no covariance inversion) https://en.wikipedia.org/wiki/Hierarchical_Risk_Parity (secondary).
- **Have:** `RiskProfile`, risk per trade, doc 10 caps (total open risk, per sector, per basket), market gate "risk x 0.75" for reduced.
- **Needs:** pure `VolTarget.Scale(forecastVol, targetVol, floor, cap)` with EWMA vol (lambda 0.94) of the instrument's daily returns as of the previous close; scale clamped to [0.5, 1.0] by default (**never above 1.0 unless the owner raises the cap**: scaling up is what the "leverage" worry is about); basket budget table `selection.basket_risk_budgets`.
- **Plug-in:** `PortfolioFiller` (per-basket constraint already in doc 10), ticket sizing P7-04/07, sim `sizing` option so we can **test it in the simulator before using it** (fixed-risk vs vol-targeted, same trades, common random numbers like P6-11).
- **Benefit:** medium: mostly smoother drawdown, not higher return.
- **Overfit:** low-medium (one parameter pair). Report net of costs only.
- **Size:** 4 agent-days (kernel + sim option 2, filler/budgets 1.5, tests 0.5).
- **Phase:** P7 extension / P10 (paper) with settings OFF.

### U8. Correlation-aware position limits using basket and cluster structure
- **Mechanism:** the P7 "pair correlation" rule extended: cap open risk per correlation cluster (hierarchical clustering of 60-day daily-return correlations, threshold e.g. rho > 0.7), and treat members of the same tight basket as one bet. Reduces hidden concentration (five banking longs = one bet).
- **Evidence:** HRP clusters by correlation (above); covariance shrinkage (Ledoit-Wolf) for stable estimates https://en.wikipedia.org/wiki/Hierarchical_Risk_Parity (secondary; **UNVERIFIED** primary).
- **Have:** nothing for correlation yet (P7-00 states "no correlation computation").
- **Needs:** `Correlation60` (pure, from `bars_agg` daily), shrunk to a constant-correlation target; clustering (single-linkage on 1 - rho), `cluster_id` on candidates, cap `max_risk_per_cluster_pct`.
- **Plug-in:** P7-02 filler constraint, P7-05 checklist line, U12 correlation matrix view.
- **Benefit:** medium-high on drawdown tail; cheap.
- **Overfit:** low (risk control, not alpha).
- **Size:** 3 agent-days (the correlation kernel is shared with U6/U11).
- **Phase:** P7 extension (it is already half in the P7 plan).

### U9. Index-relative strength and market gate by index basket
- **Mechanism:** feature `rs_vs_index`: stock 20 d return minus Nifty 50 (or its sector index) return, and the same for baskets. Long setups only when RS >= 0 (or top half); the gate can read Nifty 50 trend, Nifty Midcap/Smallcap relative strength (risk-on proxy) and India VIX percentile.
- **Evidence:** RS ranks are the same family as U4; index-relative filters are standard practitioner practice (UNVERIFIED as an edge); regime conditioning via P6-02 is our own test.
- **Have:** `index_trend`, `index_adx`, `vix`, `vix_pct`, `breadth` features; index series from P3-01d.
- **Needs:** index bars for Nifty Midcap 150, Smallcap 250 (index instruments are in the master; need a loader), `rs_vs_index` feature.
- **Plug-in:** DSL feature; gate; P6-04.
- **Benefit:** medium; low cost.
- **Overfit:** medium; test as an on/off filter on existing strategies with the P5 gates.
- **Size:** 2 agent-days.
- **Phase:** with U4.

### U10. ML features from basket aggregates (P6-04)
- **Mechanism:** add decision-time aggregate features describing the basket the candidate belongs to: basket return 1/5/20 d, basket breadth, dispersion, avg correlation, candidate's rank/percentile in basket (momentum, volume surge, ATR%), candidate residual vs basket (idiosyncratic move), basket volume vs 20 d average, sector rank.
- **Evidence:** cross-sectional ranks and market-relative features are standard in ML-for-asset-pricing; for a small sample (a few thousand trades; P6-00 expects the model may not beat baselines) every added feature costs degrees of freedom. Deflated-Sharpe / multiple-testing discipline applies (Harvey & Liu above).
- **Have:** side-table extractor design (P6-04), context features, point-in-time property test (truncation).
- **Needs:** features U4-U9 built first; the extractor reads the basket's members **as of the decision date**; a fixed feature list (max 12 new), ablation test against the baseline of P6-05 (a feature stays only if OOS lift is positive in >= 2/3 of folds).
- **Plug-in:** `trade_features`, LightGBM; contributions screen (P6-06) shows which basket features mattered.
- **Benefit:** uncertain; honest outcome may be "no lift".
- **Overfit:** high; ablation + purged walk-forward only.
- **Size:** 3 agent-days (after U4-U9).
- **Phase:** P6/P8 retro-fit, only after the baseline model exists.

### U11. Basket stat-arb, dispersion and pairs (P14)
- **Mechanism:** (a) pairs within a sector basket (cointegration/distance); (b) basket-vs-member mean reversion (member vs its sector equal-weight index, "residual reversion", Avellaneda-Lee style); (c) index-vs-constituents dispersion is an options concept (P14, needs an option chain archive).
- **Evidence:** Gatev-Goetzmann-Rouwenhorst report up to about 11% annual excess on US 1962-2002 https://alphaarchitect.com/2013/06/pairs-trading-kicks-butt/ ; later replications (2004-2009, several markets) did not find consistent profits, and profits decayed (same page summary). Avellaneda-Lee and Krauss surveys: not opened (**UNVERIFIED**). In India, pair legs need a short leg: intraday cash short is allowed (MIS) but delivery short is not, and stock futures need ~15 lakh per leg for swing (P14-00 already records this).
- **Have:** P14-00 plan with a sector-limited candidate set (<= 2,000 pairs, every pair counted toward N), the kill criterion.
- **Needs:** this note adds only: sector baskets from U1 are the candidate generator; the residual-vs-basket feature of U10.
- **Plug-in:** P14-12/13.
- **Benefit:** low-uncertain; research only.
- **Overfit:** very high (thousands of pairs) unless P14's multiple-testing protocol is kept exactly.
- **Size:** inside P14 (no extra); 1 agent-day to wire sector baskets as the generator.
- **Phase:** P14.

### U12. Basket performance insight (reports): see section 2
- **Size:** 6-9 agent-days beyond P8-06/07/09 (see table).
- **Phase:** P8 follow-ups (P8-10 catalogue).

### U13. AI assistant tools on baskets (P9)
- **Mechanism:** tools `get_basket(name, metrics[], window)`, `compare_baskets(ids[], window)`, `get_basket_context(name, date)`, `explain_basket_miss(...)`. The model narrates stored numbers (rule: numbers only from tools, no trade decisions, P9-00).
- **Have:** `get_basket` already in the cut-line list; P8-06 endpoints.
- **Needs:** thin tool wrappers over the report endpoints, golden answers on the seeded DB; add 6-8 basket questions to the P9-09 eval set ("Which basket worked best for 15m setups?" is already in doc 14).
- **Plug-in:** `xd-mcp`, `aiv` views (`aiv.basket_metrics`, `aiv.basket_membership`).
- **Benefit:** convenience and insight discovery; no edge.
- **Overfit:** n/a, but the model may over-read n=12 differences: tool responses must carry state (`insufficient`) and n.
- **Size:** 2 agent-days.
- **Phase:** P9 (after U12 endpoints).

### U14. Paper-trading by basket (P10) and capacity per basket
- **Mechanism:** paper fills labelled with basket/tier; weekly review compares sim vs paper per basket; liquidity tier tells which baskets the owner's size can actually trade (capacity = fraction of 1-minute median volume the order would take).
- **Have:** P10 plan; `adv`/volume bars.
- **Needs:** `basket_id` snapshot on `paper_fills`; capacity metric U12 (m10); slippage per tier from P13 depth when available.
- **Size:** 2 agent-days.
- **Phase:** P10-P11.

### U15. Basket-level kill switches / throttles (advisory)
- **Mechanism:** pause suggestions for a basket when its rolling 20-trade expectancy lower bound < 0 and drawdown > k x median, or its breadth/vol regime is out of the strategy's validated range. Same shape as the existing lifecycle states (P6-08) applied per `(strategy, basket)` cell, always owner-visible, never auto-applied (P7-00 Q: no automatic learning changes).
- **Size:** 2 agent-days. **Phase:** P7 extension / P10.

## 2. Basket performance insight: metrics and views

Notation: for basket B and window [t0, t1], members M(d) = B.members(asOf=d), N = |M|. Daily close c_i(d); r_i(d) = c_i(d)/c_i(d-1) - 1 (corporate-action adjusted when P1-10a adjustments exist; otherwise flag). Benchmark r_m(d) = Nifty 50 (or the chosen index). Use trading days only, India calendar. A metric below its minimum sample returns `{state: insufficient, n, min}` (P8 `MetricView`), never a number.

Two families:
- **Passive/universe family** (what the basket itself did): needs bars + membership only. Works today for any basket.
- **Strategy family** (what our strategies did in the basket): needs SimTrade/paper rows tagged to basket.

| # | Metric / view | Exact formula and window | Family | Exists? |
|---|---|---|---|---|
| m1 | Basket return (equal-weight, no rebalance, price) | R_B = (1/N_inc) * sum_i (c_i(t1)/c_i(t0) - 1); N_inc after exclusions (no_bars, starts_late, ends_early, discontinuity, bad_price); `insufficient` if N_inc < 3 | passive | **Yes, P8-07** (ADR 0102) |
| m1b | Daily-rebalanced equal-weight index | I_B(d) = I_B(d-1) * (1 + (1/N_d) * sum_i r_i(d)), N_d members with a bar on d; rebased 100 | passive | No (P8-07 is buy-and-hold). Needed for vol/DD/beta (buy-and-hold has drifting weights) |
| m2 | Excess vs Nifty 50 | R_B - R_m over window; and active return series a(d) = r_B(d) - r_m(d) | passive | Partly (both returns in P8-07; subtract in UI) |
| m3 | Annualised volatility | sigma = sqrt(252) * stdev(r_B(d)), windows 20/60/252 | passive | No |
| m4 | Max drawdown and duration | DD(d) = I_B(d)/max_{s<=d} I_B(s) - 1; MDD = min DD; duration = longest span between peaks | passive | No (P8-06 has MDD of R-curve for trades only) |
| m5 | Beta to Nifty 50, rolling | beta = Cov(r_B, r_m)/Var(r_m) over 60 d and 252 d windows; show 60 d rolling series; use Dimson (lag-1) beta for illiquid baskets | passive | No |
| m6 | Alpha / tracking | alpha_ann = 252 * (mean(r_B) - beta * mean(r_m)) (risk-free ignored, state it); tracking error = sqrt(252)*stdev(a(d)); information ratio = 252*mean(a)/(sqrt(252)*stdev(a)) | passive | No |
| m7 | Up/down capture | UpCap = mean(r_B | r_m>0)/mean(r_m | r_m>0); DownCap same for r_m<0 | passive | No |
| m8 | Relative strength line and rank | RS_B(d) = I_B(d)/I_m(d); RS return over 1/3/6/12 m; rank among the chosen baskets (1 = strongest); show rank history | passive | No |
| m9 | Breadth inside basket | b_k(d) = share of members with c_i(d) > SMA_k(i,d), k = 20/50/200; A/D share = adv/(adv+dec); new-high share = members at 52 w high / N; voter minimum like P3-01e (N_voters >= 50 or >= 5 for small baskets, state it) | passive | Partly (P3-01e: whole tracked set, k=20 only) |
| m10 | Dispersion | cross-sectional stdev of r_i(d) at day d; 20 d mean of it; also IQR | passive | No |
| m11 | Average pairwise correlation | rho_bar = 2/(N(N-1)) * sum_{i<j} rho_ij over 60 d daily returns (Ledoit-Wolf shrunk toward constant correlation); matrix view up to N <= 40, otherwise clustered heat map by sector | passive | No (P7-00 notes no correlation code) |
| m12 | Effective number of bets | N_eff = 1 + (N-1)*(1 - rho_bar) approx, or exp(entropy of PCA eigenvalue shares) | passive | No |
| m13 | Member turnover | (adds + removes in window) / average N; for rule baskets also "churn at last rebalance"; from membership history | passive | Partly (history exists for manual; rule baskets need U1) |
| m14 | Capacity / liquidity | ADV20 turnover (Rs) per member; basket median and 10th percentile; capacity per member = 1% of ADV20 (setting) -> basket max notional; share of members with ASM/GSM; share F&O; lot value for F&O | passive | Partly (turnover data exists; no basket view) |
| m15 | Contribution by member | c_i = w_i * (c_i(t1)/c_i(t0) - 1) / R_B with w_i = 1/N_inc; top 5 and bottom 5, and "share of return from top 3" (concentration) | passive | No |
| m16 | Regime-conditioned performance | R_B, vol, hit rate (share of days r_B > r_m) grouped by regime code of previous session (1..9, or the 3x3 trend x vol); n days shown; hatched if < 30 days | passive | No (regime exists in context; P8-09 "regime" lens disabled until context) |
| m17 | Strategy x basket expectancy | per cell: n trades, win rate, expectancy E[net R], credible lower bound (Beta-shrunk as P6-01), total R, profit factor, MaxDD in R; masked below 30 | strategy | **Yes**, P8-09 (`what-works?lens=basket,strategy`, pivot) |
| m18 | Strategy result vs basket hold and index | trade-set R-curve vs m1; "excess over passive" with the same dates | strategy | **Yes (P8-09 columns)** |
| m19 | Basket x side/style/timeframe/exit grids | same as m17 with lens basket,side etc. | strategy | **Yes, P8-09** |
| m20 | Basket x regime x strategy | m17 grouped by regime tag (needs P6-02 tags on trades) | strategy | Not yet (dimension disabled with reason) |
| m21 | Sim-vs-paper per basket | mean(paper net R - sim net R) per basket, slippage per basket/tier | strategy | P10-04 (not basket-sliced yet) |
| m22 | Overlap / double-count | share of basket trades also in another basket; Jaccard between member sets | both | **Yes, in P8-09 as "not additive" note**; Jaccard no |
| m23 | Hold-out stability | m17 on first half vs second half of the window; sign agreement | strategy | No; cheap guard against over-reading |

Minimum samples (defaults, stored settings): trades per cell 30 (already the P8 rule); days per regime row 30; members per basket for breadth/dispersion 5 (state), for return 3 (already).

Views to build on P8 (all read-only, GET only, URL state, masking by server as ADR 0105):
1. **Basket card / dashboard:** m1b rebased line vs Nifty 50 with the excess area, m3-m7 as a table, MDD ribbon, rolling beta line, `N_inc / N` with the exclusion list.
2. **Inside the basket:** breadth lines (m9), dispersion and rho_bar lines (m10, m11), contribution bar (m15), member table (return, RS rank, ADV, flags, contribution).
3. **Correlation:** matrix or clustered map (m11) with cluster ids that feed U8.
4. **Regime panel:** m16 bars by regime, with days per bar.
5. **Strategy x basket heat grid and equity overlay:** already P8-09; add hold-out stability (m23) and regime column (m20).
6. **Turnover and capacity:** m13, m14.
7. **Ranking board:** all baskets on RS rank (m8) and risk-adjusted return (return / vol), with `insufficient` shown rather than rank.

Where it lands: new `GET /api/reports/baskets/{id}/profile?from&to&index` (m1b-m16, one endpoint, pure `BasketAnalytics` in Domain/Reporting, computed from daily bars; cache by (basket, rule_version, asOf, window)); strategy-family stays on the existing endpoints. Performance target: 6 baskets x 50 members x 300 daily bars, p95 < 1.5 s (same budget as P8-07).
Size: profile endpoint + domain 5 agent-days, screen panels 4 agent-days (some reuse of `TimeSeriesChart`, `HeatGrid`). Recommended as P8-10x slices.

## 3. "Take more leverage", interpreted safely

**Principle:** leverage here means capital efficiency inside rules: better use of the capital and margin the owner already has, with evidence per basket, shown as advisory numbers. No setting places an order (P7/P12 rule), nothing raises exposure automatically, and every leverage-like setting defaults OFF or to the most conservative value.

Safe mechanisms (all advisory, all default off or neutral):
1. **Basket risk budgets:** `basket.risk_budget_pct` of the day's open risk (e.g. 40% max for any single basket), default = unlimited off. Surfaced in the plan as "uses 55% of budget".
2. **Volatility-targeted size scale (U7):** scale in [0.5, 1.0] by default; a scale above 1.0 needs a second setting `vol_target.allow_upsizing` (default OFF), owner act with password, and never above the risk profile's `risk_per_trade`. Net effect: lower size in turbulent baskets, not a bigger book in calm ones.
3. **Correlation-aware sizing (U8):** cluster cap on open risk.
4. **Margin-eligible lists:** a basket "MIS-eligible by the owner's broker" (the owner pastes or imports the broker's approved list; we do not scrape). Use it only to **filter** intraday candidates to names the broker will allow, and to show the leverage the broker quotes per name as information. Margin per name is the broker's, not ours.
5. **Exposure caps (existing):** total open risk, per sector, per basket, net long/short, gross notional vs funds; show "margin used vs available" from the last sync (read-only).
6. **Capacity guard:** cap suggested quantity at k% of ADV20 / minute volume per liquidity tier (m14), so "more size" is only suggested where fills are plausible.
7. **Evidence gate for any increase:** a basket's cap may rise only if (a) strategy x basket cell lower bound > 0 with n >= 100, (b) hold-out stable (m23), (c) paper-vs-sim slippage known (P10/P11), and (d) the change goes through the hypothesis loop and owner approval, as in P6-10/P10-05. Auto option follows the repo's rule: setting ON = no manual approval, OFF (default) = manual, recorded as auto with the setting snapshot, `unavailable` never counts as a pass.
8. **Reductions can be automatic, increases cannot:** a downside throttle (U15, volatility spike, drawdown) may be on by default as a suggestion; any upside change is an owner act.

Regulatory / exchange constraints found (all from secondary sources; **UNVERIFIED against primary SEBI/NSE circulars** unless stated):
- **Peak margin (SEBI, effective from 1 Dec 2020, phased to 100% from 1 Sep 2021):** brokers must collect margin upfront for intraday and delivery trades; clearing corporations take at least four random snapshots a day of client margins; the highest is the peak; shortfall penalties are 0.5% to 1% rising to 5% for repeated shortfalls (UNVERIFIED) https://www.angelone.in/news/sebis-peak-margin-rules-will-volumes-be-impacted , https://www.angelone.in/news/market-updates/sebi-peak-margin-norms-to-kick-in-from-september-1 , https://www.business-standard.com/amp/article/markets/sebi-directs-exchanges-to-implement-a-framework-to-monitor-margins-120010901557_1.html
- **Cash-market intraday leverage:** the minimum upfront margin is VaR + ELM, with a floor widely quoted as 20% of trade value, which gives at most about 5x on cash MIS (broker-specific; UNVERIFIED) https://onetradejournal.com/brokers/intraday-leverage-myths , https://www.bajajfinserv.in/sebi-new-margin-rules-intraday-trading . A report says SEBI is reviewing replacing the 20% minimum with a risk-based margin; not confirmed final https://www.niftytrader.in/markets/?p=29025 .
- **2022 relaxation:** peak-margin calculation changed to use the beginning-of-day margin (UNVERIFIED) https://www.business-standard.com/article/markets/relief-for-brokers-as-sebi-relaxes-norms-for-calculating-peak-margins-122051100516_1.html
- **Sale proceeds credit:** after 1 Dec 2020, selling from demat (T+1) holdings gives only 80% credit against the sale value for new trades that day (UNVERIFIED) https://www.business-standard.com/article/markets/sebi-s-new-margin-norms-will-be-painful-for-investors-market-participants-120081900301_1.html
- **Derivatives (2024-2025):** SEBI circular of 1 Sep 2025 sets intraday position limits for index options (net Rs 5,000 crore, gross Rs 10,000 crore futures-equivalent) with at least four random snapshots, one between 14:45 and 15:30, effective 1 Oct 2025; expiry-day penalty clause from 6 Dec 2025 https://www.business-standard.com/amp/markets/news/sebi-issues-new-rules-for-monitoring-intraday-derivative-positions-125090200101_1.html , https://www.taxmann.com/post/blog/sebi-sets-intraday-position-limits-for-equity-index-derivatives . Irrelevant at our size but shows monitoring is real time. The 2024-10-01 derivatives measures (lot size, weekly expiry limits) are already listed in P14-00 as UNVERIFIED.
- **Delivery/CNC:** no leverage; MTF (margin trading facility) and pledge-based margin exist but are broker products with their own eligible lists and interest; **not researched here** (no source opened). Dhan's `MTF leverage` flag is in the instrument master.
- **Algo rules:** SEBI/NSE retail algo framework (broker-registered strategies, static IP, order-rate limits) is already the subject of P12/P11-05 and doc 19; not re-researched here. Until P12 the system places no orders, so none of these rules applies to the system's own behaviour; they apply to the owner's manual orders at the broker, which the broker enforces.
- **Consequence for design:** treat all margin figures as **inputs the broker provides** (the account sync's available funds, the broker's per-name MIS/MTF multiplier), store them with a date, never derive or hard-code leverage ratios, and show `unavailable` when absent. Add a data row "regulatory facts" with `confirmed` status after the owner or an agent opens the primary circulars (P14-01 already does this for derivatives).

## 4. Ranked recommendation: top 8 to build first, and what not to do

Ranking = (evidence strength x usefulness) / (cost x overfit risk), with dependencies.

| Rank | Build | Why first | Depends on | Size (d) | Phase |
|---|---|---|---|---|---|
| 1 | **U1 rule-based baskets with recorded dated membership** (index, liquidity tier, volatility tier, sector, F&O) | Everything else needs it; starts the point-in-time clock now | P1-15 | 5 | P7.5 / early P8 |
| 2 | **Basket profile report m1b-m16** (return, vol, MDD, beta, RS rank, breadth, dispersion, correlation, contribution, capacity) | Pure read, no overfit, answers "full performance insight" | U1 (works on manual baskets earlier), P8-07 | 9 | P8-10x |
| 3 | **U8 correlation clusters + per-basket/cluster caps** in selection | Cheapest real risk gain; half in P7 plan | P7-02 | 3 | P7 ext |
| 4 | **U6 basket breadth/dispersion/correlation context and use in the market gate** | Makes the P7-06 gate real with a cleaner universe | P3-01e, U1 | 4 | P8 |
| 5 | **U4+U9 relative-strength rank and index-relative features** (fixed lookbacks) | Best-evidenced signal; fills `sector_rs`, `RS_ROTATION` | U1 | 5 | P8-P9 |
| 6 | **U5 sector strength** + `RS_ROTATION` swing test | Natural second step of 5 | #5 | 3 | P9 |
| 7 | **U7 vol-targeted sizing + basket risk budgets** (down-scaling only, sim test first) | Capital efficiency without more risk | #3, sim option | 4 | P7 ext / P10 |
| 8 | **U3 `universe` cell dimension (3 behaviour tiers)** and **U13 AI basket tools** | Learning conditioning once data exists; assistant makes it usable | P6-01/02, U1, #2 | 3+2 | P9-P10 |

Later or conditional: U10 ML basket features (only after the baseline model exists and ablation says lift), U2 grid of strategy x basket (pre-registered 8 baskets), U14 paper per basket, U15 basket throttles, U11 pairs (inside P14, kill criterion stands).

**What NOT to do**
1. **No data-mined baskets.** Never build a basket from "stocks that did well in the backtest" (then evaluate on the same window). Baskets are defined by a rule fixed before looking at strategy results, stored with `rule_version`, and changed only by a new version (new basket id).
2. **No look-ahead membership.** Do not evaluate history with today's members. Until membership has been recorded point in time, label results "members as of <date>" (existing P3/P8-07 convention). Index membership for dates before our first snapshot is not recoverable from free files (UNVERIFIED) and must not be faked.
3. **No survivorship hidden.** Delisted/retired names are not in the master; say so on every basket view (ISS-22). Equal-weight price-return only; no dividends in the passive numbers.
4. **No cell explosion.** At most one new cell dimension (`universe`, 3 tiers, default `*`); a child cell is used only if its lower bound beats the parent's. Every basket x strategy comparison counts toward N and uses the `Factor(N)` machinery; hold-out stability m23 before believing a ranking.
5. **No ranking by tiny n.** Rows below 30 trades or 30 days show `insufficient`, never a rank or colour. No "best basket" label unless the credible lower bounds separate.
6. **No leverage by stealth.** No setting that increases size, scale up above 1.0, raises margin use, or places or modifies an order. Increases are owner act with password, evidence-gated, default OFF. No ML model output may change size or risk without the hypothesis loop and approval.
7. **No short momentum factor assumptions.** Long-only momentum vs a basket; do not backtest long/short factor spreads we cannot trade (limited short selling/SLB depth, UNVERIFIED).
8. **No tuned parameters on a single regime.** Lookbacks fixed from the literature (126/252/skip 21, EWMA 0.94); thresholds not optimised on PnL; regime splits report coverage and days.
9. **No double counting.** Overlapping baskets are never summed (P8-09 rule); portfolio totals come from trades, not from baskets.
10. **No unreviewed regulatory numbers in code.** Margin multipliers, 20% floor, penalty rates come from the broker/sync or a `confirmed` fact row, never constants.

## 5. Source list (opened = only the search snippet; none fetched in full)
- Nifty200 Momentum30 factsheet https://www.niftyindices.com/Factsheet/Factsheet_Nifty200_Momentum30.pdf
- Moreira & Muir, Volatility-managed portfolios https://www.nber.org/papers/22208
- Barroso & Santa-Clara, Momentum has its moments https://ciencia.ucp.pt/en/publications/momentum-has-its-moments/
- Cederburg et al. critique https://www.lehigh.edu/~xuy219/research/COWY.pdf
- Moskowitz & Grinblatt, Do industries explain momentum https://ideas.repec.org/a/bla/jfinan/v54y1999i4p1249-1290.html
- Asness, Moskowitz, Pedersen https://pages.stern.nyu.edu/~lpederse/papers/ValMomEverywhere.pdf
- MSCI sector momentum https://www.msci.com/research-and-insights/quick-take/sector-momentum
- Gatev et al. summary https://alphaarchitect.com/2013/06/pairs-trading-kicks-butt/
- Zweig breadth thrust https://articles.stockcharts.com/article/articles-arthurhill-2019-01-perspective---40-years-of-the-zweig-breadth-thrust---weight-of-the-evidence/
- Harvey & Liu backtesting https://people.duke.edu/~charvey/Research/Published_Papers/P120_Backtesting.PDF
- HRP https://en.wikipedia.org/wiki/Hierarchical_Risk_Parity
- India momentum studies https://www.addletonacademicpublishers.com/contents-emfm/248-volume-9-3-2014/2268-evidence-and-sources-of-momentum-profits-a-study-on-indian-stock-market , https://sgbeta.inflibnet.ac.in/items/3cabc26f-85b5-4924-977d-4e6aa4e177f0/full
- Margin/regulation: URLs in section 3.
- Not found/not opened: Bailey & Lopez de Prado deflated Sharpe ratio, Avellaneda & Lee, Krauss survey, Ledoit-Wolf, Hamilton regime switching, any primary SEBI circular text. These remain UNVERIFIED and should be fetched when the relevant slice is specced.

## 6. Open questions for the owner
1. Is a rule-basket nightly job (U1) acceptable as the first step, and which 8 baskets are pre-registered (suggested: Nifty 50, Nifty 100, Midcap 150, F&O stocks, Top-100 liquidity, Top-100 low-vol, Top-100 momentum, one per 4 largest sectors)?
2. Which index to benchmark against by default (Nifty 50 vs Nifty 500)?
3. Should the broker's MIS/MTF eligible list be imported (file) as a basket, or kept out of scope until P12?
4. Is down-scaling-only volatility targeting enough for "more leverage", or does the owner want up-scaling studied (simulation only, never active)?
