# CF-09 — Sequential failure test for paper trading (stop for failure only, never pass early)

- **Status:** Draft (written 2026-10-27; the decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** CF — Confidence programme (row added by CF-00; UNVERIFIED). Adds a report line beside the P10 phase check (P10-04, P10-06) and a hard-fail input to CF-11.
- **Design refs:** docs/design/18-roadmap.md (P10 row) · 09-learning-engine.md §9 · docs/research/confidence-learning-and-validation-speed.md §2.3, §2.4, §2.6 · P10-00 D6 and D7 · P6-09 (drift) · ADRs 0083, 0116, 0119
- **Needs:** CF-08 (the edge series), CF-01 (the design-effect estimator; placeholder rho 0.3 until then), P10-04 (series of the counted trades). Feeds CF-11 (hard fail) and CF-12 (kill rules).
- **Size:** about 3 agent-days (pure calculator and tests 1.5, query, API and verb 1, report line 0.5). Uncalibrated.
- **Safety:** no. A pure calculation over stored grades; it changes no state, no setting, no ticket; the boundary only **flags**. The action taken on a flag is CF-12's, behind the auto/manual setting.

## Assumptions (taken as recommended)
1. **What exists (read).** The P10 pass rule is fixed-sample: 60 counted trades, 84 elapsed days, 80 % session coverage, mean net R above 0 and not below the 5th percentile of the backtest band; below 30 trades `insufficient`, 30 to 59 `provisional` (P10-00 D6). At 60 trades with a spread of about 1.5 R the standard error is 0.19 R, so an edge of +0.2 R has its interval's lower end above zero only about 27 % of the time (D7). P6-09 has a band alarm (20-trade windows, 3 consecutive checks) and a CUSUM (k 0.5, h 10), report only. Nothing is sequential and nothing speaks about power.
2. **The series.** The primary input is the **edge series** of CF-08 (graded shadow rows with `counts_for_edge`, current edge epoch, ordered by session then trade key); the P10 behavioural series is shown beside it. The test restarts when a P10-05 epoch restarts.
3. **Nothing here can pass a strategy.** The fixed gate of P10 stays the only pass; this slice never lets a boundary count as a pass, shortens no calendar gate, and changes no P10 number.

## Decisions (researched)
**D1. A Gaussian-mean SPRT, used on one side.** Hypotheses on the mean net R per independent trade: H0 `mu = 0` against H1 `mu1 = +0.2 R` (setting), `sigma = 1.5 R` (setting, from P10-00 D7: a 40 % hit rate at 2 R and -1 R has mean 0.2 and sd 1.47). Log likelihood ratio per trade `llr(x) = (mu1 / sigma^2) * (x - mu1 / 2) = 0.088889 * (x - 0.1)`. Boundaries with `alpha = 0.05`: **caution** `ln(beta1 / (1 - alpha))` with `beta1 = 0.20`: `ln(0.2 / 0.95) = -1.5581`; **failure** with `beta2 = 0.05`: `ln(0.05 / 0.95) = -2.9444`; **on track (a label, never a pass)** `ln((1 - beta1) / alpha) = ln(16) = 2.7726`. *Why two lower levels:* at the research's `beta = 0.2` a good +0.2 R strategy is wrongly stopped up to `0.2 / 0.95 = 21 %` of the time (Ville's inequality on the likelihood ratio, Gaussian assumption); that is acceptable as a warning, not as a stop, so the stop uses `beta2 = 0.05`, a wrong stop of a +0.2 R strategy at most `0.05 / 0.95 = 5.3 %`. The evidence ratio at the levels is `e^1.5581 = 4.75` and `e^2.9444 = 19`. *Rejected:* a Hoeffding-type e-process for bounded R (needs the range, much weaker); the mixture e-process for unknown variance in production (kept as the upgrade path, Ramdas et al., safe anytime-valid inference, [arXiv 2210.01948](https://arxiv.org/abs/2210.01948), abstract only read); one lower boundary at `beta = 0.2` (too many wrong stops). *Source:* confidence-learning-and-validation-speed.md §2.3 (Wald SPRT standard).
**D2. What "failure" means, said plainly.** The indifference point of this SPRT is `mu1 / 2 = +0.1 R`: the test asks whether the evidence says the mean is nearer 0 than +0.2 R. A strategy whose true mean is below +0.1 R drifts down and **will** eventually cross, and one whose true mean is exactly +0.1 R has zero drift and can too, slowly. That is intended (a mean under +0.1 R net is not worth running at this scale) but the setting `mu1` is the owner's lever, and the text never says "no edge", it says "the evidence favours a mean near 0 over +0.2 R at the stated odds".
**D3. Reflect at the on-track level.** The running sum is `S_n = min(U, S_{n-1} + llr_n)` with `U = 2.7726` and `S_0 = 0`, so a long good run cannot bank credit that hides a later decay. Before the sum first reaches `U` the Ville bounds of D1 hold exactly under the Gaussian model; afterwards a drop of `U - L1 = 4.3307` (caution) or `U - L2 = 5.7170` (failure) is needed, which for a true +0.2 R Gaussian series has probability about `e^-4.33 = 1.3 %` and `e^-5.72 = 0.33 %` per return to the cap (the exponent is 1 because `2 (mu - mu1/2) / mu1 = 1` at `mu = mu1`). CF-12's rolling windows are the second watch.
**D4. Correlated same-day candidates are discounted, not ignored.** Each trade's `llr` is divided by its day's design effect `DEFF = 1 + (m - 1) * rho` (`m` candidates that day, `rho` from CF-01, placeholder 0.3). Five same-day losses of -1 R count as `5 / 2.2 = 2.27` independent losses.
**D5. Unknown variance, handled conservatively.** Each term uses `sigma_k = max(sigma_config, sample sd of the first k trades)` once `k >= 20`; never lower than the configured 1.5. A larger sigma only shrinks the step, so it delays a stop, never hastens it. Each term is computed once with the sigma known at its step (causal; no recomputation of the past).
**D6. The result is derived on demand.** A pure function of the series and the settings snapshot; "ever crossed in this epoch" is read from the replay, so the failure is latched without any new mutable state. The owner clears it by an epoch restart (P10-05) or by changing a setting, either of which is audited. A state is one of `monitoring`, `on_track`, `caution`, `failure`; `on_track` is a label that can revert.
**D7. Where it shows.** The shadow summary (CF-08), the P10 phase tab as an extra line (the P10 criterion text and result are unchanged), CF-11 (a `failure` is a hard fail) and CF-12 (a kill-rule input). Never as a PASS.

Expected trades to a boundary (drift per trade `0.088889 * (mu - 0.1)`; independent trades; rounded):

| True mean | Drift per trade | To caution (-1.5581) | To failure (-2.9444) | To on-track (+2.7726) |
|---|---|---|---|---|
| -0.2 R | -0.026667 | 58 | 110 | never |
| 0 R | -0.008889 | 175 | 331 | never |
| +0.2 R | +0.008889 | rarely (at most 21 %) | rarely (at most 5.3 %) | 312 |

At 6 candidates a session with `DEFF = 2.5` (2.4 effective trades a session) a -0.2 R strategy reaches caution in about `58.4 / 2.4 = 25` sessions and failure in about `110.4 / 2.4 = 46`; with 1.5 armed trades a session it would take 39 and 74 sessions. A +0.2 R strategy needs about 312 independent trades (130 sessions at 2.4) before the label reads on track: sequential testing saves time on **bad** strategies, not on good ones.

## Goal
The owner is told early, with the odds stated, when forward evidence says a strategy is probably not worth continuing, without ever being told early that it is good.

## Scope
**In:** pure Domain `SequentialFailureTest` (options, step, series, result with first-crossing index and the sum path), `SequentialOptions`; Application query `GetSequentialStatusQuery` and handler over the edge series and the behavioural series; verb `xd paper sequential`; `GET /api/paper/sequential`; settings; a report line; ADR; docs; `gen-api`.
**Out:** any pass use; the demotion or other action (CF-12); the effective-n estimator (CF-01); the mixture e-process; tests on the win rate or on cell-level series with fewer than 10 trades; a screen (the line shows in existing tabs; a small card may follow).

## Acceptance criteria
1. **Step values.** With `mu1 = 0.2`, `sigma = 1.5`: `llr(-1) = -0.097778`, `llr(+2) = +0.168889`, `llr(0) = -0.008889`, `llr(0.5) = +0.035556` (unit, 6 places).
2. **Boundaries.** `L1 = -1.558145`, `L2 = -2.944439`, `U = 2.772589` (unit; the settings are recomputed, not stored).
3. **Straight losses.** 15 losses of -1 R: sum `-1.46667`, `monitoring`; the 16th: `-1.56444`, `caution`; 30 losses: `-2.93333`, still `caution`; the 31st: `-3.03111`, `failure` (unit).
4. **Mixed series.** 7 wins of +2 R first, then losses: after the wins `+1.182222`; `caution` at trade 36 (29 losses: `1.182222 - 29 x 0.097778 = -1.653333`; 28 losses give `-1.555556`, not yet); `failure` at trade 50 (43 losses: `-3.022222`; 42 give `-2.924444`, not yet) (unit).
5. **Order independence of a good series.** 8 wins of +2 R and 12 losses of -1 R in any order: final sum `+0.177778`; in the worst order (all losses first) the path bottoms at `-1.173333`, above `L1`: never `caution` (property test over orderings).
6. **Never a pass.** 400 trades of +2 R on every trade: the sum sticks at `U = 2.7726` and the state is `on_track`, whose text contains no "pass", "proven" or "ready"; the result type has no member that CF-11 can read as a pass (unit and architecture test).
7. **Reflection.** After the sum sits at `U`, **45** consecutive losses of -1 R reach caution (`ceil((2.772589 + 1.558145) / 0.097778) = ceil(44.29) = 45`) and **59** reach failure (`ceil((2.772589 + 2.944439) / 0.097778) = ceil(58.47) = 59`); without reflection a sum banked at +20 would need `ceil((20 + 2.944439) / 0.097778) = 235` losses to reach failure (unit).
8. **Design effect.** Five same-day losses with `m = 5`, `rho = 0.3` contribute `5 x (-0.097778) / 2.2 = -0.222222`; `m = 1` contributes the full term; `rho = 0` contributes the full term (unit).
9. **Sigma floor.** At `k >= 20` with a sample sd of 1.2 the used sigma is 1.5; with a sample sd of 2.0 it is 2.0 and the step shrinks to `0.2 / 4 x (x - 0.1) = 0.05 (x - 0.1)` (unit).
10. **Latched, derived, restartable.** A series that crossed `L2` and then recovered still reads `failure` for the epoch; after an epoch restart the series is empty and the state `monitoring` (integration with fake store).
11. **P10 untouched.** The P10-06 phase check output is byte-identical with and without this slice (golden); the sequential line is an extra, labelled "report only, never a pass".
12. **Honesty text.** Every response states: Gaussian model, the odds (4.75 and 19), that the indifference point is `mu1 / 2`, that same-day correlation is discounted with the rho shown (placeholder or estimated), and that fixed gates still decide a pass. No clock, no random source in the calculator; same series, same bytes; no trade in a log line; Architecture tests green.

## Contract
- **API:** `GET /api/paper/sequential?scope=strategy|system` (owner session) -> `SequentialStatusView` (state, sum, boundaries, trades counted, effective n, first crossing date, rho used, settings hash, path summary). Problem 404 when no series. `gen-api`.
- **Data:** none; settings `confidence.sequential.*` (`mu1` 0.2, `sigma` 1.5, `alpha` 0.05, `beta_caution` 0.20, `beta_fail` 0.05, `sigma_from_n` 20) in the versioned settings home; each response carries the snapshot hash.
- **UI:** a line on the shadow summary and the P10 phase tab (lazy tabs already exist; text only, status not by colour alone).
- **Jobs:** none; computed on request, cheap.

## Non-functional
Linear in the number of trades (hundreds); p95 under 20 ms. Pure and deterministic. No secrets or trade rows in logs.

## Test plan
Unit (Domain): step, boundaries, straight and mixed series, reflection, design effect, sigma floor, never-pass, order independence (property). Unit (Application): series assembly, epoch restart, latch. Integration: the endpoint on a seeded edge series; golden for P10-06. Architecture: calculator pure; no reference from the result type to any "pass" concept.

## Risks
The Gaussian model fits fat-tailed R only roughly; the odds are approximate (stated). Using the edge series assumes shadow fills are close to what P11 will show. Zero drift at +0.1 R means a marginal strategy can be stopped by chance: the owner chooses `mu1`.

## UNVERIFIED
The design-effect estimator (CF-01). That CF-08 provides the ordering and the day grouping needed. The Ville bound under non-Gaussian R (approximate). `mu1 = 0.2` and `sigma = 1.5` are proposals (the sigma follows P10-00 D7). The two lower odds levels are proposals.

## Open owner questions
1. `mu1` (0.2 R) and the indifference point it implies (0.1 R net). 2. Two lower levels (caution 21 %, failure 5.3 % wrong-stop odds) or only the failure level? 3. Should the P10 phase tab show the line, or only the shadow summary?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`)
- [ ] Design docs/ADR updated (docs 09, 18); `scripts/build-docs.sh`
