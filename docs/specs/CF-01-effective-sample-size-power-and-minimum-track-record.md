# CF-01 — Effective sample size, power and minimum track record

- **Status:** Draft (written 2026-10-27 under the owner's delegation; decisions below researched; the owner's Approved is needed before build)
- **Phase / roadmap row:** CF programme ([CF-00](CF-00-confidence-programme-plan.md)), slice 1; touches P5 (Lab verdict) and P6 (cell statistics)
- **Design refs:** docs/design/09-learning-engine.md layer 1 · 11-strategy-lab.md §4 · 16-configuration.md (`learning.min_trades`, `qualification_gates`) · [P6-01](P6-01-cell-statistics-bayesian-and-recency.md) (its "credible interval treats trades as independent" limit) · [P5-03](P5-03-test-ledger-and-the-gates.md) · research [confidence-data-and-small-sample](../research/confidence-data-and-small-sample.md) §2.2, §2.3, §2.9
- **Needs:** nothing unbuilt. Built: `CellStatsCalculator`, `VerdictEvaluator`, `OosEvidenceBuilder`, `research.cell_stats`, `lab.lab_results`.
- **Size:** 4.5 agent-days (calculator and power 1.5; cluster key carried into `CellTrade` and the OOS trades 1; cell-stats and verdict wiring plus migration 1; labels, API, screens text 0.5; tests and ADR 0.5)
- **Safety:** no network, no order, no change to a stored verdict. Gate mode is OFF.

## Goal
Every cell row and every Lab verdict shows how many trades it really rests on (effective n, after same-day clustering and serial correlation), how many it would need to see an edge of the size the gates ask for, the smallest edge it could see now, and the minimum track record length, so "34 trades" can no longer look like 34 independent facts.

## Decisions (researched)
1. **Effective n is the smaller of a cluster estimate and a serial estimate, not their product.** Reason: same-day correlation and lag-1 autocorrelation partly describe the same dependence (a market-wide day makes consecutive same-day trades alike); multiplying the two design effects would double count and make most cells read about 1. Rejected: the product. Source: Kish design effect (https://en.wikipedia.org/wiki/Design_effect, background); the effective-sample-size correction for AR(1) is the standard `n (1 - r1) / (1 + r1)` (Bailey and López de Prado treat autocorrelation in the same way; the formula is standard, hand-checked below).
2. **The cluster is the India entry session date.** Reason: `SimTrade.TradeDate` exists and is the correlation that matters most (one gap-down day, 50 stocks); sector or basket clusters are optional later (BK-08 correlation clusters are not built: UNVERIFIED). Rejected: instrument as cluster (it would not catch the cross-sectional same-day case, which is the large one). Source: research §2.2 example, repo `SimTrade`.
3. **Cluster correlation is the one-way ANOVA (ICC) estimator, clipped to [0, 1].** `rho = (MSB - MSW) / (MSB + (m0 - 1) MSW)`, `m0 = (N - sum(m_i^2)/N) / (k - 1)`, design effect `DE = 1 + (m_bar - 1) rho` with `m_bar = N / k`, `n_eff = N / DE`, never above N and never below 1. Reason: closed form, no iteration, pure. A negative estimate is set to 0 (no credit above N: shrinking the evidence is allowed, inflating it is not). Rejected: a mixed-model fit (needs a solver in Domain). Source: standard one-way random-effects ANOVA (Snijders and Bosker, *Multilevel Analysis*; book not opened here: UNVERIFIED), Kish for DE.
4. **Serial correlation uses the date-ordered daily mean R series** (one value per session), not the trade list. Reason: the trade list has ties within a session, which belong to the cluster estimate. It is estimated only when there are at least 20 sessions (`Confidence:EffectiveN:MinSessionsForAutocorr`, default 20); below that the serial estimate is `unavailable` and the cluster estimate alone is used. A negative lag-1 is set to 0. Rejected: ignoring serial dependence (swing trades overlap in time: `SessionsHeld`). Not done in v1: assigning a swing trade to every session it was open on (a trade belongs to its entry session only); the overlap graph is a later improvement and the report says so.
5. **n_eff is unweighted.** The recency weights of P6-01 change how much each trade counts in the mean, not how independent the trades are. n_eff is computed on the window's trades without weights and printed beside the weighted `N`. Rejected: Kish weight-based n_eff in the same number (it would conflate two things). 
6. **Power uses the sample standard deviation of R, floored.** `sigma = max(sampleSD, Confidence:Power:SigmaFloorR)`, floor default 0.5 R, because the SD of 10 trades is unreliable. Alpha one-sided 0.05, power 0.80 (z 1.6449 and 0.8416), both tunable. The required n is `ceil(((z_a + z_b) sigma / delta)^2)` in **effective** trades; the minimum detectable edge for the n_eff held is `(z_a + z_b) sigma / sqrt(n_eff)`. Reason: the same two numbers answer "how many more" and "how big an edge could I see". Rejected: a t-quantile version (matters below n = 15, where the cell is `unavailable` anyway). Source: standard one-sample power formula (research §2.9).
7. **MinTRL (minimum track-record length) is shown per trade, not per year.** `MinTRL = 1 + (1 - skew SR + (kurt - 1)/4 SR^2) (z_a / (SR - SR*))^2` with per-trade `SR = mean R / sd R`, `SR* = 0` default, skew and kurtosis from the window (sample, `kurt` = 3 for normal). Needs n >= 20 for skew and kurtosis; below that, normal values (0 and 3) are used and the row says "normal assumed". Reason: Bailey and López de Prado's formula is in per-observation units; the research note's worked value 273 was recomputed (below). Rejected: annualised units (the engine has no common period). Source: Bailey and López de Prado, "The Sharpe ratio efficient frontier" (https://ideas.repec.org/a/rsk/journ4/2223785.html; formula constants to be checked against the paper before coding: UNVERIFIED, hence a test against the value derived by hand here).
8. **How n_eff is used: three modes, default `ReportOnly`.** `Off` (nothing shown), `ReportOnly` (shown everywhere, no verdict or "enough" flag changes), `Gate` (the `Enough` flag of a cell row and gate 1 `oos_trades` of the verdict compare **n_eff** with `min_trades`/`MinOosTrades` instead of n). Reason: the new rule is stricter and would move existing verdicts; CLAUDE.md and the programme rule forbid doing that silently. In `ReportOnly` the verdict adds a limit line "would read FAIL on trades if effective n were used" and the cell row carries `enoughByNEff` beside `enough`. Rejected: switching straight to `Gate`. 
9. **A cell with n below 5 gets no n_eff, power or MinTRL.** They read `unavailable: n < 5`. Reason: variance components from 4 points are noise.
10. **Where the numbers appear:** `CellStatsRow` (new members), the `research.cell_stats` snapshot columns, the Lab verdict document (`oosEvidence` block gains `nEff`, `designEffect`, `neededTrades`, `minDetectableEdge`, `minTrl`, all optional), the Lab report limits, and the P8 Learning screen text "n=34, effective 11". Reason: "on every cell/verdict" is the owner's requirement.

## Scope
**In:** pure `Domain/Confidence/` (`EffectiveSampleSize`, `PowerAnalysis`, `MinTrackRecord`, `ConfidenceOptions`); `CellTrade` gains `EntryDate` and `ClusterKey` (session date as text, default derived); `OosEvidence` gains an optional `Dependence` block; wiring in `CellStatsCalculator.Row(...)`, `OosEvidenceBuilder`, `VerdictEvaluator` (limit line and, only in `Gate`, the oos_trades comparison); migration adding nullable columns to `research.cell_stats`; the optional member in `lab.lab_results.result` (no migration); `GET /api/cells` and `GET /api/lab/runs/{id}` view fields; ADR; docs; the `Confidence` settings keys.
**Out:** shrinkage ([CF-02](CF-02-empirical-bayes-shrinkage-of-expectancy-and-parent-child-pooling.md)); block bootstrap and deflation ([CF-03](CF-03-deflated-sharpe-pbo-and-block-bootstrap.md)); sector/basket clusters; the overlap graph of swing trades; sequential tests; changing the credible interval itself (CF-02 does that with n_eff); the readiness scorecard (CF-12); any frontend redesign (text only, prototype-matched labels).

## Acceptance criteria
Every number below was recomputed by hand and by script on 2026-10-27.
1. **Cluster, equal clusters.** 30 trades, 6 on each of 5 dates, within-date correlation 0.5 (given): `DE = 1 + (6 - 1) × 0.5 = 3.5`; `n_eff = 30 / 3.5 = 8.57`. *Unit: `EffectiveSampleSizeTests.KishEqualClusters` (the function that takes rho directly).*
2. **Cluster, estimated from data.** 12 trades in 3 sessions of 4: session A R = 1, 1, 0.5, 1.5; B = -1, -0.5, -1, -1.5; C = 0.2, -0.2, 0, 0. Grand mean 0; SSB = 4 × (1² + (-1)² + 0²) = 8, df 2, MSB = 4; SSW = 0.5 + 0.5 + 0.08 = 1.08, df 9, MSW = 0.12; m0 = 4; `rho = (4 - 0.12) / (4 + 3 × 0.12) = 3.88 / 4.36 = 0.8899`; `DE = 1 + 3 × 0.8899 = 3.6697`; `n_eff = 12 / 3.6697 = 3.27`. *Unit.*
3. **Many stocks, one day.** 50 trades on one date with average pairwise correlation 0.4: `n_eff = 50 / (1 + 49 × 0.4) = 50 / 20.6 = 2.43`. *Unit (rho input form).* One cluster of all trades: the ANOVA has k = 1 and is `unavailable`; the function then returns n_eff = 1 for a single-date set and says why.
4. **Serial.** Daily mean R series 2, 2, 1, -1, -2, -2: mean 0; sum of squares 18; lag-1 products 4 + 2 - 1 + 2 + 4 = 11; `r1 = 11 / 18 = 0.611`; `n_eff = 6 × (1 - 0.611) / (1 + 0.611) = 1.45`. And `n = 100, r1 = 0.3` gives `100 × 0.7 / 1.3 = 53.85`. A negative r1 gives n_eff = n. A series shorter than the minimum gives `unavailable`. *Unit.*
5. **Combination.** n_eff is the smaller of the two defined estimates; with only one defined, that one; with none, `unavailable` and `n` is shown alone. Never above n, never below 1. *Unit.*
6. **Power.** sigma 1.2 R, edge 0.15 R, alpha 0.05 one-sided, power 80 %: `((1.6449 + 0.8416) × 1.2 / 0.15)² = (19.892)² = 395.7`, **396** trades (the research note printed 395 from a rounded z of 2.485). Minimum detectable edge: n = 34 gives `2.4865 × 1.2 / √34 = 0.512 R`; n_eff = 11 gives `2.4865 × 1.2 / √11 = 0.900 R`. With the floor: sample SD 0.3 reads sigma 0.5 (floor), not 0.3. *Unit.*
7. **MinTRL.** per-trade SR 0.10, SR* 0, skew 0, kurt 3, 95 %: `1 + (1 + 0.5 × 0.01) × (1.6449 / 0.10)² = 1 + 1.005 × 270.6 = 273.0`, **273** (the note's value, reproduced). SR ≤ SR* gives `unavailable: no edge to detect`. *Unit.*
8. **Years to evidence.** A cell with 34 trades and n_eff 11 over 2.1 years has n_eff arriving at `11 / 2.1 = 5.24` a year; to reach 396 at that rate takes `396 / 5.24 = 75.6` years, printed as "about 76 years at this rate". A cell with zero rate prints `unavailable`. The text always states "if the rate and the edge hold". *Unit.*
9. **Cell rows.** `Compute` gives each row `NEff`, `DesignEffect`, `NEffMethod` (`cluster|serial|none`), `NeededTrades`, `MinDetectableEdgeR`, `MinTrl`, `EnoughByNEff`; the existing `Enough`, `N`, the windows, the weights and every existing field are byte-for-byte unchanged in `Off`, `ReportOnly` and `Gate` except `Enough` in `Gate`. A snapshot recomputed from the same stored trades gives the same numbers. *`CellStatsCalculatorTests` regression table from P6-01 AC 2 still passes unchanged.*
10. **Verdict.** In `ReportOnly` the outcome, the six gates and their reasons equal today's byte for byte, and the verdict carries `dependence` and the limit line when n_eff is below the gate. In `Gate`, the `oos_trades` gate compares `ceil(n_eff)` to `MinOosTrades × MultipleTesting.Factor(N)`. Worked: gate 30, N = 16 variants gives factor 2.0 and threshold 60; 34 trades with n_eff 11: `ReportOnly` reads 34 vs 60 as today (the gate fails; 34 is not below half of 60, so not a hard miss) and the added line says "effective 11 of 60 needed"; `Gate` reads 11 vs 60, below half (30), a **hard** miss. *`VerdictEvaluatorTests`.*
11. **Old data.** An old `lab_results` document and an old snapshot read "not computed"; no migration touches existing rows' values. *Integration.*
12. **Trade carries its cluster.** `CellTrade.EntryDate` and `ClusterKey` come from `SimTrade.TradeDate`; the loader sets them; an old call that builds `CellTrade` with four arguments still compiles and gets the exit date as cluster (and the row says `NEffMethod = none`, since a cluster per exit date is not the entry cluster). *Unit + handler test.*
13. **No leak.** n_eff at an as-of date uses only trades that exited on or before it (the same window rule as P6-01 AC 6). *Calculator test.*
14. Contract regenerated (`scripts/gen-api.sh`); architecture tests green; `scripts/check.sh` green.

## Contract
- **Settings** (section `Confidence`, versioned, owner-tunable; all defaults shown):
  `EffectiveN:Mode = ReportOnly` (`Off|ReportOnly|Gate`), `EffectiveN:MinSessionsForAutocorr = 20` (5..250), `EffectiveN:MinTradesForEstimate = 5` (3..50), `Power:Alpha = 0.05` (0.001..0.2), `Power:Power = 0.80` (0.5..0.99), `Power:SigmaFloorR = 0.5` (0.1..5), `Power:TargetEdgeR = 0.15` (the gate's `MinExpectancyR` is the default source), `MinTrl:Confidence = 0.95`, `MinTrl:BenchmarkSr = 0`. Out of range values are refused on start (`ValidateOnStart`) and on the owner's change; changing `Mode` to `Gate` is an owner act with a recorded snapshot.
- **API:** `GET /api/cells`, `GET /api/cells/{key}` rows gain optional `dependence { nEff, designEffect, method, neededTrades, minDetectableEdgeR, minTrl, yearsToEvidence, enoughByNEff, note }`; `GET /api/lab/runs/{id}` verdict gains the same optional block under `oosEvidence`. All members nullable; unknown = `null` plus `note: "unavailable: <reason>"`.
- **Data:** migration `AddCellStatsDependence`: nullable `n_eff double precision`, `design_effect double precision`, `n_eff_method text` (check in `cluster, serial, none`), `needed_trades integer`, `min_detectable_edge_r double precision`, `min_trl integer`, `enough_by_n_eff boolean` on `research.cell_stats`; Down drops the columns; the table stays insert-only for `xd_app` (`ResearchGrantsTests`). No migration for the Lab document (jsonb).
- **Domain API (pure):** `EffectiveSampleSize.Cluster(IReadOnlyList<(string Key, double R)>)`, `EffectiveSampleSize.Serial(IReadOnlyList<double> dailyR, int minSessions)`, `EffectiveSampleSize.Combine(...)`; `PowerAnalysis.TradesNeeded(sigma, delta, alpha, power)`, `PowerAnalysis.MinDetectableEdge(sigma, nEff, alpha, power)`; `MinTrackRecord.Trades(sr, srStar, skew, kurt, confidence)`; normal quantile reused from the existing distribution helper next to `BetaDistribution` (name and location UNVERIFIED; if absent, a 40-line rational approximation with a test against the table values 1.6449 and 0.8416).
- **UI:** text only in the existing Learning cell table and the Lab report limits ("n=34, effective 11; needs about 396 to see 0.15 R; smallest visible now 0.51 R"); no new screen. Loading, empty and unavailable states reuse the existing row states.

## Non-functional
Pure and deterministic, O(n) per cell per window; adds milliseconds to a snapshot of a few thousand trades. Logs carry counts only. No secret, no network.

## Test plan
- **Unit (Domain):** the hand tables in AC 1 to 8 with tolerance 1e-3; edge cases: all trades on one date; one trade per date (rho clipped to 0, n_eff = n); identical R everywhere (MSW = 0, MSB = 0: define 0/0 as rho 0); sigma = 0 (floored); NaN or infinity input (`unavailable`, never a pass); k = 1 or n < 5; a monotone series (r1 near 1: n_eff near 1 not 0).
- **Property tests:** n_eff ≤ n; n_eff ≥ 1; adding an independent trade never lowers n_eff by more than 1; the function is order-independent for the cluster estimate.
- **Application:** compute handler writes the new columns; an old snapshot reads null; a recomputation is idempotent.
- **Integration:** API returns the block; `ReportOnly` verdict equals the stored one; `Gate` changes only `oos_trades`; anonymous 401 as before.
- **Frontend (MSW):** the text renders for a cell with and without the block.
- **Architecture:** `Domain/Confidence` has no dependency outside Domain.

## Honest limits (printed with the numbers)
- n_eff is an estimate from the same thin sample it corrects; at n below 20 the cluster ICC and any autocorrelation are noisy (the row says so).
- It captures same-session and serial dependence; it does not capture dependence through a common factor across weeks (a regime).
- The entry-session cluster is coarse: two trades on different days can still be one macro move.
- Power assumes roughly normal R; R is skewed and fat-tailed, so the required n is a lower bound.
- A power statement about an edge says nothing about whether the edge exists.

## Risks
1. `Gate` would turn many "enough" cells into "not enough": that is the point, and the reason it is OFF. Mitigation: ReportOnly shows the count of cells that would flip before the owner decides.
2. The ANOVA estimator is unstable with many singletons and a few big clusters; the clip and the n < 5 rule limit harm. Mitigation: property tests.
3. The `CellTrade` signature change touches every constructor call (P6 tests, P8-05 `WhatWorks`, `PointInTimeCells`): UNVERIFIED count; mitigated by optional parameters at the end.

## UNVERIFIED
- The normal-quantile helper's existence and location in `Domain`.
- That `OosEvidenceBuilder` can see the entry date of each OOS trade (it builds from the selection's winners; `SimTrade.TradeDate` exists, the path to it was not opened).
- The exact MinTRL constants against the paper (hand-checked here, 273).
- Whether other callers (`PointInTimeCells`, `WhatWorksBuilder`, `MetaPolicyEvaluator`) need n_eff too; only cells and the verdict are in scope.

## Open questions (owner)
1. Confirm the three modes and the `ReportOnly` default (recommended: yes).
2. Power target: alpha 0.05 one-sided, power 80 %, target edge = the 0.15 R gate (recommended: yes; the target edge for display could instead be 0.20 R).
3. May `Gate` later replace n by n_eff in gate 1 only, or also in the evidence label of cells? (Recommended: gate 1 and the evidence label together, after one month of ReportOnly numbers.)
4. Evidence label thresholds on n_eff: "watch" 30, "usable" 60 with a lower bound above 0, "scalable" 150 (proposals from the research note, UNVERIFIED as a standard; your call).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR added (effective n is shown beside n; `Gate` is the owner's switch); doc 09 and doc 11 text updated; `scripts/build-docs.sh`
