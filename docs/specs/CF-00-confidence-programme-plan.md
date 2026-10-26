# CF-00 — The confidence programme: plan and slice map

- **Status:** Draft (written 2026-10-27 under the owner's delegation; decisions below researched; the owner's Approved is needed before any slice is built)
- **Phase / roadmap row:** cross-cutting programme on P5 (Lab), P6 (learning engine), P8 (reports), P10 (paper) and the data layer (P1). Not a roadmap phase; it raises the odds that the verdicts of existing phases are honest.
- **Design refs:** docs/design/09-learning-engine.md · 11-strategy-lab.md §4 · 05-data-and-brokers.md §4.2, §6.1 · 16-configuration.md · ISS-19, ISS-22, ISS-25 in 18-roadmap.md · research notes [confidence-data-and-small-sample](../research/confidence-data-and-small-sample.md) (primary), [confidence-edge-realism](../research/confidence-edge-realism.md), [confidence-learning-and-validation-speed](../research/confidence-learning-and-validation-speed.md)
- **Size:** this plan 0.5 agent-day; slices CF-01..CF-12 below
- **Safety:** SAFETY slices are marked. A new setting is OFF or report-only. No slice changes an existing verdict by default.

## Goal
Raise the probability that what the learning engine and the Lab say about a cell or a strategy is honest when the history is thin (about 2.1 years, ISS-19), by two means only: **statistics that say "unknown" correctly and still use small samples well** (effective n, power, shrinkage, deflation, block resampling) and **more honest data** (a longer history with its seams and survivorship named). It does not create an edge and does not make a weak strategy pass; its main effect is to make fewer false positives and to print how much evidence is missing.

## The rule of the programme (applies to every CF slice)
1. **Show beside, never replace.** A new figure (effective n, power, shrunk mean R, deflated Sharpe, PBO, block-bootstrap interval) appears next to the old figure on the same row and the same report, labelled "new, not used in the verdict", until the owner switches it on.
2. **Settings are versioned, owner-tunable and default OFF or report-only.** One section `Confidence` in the versioned settings (ADR 0002; the existing settings store is UNVERIFIED in detail, each slice names its keys and the build confirms the mechanism). Every key that can change a verdict has the mode `Off | ReportOnly | Gate`. Defaults: pure display figures are `ReportOnly`; anything that can change a PASS, a state or a plan is `Off`. `Gate` is an owner act (authenticated, as designed), the decision is recorded with the setting snapshot, and the CLAUDE.md approvals-auto rule holds (an `unavailable` input never counts as a pass).
3. **Missing input is "unavailable", never a pass and never a zero.** A figure that cannot be computed (n too small, no series stored) is printed as `unavailable: <reason>`.
4. **Pure first.** Every statistic is a pure `Domain` function with hand-worked tests; wiring and migrations come after.
5. **Network fetches are OFF until the owner enables them**; an allow-list change is the owner's security act (ADR 0097 pattern: the allow-list is read-only to the app, `BrokerEndpoints` in `XDrishti.Infrastructure/Brokers`). Specs name the entry; they never add it.
6. **Old results stay readable.** Insert-only stores (ADR 0045) get nullable new columns or a new member in the jsonb document; an old row reads "not computed".

## Verified facts the slices rest on (read on 2026-10-27)
- `CellTrade` carries only `ExitDate`, `R`, `MaeR`, `MfeR` (`Domain/Learning/CellTrade.cs`): no entry date, instrument or cluster key. `SimTrade` does carry `TradeDate`, `InstrumentId`, `EntryEpochSeconds`, `ExitEpochSeconds`, `SessionsHeld`, `RExact`, `RFilled` (`Domain/Research/SimTrade.cs`).
- `CellStatsCalculator` shrinks the **win rate only** (`BetaShrinkage`, strength constant 20, the parent includes the cell's own trades); expectancy and profit factor are recency-weighted but not shrunk; `Row(...)` sets `Enough = trades.Count >= MinTrades` (plain count).
- `MultipleTesting.Factor(N) = 1 + 0.25 * log2(N)`, "not a statistical claim" (its own comment); `QualificationGates.Default = 30 trades, 0.15 R, PF 1.2, 60 % folds, 10 R drawdown, 5 pp calibration`.
- `MonteCarlo.Run` is an iid bootstrap in exit order ("clustered losses are understated" in its summary); `SignFlipBaseline` is a lenient null (P5-05 assumption 9).
- Embargo and purge exist in `ExitSelection` (`Embargoed`, `Purged`) and `TripleBarrierLabel.ResolvedEpochSeconds`; `OosPool` pools test windows of the walk-forward winners.
- The broker allow-list holds **one** Dhan chart endpoint: `POST /v2/charts/intraday` (`BrokerEndpoints.DhanIntradayChart`, windows of at most 90 days in `DhanIntradayChartClient`, bars through `IIntradayBarSource` / `DhanIntradayBarSource`, throttle `Data:Dhan:RequestsPerSecond`). **`/v2/charts/historical` (daily) is not in the allow-list.** (Research note said it "is already described as allow-listed"; the code says it is not.)
- Corporate actions today are `split` or `bonus` only (P1-10a), detected then confirmed by the owner; no rights, demerger or dividend kinds.
- No statistics for effective n, power, deflation, PBO or sequential tests exist in `Domain` (grep of the research note; not re-run here: UNVERIFIED beyond the files listed above).

## Slice map

Sizes are agent-days, uncalibrated (earlier phases overran; use ×1.3). "Moves" = which assessment line the slice moves: **M** = moderate confidence that the system can *identify worthwhile setups*; **L** = low confidence that there is a *real edge after costs*. A slice that only makes the system *say* it does not know moves neither number; it moves the *honesty* of the answer, listed as H.

| Slice | Title | Size | Order | Needs | Safety | Moves |
|---|---|---|---|---|---|---|
| **CF-01** | Effective sample size, power and minimum track record | 4.5 | 1 | nothing | no | H now; M (stops over-trusting thin cells) |
| **CF-02** | Empirical-Bayes shrinkage of expectancy; parent-child pooling | 3.5 | 2 | CF-01 | no | M (small cells become usable without hiding) |
| **CF-03** | Deflated Sharpe, PBO and block bootstrap | 6.5 (A 3.5, B 3) | 3 | CF-01 | no | L (fewer lucky passes) |
| **CF-04** | History backfill to five years: daily bhavcopy, corporate actions | 9.5 (A 4, B 5, C 0.5) | 2 (A) / 4 (B) | owner decisions, allow-list act | **yes** | M and L (the only slice that adds evidence) |
| [CF-05](CF-05-cost-stress-gate-and-fill-one-minute-later-test.md) | Cost stress gate (1x / 1.5x / 2x), slippage tiers, fill-one-minute-later test | 5.5 | 3 | P3-03b/c/f, P5-03/05/06 | no | L (cost is the largest false-edge source) |
| [CF-06](CF-06-preregistration-trial-ledger-and-locked-holdout-in-the-lab.md) | Pre-registration, trial ledger, locked hold-out | 6 | 3 | CF-04 for a useful hold-out | no | L |
| [CF-07](CF-07-random-entry-null-placebo-runs-and-plateau-gate.md) | Random-entry null, placebo / mirror runs, plateau gate | 5.5 | 4 | CF-06 (looks are counted) | no | L |
| [CF-08](CF-08-shadow-paper-trading-of-every-nightly-candidate.md) | Shadow paper trading of every nightly candidate (forward-only) | 6 | 3 | P7 plan, P10-01/04/05; CF-01 later | no (paper only) | M and L (more forward trades) |
| CF-09..CF-12 | Not seen when this plan was written: per CF-05..CF-08 they include a scorecard (CF-11) and later slices that CF-08 feeds (CF-09, CF-10, CF-12); possibly sequential test / decay, combinatorial purged CV, small-sample model controls | see files | after CF-08 | CF-01..CF-08 | no | H and M |

CF-05..CF-12 are written by another agent. CF-05..CF-08 were read by title, size and Needs line only (not reviewed); CF-09..CF-12 did not exist when this table was written, so their row is a placeholder. Where the final files differ, the files win and this table is corrected.

Sizes of CF-01..CF-04 total **24 agent-days** (4.5 + 3.5 + 6.5 + 9.5). Adding CF-05..CF-08 (23 days) gives about **47 agent-days** before CF-09..CF-12.

### Order and parallelism
- **Wave 1 (parallel, no shared files):** CF-01 (Domain `Confidence/`, cell stats, gates), CF-04 Part A (data layer; owner decisions first). 
- **Wave 2:** CF-02 (needs CF-01's `EffectiveSampleSize`), CF-04 Part B (needs the owner's setting switch and the adjustment seam test), CF-05, CF-08 in parallel worktrees.
- **Wave 3:** CF-03 A (DSR, block bootstrap), then CF-03 B (PBO, needs the per-variant series stored and benefits from CF-06/CF-07 grids), CF-06, CF-07 after CF-06.
- **Wave 4:** CF-09 to CF-11 as their files say. **Last:** CF-12 (the scorecard-style slice reads CF-01 to CF-08).
- Two writers never touch the same file: CF-01, CF-02 and CF-03 all edit `Domain/Learning/CellStatsCalculator.cs` or `Domain/Lab/VerdictEvaluator.cs`; run them in the order CF-01, CF-02, CF-03 or in separate worktrees with a rebase between.

## What the programme moves in the confidence assessment

The owner's current reading (from the research notes): **moderate** confidence that the system can *identify worthwhile setups*; **low** confidence that there is a *real edge after costs*.

| Slice | Moderate: identify worthwhile setups | Low: real edge after costs |
|---|---|---|
| CF-01 | Stops treating 34 clustered trades as 34; thin cells read "n=34, effective 11" instead of looking decided. Ranks fewer cells as setups but with true weight. | Prints the smallest detectable edge (0.51 R at n=34, 0.90 R at n_eff=11): the plain statement that a 0.15 R edge cannot be seen with this history. No change to the odds, a change to the owner's knowledge of them. |
| CF-02 | Biggest move: a 10-trade cell at +0.40 R reads +0.165 R, a 40-trade cell +0.258 R; cells become comparable. | Small: shrinkage lowers the chance that the best-looking cell is an extreme draw. |
| CF-03 | Small. | Real: the luck bar for 16 variants (SR 0.90 per year in the worked example) replaces an uncalibrated factor; PBO flags a sweep whose winner does not repeat out of sample. |
| CF-04 | A longer history (about 2.1 y to up to 5 y for 1-minute bars; 10+ y daily) lifts the walk-forward from about 4 test windows toward about 10; regimes get several examples each. | The only slice that adds independent evidence. Cannot add survivorship-free data (Dhan serves active instruments only). |
| CF-05..CF-08 | CF-08 adds forward trades (more evidence for cell counts) | CF-05, CF-06 and CF-07 attack the edge-after-costs line directly (cost stress, hold-out and trial count, random-entry null). |

**Honest limits of the whole programme** (printed in every slice's report):
1. Statistics cannot manufacture information. With about 2 years, a 0.15 R edge needs about 396 independent trades at sigma 1.2 R; the best case here is a clear "unknown".
2. 1-minute history reaches about 5 years at most (Dhan), and only for instruments that are active today (survivorship). Daily bhavcopy reaches 10+ years but cannot replay intraday strategies.
3. A longer history is more of the same market. 2021-2026 is one bull-dominated regime pair; it does not contain every regime.
4. Every new statistic has its own assumptions (normal approximations, stationarity). They are shown beside the old figures to be checked on known-null data (CF-03) before anyone relies on them.
5. Nothing here places or enables an order. P7 places none.

## Owner inputs needed (one list for the programme; each slice repeats its own)
1. Approve the vocabulary: modes `Off | ReportOnly | Gate`, section `Confidence`, defaults as above.
2. Thresholds to confirm or tune (proposals in each slice): sigma floor 0.5 R; power 80 %, alpha 0.05; evidence labels "watch" n_eff 30, "usable" 60 with a lower bound above 0, "scalable" 150 (the learning-and-speed note marks these UNVERIFIED and an owner decision).
3. CF-04: network fetch Yes/No (default No), the allow-list act for daily history, NSE terms read and an ADR (as BK-01), disk budget (+4 to 9 GB estimate), whether to keep a hold-out of the last months (roadmap Q7).
4. Whether CF-02's shrinkage may later feed the cell evidence label (a `Gate` act, never by default).

## Acceptance criteria (of this plan)
1. Every slice CF-01..CF-12 has a spec file named in the table, in Draft; CF-01..CF-04 are written here, CF-05..CF-12 by the parallel agent (CF-05..CF-08 existed at the time of writing). *Verifiable check: file list.*
2. No slice sets a new key to `Gate` or ON by default. *Check: each slice's Contract lists defaults.*
3. Every worked number in CF-01..CF-04 was recomputed (python, 2026-10-27); where it differs from the research note, the spec says so. Known differences: required trades for a 0.15 R edge is 395.7, so **396** (note: 395); requests for the 2.9-year gap are about **6,000** (12 chunks × 500), not 7,500 (note), about 25 minutes at 4 requests/s.

## Open questions
1. Does the owner want CF-04 B (1-minute backfill) before CF-02/CF-03, since it adds evidence rather than caution? Recommended: A first (cheap, no allow-list), B once the seam test design is approved.
2. One `Confidence` settings section, or one per module? Recommended: one section, one mode key per statistic.

## Done when
- [ ] The owner has read CF-00..CF-12 and set each slice Approved or deferred
- [ ] CF-01 built first and its n_eff visible on the Learning and Lab screens
- [ ] This plan's table corrected to the final CF-05..CF-12 files
