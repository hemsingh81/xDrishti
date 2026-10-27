# CF-07 — Random-entry null, placebo / mirror runs and the plateau gate

- **Status:** Draft (written 2026-10-27; the decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** CF — Confidence programme (row added by CF-00; UNVERIFIED). Extends P5-05 (robustness) and P5-06 (verdict and report).
- **Design refs:** docs/design/11-strategy-lab.md §3, §4 · 08-exits-and-risk.md §3 (plateau preference in exit selection) · docs/research/confidence-edge-realism.md §3 rows 4 to 7, §4 rows 7 to 9 · ADRs 0060, 0062, 0063
- **Needs:** P5-04/05/06 (built), CF-00 (report-only rule, settings home), CF-06 (trial ledger: these runs are looks and are counted). Feeds CF-11 rows 7, 8, 9.
- **Size:** about 5.5 agent-days: random-entry engine seam and null 3, placebo / mirror set 1.5, plateau gate 1. Uncalibrated (P5-05 itself called the random-entry mode "an engine change").
- **Safety:** no. Research-side only; new gates start report-only.

## Assumptions (taken as recommended)
1. **What exists (read).** P5-05's baseline is a **sign-flip** null: it keeps each out-of-sample trade's size and flips its sign with a seeded coin; its own honest limits say it is lenient (it tests mean R against 0 given the trade sizes, while random entries with a 2R target lose money before costs) and that robustness never changes the verdict. The sweep is one parameter at a time, up to 4 parameters x 4 steps (`0.8, 0.9, 1.1, 1.2`), run on the first candidate exit over the whole window, in-sample, and judges each parameter `stable | fragile | no_base_edge | inconclusive` (`SensitivityAnalysis`), with no gate on the verdict. `SelectionOptions.PreferPlateau` (default on) already prefers a plateau among **exit** candidates (doc 08 §3); nothing does it for strategy **parameters**. `SeededRandom`, `MonteCarlo` and the seed rule "first 8 bytes of sha256 of the run key" exist.
2. The engine has **no** entry-override seam today (P5-05 assumption 9). UNVERIFIED how large the change in `XDrishti.Simulation` is; the first task is a read of the walker and a one-page seam design.
3. Everything here is a **look at the data**, so each placebo or null run is counted by the trial ledger of CF-06 as one trial per family-level null (not per seed: the 1000 seeds are one test).

## Decisions (researched)
**D1. Random-entry null replaces the sign-flip as the main chance test; the sign-flip stays beside it.** For every out-of-sample trade of the walk-forward winners, draw an entry minute uniformly inside the strategy's own entry window (`TimeRules`) in the **same instrument, session, side**, with the **same initial risk distance** (as a fraction of the entry price), the same target multiple, time stop, and sizing rule, costed with the same sheet and slippage preset. A draw whose limit is not reached is redrawn up to 5 times, then dropped and counted. One **seed** = one full set of such trades; `random_mean_s` is its mean net R. 1000 seeds (`confidence.nulls.draws`, 200 to 5000). The real mean must exceed the 95th percentile of `random_mean_s`; the p-value is `(count_ge + 1) / (draws + 1)`. Seed: first 8 bytes of sha256 of `run key + ":random-entry"`. *Why:* it answers "does the rule's timing add anything over entering anywhere in the same window with the same exits and costs", which the sign-flip cannot. *Rejected:* more sign-flip draws (same flaw); a bootstrap of non-signal days (does not keep the stop/target structure). *Source:* confidence-edge-realism.md §3 row 4.
**D2. Placebo set = 20 fixed, named shapes, and the real strategy must rank first.** Deterministic generator over the real out-of-sample signals, no free parameters: `mirror` (opposite side, mirrored stop and target) = 1; `shift_+15/+30/+45/+60` (same signal, entry that many minutes later; shifting earlier is look-ahead and is excluded) = 4; `date_shift_+1..+5` and `date_shift_-1..-5` (the same clock-time signal on another session of the same instrument; breaks signal-return timing and keeps marginals) = 10; `instrument_roll_1..5` (the day's signals reassigned to the next instrument in a fixed order) = 5. Total 20. With the real strategy included there are 21 members, so the real one must rank **first** (1/21 = 0.0476 at most 0.05); `p = rank / (k + 1)`. A placebo that cannot be computed (data missing) reduces `k`; with `k < 19` the test reads `inconclusive`, not pass. *Why:* a real edge must not also earn as a fade or as a shifted copy; a "same family top 5 %" claim needs enough comparators. *Rejected:* a hand-picked "nonsense rules" list (the owner could tune it until the strategy wins); inverting arbitrary filters (needs the rules DSL to negate a program, UNVERIFIED feasible). *Source:* confidence-edge-realism.md §3 rows 5 and 6.
**D3. Mirror gate.** `mirror` net expectancy at most 0 (same costs). A positive mirror means the "edge" is drift or beta, not timing.
**D4. Plateau gate on parameters.** For every swept parameter with at least 3 valid points: (a) the two **adjacent** points (by value order, e.g. x0.9 and x1.1) each keep at least 70 % of the base net expectancy (base must be above 0), and (b) the median net expectancy over **all** valid points of that parameter is above 0. The gate passes when every swept parameter passes; a parameter with fewer than 3 valid points is `inconclusive`. A plateau centre is **reported** (the value maximizing the mean of itself and its two neighbours), not applied; changing the declared value is a new strategy version and a new trial. The sweep stays in-sample and is labelled so. *Why:* an edge on a spike is the optimiser's, not the market's. *Rejected:* gating on the maximum; a smoothness penalty inside selection (changes the estimate that is then tested).
**D5. All three start report-only.** Rows with value, threshold, status in the report; `confidence.nulls.enforce` and `confidence.plateau.enforce` default OFF; ON (owner act, fresh password) a failed row caps the outcome at MARGINAL, never raises it. `unavailable` is fail for any consumer (CF-11).

## Goal
A Lab report can say, with numbers and a seed, whether the strategy beats random entry with the same exits and costs, whether its mirror and shifted copies also earn, and whether its parameters sit on a plateau rather than a spike.

## Scope
**In:** Simulation seam `EntryOverride` (a walk with a forced entry minute, everything else the same rules), pure Domain `RandomEntryNull`, `PlaceboSet`, `PlateauGate`, results in the stored document (`confidence.nulls`), runner stage `nulls` for `robust` runs, report rows and warnings, settings, ADR, docs 11 and 16, `gen-api`.
**Out:** enforcing the gates in the verdict (setting flip); joint parameter grids (needs the cost model of P5-00 risk 3); shuffled-label nulls for ML overlays (CF-10); deflated Sharpe, PBO (CF-03); benchmarks; changing which value a strategy declares.

## Acceptance criteria
1. **Null by hand.** Real mean +0.12 R; random-entry means over 1000 seeds with mean 0.00 and sd 0.04: 95th percentile `0 + 1.645 x 0.04 = 0.0658` (the stored percentile is the empirical one; the Gaussian figure is the oracle for a constructed series): **pass**. Random mean 0.07, sd 0.04: 95th percentile 0.1358, real 0.12 below: **fail**. Counting: 38 of 1000 seed means at or above the real mean gives `p = 39 / 1001 = 0.0390` (pass at 0.05); 50 gives `51 / 1001 = 0.0509` (fail) (unit, constructed series).
2. **Seam properties.** A forced entry at the minute the strategy itself entered reproduces the original trade byte for byte (same prices, same R); a forced entry never uses bars after the exit decision (leak test, as P3-08); the same seed gives the same result, another seed differs; a redraw cap of 5 drops and counts (property and unit tests in `XDrishti.Simulation.Tests` / Domain tests).
3. **Same structure.** Each random trade has the real trade's risk fraction, target multiple, time stop, side, instrument and session, and its charges come from the same sheet at its own quantity (unit with a fake bar source).
4. **Placebo set.** The generator yields exactly 20 named shapes for a constructed signal list, in a fixed order; none looks ahead; `mirror` flips side and mirrors stop and target; `instrument_roll_k` is a permutation of the day's signals (property test).
5. **Rank by hand.** Real +0.12 R above all 20 placebos: rank 1, `p = 1/21 = 0.0476`: pass. Best placebo +0.13 R: rank 2, `p = 2/21 = 0.0952`: fail. Only 18 placebos computable: `inconclusive` whatever the ranks.
6. **Mirror by hand.** Real +0.12, mirror -0.10: pass. Real +0.12, mirror +0.08: fail with the text "mostly drift".
7. **Plateau by hand (sweep steps).** Base 0.12; x0.8 0.05; x0.9 0.10; x1.1 0.11; x1.2 0.04: neighbours keep `0.10 / 0.12 = 83.3 %` and `0.11 / 0.12 = 91.7 %`; median of {0.04, 0.05, 0.10, 0.11, 0.12} = 0.10 > 0: **pass**. Base 0.15 with neighbours 0.02 and 0.02 (13.3 % each): **fail**. Base 0 or negative: `no_base_edge`, fail. Research example: opening-range length 5 / 10 / 15 / 20 / 30 minutes with 0.02 / 0.11 / 0.12 / 0.10 / 0.01: at 15 the neighbours keep 91.7 % and 83.3 %, median 0.10, **pass**, plateau centre 15 (smoothed 0.1100 against 0.0833 at 10 and 0.0767 at 20); with 0.01 / 0.02 / 0.15 / 0.02 / 0.01: neighbours of 15 keep 13.3 %, **fail**.
8. **Report-only.** With both `enforce` settings OFF the verdict outcome, gates and evidence of a robust run equal those of a robust run without the stage (additive, as P5-05 AC7); ON, a failed row caps at MARGINAL.
9. **Ledger.** One `trial_kind = 'null'` row per family-level null (not per seed) is appended once per run (idempotent by run id), so the next verdict's factor includes it (CF-06 D3).
10. A cancel during the stage stores nothing; a crash is `lab.run_crashed`; the stored block states "in-sample sweep" and the redraw/dropped counts; no strategy text or trade in logs; Architecture tests green.

## Contract
- **API:** `POST /api/lab/runs` accepts method `robust` with optional `nulls: true` (default true) and `draws`; `GET /api/lab/runs/{id}` and `.../report` gain `confidence.nulls` (`randomEntry`: real, mean, p5/p50/p95/p99, p, dropped share, seed; `placebos`: 20 rows with expectancy and rank; `mirror`; `plateau`: per parameter ratios, median, centre). OpenAPI regenerated.
- **Data:** no migration for the result document; `lab.test_ledger.trial_kind` from CF-06 takes `'null'`. Settings `confidence.nulls.*` (draws 1000, percentile 95, redraws 5, placebo shapes fixed, `enforce` false) and `confidence.plateau.*` (neighbour retention 0.70, min points 3, `enforce` false).
- **UI:** the Lab report panel gets a "Chance and plateau" block (null histogram summary as a table, placebo ranking table, parameter plateau rows). A chart is optional; status not by colour alone.
- **Jobs:** stage `nulls` after `robustness` in `LabRunner`; progress counts seeds done.

## Non-functional
Budget: 320 trades x 1000 seeds is 320,000 single-trade walks over at most 375 one-minute bars, about 1.2 x 10^8 bar steps; bars are loaded once per (instrument, session) and reused across seeds. Proposed target: p95 under 60 s on the owner's data, one run in memory at a time (UNVERIFIED, not timed). Deterministic. No network.

## Test plan
Unit (Domain): null statistics, placebo generator, rank and p, plateau gate, centre. Unit (Simulation): the seam properties against the independent walker oracle of P3-08 (forced entry equals the oracle's walk). Unit (Application): runner stage order, cancel, additive verdict, ledger row once. Integration: a robust run on the seeded fixture returns the block; the old stored result without it still reads. Frontend (MSW): block renders. Architecture: unchanged layering.

## Risks
The random-entry null assumes the real trades' risk distances are representative; clustered days still make seeds correlated across trades inside a seed (not across seeds), so the null is not wider than reality (state it). Placebo date-shifts reuse real market days, so a strong trending period lifts all of them (that is the point: it is what drift looks like). With 3 folds of data the whole block is noisy.

## UNVERIFIED
The walker's change size for the entry override. Whether P3-02e (BUY to SELL mirror) is built; the placebo `mirror` here is applied at trade level and does not need it. Runtime on real data. The percentile and 70 % retention are proposals. That the number of looks (D1 of CF-06) should count a null as one trial.

## Open owner questions
1. 95th percentile and rank 1 of 21 as the bar, or looser (for example top 2 of 21) while data are short? 2. 1000 seeds (cost versus precision)? 3. Accept the 70 % plateau retention? 4. When the three gates go from report-only to blocking.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); no migration beyond CF-06's column
- [ ] Design docs/ADR updated (docs 11, 16); `scripts/build-docs.sh`

## Review fixes 2026-10-27
1. Pass rule: pass if and only if `p <= 0.05` (`(count_ge + 1) / (draws + 1)`); the 95th percentile is displayed only and is not a second test.
2. Random-entry null, placebos and plateau run only inside the clamped development window (CF-06 AC8); no hold-out session is ever read here.
3. AC9 now matches CF-06 D3, which counts one trial per null row (see CF-06 Review fixes).
