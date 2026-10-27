# CF-11 — Readiness scorecard, per strategy and for the system

- **Status:** Draft (written 2026-10-27; the decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** CF — Confidence programme (row added by CF-00; UNVERIFIED). Reads P5, P6, P10 and the other CF slices; adds one lazy screen.
- **Design refs:** docs/design/09-learning-engine.md §7 (cell lifecycle) · 11-strategy-lab.md §5 · 13-reports-and-ui.md · 18-roadmap.md · docs/research/confidence-edge-realism.md §4 · confidence-learning-and-validation-speed.md §2.4, §3 · P6-08, P10-00 (D5, D6, D7), P10-04, P10-06, P5-06
- **Needs:** CF-01 (n_eff, power), CF-05 (rows 4, 5, 14), CF-06 (rows 1, 12, trial factor), CF-07 (rows 7 to 9), CF-08 (edge series), CF-09 (hard fail), CF-10 (calibration), CF-12 (kill-rule state, optional). Until a producer exists its input is `unavailable`, which counts as fail.
- **Size:** about 7 agent-days (pure calculator and tests 3, API, snapshots and daily service 1.5, gate wiring to the cell lifecycle 1, screen 1.5).
- **Safety:** yes (light). The scorecard is derived and read-only, but it is the precondition of owner acts that start paper following and, later, scale-up; it can block them once `enforce` is ON. It never raises size by itself and places no order.

## Assumptions (taken as recommended)
1. **Two research notes proposed overlapping things and this spec merges them.** The edge note has a 15-row go / no-go checklist on out-of-sample evidence; the speed note has a readiness score of six sub-scores whose **minimum** is the score, with hard fails and four labels. They answer different questions: *is the research result believable* (checklist, before any paper size) and *is the forward evidence strong enough to move on* (score). One object carries both.
2. **Cell lifecycle (read).** `CellState` is `Research -> Candidate -> Paper -> Active -> Paused -> Retired`; `Candidate` "passed the out-of-sample gates; waits for the owner"; `Paper` "approved to be followed on paper"; `Active` "stayed within its band for the paper days". Moves are owner acts with changelog rows (P6-08). The scorecard becomes a **precondition** of two of those moves.
3. **Every threshold is a proposal** in versioned settings (home from CF-00); each scorecard snapshot stores the settings hash it used. Inputs that cannot be computed are `unavailable`, and `unavailable` is fail (CLAUDE.md approvals rule). A sub-score that does not apply (no ML model: no calibration) is `not_applicable` and is left out of the minimum, and says so.
4. **Report-only first.** With `confidence.gate.enforce` OFF (default) the gates only show a warning on the owner act; ON (owner act, fresh password) they refuse the move.

## Decisions (researched)
**D1. Part A, the edge checklist (per strategy version, from stored results).** Pass / fail / unavailable per row; thresholds are the research proposals and every row names its producer.

| # | Criterion | Go threshold | Producer |
|---|---|---|---|
| 1 | Rationale and falsifier registered, plan frozen | present, hash frozen | CF-06 |
| 2 | Independent trades (OOS plus hold-out) | n_eff at least 300 and at least 30 per used cell | CF-01 |
| 3 | Net expectancy at 1x costs | at least `0.15 x Factor(N)` R | Lab result, CF-06 ledger |
| 4 | Cost stress | 1.5x at least +0.08 R and 2x above 0 | CF-05 |
| 5 | Fill one minute later | at least 70 % of expectancy kept, per signal | CF-05 |
| 6 | t-statistic of OOS plus hold-out | at least `2.0 x Factor(N)`, `SE = sd / sqrt(n_eff)` | Lab result, CF-01 |
| 7 | Random-entry null | real mean above the 95th percentile of 1000 seeds | CF-07 |
| 8 | Placebo and mirror | mirror at most 0 and real ranks first of 21 | CF-07 |
| 9 | Plateau | neighbours keep 70 %, grid median above 0 | CF-07 |
| 10 | Stability | at least 70 % of calendar quarters at or above -0.05 R; no year over 60 % of total R; no instrument over 15 % | Lab result |
| 11 | Drawdown | observed max drawdown within the Monte Carlo 90th percentile and MC 95th under 20 R (or the account loss budget) | P5-05 block |
| 12 | Hold-out | used once, at least 50 % of dev OOS and above 0, at least 30 trades | CF-06 |
| 13 | Benchmark | beats the same-universe equal-weight / buy-and-hold net on a risk-adjusted basis | UNVERIFIED producer (P8-07 baskets benchmarks) |
| 14 | Capacity and tier | all trades tier A or B, order at most 1 % of the minute value | CF-05 |
| 15 | Paper confirm | the P3-09 20-trade check passed, the P10 phase check MET, and the edge-series mean within 1.5 SE of the OOS mean | P3-09, P10-06, CF-08 |

**Gate R (research to paper-following):** rows 1 to 12 and 14 all pass. **Gate S (paper to scale-up / next phase):** Gate R, rows 13 and 15, and the readiness label "Ready for the next phase" (D2). Row 3 and the existing `expectancy_R` gate use the same `0.15 x Factor`; the scorecard is stricter on count (n_eff 300 against `30 x Factor` trades) and says so.
**D2. Part B, the readiness score = the minimum of six sub-scores (0 to 100), piecewise linear between anchors, clamped.** The minimum, not the mean, so one weak link caps the score.

| Sub-score | 0 | 50 | 100 | Input |
|---|---|---|---|---|
| Evidence | n_eff at most 15 | 30 | at least 100 | forward n_eff, after costs (edge series, CF-08, CF-01) |
| Effect | lower 90 % bound of mean net R at most 0 | 0.10 R | at least 0.15 R | forward mean net R |
| Power at 0.3 R | at most 30 % | 50 % | at least 80 % | `P(Z > 1.645 - 0.3 sqrt(n_eff) / 1.5)` |
| Backtest agreement | mean below the band's 5th percentile | inside the band | inside the band and at least 70 % of months (with 5 or more trades) positive | P10-04 band |
| Calibration | ECE above 10 pp | 5 pp | at most 3 pp | CF-10 (not applicable without a model) |
| Stability | drift alarm on, or at most 3 of 6 folds positive | 4 of 6 and no alarm | at least 5 of 6 and no alarm | P6-05, P6-09 |

With fewer than 6 folds the stability sub-score is capped at 50 (the P5-03 / P6-05 cap). *Rejected:* an average (hides a fatal weak link); a weighted sum with tuned weights (invites tuning to a wanted label).
**D3. Hard fails (score 0, label "Not ready", each listed by name).** An integrity fault (the P7 and P10 integrity checks; the exact list is UNVERIFIED); the cost sheet not confirmed and not checked against a bill (P10-00 D5, CF-05); any `unavailable` input to a sub-score that applies; the CF-09 failure boundary crossed in this epoch; a hold-out reused for a pass claim (CF-06); evidence only from tier C; an active CF-12 kill-rule demotion.
**D4. Labels.** Below 40 "Not ready"; 40 to 69 "Building"; 70 to 84 "Usable with advice"; 85 or more "Ready for the next phase". Every label is shown with the weakest sub-score by name and what would move it ("needs n_eff 55 more", from the anchors).
**D5. System level.** `system = min(integrity, capital-weighted mean of the strategy scores)` with weights = planned risk share (equal weights, labelled, when no plan exists; whether the plan exposes risk share per cell is UNVERIFIED) and `integrity` = 100, or 0 when any integrity check fails. One number, the weakest strategy and the weakest sub-score are named.
**D6. Where it bites.** `IReadinessGate` is called by the owner act that moves a cell `Candidate -> Paper` (Gate R) and `Paper -> Active` (Gate S). Enforce OFF: the act proceeds with a visible warning and the result is recorded in the changelog row. Enforce ON: refused with the failing rows. Auto option (CLAUDE.md approvals rule): `confidence.gate.auto_to_paper` default OFF; ON lets the system make the `Candidate -> Paper` move itself when Gate R passes, recorded as `auto` with the settings snapshot, never as an owner approval; it never exceeds the small paper size set in the account's risk profile and never makes any move toward `Active`. Turning either switch ON is an owner act with a fresh password.
**D7. Snapshots.** A daily service stores an insert-only snapshot (jsonb, settings hash) so a gate decision can cite exactly what was true and a trend can be drawn; a gate use stores its own snapshot.

## Goal
One screen and one object answer, per strategy and for the whole system: how believable is the edge, how strong is the forward evidence, what is the weakest link, and may this move to the next stage.

## Scope
**In:** Domain `EdgeChecklist` (15 rows, Kleene-style three states as `Domain/Planning/Kleene` if it fits, UNVERIFIED), `ReadinessScore` (anchors, interpolation, hard fails, labels, "what would move it"), `SystemReadiness`, `Power` use (from CF-01); Application `GetScorecardQuery/Handler`, `ListScorecardsQuery`, `SnapshotScorecardsService` (id `confidence-scorecard`), `IReadinessGate` and its call from the lifecycle owner acts; endpoints; migration `AddConfidenceSnapshots`; the `readiness` frontend feature with a lazy route and one nav entry; settings; ADR; docs; `gen-api`.
**Out:** producing the inputs (CF-01, 05, 06, 07, 08, 09, 10, 12 own them); changing the existing verdict; automatic scale-up (P11-04 owns the risk ramp and must call the gate with its own auto setting); the benchmark producer (row 13); any order.

## Acceptance criteria
1. **Research example, all rows 2, 3, 6.** 320 independent trades, mean +0.20 R, sd 1.0 R: `SE = 1 / sqrt(320) = 0.05590`, `t = 3.578`. With `N = 2` trials (`Factor = 1.25`): row 3 needs `0.1875` (pass), row 6 needs `t >= 2.5` (pass). With `N = 8` (`Factor = 1.75`): row 3 needs `0.2625` (**fail**, 0.20 is below), row 6 needs 3.5 (pass, barely); with `N = 64` (`Factor = 2.5`): row 3 needs 0.375 (fail), row 6 needs 5.0 (fail). *The research note passed this case at 8 trials by checking rows 2 and 6 only; row 3 fails it.* Row 2 passes at 320 (at least 300) (unit).
2. **Sub-score interpolation.** Evidence: n_eff 15 gives 0, 22.5 gives 25, 30 gives 50, 45 gives `50 + 50 x 15 / 70 = 60.7`, 100 gives 100. Effect: bound 0 gives 0, 0.05 gives 25, 0.10 gives 50, 0.125 gives 75, 0.15 gives 100. Power: 30 % gives 0, 38.08 % gives 20.2, 50 % gives 50, 80 % gives 100. Calibration: 10 pp gives 0, 6 pp gives 40, 5 pp gives 50, 4 pp gives 75, 3 pp gives 100 (unit).
3. **Power by hand.** `n_eff = 45`: `z = 0.3 x sqrt(45) / 1.5 = 1.3416`, power `P(Z > 1.645 - 1.3416 = 0.3034) = 38.08 %`. Check against P10-00 D7: `n = 60`, `delta = 0.2`: `z = 1.0328`, power `P(Z > 0.612) = 27.0 %`; `n = 60`, `delta = 0.5`: 82.5 % (unit).
4. **Research hand example, recomputed.** n_eff 45 (60.7), lower bound 0.05 (25), power 38.08 % (20.2), inside the band (50), ECE 6 pp (40), 5 of 6 folds and no alarm (100): score = **20.2**, label "Not ready", weakest link **power at 0.3 R**. *The research note computed 25 with the effect bound as the weakest link, from an evidence score of 70 and a power score of 37 that its own anchors do not give.* A hard fail turns any score into 0 with the fail named (unit).
5. **Min, not mean.** Sub-scores 100, 100, 100, 100, 100, 30 give 30 (the mean would be 88); a not-applicable calibration is omitted and listed (unit, property).
6. **Hard fails.** Each of the seven in D3, set alone with otherwise perfect inputs, gives 0 and "Not ready" with the name; an `unavailable` applicable sub-score is a hard fail; `not_applicable` is not (unit).
7. **Labels and what-would-move-it.** 39.9 is "Not ready", 40 "Building", 69.9 "Building", 70 "Usable with advice", 84.9 the same, 85 "Ready for the next phase". For an evidence sub-score at n_eff 45 the text says "55 more effective trades reach 100" (`100 - 45`); every such distance is computed from the anchors, never invented (unit).
8. **System level.** Strategies A (risk share 60 %, score 72) and B (40 %, 30): weighted mean `0.6 x 72 + 0.4 x 30 = 55.2`, "Building", weakest link B; with an integrity fault: 0; with no risk shares the weights are equal and labelled (unit).
9. **Gates.** Gate R needs rows 1 to 12 and 14 all pass; row 13 or 15 failing does not fail Gate R but fails Gate S; with `enforce` OFF the move proceeds and the changelog row carries the failing rows; ON it is refused with 409 `confidence.gate_failed` and the rows; `unavailable` row blocks (integration with the lifecycle handler).
10. **Auto option.** `auto_to_paper` OFF (default, no row): no automatic move; ON: the move is made when Gate R passes, changelog actor `auto`, settings hash recorded, size never above the paper profile, never toward `Active`; turning ON needs the owner session and a fresh password; the history is append-only (integration).
11. **Existing verdict unchanged.** PASS / MARGINAL / FAIL of stored P5 runs are byte-identical with and without this slice (golden).
12. **Screen.** `/readiness` shows the system card, a sortable strategy table (label, score, weakest sub-score, rows passed of 15, hard fails), a detail view with the 15 rows (value, threshold, status as text and icon) and six sub-score bars with their anchors; loading, empty (no strategy yet), error states; status not by colour alone; lazy chunk; the eager cost is the nav entry only (bundle guard) (frontend tests with MSW).
13. Snapshots are insert-only (grants test); a decision cites its snapshot id; no secrets or trade rows in logs; Architecture tests green.

## Contract
- **API:** `GET /api/confidence/scorecards` -> system card plus list; `GET /api/confidence/scorecards/{strategyId}/versions/{v}` -> checklist, sub-scores, hard fails, labels, what would move it, settings hash; `PUT /api/confidence/settings` (owner act, fresh password) for `enforce` and `auto_to_paper`. Problems: 404, 403 step-up, 409 `confidence.gate_failed`. `gen-api`.
- **Data:** migration `AddConfidenceSnapshots`: `confidence.scorecard_snapshots(id, strategy_id, version, taken_at, scope 'daily'|'gate', body jsonb, settings_hash)` insert-only; changelog gains the gate result columns (extends P6-08). Settings `confidence.score.*` (anchors, 0.3 R, sigma 1.5, month rule), `confidence.gate.*`.
- **UI:** route `/readiness`, feature folder `frontend/src/features/readiness`, nav entry; no prototype screen exists, follow the Plans and Learning look (same convention as P10-00 D12).
- **Jobs:** `confidence-scorecard` daily after the shadow grade; idempotent per day; starts paused.

## Non-functional
Pure computation over stored summaries; the list view under 200 ms for tens of strategies; snapshots are kilobytes. Deterministic. Observability: one log event per snapshot with counts only.

## Test plan
Unit (Domain): every table and example above, properties (min, clamp, monotone anchors, order independence). Unit (Application): inputs from fakes, `unavailable` propagation, gates, auto path. Integration: endpoints, snapshots grants, lifecycle refusal and warning paths. Frontend (MSW): the screen states. Architecture: Domain purity; `IReadinessGate` is the only route from the lifecycle handler to the scorecard.

## Risks
Most producers do not exist yet, so for a long time every row reads `unavailable` and every score 0: correct, and the screen must say "waiting for CF-xx" rather than look broken. Thresholds are judgement; tuning them after seeing a wanted label would defeat the point, so changes are audited settings with reasons. Row 2's 300 independent trades is far from the data in hand (about 2.1 years): that is the finding, not a defect.

## UNVERIFIED
The list of integrity checks. Whether the plan exposes risk share per cell. Whether P8-07 benchmarks give row 13. The definition of "sign stable by month" (proposal: 70 % of months with 5 or more trades positive). The 15 research thresholds and the anchors are proposals. Whether `Kleene` (Domain/Planning) is reusable here.

## Open owner questions
1. The thresholds (research open question 1): accept the proposals as a starting set? 2. Gate R as the precondition of `Candidate -> Paper` (recommended) or of something else? 3. Enforce ON when? 4. `auto_to_paper` ON ever (recommended: OFF until you have watched Gate R pass manually a few times)? 5. Row 13 (benchmark) required for Gate S even though no producer exists yet?

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddConfidenceSnapshots` added and tested up and down
- [ ] Design docs/ADR updated (docs 09, 11, 13, 18); `scripts/build-docs.sh`

## Review fixes 2026-10-27
1. **Deadlock fix (with CF-06).** Before the hold-out unlock, rows 2, 3 and 6 use development OOS only, labelled "pre-hold-out"; after the unlock they are recomputed on OOS plus hold-out. CF-06's auto-unlock precondition uses the pre-hold-out values.
2. **Kill-rule hard-fail term (D3).** Until CF-12 ships there is no producer, so "an active CF-12 kill-rule demotion" reads `not_applicable`, not `unavailable`; it becomes applicable when CF-12 ships.
3. AC6 is read with this change: that term is not an `unavailable` hard fail before CF-12.
