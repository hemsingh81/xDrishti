# CF-02 — Empirical-Bayes shrinkage of expectancy, and parent-child pooling

- **Status:** Draft (written 2026-10-27 under the owner's delegation; decisions below researched; the owner's Approved is needed before build)
- **Phase / roadmap row:** CF programme ([CF-00](CF-00-confidence-programme-plan.md)), slice 2; extends P6 layer 1 (cell statistics)
- **Design refs:** docs/design/09-learning-engine.md layer 1 · 16-configuration.md `bayes_prior` · [P6-01](P6-01-cell-statistics-bayesian-and-recency.md) (assumption 1: strength 20, parent includes the cell; limits) · ADR 0064 · research [confidence-data-and-small-sample](../research/confidence-data-and-small-sample.md) §2.1, §2.2
- **Needs:** [CF-01](CF-01-effective-sample-size-power-and-minimum-track-record.md) (`EffectiveSampleSize`, the `CellTrade` cluster key). Built: `BetaShrinkage`, `CellStatsCalculator`, `research.cell_stats`.
- **Size:** 3.5 agent-days (pure `NormalShrinkage` and estimators 1.5; leave-one-out parent chain and wiring into the calculator 1; migration, API fields 0.5; tests and ADR 0.5)
- **Safety:** no network, no order. Shrunk figures are shown beside the raw ones; nothing that exists changes by default.

## Goal
Each cell shows, next to its raw expectancy in R, a **shrunk expectancy** pulled toward its parent group by an amount the data themselves justify, with a lower credible bound, so a 10-trade cell at +0.40 R no longer outranks a 40-trade cell at +0.25 R, and small cells stay visible instead of being hidden by `min_trades`.

## Decisions (researched)
1. **A new `NormalShrinkage`, `BetaShrinkage` untouched.** Reason: `BetaShrinkage` is shared by the cell statistics and the report engine (its own comment: P8-05, ADR 0099); changing it would silently change both. Add the expectancy version beside it and an estimated-strength Beta *beside* the constant-20 one. Rejected: parameterising `BetaShrinkage` in place. Source: repo (`Domain/Learning/BetaShrinkage.cs`); engineering rule "prefer extending over modifying".
2. **Normal-normal model on mean R.** Cell true mean `theta_i ~ N(mu_parent, tau²)`; observed mean `m_i ~ N(theta_i, sigma_i² / n_i)`. Posterior mean `mu_parent + B (m_i - mu_parent)` with `B = tau² / (tau² + sigma_i² / n_i)`, posterior variance `B sigma_i² / n_i`. Reason: R is a continuous per-trade result; the standard conjugate form gives the research note's formula. Rejected: shrinking profit factor (a ratio, no clean posterior; reported raw only). Source: Efron and Morris (1973, 1975) on empirical Bayes shrinkage; Gelman et al., *Bayesian Data Analysis* 3rd ed. ch. 5 (hierarchical normal model; books not opened: UNVERIFIED as pages, the formulas are standard and hand-checked below).
3. **`n_i` is the smaller of CF-01's effective n and the Kish weight n of the recency weights, `(sum w)² / sum w²`.** Reason: the existing mean is recency-weighted; its precision is lower than the raw count and the trades are not independent. Where CF-01 is `unavailable`, `n_i` is the weight-based n alone and the row says so. Rejected: the raw count (it overstates the precision that decides B).
4. **tau² by DerSimonian-Laird across sibling cells.** `w_i = n_i / sigma_i²` (pooled `sigma²` where a cell has fewer than 5 trades), `m_w = sum(w_i m_i) / sum(w_i)`, `Q = sum w_i (m_i - m_w)²`, `tau² = max(0, (Q - (K - 1)) / (sum w_i - sum w_i² / sum w_i))`, over the K siblings of the cell's parent (cells with at least `Confidence:Shrinkage:MinTradesForSibling` = 5 trades). Then floor at `Confidence:Shrinkage:TauSquaredFloor` = 0.01 R². Reason: unequal n; closed form; estimated, not guessed. The floor is needed because the estimate is often exactly 0 with thin data (worked example 3 below: B = 0 for every cell, which would make every cell equal and hide ranking). Rejected: plain variance of the cell means (not corrected for the sampling noise of the means); a full REML fit (needs an optimiser in Domain). Source: DerSimonian and Laird, Controlled Clinical Trials 1986 (formula standard; paper not opened: UNVERIFIED).
5. **The Beta strength is estimated the same way, not constant 20.** Rates `p_i = wins_i / n_i` with binomial noise `p̄(1 - p̄)/n_i` give `tau_p²` by DL; the intra-class correlation `rho = tau_p² / (p̄ (1 - p̄))`; strength `s = (1 - rho)/rho`, clamped to `[Confidence:Shrinkage:StrengthMin = 5, StrengthMax = 200]`; with no estimate (rho = 0 or fewer than 3 siblings) the strength falls back to the configured 20 and the row says "default". The estimated-strength win rate is a **second** column; the existing posterior (strength 20) stays. Reason: P6-01 states strength is a constant; the data can say if 20 is too heavy or too light. Rejected: replacing 20. Source: Beta-binomial moment estimators (standard; Gelman BDA3 ch. 5: UNVERIFIED as pages).
6. **The parent excludes the cell itself (leave-one-out).** `mu_parent(i) = sum_{j != i} n_j m_j / sum_{j != i} n_j`. Reason: P6-01 states the parent contains the child, which pulls a cell toward itself; for a cell that is most of its parent (a strategy with one big cell) this is a large bias. Rejected: keeping the old parent (the limit stays on the old figures; the new column fixes it). Source: P6-01 "Honest limits"; research §2.1.
7. **The chain is regime cell → same cell with regime `*` → strategy → system.** The first parent that has at least `Confidence:Shrinkage:MinParentTrades` = 30 trades (excluding the cell) is used; the row records `parentLevel`. Reason: regime cells are the thinnest and the research note's chain. **Regime cells are not built** (P6-02 has no spec file in this folder; the regime is `*` in P6-01): until they exist the chain is `* → strategy → system`; the code takes the regime level as an optional parent and has a test with a synthetic regime. Rejected: a hard regime split (multiplies n by up to 9). Source: P6-00 (admits "most cells will stay below 30"); research §2.1.
8. **Display and use: mode `Confidence:Shrinkage:Mode`, default `ReportOnly`.** `Off` (no figures), `ReportOnly` (figures stored and shown beside, no label changes), `Gate` (the cell's evidence label uses the shrunk lower bound instead of raw n and mean). `Gate` is an owner act. Reason: programme rule 1. The report engine's win-rate shrinkage (P8-05) is not touched.
9. **The lower bound is the 5 % quantile of the posterior, `shrunk mean - 1.6449 sd`.** Reason: P6-01 defines the Beta "confidence" as the 5 % quantile (credible level 0.90, equal tails); the new column uses the same quantile so the two lower bounds mean the same thing. Rejected: a 1.2816 multiplier (a 10 % one-sided quantile under the same name).

## Scope
**In:** pure `Domain/Learning/NormalShrinkage`, `ShrinkageEstimators` (DL tau², estimated Beta strength, leave-one-out parent, parent chain); `CellStatsRow` new members; the calculator's second pass; migration adding nullable columns to `research.cell_stats`; view fields; ADR; docs; `Confidence:Shrinkage` keys.
**Out:** instrument-level cells and cross-sectional pooling by instrument or sector (research §2.2 second half: needs a cluster key beyond the session; later slice); shrinking profit factor, drawdown, MAE/MFE; changing `BetaShrinkage`, the report engine, ranking or selection (P7 reads neither); using shrunk figures in the Lab verdict (the Lab verdict is walk-forward on pooled OOS trades, not per cell); Bayesian model averaging; any screen redesign.

## Acceptance criteria
Numbers recomputed on 2026-10-27 (python and by hand).
1. **The research example.** Parent mean +0.10 R, sigma² 1.44, tau² 0.04. n = 10, mean +0.40: `B = 0.04 / (0.04 + 1.44/10) = 0.04 / 0.184 = 0.2174`; shrunk `0.10 + 0.2174 × 0.30 = 0.1652`; posterior variance `0.2174 × 0.144 = 0.03130`, sd 0.1769; 5 % quantile `0.1652 - 1.6449 × 0.1769 = -0.1258`. n = 40, mean +0.40 (the note's second case): `B = 0.04 / (0.04 + 0.036) = 0.5263`; shrunk `0.10 + 0.5263 × 0.30 = 0.2579`; variance `0.5263 × 0.036 = 0.01895`, sd 0.1376; quantile `0.2579 - 0.2264 = +0.0315`. So the 40-trade cell clears zero, the 10-trade one does not. *`NormalShrinkageTests`.*
2. **Win-rate analogue stays.** 6 wins in 10, parent 0.5, strength 20: `(10 + 6) / (20 + 10) = 0.5333`, unchanged by this slice. *Existing `BetaShrinkage` test passes unchanged.*
3. **tau² can be exactly 0.** Five sibling cells, sigma² 1.44, means 0.40, 0.10, -0.05, 0.20, 0.15 at n 10, 20, 30, 40, 25: weights `n/1.44` sum 86.81; weighted mean 0.130; Q = 1.337; K - 1 = 4; `Q - 4 < 0` so tau² = 0 (the spread is smaller than the noise). With the floor 0.01: the n = 10 cell has `B = 0.01 / (0.01 + 0.144) = 0.0649`, and its shrunk mean is `0.1065 + 0.0649 × (0.40 - 0.1065) = 0.1256` where 0.1065 is its leave-one-out parent (n-weighted mean of the other four: 0.1065). Without the floor B = 0 and every cell reads 0.1065. *Unit.*
4. **tau² positive.** Five siblings, sigma² 1.44, means 0.60, -0.30, 0.10, 0.45, -0.20 at n 10, 20, 30, 40, 25: weighted mean 0.128; Q = 8.856; `tau² = (8.856 - 4) / 66.67 = 0.0728`. Cell 1 (n = 10, mean 0.60): leave-one-out parent `(20×-0.30 + 30×0.10 + 40×0.45 + 25×-0.20)/115 = 10/115 = 0.0870`; `B = 0.0728 / (0.0728 + 0.144) = 0.3359`; shrunk `0.0870 + 0.3359 × 0.5130 = 0.2593`; variance `0.3359 × 0.144 = 0.04837`, sd 0.2199; 5 % quantile `0.2593 - 0.3618 = -0.1025`. *Unit.*
5. **Estimated Beta strength.** Five cells with wins 6, 12, 13, 24, 13 of n 10, 20, 30, 40, 25 (rates 0.60, 0.60, 0.433, 0.60, 0.52; pooled 0.544): binomial-noise Q = 2.42 < 4 so `tau_p² = 0`, `rho = 0`, no estimate: the row uses 20 and says "default". A set with a genuine spread gives `rho > 0` and `s = (1 - rho)/rho` within [5, 200]; a test fixes `rho = 0.0476` and expects `s = 20.0` (the doc 16 value, as a consistency check). *Unit.*
6. **Leave-one-out.** Two cells of one strategy: A with 10 trades, mean 0.5 R; B with 90 trades, mean 0.0 R. The old parent (includes the cell) is the strategy mean `(10×0.5 + 90×0)/100 = 0.05` for both. The new parent of A is B's mean 0.0; the new parent of B is A's mean 0.5. *Unit.*
7. **Parent chain.** With a synthetic regime cell of 12 trades and its `*` cell of 40 (excluding itself 28): the regime parent has fewer than 30 trades excluding the cell, so the next level is tried; `parentLevel` reads `strategy` when that has at least 30. With none at or above 30 the system level is used; with the system under 30 the row reads `unavailable: no parent with 30 trades` and shows raw figures only. *Unit.*
8. **Zero-information cases.** n = 0: shrunk mean equals the parent, quantile wide or `unavailable`; sigma = 0 with n ≥ 5 is floored at `Confidence:Power:SigmaFloorR` (CF-01); NaN or infinite input is `unavailable`, never a number; K < 3 siblings uses the default strength and the tau² floor and says so. *Unit.*
9. **Beside, not instead.** `Enough`, `N`, the raw expectancy, profit factor, the Beta posterior and `Lower` of every existing row are byte-for-byte the same in all three modes except the evidence label in `Gate`. The P6-01 regression table (AC 2 and 4 of P6-01) still passes unchanged. *Calculator tests.*
10. **Idempotent and no leak.** Recomputing the same as-of date gives the same numbers; a trade exiting after the as-of date changes nothing (the P6-01 AC 6 rule). *Tests.*
11. **Order independence.** Shuffling the cells in the input leaves each cell's shrunk figures unchanged. *Property test.*
12. Contract regenerated; migration present with Down; `ResearchGrantsTests` green (table stays insert-only); `scripts/check.sh` green.

## Contract
- **Settings** (`Confidence:Shrinkage`, versioned, owner-tunable): `Mode = ReportOnly` (`Off|ReportOnly|Gate`), `TauSquaredFloor = 0.01` (0..1), `MinTradesForSibling = 5` (3..30), `MinParentTrades = 30` (10..200), `StrengthMin = 5`, `StrengthMax = 200` (1..1000), `QuantileLevel = 0.05` (0.01..0.25). The existing `PriorStrength` (20) is untouched and still the fallback.
- **API:** `GET /api/cells`, `GET /api/cells/{key}` rows gain optional `shrinkage { parentLevel, parentMeanR, tauSquared, tauSource (estimated|floor), blend, shrunkMeanR, shrunkLowerR, estimatedStrength, strengthSource (estimated|default), shrunkWinRate, note }`; all nullable; unknown = null plus `note`.
- **Data:** migration `AddCellStatsShrinkage`: nullable `parent_level text` (check in `regime,star,strategy,system`), `parent_mean_r`, `tau_squared`, `blend`, `shrunk_mean_r`, `shrunk_lower_r`, `estimated_strength`, `shrunk_win_rate` (all `double precision`), `tau_source text`, `strength_source text` on `research.cell_stats`; Down drops them; insert-only grants kept.
- **Domain API (pure):** `NormalShrinkage.Posterior(m, sigma2, nEff, parentMean, tau2, level)` returns mean, lower, blend; `ShrinkageEstimators.TauSquared(IReadOnlyList<(double Mean, double N, double Sigma2)>, floor)`, `.BetaStrength(...)`, `.LeaveOneOutParent(...)`.
- **UI:** one extra column pair in the Learning cell table ("Shrunk R", "Lower"), placed by the existing table pattern; the evidence chip text unchanged in `ReportOnly`.

## Non-functional
Pure and O(cells × windows) with one extra pass; milliseconds for thousands of trades. Deterministic. Logs counts only.

## Test plan
Unit: AC 1 to 8 tables at tolerance 1e-3; property tests (blend in [0, 1]; shrunk mean lies between raw and parent; monotone in n: larger n gives a larger blend; scale: multiplying R by 2 multiplies shrunk mean by 2 and tau² by 4); DL on K = 1, 2, 3; zero variance. Application: handler writes the columns, old snapshot null, idempotent. Integration: API fields and null for old rows. Frontend (MSW): two columns render and degrade to a dash on null. Architecture: Domain only.

## Honest limits
- Shrinkage lowers the chance that the best-looking cell is an extreme draw; it cannot tell a real small edge from none.
- tau² from a handful of siblings is itself noisy; the floor is a tunable assumption, not a measurement.
- Siblings share the market period, so their between-cell variance is understated and shrinkage is a bit too weak, not too strong.
- The normal model for R is an approximation (skew, fat tails).
- With regime cells not built, the regime level of the chain is untested on real data.

## Risks
1. Cells with one sibling (a strategy with a single cell) have no between-cell information: they use the floor and say so.
2. A reader may take "shrunk R" as the forecast. The column header and note say "estimate of the past edge, pulled toward its group".
3. Estimating strength may give 200 (no shrinkage) or 5 on thin data; the clamp and the "default" fallback bound the damage.

## UNVERIFIED
- Where the `Quantile` of the normal and the existing helpers live (the Beta ones are in `BetaDistribution`).
- That every cell input carries enough information to give a per-cell sigma² (`CellStatsRow` has no stored SD today: it must be added or computed in the calculator; confirmed absent in the row members read, not in the whole file).
- That a P6-02 regime cell will use the same `CellId` shape (`CellId.Regime` is referred to in P6-01 as `*`; the type itself was not opened).
- The referenced papers and books (formulas hand-checked only).

## Open questions (owner)
1. `TauSquaredFloor` 0.01 R² (a prior sd of 0.1 R between cells) is my proposal; a larger floor shrinks more. Your call. (Recommended: start 0.01 and check the share of cells at the floor in the first report.)
2. May `Gate` later make the evidence label use the shrunk lower bound? (Recommended: yes, after reading ReportOnly figures for a month.)
3. Is instrument-level pooling (cell × instrument shrunk to the cell) wanted? It needs the cluster key work beyond CF-01 (sector or basket); I have left it out.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR added; doc 09 layer 1 text updated; `scripts/build-docs.sh`

## Review fixes 2026-10-27
Status stays Draft; size 3.5 -> **4 agent-days** (+0.5 fixture and extra cases). Amends the text above.
1. **Lower bound is optimistic.** The 5 % quantile uses only the posterior sd; it ignores the uncertainty in tau^2, the parent mean and sigma^2. Added to the honest limits and as a column note ("lower bound ignores uncertainty in the pooling parameters"). It must **not feed `Gate`** until validated (a seeded simulation of coverage, written as a separate acceptance step before `Gate` is offered).
2. **Floor report.** Every snapshot reports the count and share of cells whose tau^2 is at the floor (`tauFloorCells`, `tauFloorShare`) in the view and the log (counts only).
3. **DL and leave-one-out consistency.** Decision 4 estimates tau^2 over **all** siblings including cell i, while the parent mean excludes i. Chosen rule: compute tau^2 once per parent group over all siblings (a stated, small inconsistency); the leave-one-out tau^2 variant is a documented alternative, not built. The limit is printed.
4. **Per-cell sigma^2 pooling.** For 5..30 trades the cell's sigma^2 is shrunk toward the parent's pooled sigma^2 with weight n/(n+30) (`SigmaPoolStrength = 30`), below 5 trades the pooled value alone; above 30 the cell's own value.
5. **Parent-level switching.** The parent level can change between recomputes as trades cross the 30-trade line; no hysteresis is built, accepted as a limit and shown by `parentLevel` on each row (history of snapshots is insert-only so the switch is visible).
6. **Regression fixture.** A stored-verdict and snapshot fixture (P6-01 table) is re-run in all three modes; every existing field must be byte-identical in Off and ReportOnly.
