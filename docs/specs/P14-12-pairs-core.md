# P14-12 — Pairs core: log-price spread, OLS hedge ratio, the Engle-Granger residual test with pinned MacKinnon values, half-life, Benjamini-Hochberg and z-score signals, pure, with hand-worked tables and honest `unavailable`

- **Status:** Approved (owner, 2026-11-03, blanket approval of all stories)
- **Phase / roadmap row:** P14 — slice P14-12 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Pairs core (pure)"); protocol S items 2 and 3.
- **Design refs:** docs/design/20 §C (`PAIRS`) · 19 §1 (multiple testing) · P14-00 protocol S, D10, D11 · ADR 0059 (`MultipleTesting.Factor`). New ADR: **next free number at build time** (the test, its tables, the BH rule and the signal state machine).
- **Needs:** nothing built (pure code). **Needed by:** P14-13.
- **Size:** 3 agent-days (estimate).
- **Safety:** no — pure functions in Domain; no I/O, table, setting, route or order.

## Goal
Given two aligned price series, the core says — deterministically and with every doubtful value `unavailable` — what the hedge ratio is, whether the spread looks stationary (Engle-Granger statistic and its p-value), how fast it reverts, which of many tested pairs survive a false-discovery control, and, bar by bar, whether a spread position should open, close or stop.

## Verified facts (read 2026-11-03 in `backend/src`)
- No pair, spread, regression, unit-root or FDR code exists. `MultipleTesting.Factor(N)` and `SeededRandom` (SplitMix64) are in `Domain/Lab`; `NormalDist` comes with P14-05 (if this slice is built first it adds `NormalDist` to `Domain/Common` and P14-05 uses it).
- **MacKinnon values are UNVERIFIED** in this text: they are written from secondary memory of MacKinnon (2010) "Critical Values for Cointegration Tests" (Queen's Economics Department Working Paper 1227, Table 2, N = 2, constant, no trend) and MacKinnon (1994) "Approximate asymptotic distribution functions for unit-root and cointegration tests" (JBES 12(2)); the build agent opens both sources, pins the coefficients in one table class with the citation, and the ADR records the page.

## Scope
**In:** `Domain/Pairs` (pure, no I/O, no clock, no `Random.Shared`):
- `LogSpread` (`s_t = ln y_t − α − β·ln x_t`), `HedgeRatio.Ols(ln y, ln x)` (α, β, residuals, R², n).
- `EngleGranger.Test(ln y, ln x, options)` → τ, lags used, n, critical values at n, asymptotic p-value; `MacKinnonTables` (pinned coefficients).
- `HalfLife.Of(spread)` (AR(1) by OLS).
- `BenjaminiHochberg.Reject(p-values, q)`.
- `ZScore.Of(window, value)`; `PairSignal` (the entry, exit and stop state machine); `ReturnCorrelation` (Pearson of daily log returns).
- `PairsOptions` (defaults below), result types with closed reasons; unit and property tests; ADR.

**Out:** choosing candidates, reading bars, simulating legs or costs, the verdict (P14-13); Johansen or multi-asset tests; Kalman or rolling hedge ratios (a later version, counted as a new trial if ever added).

## The rules
- **OLS.** `β = Σ(x−x̄)(y−ȳ) ÷ Σ(x−x̄)²`, `α = ȳ − β·x̄`, residual `e_t = y_t − α − β·x_t`, sums in index order (canonical). `Σ(x−x̄)² ≤ 1e-14·n` → `degenerate_x`.
- **Engle-Granger.** On the residuals: `Δe_t = γ·e_{t−1} + Σ_{i=1..p} φ_i·Δe_{t−i} + ε_t` by OLS **without a constant** (the residuals have mean zero by construction), `p = PairsOptions.Lags` (default 1, fixed and pre-registered; a different `p` is a different trial). `τ = γ ÷ se(γ)` with `se² = s² ÷ Σ e_{t−1}²` adjusted for the lag regressors by the usual OLS covariance, `s² = SSR ÷ (m − k)` where `m` is the number of equations and `k` the regressors. Critical values at the sample size `T`: `c(T) = τ∞ + β1/T + β2/T²` (MacKinnon 2010, N = 2, constant). **p-value:** the MacKinnon (1994) approximation `p = Φ(Σ_j γ_j·τ^j)` with the small-p and large-p coefficient sets and `τ*`, `τmin`, `τmax` for N = 2 with constant (`p = 0` below `τmin`, `1` above `τmax`), labelled `asymptotic`.
- **Half-life.** `Δs_t = a + b·s_{t−1} + ε` by OLS; `HL = −ln 2 ÷ ln(1 + b)` bars. `b ≥ 0` → `not_mean_reverting`; `1 + b ≤ 0` → `oscillating`. A pair passes formation only with `HL ∈ [1, 20]` sessions (P14-13 converts bars to sessions).
- **Benjamini-Hochberg** at `q` (0.05): sort `p` ascending (ties by input index), find the largest `k` with `p_(k) ≤ k·q ÷ m`, reject the first `k`. **An `unavailable` test counts in `m` with `p = 1`** (it can never be rejected and it widens every threshold: conservative).
- **Z-score.** `z = (s − mean(W)) ÷ sd(W)` over the window `W` of earlier spread values only (sample sd, `n − 1`); fewer than `MinWindow` (30) values → `short_window`; `sd ≤ 1e-12` → `flat_window`.
- **Signal state machine** (`PairSignal`): flat → `long_spread` when `z ≤ −entry`, `short_spread` when `z ≥ entry`; in a position → `exit` when `|z| ≤ exit`, `stop` when `|z| ≥ stop` (stop checked first); an `unavailable` z **never opens, never closes by rule** — it holds and counts (`z_unavailable`), and the caller's session square-off still applies. Defaults `entry` 2.0, `exit` 0.5, `stop` 3.5 (protocol S.3). `long_spread` = long Y, short `β` × X (value weights from P14-13).
- **Inputs.** Any non-finite or non-positive price → `bad_input`; series of different length or fewer than `MinObservations` (60) → `too_short`; no exception on a number.

## Hand tables
**OLS and Engle-Granger (p = 0 lags, the statistic only).** `x = [1, 2, 3, 4, 5]`, `y = [3.05, 4.90, 7.00, 9.10, 10.95]` (`y = 1 + 2x + e`, `e = [0.05, −0.10, 0, 0.10, −0.05]`, orthogonal to 1 and `x`):
| Quantity | Value |
|---|---|
| β, α | 2.000000, 1.000000 (exact) |
| residuals `e` | 0.05, −0.10, 0.00, 0.10, −0.05 |
| `Δe` (t = 2..5), `e_{t−1}` | −0.15, 0.10, 0.10, −0.15; 0.05, −0.10, 0.00, 0.10 |
| γ | −0.0325 ÷ 0.0225 = −1.444444 |
| SSR, `s² = SSR ÷ 3` | 0.018056, 0.006019 |
| se(γ), τ | 0.517194, −2.792848 |

(With 4 equations this is an algebra check only; the `too_short` rule is bypassed by an internal test seam.)

**MacKinnon (2010) critical values, N = 2, constant (UNVERIFIED coefficients):**
| Level | τ∞ | β1 | β2 | c(252) | c(63) |
|---|---|---|---|---|---|
| 1 % | −3.89644 | −10.9519 | −22.527 | −3.9403 | −4.0760 |
| 5 % | −3.33613 | −6.1101 | −6.823 | −3.3605 | −3.4348 |
| 10 % | −3.04445 | −4.2412 | −2.720 | −3.0613 | −3.1125 |

**Half-life.** `b = −0.10` → `−ln 2 ÷ ln 0.9` = 6.578813 bars; `b = 0` → `not_mean_reverting`; `b = −1.2` → `oscillating`.

**Benjamini-Hochberg** (P14-00 S.2): `p = 0.001, 0.008, 0.02, 0.04, 0.30`, `m = 5`, `q = 0.05`: thresholds 0.01, 0.02, 0.03, 0.04, 0.05 → `k = 4`, the first four rejected. Adding one unavailable test (`m = 6`, p = 1): thresholds 0.00833, 0.01667, 0.025, 0.0333, 0.0417, 0.05 → `p_(4) = 0.04 > 0.0333`, `p_(3) = 0.02 ≤ 0.025` → `k = 3`.

**Z-score.** Window 0.010, 0.012, 0.008, 0.011, 0.009 (mean 0.010, sd 0.0015811), value 0.016 → z = 3.794733 (with `MinWindow` lowered to 5 for this case) → state `short_spread` from flat at `entry` 2.0, and `stop` if already long at `stop` 3.5.

## Acceptance criteria
1. **Hand tables.** Every value above within 1e-6 (critical values within 1e-4); BH both cases; the signal transitions table (flat/long/short × z below, between and beyond each threshold, and `unavailable`).
2. **Exact cointegration (property).** For `y = a + b·x` exactly (any seeded `a`, `b ≠ 0`), β = b within 1e-10 and the test is `unavailable/zero_residual_variance` (never τ = −∞ or a p of 0).
3. **Size and power (property, seeded, fixed thresholds).** Two independent Gaussian random walks of 252 steps: the 5 % critical value rejects in at most 8 % of 1,000 seeds (nominal 5 %, slack for the finite sample). `x` a random walk and `y = 1 + 0.8x + u`, `u` AR(1) with ρ = 0.5: rejects in at least 90 % of 200 seeds.
4. **p-value consistency.** `p(τ)` is non-decreasing in τ and in [0, 1]; at the asymptotic critical values τ∞ of the table, `p` is 0.01, 0.05 and 0.10 within ±0.003 (the two MacKinnon sources must agree; a failure means a mis-pinned coefficient).
5. **BH properties.** Rejections are a prefix of the sorted list; adding a test with p = 1 never adds a rejection; permuting the inputs gives the same rejected set.
6. **Honest states.** Each closed reason (`bad_input`, `too_short`, `degenerate_x`, `zero_residual_variance`, `not_mean_reverting`, `oscillating`, `short_window`, `flat_window`, `z_unavailable`) is reached by a case; no result carries both a value and a reason; nothing throws on a number.
7. **Determinism.** Same inputs → the same bits; no shared state (thread-pool run).
8. **Architecture.** `Domain/Pairs` depends on `System`, `Domain/Common` and `Domain/Lab` only; nothing outside research code references it.

## Contract
- **API, data, settings, UI:** none. `PairsOptions`: `Lags` 1, `MinObservations` 60, `MinWindow` 30, `Q` 0.05, `MinCorrelation` 0.8, `HalfLifeSessions` [1, 20], `Entry` 2.0, `Exit` 0.5, `Stop` 3.5.

## Non-functional
One Engle-Granger test on 252 points ≤ 50 µs; 2,000 candidate pairs per formation ≤ 1 s on the owner's machine (UNVERIFIED; a benchmark test, not gated).

## Test plan
- **Unit:** the hand tables; the transitions table; each closed reason.
- **Property (seeded, fixed seeds):** criteria 2 to 5 and 7.
- **Architecture:** criterion 8.

## Decisions (researched)
1. **Engle-Granger with a fixed lag.** One pre-registered test per pair keeps N honest; an information-criterion lag choice is a hidden search per pair.
2. **Asymptotic p-values for BH, finite-sample critical values printed.** BH needs p-values; MacKinnon (1994) gives them asymptotically; the 2010 finite-sample critical values are printed beside each decision so the gap is visible.
3. **Unavailable counts in m with p = 1.** Dropping untestable pairs from `m` would loosen the threshold for the rest.
4. **An unavailable z holds.** A missing spread value must not trade, and must not end a trade by rule either; the session square-off still closes it.

## Risks and honest limits
- The coefficients are UNVERIFIED until the sources are opened; criterion 4 is the guard.
- Engle-Granger depends on which series is `y`; the core tests the pair as given and P14-13 fixes the order (the larger turnover as `y`), counted as part of the trial text.
- A cointegration p-value over 252 daily closes says little about a 5-minute intraday spread; P14-13 is where that is judged.

## Open questions (owner)
None blocking. Defaults as `PairsOptions`.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to check the pinned coefficients against the cited pages, any unavailable test dropped from `m`, any trade opened or closed on an unavailable z)
- [ ] ADR written (with the source pages of the tables); doc 20 §C updated
