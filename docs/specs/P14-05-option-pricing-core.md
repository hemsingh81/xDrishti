# P14-05 — Option pricing core: Black-76 on a forward, Black-Scholes as its spot form, an implied-volatility solver, Greeks, the forward by parity and the labelled fallback, pure and honest

- **Status:** Approved (owner, 2026-11-03, blanket approval of all stories)
- **Phase / roadmap row:** P14 — slice P14-05 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Option pricing core (pure)"); phase acceptance criterion 1.
- **Design refs:** docs/design/20 §C · P14-00 D3 (vendor IV as given; Black-Scholes only as a labelled, counted fallback), D6 (positions exit before expiry: no early-exercise pricing), O.5(e) (a Black-Scholes share above 5 % is INCONCLUSIVE) · ADR 0178 (parity forward, `r` = `Options:Quality:Rate`, `T` in calendar days ÷ 365, European style). New ADR: **next free number at build time** (only if the units below are accepted as a design rule; otherwise doc 20 §C carries them).
- **Needs:** nothing built (pure code). **Needed by:** P14-07 (half-spread in premium terms), P14-08 (delta for sizing, the fallback price, `T`), P14-10 (spread legs), P14-11 (ATM IV interpolation), the P14-02 pilot (criterion 12: "IV and theta units against one hand-computed value").
- **Size:** 2 agent-days (estimate).
- **Safety:** no — pure functions in Domain; no I/O, no setting, no table, no route, no order.

## Goal
One pure, deterministic library prices a European option, inverts a price to an implied volatility and gives delta, gamma, vega and theta, with every input or output that is not a usable number returned as `unavailable` with a closed reason, never as zero, NaN or an exception. Later slices use it only where the plan allows: vendor prices and vendor IV first, a model price only as a labelled and counted fallback.

## Verified facts (read 2026-11-03 in `backend/src`)
- No pricing, IV or Greek code exists: `Domain/Options` holds `OptionScope`, `LotSizeLookup`, `EpochLookup`, `OptionUnderlyings` and the `Quality` checks (P14-01, P14-04); `ParityCheck` computes `F = K0 + (Cmid − Pmid)/DF` inline for the quality check only.
- `Options:Quality:Rate` (0.065, range 0-0.2, UNVERIFIED) and the `T` convention (calendar days to expiry ÷ 365) are set by ADR 0178; this slice uses the same convention so parity and pricing agree.
- Stored vendor values: `options.chain_snapshots.iv/delta/gamma/theta/vega` (`real`, null with flag `bad_greeks`, IV percent UNVERIFIED, theta per day UNVERIFIED) and `options.rolling_bars.iv` (`real` null, unit UNVERIFIED until the P14-03 probe). Prices are integer paise in both tables.
- There is no `erf` in .NET; `SeededRandom` (SplitMix64, `Domain/Lab`) exists for property tests.

## Scope
**In:** `Domain/Options/Pricing` (pure, no I/O, no clock, no `Random.Shared`):
- `NormalDist` (`Cdf`, `Pdf`), `OptionKind` (`Call`, `Put`).
- `Black76.Price(kind, F, K, T, DF, σ)` and `Black76.Greeks(...)`; `BlackScholes` as the spot form with `F = S·e^((r−q)·T)`, `DF = e^(−r·T)`, `q` = 0 unless given.
- `ImpliedVol.Solve(kind, price, F, K, T, DF)`.
- `ParityForward.From(quotes, DF)` and `ExpiryTime.YearFraction(at, expiryDate)`.
- `IvUnits.FromVendor(value)` (percent → fraction, UNVERIFIED unit carried as a label).
- `PricedValue` / `Unavailable` result types with closed reasons; `PriceSource` (`vendor_bar`, `vendor_chain`, `bs_fallback`) as a label other slices carry.
- Unit and property tests; doc 20 §C units table.

**Out:** reading any table (P14-07/08 own their readers); American or early-exercise pricing (D6); a volatility surface or smile fit (out of P14); dividends schedules (`q` is a parameter, default 0; indices use the parity forward); choosing when the fallback may be used (P14-08's rule; this slice gives the function and the label).

## The rules
- **Model.** Black-76 on the forward: `d1 = (ln(F/K) + σ²T/2)/(σ√T)`, `d2 = d1 − σ√T`; call `DF·(F·N(d1) − K·N(d2))`, put `DF·(K·N(−d2) − F·N(−d1))`. Black-Scholes is the same with `F = S·e^((r−q)T)`. European exercise only (NSE index and stock options; stocks UNVERIFIED, ADR 0178).
- **Units (fixed here, printed in doc 20 §C).** Prices in rupees as `double` inside the core (callers convert paise ↔ rupees at their edge; a paise result is rounded half away from zero). `σ` as a fraction per year. `T` = (expiry date at 15:30 India time − `at`) in seconds ÷ (365 × 86,400). Delta per rupee of the underlying spot (`∂V/∂S` with `F = S·e^((r−q)T)`, so call delta `= e^(−qT)·N(d1)`); gamma per rupee; **vega per 1 volatility point** (σ + 0.01); **theta per calendar day** (÷ 365). Vendor IV is read as percent and divided by 100 (`IvUnits`, UNVERIFIED until the P14-02 pilot; the label `iv_unit=percent_assumed` travels with every value).
- **`NormalDist.Cdf`** to an absolute error ≤ 1e-14 on [−38, 38] (a published double-precision `erfc` algorithm, for example W. J. Cody's rational approximations; the build cites its source in a code comment); outside that range 0 or 1 exactly.
- **Honest states (closed list).** Inputs: `S, F, K ≤ 0` or non-finite → `bad_input`; `σ ≤ 0`, non-finite or > 5.0 → `bad_vol`; `T ≤ 0` → `expired`; `DF ∉ (0, 1]` → `bad_rate`. IV solver: price non-finite or ≤ 0 → `bad_price`; price ≤ the forward intrinsic `DF·max(F − K, 0)` (call) / `DF·max(K − F, 0)` (put) plus 1e-12 → `below_intrinsic`; price ≥ `DF·F` (call) / `DF·K` (put) → `above_upper_bound`; no root within the bracket after the iteration cap → `no_convergence`; vega below 1e-8·F at the root (a deep in- or out-of-the-money price that pins no volatility) → `vol_not_identified`. Forward: no strike with both sides two-sided → `no_two_sided_quote`. **Nothing returns 0, NaN or ∞ for an unavailable value, and nothing throws on a number** (only a null argument throws).
- **IV solver.** Bracket σ ∈ [1e-4, 5.0]; Newton from `σ0 = sqrt(2·|ln(F/K)|/T)` clipped into the bracket (0.2 when that is 0), with a bisection step whenever Newton leaves the bracket or vega < 1e-12; stop when `|price(σ) − price| ≤ 1e-10·max(1, price)` or the bracket is narrower than 1e-12; at most 100 iterations. Deterministic: the same inputs give the same bits.
- **Forward by parity** (ADR 0178 decision 2): `F = K0 + (Cmid − Pmid)/DF` at the strike `K0` nearest the underlying price among strikes where both sides have a bid and an ask, not crossed; ties take the lower strike. Without such a strike the forward is `unavailable/no_two_sided_quote` and a caller may use `F = S·e^(rT)` only with the label `forward_from_rate`.
- **The fallback label.** `bs_fallback` is the source of any premium this core computes for a caller; the caller counts it. The core never decides to use it.

## Hand table (all values to 6 decimals; tolerance 1e-6 absolute on prices and Greeks)
| # | S | K | σ | T (years) | r | F | DF | d1 | call | put | Δ call | Δ put | Γ | vega /pt | θ call /day | θ put /day |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A | 100 | 100 | 0.20 | 0.25 | 0 | 100.000000 | 1.000000 | 0.050000 | 3.987761 | 3.987761 | 0.519939 | −0.480061 | 0.039844 | 0.199222 | −0.021833 | −0.021833 |
| B | 100 | 105 | 0.25 | 0.5 | 0.065 | 103.303389 | 0.968022 | −0.003763 | 6.309906 | 7.952263 | 0.498499 | −0.501501 | 0.022567 | 0.282093 | −0.027075 | −0.008974 |
| C | 22,000 | 22,000 | 0.15 | 7/365 | 0.065 | 22,027.441758 | 0.998754 | 0.070397 | 196.231665 | 168.824094 | 0.528061 | −0.471939 | 0.000871 | 12.124371 | −15.024294 | −11.111367 |

Row A is the phase criterion: call `= 100·(2·N(0.05) − 1)` with `N(0.05) = 0.5199388058`. Row B checks parity: `C − P = DF·(F − K) = −1.642357`. Row C is a Nifty-like weekly. Theta here is the Black-Scholes theta with `q = 0` per calendar day.

**IV cases:** A's call 3.987761 → σ 0.200000 (|Δσ| ≤ 1e-8); B's put 7.952263 → 0.250000; C's call 196.231665 → 0.150000. A call of 9.50 at S = 100, K = 90, r = 0, T = 0.25 → `below_intrinsic` (intrinsic 10). A call of 100.0 at S = 100, r = 0 → `above_upper_bound`. The `vol_not_identified` case is built by the test from a far out-of-the-money price whose root has vega below the limit (the build records the numbers it uses).

**Forward case (as P14-04's fixture):** K0 = 100, Cmid 5.0, Pmid 4.0, DF 1 → F = 101.0.

## Acceptance criteria
1. **Hand table.** Rows A to C give every column within 1e-6; the IV cases recover σ within 1e-8 or return the stated reason.
2. **Parity (property).** For 10,000 seeded cases (S 10-50,000; K/F 0.5-1.5; σ 0.02-2.0; T 1 hour-2 years; r 0-0.15): `|C − P − DF·(F − K)| ≤ 1e-9·max(F, K)`.
3. **Bounds and monotonicity (property).** Every price is within `[DF·max(F−K,0), DF·F]` (call) and the put's analogue; price strictly increases with σ where vega > 1e-8·F; call delta in [0, 1], put delta in [−1, 0], gamma ≥ 0, vega ≥ 0.
4. **Greeks by finite difference (property).** Delta, gamma, vega and theta agree with central differences of the price (step 1e-4 relative; theta by one calendar day back) within 1e-5 relative or 1e-8 absolute, on the same seeded cases.
5. **IV round trip (property).** For σ ∈ [0.05, 1.5], T ∈ [1 day, 1 year], K/F ∈ [0.7, 1.3]: `Solve(Price(σ)) = σ` within 1e-8 wherever vega ≥ 1e-6·F; elsewhere a closed reason. Never a σ outside the bracket.
6. **Honest states.** Each reason of the closed list is reached by a hand case; non-finite inputs never throw and never return a number; the result type cannot carry both a value and a reason.
7. **Determinism.** Same inputs → identical bits on repeated calls and across a thread pool run (no shared state).
8. **Units.** A test pins vega per point (row A 0.199222, not 19.922) and theta per calendar day (row A −0.021833, not −7.969); `IvUnits.FromVendor(20.0)` = 0.20 with the label `percent_assumed`.
9. **Architecture.** `Domain/Options/Pricing` depends on nothing outside `System` and `Domain/Common`; no decision layer (`Selection`, `Planning`, `Checklist`, `LiveTrading`, `Paper`, `XDrishti.Execution`, `XDrishti.Strategies*`) references it in this slice.

## Contract
- **API, data, settings, UI:** none.
- **Domain API (names may change in the build; the shape may not):** `Black76.Price(OptionKind, double F, double K, double T, double DF, double sigma) → PriceResult`; `Black76.Greeks(...) → GreeksResult`; `BlackScholes.Price(OptionKind, double S, double K, double T, double r, double sigma, double q = 0)`; `ImpliedVol.Solve(OptionKind, double price, double F, double K, double T, double DF) → VolResult`; `ParityForward.From(IReadOnlyList<StrikeQuotes>, double underlying, double DF) → ForwardResult`; `ExpiryTime.YearFraction(DateTimeOffset at, DateOnly expiry) → TimeResult`.

## Non-functional
`Price` + `Greeks` ≤ 1 µs and `Solve` ≤ 20 µs per call on the owner's machine (UNVERIFIED; measured by a benchmark test, not gated): 10,000 re-marked trades × a few hundred bars stay far under the P14-00 budget of 5 minutes. No allocation per call beyond the result struct.

## Test plan
- **Unit:** the hand table, the IV cases, the forward case, each closed reason, `NormalDist` against ten tabled values (`N(0) = 0.5`, `N(1.96) = 0.9750021048517795`, `N(−8)`, `N(38)`), `ExpiryTime` across midnight and at 15:30 exactly (`expired`).
- **Property (seeded, ≥ 10,000 cases, fixed seed):** criteria 2 to 5 and 7.
- **Architecture:** criterion 9.

## Decisions (researched)
1. **Black-76 on a forward as the one formula.** A forward from parity (ADR 0178) carries the dividend and rate the market prices in, so index options need no dividend schedule; Black-Scholes with spot is its special case. Rejected: a Black-Scholes core with a dividend-yield input (one more unverified number).
2. **Vega per point, theta per calendar day.** These are the units Indian vendors usually print (UNVERIFIED for Dhan; the pilot compares row C with a stored chain row); fixing them in one place stops each caller from guessing.
3. **Safeguarded Newton, no closed-form approximation.** Deterministic, accurate to 1e-8 and simple to test; rational-approximation solvers (Jäckel's "Let's be rational") are faster but bring an unverified port. Rejected for this slice; the interface allows it later.
4. **`vol_not_identified` instead of a clipped volatility.** A price that pins no volatility must not become a number that later feeds IV rank or a fallback price.

## Risks and honest limits
- Vendor IV and Greeks come from an unknown model and rate (P14-02 risk); this core does not reconcile them, it only lets the pilot compare.
- European style for stock options is UNVERIFIED (ADR 0178); if NSE stock options were American for some past window, prices near expiry would be slightly low. Positions exit before expiry (D6), which bounds the effect.
- A fallback price is a model price: it carries the model's error, which is why P14-09 counts it and caps its share.

## Open questions (owner)
None blocking. Defaults recorded: units as above; `q` = 0; the parity forward first, `forward_from_rate` labelled.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any unavailable value returned as a number, any throw on a number, any unit mismatch between vega and theta and the table)
- [ ] Doc 20 §C units table; an ADR only if the units are accepted as a design rule
