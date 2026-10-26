# Confidence in the learning loop and speed of honest validation

Research only, no code. Date 2026-10-27. Repo facts come from docs/design/09, specs P6-05/06/09/12/13, P10-00/01, P11-00 (read in part; the P7-12 and JOURNEY files were not read in full: UNVERIFIED claims about them are marked).
Web sources were read as search abstracts, not full texts: formulas below are standard and hand-checked, citations are pointers. Mark "UNVERIFIED" = not confirmed in repo or source.

## 0. What the repo already has (so we do not rebuild it)
- Purged, embargoed (7 d) quarterly walk-forward, 3 baselines, last 6 months as a one-use hold-out, fewer than 6 folds caps a verdict at "inconclusive" (P6-05, ADR 0067).
- Isotonic (PAV) calibration, 10 bin reliability, contributions (P6-06). Triple-barrier labels (P6-03). Meta-labelling policy with "does not help" as an answer (P6-13). Frozen-model counterfactual (P6-12). Drift: band alarm + CUSUM, replay only, no auto-pause (P6-09). Bayesian shrunk cell stats, 5 % posterior quantile (P6-01). Multiple-testing factor on margins (P6-05).
- Paper: 60 counted trades AND 84 days AND 80 % coverage, mean net R > 0 and not below the 5th percentile of the backtest-band (P10-00 D6). Paper entries only for tickets that were approved AND armed (P10-01 AC2).

Main weakness: most gates are fixed-sample and fixed-calendar. Nothing is sequential, nothing is a power statement, and the paper count only sees the owner-approved slice.

## 1. ML on financial data, safely

| Item | Repo status | Gap | Application change | Hand example | Size |
|---|---|---|---|---|---|
| Labels: triple barrier | Built P6-03, horizon in ADR 0065 | None for labels; check label-overlap weighting | Add sample uniqueness weights (average uniqueness, AFML ch.4) to training and to n_eff reporting | 3 trades overlap 2 bars of 4: each weight 1/avg-concurrency, so 3 overlapping trades count about 1.8 not 3 | 1.5 d |
| Purged/embargoed CV | Built | Folds only ~3 until more history (spec says so) | Add combinatorial purged CV (CPCV) to get many test paths from few quarters, and report PBO | 6 blocks, 2 test per split = 15 splits, 5 paths, instead of 3 folds | 4 d |
| Calibration | Isotonic, 10 bins | Isotonic overfits at small n (step function, PAV needs hundreds) | Switch default to Platt (sigmoid, 2 params) when calibration n < ~500; isotonic only above; report ECE with a bootstrap CI and log-loss-vs-base-rate | n=80: isotonic has up to 80 steps; Platt has 2 | 2 d |
| Beat-baseline probability | Margins on log loss/Brier | Margin is not a probability | Paired block-bootstrap of per-fold loss difference vs best baseline; report P(model beats baseline); require >= 0.90 and lower CI > 0 | If model beat baseline in 5 of 6 folds, sign test p=0.109 one-sided: not enough alone | 2 d |
| Simple vs complex | ML.NET trainer; logistic baseline | No explicit complexity ladder | Fix a ladder: base rate -> cell shrunk rate -> L2 logistic (<= 8 features) -> GBM with monotone constraints and depth <= 3. A rung is allowed only if it beats the rung below. Rule of thumb: events-per-variable >= 10-20 for logistic, so n=120 events supports ~6-12 features | 120 trades / 15 per feature = 8 features max | 2 d |
| Feature stability | Contributions, permutation importance (P6-06) | No stability across folds | Per-fold importance rank; drop features whose sign flips in > 1/3 folds; PSI per feature vs train | PSI = sum (a-e) ln(a/e); bins e=.5/.5, a=.7/.3 gives .2*0.336+(-.2)*(-0.511)=0.169 (0.1-0.25 = watch) | 2 d |
| Concept drift | Band + CUSUM, report only | Not acted on, no feature/label-side drift, no calibration drift | Add Page-Hinkley / ADWIN on residuals and a rolling ECE; wire to a proposed, then (setting ON per CLAUDE.md auto option) executed, demotion to shadow | See 4 | 3 d |
| Regime handling | Regime `*` until P6-02 (UNVERIFIED if built) | Regime splits shrink n | Use regime as a feature or shrinkage parent, not as a hard split, until each regime cell has >= 30 trades | 4 regimes x 60 trades = 15 each: too thin; shrink to parent | 1 d |
| Champion/challenger, rollback | Registry (P6-07), lifecycle (P6-08) | Rollback trigger not numeric | Rules: challenger runs in shadow >= 30 graded candidates and >= 4 weeks; promoted only if frozen-vs-retrained counterfactual (P6-12) lower CI > 0; auto-rollback to the previous champion when rolling ECE > 5 pp (doc 16) or 20-trade mean R below band for 3 checks (P6-09) | -- | 2 d |
| "ML is advisory unless it beats no-ML with a locked hold-out" | Hold-out exists; P7 says Advisory | Not a single enforced predicate | Make `MlMayInfluence = beatsBaseline(holdout, once) AND forwardShadowOk`. Hold-out burned once per model family; second look counts as a new trial (deflate) | -- | 1 d |

Sources: AFML book listing https://www.exlibris.ch/de/buecher-buch/english-books/marcos-lopez-de-prado/advances-in-financial-machine-learning/id/9781119482086/ ; purged CV https://en.wikipedia.org/wiki/Purged_cross-validation ; DSR https://papers.ssrn.com/abstract=2460551 ; PBO/CSCV https://papers.ssrn.com/abstract=2326253. Platt-vs-isotonic small-sample claim: standard (Niculescu-Mizil and Caruana 2005, not fetched) UNVERIFIED here. EPV 10-20 rule: Peduzzi et al. 1996, UNVERIFIED here.

Deflation add-on (about 1.5 d): report Deflated Sharpe / probability the best cell's Sharpe beats the max expected from N trials, with N = cells tried (ledger already counts experiments, P6-11).

## 2. Faster honest validation

**2.1 Shadow-paper ALL candidates (biggest lever).** Today paper count = approved+armed tickets only (P10-01 AC2), about 43-58 trades in 12 weeks (P10-00 D11). Plan items have statuses proposed/modified/rejected/lapsed (UNVERIFIED whether every nightly candidate before the cut is stored; if only the proposed list is stored, that is the list to log).
- Change: each nightly plan writes every generated candidate (including rejected, not-armed and below-the-line) into `paper.shadow_entries` with the frozen config hash, then grades them with the same replay simulator and cost sheet (the P10-01 simulator already grades from 1-minute bars; this reuses it). Strictly forward-only, written before the session opens (timestamped and hashed), never back-filled. Kept in a separate count: owner-approved trades remain the "behavioural" count; shadow trades are the "edge" count. Do not merge them in the exit criterion without the owner's decision.
- Honest caveats: candidates on the same day are correlated (same market move). Effective n is lower: n_eff = n / (1 + (m-1) rho). With m=5 same-day candidates and rho=0.3, n_eff = n/2.2.
- Example: 1.5 armed trades/session -> 60 trades at 40 sessions (about 8 weeks). 6 candidates/session gives 240 trades in 40 sessions; n_eff about 240/(1+5*0.3)=96. 60 effective trades by about 25 sessions (5 weeks), vs 12. Not 4x, about 2x, once correlation is counted.
- Size 4-5 d (migration, writer, grader reuse, report split). Selection caution: the rule that chooses what to approve must not look at shadow results from the same epoch.

**2.2 Forward-only logging from day 1.** Same item; also persist the model score, features and config hash per candidate so the learning curve (section 4) can be built later with no hindsight. 1 d on top of 2.1.

**2.3 Sequential testing (stop early for failure).** Keep the fixed gate for "pass", add an e-process/SPRT only to *stop for failure* or flag "on track". Gaussian-mean SPRT, H0 mean 0 vs H1 mean 0.2R, sd 1.5R:
 llr per trade = (mu1/sd^2)(x - mu1/2) = 0.0889 (x - 0.1). Bounds alpha 0.05, beta 0.2: accept H0 at ln(0.2/0.95) = -1.558; accept H1 at ln(0.8/0.05) = 2.77.
 If the truth is -0.2R: drift = 0.0889 (-0.2-0.1) = -0.0267 per trade, so about 58 trades to cross -1.558 (about 12 weeks at the current pace; about 5 with shadow). If truth is 0: -0.0089 per trade, about 175 trades. If truth is +0.2R: +0.0089, about 312 trades to accept. So sequential stopping mainly saves time on *bad* strategies, not good ones. Fixed-n SD is estimated, so use a mixture e-process / sub-Gaussian bound for unknown variance in production. Source: Ramdas et al., safe anytime-valid inference https://arxiv.org/abs/2210.01948 (abstract only read); Wald SPRT standard. Size 3 d (pure calculator + report line, no gate change). Never use it to pass early: pass still requires the fixed gate.

**2.4 Power for N trades.** n = ((z_a + z_b) sd / d)^2, one-sided a=0.05, 80 % power: z sum 2.486.
 d=0.2R, sd 1.5: n = (2.486*7.5)^2 = 348. d=0.3R: 155. d=0.4R: 87. d=0.5R: 56.
 So 60 trades only detects an edge of about 0.5R with 80 % power; at +0.2R, power is about P(Z > 1.645 - 0.2*sqrt(60)/1.5) = P(Z > 0.61) = 27 %. This matches P10-00 D7. Consequence: the 60-trade gate is a "not clearly broken" gate, not proof. Show power on the scorecard. Size 1 d.

**2.5 Pooling.** Pool cells of one strategy family with a hierarchical (shrinkage) estimate (P6-01 already shrinks); report effect for the family and for a cell only as a deviation. Pool across instruments only if returns are in R and instruments are not the same day/market shock (use n_eff). Size 2 d. Minimum viable evidence (suggested): n_eff >= 30 for "watch", >= 60 and lower 90 % bound > 0 for "usable", >= 150 for "scalable" (UNVERIFIED thresholds, the owner's decision).

**2.6 Cheating guard list.** No peeking-based early *pass*; frozen config hash per epoch (exists, P10-00); shadow candidates written before the open; hold-out used once; selection of cells for approval cannot use shadow data of the same epoch.

## 3. One readiness scorecard
Per cell/strategy, a 0-100 score = the minimum (not average) of six sub-scores, so one weak link caps it; plus a hard-fail list. Numbers are proposals.

| Sub-score | 0 | 50 | 100 | Source in repo |
|---|---|---|---|---|
| Evidence n_eff (forward, after costs) | < 15 | 30 | >= 100 | paper + shadow |
| Effect: lower 90 % bound of mean net R | <= 0 | 0 to 0.1 | >= 0.15 | P10, P11-02 stats |
| Power at 0.3R | < 30 % | 50 % | >= 80 % | new 2.4 |
| Backtest agreement: paper mean inside the backtest band | below 5th pct | in band | in band and sign stable by month | P10-04 |
| Calibration: ECE (pp) | > 10 | 5 | <= 3 | P6-06/09 |
| Stability: drift alarms, folds positive | alarm on | 4 of 6 folds | no alarm and >= 5 of 6 | P6-05/09 |

Hard fails (score capped at 0, label "not ready"): integrity fault, unconfirmed cost sheet (P10-00 D5), any `unavailable` input (CLAUDE.md auto rule), sequential failure boundary crossed, hold-out reused.
Label: <40 Not ready, 40-69 Building, 70-84 Usable with advice, >= 85 Ready for the next phase. System level = weighted by planned risk share, min of (cell scores weighted by capital) and the integrity checks; show a single number and the weakest link by name.
Hand example: cell has n_eff 45 (score 70 by interpolation 30-100), lower bound 0.05 (25), power 0.3R at n=45: z=0.3*6.7/1.5=1.34, power=P(Z>0.31)=38 % (37), band in (50), ECE 6 (45), stable (70): score = min = 25 -> "Not ready", the effect lower bound is the reason shown. Size 4 d (calculator, API, UI card, tests).

## 4. Is the learning loop helping?
- Frozen counterfactual (built P6-12): keep, and also freeze a *no-ML* arm at day 0 of each epoch; compare retrained vs frozen vs no-ML on the same candidates (shadow set gives more of them). Report mean R difference with a paired bootstrap CI; "no difference" is an answer.
- Learning curve: out-of-time score (log loss minus base-rate log loss) by training-set size, refit at n = 50, 100, 150, ...; if it is flat, more data/learning will not help; if rising, say how many more trades. 2 d.
- Calibration drift: rolling ECE and reliability slope per month; slope below 0.8 or above 1.2 means over/under-confident; alarm and fall back to base-rate probability. 1.5 d.
- Decision-quality metric: share of owner-approved tickets whose model score was in the top half, and mean R of top-half vs bottom-half (a lift check). 1 d.
- Example: frozen mean R 0.10, retrained 0.12 over 80 shared trades with paired sd 0.8: SE 0.089, diff 0.02 -> z 0.22: no evidence learning helps; keep advisory.

## 5. Order and totals (agent-days)
1. Power + scorecard + sequential failure line: 1 + 4 + 3 = 8 d (pure, no data change, quick win).
2. Shadow-all-candidates forward log + report split: 5-6 d (largest speed-up, about 2x in effective wall-clock for the edge count; the 12/8 week calendar gates still apply to the behavioural count unless the owner changes D6 in writing).
3. Calibration default by n, sample-uniqueness weights, complexity ladder, feature stability: about 8 d.
4. CPCV + PBO + deflation: 5.5 d. Learning curve, calibration drift, rollback rules, hold-out predicate: about 7 d.
Total about 34 d. Spec-first (Draft), ADR for any change to the P10 exit criterion (design decision change).

## Honest limits
Wall-clock cannot be cut below the market's ability to generate independent outcomes; correlated same-day candidates add less than their count. Sequential tests mainly shorten the path to "this is bad". Passing still needs roughly 150-350 trades for a 0.2-0.3R edge. The 60-trade gate has ~27 % power at 0.2R.
