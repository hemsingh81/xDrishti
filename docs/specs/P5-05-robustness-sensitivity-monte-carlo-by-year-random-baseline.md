# P5-05 — Robustness: parameter sensitivity, Monte Carlo, by year, random baseline

- **Status:** Approved (owner's standing run instruction of 2026-10-22, "proceed without waiting": every open question below is taken as recommended). Depends on [P5-04](P5-04-full-test-walk-forward-over-timeframe-side-exit.md) (built, ADR 0060) and [P5-03](P5-03-test-ledger-and-the-gates.md) (ADR 0059).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 5 of [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §3, §4 (robustness block), §5 (ledger), §6 · 16-configuration.md `strategy_lab` · ADRs 0058, 0059, 0060.

## Assumptions (taken as the recommended answers; none is an owner-only decision)
1. **A new method `robust`** = the full test of P5-04 plus a `robustness` stage. Robustness is opt-in because the sweep multiplies the cost (up to 17 plain runs over the whole universe). `full` runs are unchanged. The method is part of the run key, so `full` and `robust` of the same input are different runs; both carry the same walk-forward verdict.
2. **The verdict is not changed by robustness.** The block is attached to the `lab.lab_results` document (a new optional member `robustness` of the verdict; no migration: the document is jsonb and an old document has none). The gates of P5-03 stay the only thing that makes PASS/MARGINAL/FAIL; robustness adds numbers and limits next to it (P5-06 will weigh it in the report).
3. **Sensitivity is one-at-a-time and in-sample.** The sweep takes the first `Robustness:SweepParams` (default 4, at most 4) parameters the strategy declares, in declaration order, and for each one runs the strategy at the declared value times each of `Robustness:SweepSteps` (default 0.8, 0.9, 1.1, 1.2; a whole-number parameter is rounded, a point equal to the declared value or to another point is dropped). Each point is a plain run of the **first candidate exit** (the default exit, 2R) on the **default timeframe of the run** (the one the quick test uses), over the whole window and universe, costed, as the full test; the base is the same run with the declared values. A point is *exploration, in-sample*: it is not walk-forward and never feeds the verdict. At most 16 points. A point the engine refuses (for example a value outside a parameter's range) is shown with its error, not hidden, and does not fail the run.
4. **Each sweep point is a ledger entry** (P5-00 table). After the last cancel check, one row per point is appended to `lab.test_ledger` with method `sweep`, a derived variant hash (sha256 of the parent hash and `name=value`), the parent hash set to the version's hash, and a derived run id (so a retry appends nothing twice). They count as variants of the family: the next run of the family is judged with a tighter threshold (up to 16 more variants). The run itself was judged before the sweep, with the count it had. The sweep runs are **not** stored in `research.sim_runs` (only their summary is in the document).
5. **Judging a sweep.** Per parameter, `stable` when the base mean R is above 0 and every valid point has mean R above 0 and at least half of the base mean; `fragile` when the base has an edge and a point breaks either; `no_base_edge` when the base mean R is not above 0; `inconclusive` with fewer than two valid points. The thresholds are constants of the Domain.
6. **Monte Carlo** is a seeded bootstrap of the out-of-sample trades the verdict was judged on (trades of the walk-forward winners in their test windows, in exit order): `Robustness:McDraws` (default 1000) draws, each of n trades with replacement. It reports the 5th, 50th and 95th percentile of the mean R, the median and 95th percentile of the maximum drawdown in R, and the share of draws whose mean R is not above 0, beside the observed values. Needs at least 2 trades, otherwise the block says so. The seed is the first 8 bytes of the sha256 of the run key, so the same run gives the same numbers.
7. **By year** groups the same out-of-sample trades by the calendar year of the entry: trades, mean R, total R, profit factor (null without a loss), and whether the year is positive. With 2.1 years of data there are three partial years; the block says so in its limits.
8. **`by_regime` is unavailable** (P3-01d, the market-context job, is not built): the block carries the text `unavailable: the market-context job (P3-01d) is not built` and no numbers.
9. **The random baseline is a sign-flip null, not a re-run with random entries** (deviation from the wording of P5-00; recorded in ADR 0061). The engine has no random-entry mode and adding one is an engine change, not a Lab slice. The null keeps each out-of-sample trade's size and flips its sign by a seeded coin: it asks how often chance alone, with this many trades of these sizes, gives a mean R at least as high as the observed. It reports the observed mean, the 95th percentile of the null mean and the p-value `(count + 1) / (draws + 1)`. It does not say what a random entry with the same exit would earn (with a 2R target that is negative before costs); the limit is printed.
10. **Cancel and failure.** A cancel during robustness stores nothing (as P5-04). A refused sweep point does not fail the run; a crash of the stage itself is the usual `lab.run_crashed`.

## Goal
`POST /api/lab/runs` with method `robust` runs the full test, then answers: do the numbers survive moving the parameters, resampling the trades, splitting by year and a chance baseline? The answers are in the stored result next to the verdict, with the limits in words.

## Scope
**In:** method `robust` in `StartLabRunHandler` (same input rules, costs mandatory, the whole universe) and `LabRunner` (stage `robustness`); pure Domain parts (`SeededRandom`, `MonteCarlo`, `SignFlipBaseline`, `YearBreakdown`, `SweepPlan`, `SensitivityAnalysis`); `LabRobustness` service; `RobustnessView` in the verdict document; settings `Robustness` (section of `StrategyLab`); ledger rows of the sweep; labels; ADR 0061; docs.
**Out:** a re-run with random entries; walk-forward inside the sweep; sweeping the exit candidates (P5-04 already walks them); `by_regime` numbers (P3-01d); weighing robustness into PASS/FAIL (P5-06); the page (P5-08b); a grid over several parameters at once (needs the cost model of P5-00 risk 3 first).

## Acceptance criteria
1. Given method `robust`, then the run is queued under its own key and costs are mandatory (422 `lab.invalid_input` without a charge sheet); `quick` and `full` behave as before.
2. Given a robust run, then the result carries `robustness` with `sensitivity`, `monteCarlo`, `byYear`, `randomBaseline`, `byRegime` (unavailable) and limits; `full` results have `robustness` null.
3. Monte Carlo and the baseline are deterministic for a seed and differ for another; hand-checked tables for percentiles, drawdown, the share of non-positive means and the p-value (unit).
4. By year groups by entry year with the exact numbers of a hand-made list (unit).
5. The sweep plan takes at most four parameters and 16 points, rounds whole-number parameters, drops duplicates and the declared value (unit); the analysis gives stable / fragile / no_base_edge / inconclusive on hand tables (unit).
6. A sweep point appends one ledger row with method `sweep`; appending twice writes once; a point that the engine refuses is shown with its error and still counts as a variant that was looked at.
7. The verdict (outcome, gates, evidence) of a robust run equals that of a full run on the same input (the block is additive).
8. A cancel during `robustness` stores no result, no `sim_runs` row and no sweep ledger row.
9. Architecture tests stay green; no strategy text, trade or secret in a log line.

## Contract
- **API:** `POST /api/lab/runs` accepts `method: "robust"`; `GET /api/lab/runs/{id}` returns `verdict.robustness` (`RobustnessView`) or null. OpenAPI regenerated.
- **Data:** no migration. `lab.lab_results.result` holds the member; `lab.test_ledger` takes rows with `method = 'sweep'`.
- **Config:** `StrategyLab:Robustness:{SweepParams 1..4, SweepSteps 1..4 positive numbers, McDraws 100..20000}`; the values are in the run's config text (so they are in the key).
- **Events/jobs:** same dispatcher; stage `robustness`; progress counts instruments done over instruments x (1 + points).

## Non-functional
Deterministic. Cost: up to 17 plain runs over the universe in one timeframe, one run in memory at a time; not timed on the owner's data (P5-00 risk 3). No network.

## Test plan
Unit (Domain): seeded generator, Monte Carlo, sign-flip, by year, sweep plan, sensitivity analysis. Unit (Application): `LabRobustness` with fake handlers (points, errors as points, ledger rows, no params, too few trades); runner (`robust` stage order, cancel stores nothing); handler (`robust` accepted, key differs from `full`). Integration: the API accepts `robust` (queued, refused without a sheet). Architecture: unchanged.

## Honest limits (printed in the block)
- The sweep is in-sample on the whole window; a plateau there is not evidence, a cliff is a warning.
- One parameter at a time: interactions are not seen.
- Monte Carlo resamples the trades as independent; clustered losses (one bad month) are understated.
- The baseline is a sign-flip null, not a random-entry run.
- Three partial years of data: by-year numbers are thin; no regime split.

## Open questions (all taken as recommended)
1. Opt-in method or part of every full test? Recommended: opt-in (`robust`). 2. Sweep with a fixed 2R exit or the chosen exit? Recommended: fixed first candidate (no walk-forward per point). 3. Does robustness change the outcome? Recommended: no, until P5-06. 4. Sweep points as ledger variants? Recommended: yes (P5-00). 5. Sign-flip null instead of random entries? Recommended: yes, say so.

## Done when
- [x] Questions answered, status Approved
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green
- [ ] `scripts/gen-api.sh`; ADR 0061; docs 11, 16, roadmap; `scripts/build-docs.sh`
