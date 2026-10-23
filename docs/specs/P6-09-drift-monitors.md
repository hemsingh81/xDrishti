# P6-09 — Drift monitors

- **Status:** Approved (under the owner's standing run instruction of 2026-10-22; every open question taken as recommended). Built 2026-10-23.
- **Phase / roadmap row:** P6 — Learning engine & ML.NET model, slice P6-09 of [P6-00](P6-00-learning-engine-and-ml-plan.md), 2 d
- **Design refs:** docs/design/09-learning-engine.md §1 layer 4, §10 · docs/design/16 (`drift: window_trades 20, below_band_checks 3, action pause`; `max_calibration_error_pp 5`) · ADR 0074 · ADR 0064 (cells) · ADR 0067 (metrics) · ADR 0070 (models) · ADR 0073 (lifecycle, changelog)

## Assumptions (taken as recommended; the owner may reverse any later)
1. **Replayed history only, read only.** The "live" stream is the last `--monitor-months` (default 6) of stored runs; everything before is the reference. Nothing is persisted, no changelog row is written, no cell state is read or changed. Auto-pause (the doc 16 action `pause`) is therefore **not** done: the report lists a *proposed* transition (pause when the first alarm of a cell goes on; "recovered in shadow" when the last goes off). Acting on it is a later slice, with live data at P10.
2. **The band** is made from the reference trades only and is frozen: mean R minus 1.645 standard errors of a window mean (reference sd over the square root of `window_trades`), the one-sided 5 % edge. It needs 30 reference trades (doc 16 `min_trades`) and some spread; otherwise the cell is listed as not monitored. The doc's "credible band" for expectancy has no Bayesian form in P6-01 (only the win rate has), so this normal approximation is the stated stand-in.
3. **Checks** are made after every 20 new trades (non-overlapping windows) and only after the last trade of an instant, so a check sees only trades known by then. The band alarm fires after 3 consecutive checks below the edge and clears after 3 consecutive checks at or above the half-way point between the edge and the reference mean (hysteresis).
4. **CUSUM** is one-sided (downward) on R standardised by the reference: allowance k 0.5, threshold h **8**, sum capped at 2 h, cleared at 0.5 h. h 8, not the textbook 5: with the mean and spread estimated from a few dozen trades, 5 alarmed on about one stationary series in five over 120 trades (measured in the test).
5. **Calibration** of a registered model is mean predicted against observed win rate on a rolling window of 100 outcomes, checked every 50, in percentage points, with the window's ECE and Brier (P6-05 metrics) in the evidence. A breach needs the gap beyond the 5 pp limit **and** beyond 2.5 standard errors (a window of 100 cannot see 5 pp); 3 breaches in a row fire; 3 checks within half the limit clear. Only outcomes dated after the model's last training trade are read (older ones are in-sample and hide drift); a model with no hold-out leaves none, and the report says so. Outcomes are ordered by when their label became known (the end of the label horizon).
6. Calibration alarms propose **no** cell transition: the fix is recalibration or retraining, a decision (P6-10).
7. Win-indicator CUSUM is not built (R only). Log ids 4560 and 4561. ADR number 0074. No endpoint, no screen, no migration.

## Goal
Given stored runs (and optionally a registered model), replay the last months as if they were arriving, and report when rolling expectancy fell below the cell's band, when the CUSUM tripped, and when the model's calibration slipped, with the numbers, when each cleared, and what transition each would suggest.

## Scope
**In:** Domain `DriftOptions`, `RollingBandDetector`, `CusumDetector`, `CalibrationDriftDetector`, `DriftReplay`, `DriftReport` and the small records; Application `ReplayDriftQuery`/`ReplayDriftHandler`, `DriftLog`; verb `xd learn drift`; ADR 0074; docs.
**Out:** persisting events, writing changelog rows or lifecycle transitions, reading the cell state, the pause action, paper days, an endpoint, a screen, win-indicator CUSUM, live use (P10), opening lessons (P6-16).

## Acceptance criteria
1. A constructed series fires the band alarm after three checks below the edge and clears after three back above the clear level; two checks below do not fire; a level between the edge and the clear level does not clear.
2. A constructed shift fires the CUSUM and clears when it ends; a shift under the allowance does not fire.
3. A constructed calibration slip fires and clears at the expected checks; a gap inside the noise limit does not fire; a probability outside 0 to 1 is refused.
4. Seeded stationary series (300 for expectancy, 200 for calibration) alarm rarely: band under 3 %, CUSUM under 8 %, calibration under 3 %.
5. No look-ahead: adding later trades does not change any event at or before an earlier instant; several trades with one instant are read together.
6. Determinism: the input order does not change the events or the report text.
7. Cells with a short or flat reference are listed as not monitored; bad options, repeated cells, non-numbers are refused.
8. Proposals follow the alarms (pause on the first, recovery on the last) and are marked "not recorded"; nothing is written.
9. Handler: stored runs to a report; bad request refused before any read; unknown run and unknown model are not found.
10. Verb `xd learn drift` parses its options without I/O; exit codes as the other `xd` verbs. The handler is internal sealed and registered.

## Contract
- **API:** none. **Data:** none. **UI:** none. **Errors:** `learning.invalid`, `learning.run_not_found`, `models.not_found`, `drift.invalid`, `drift.no_band` (per cell, not an error of the run).

## Non-functional
Pure O(n) per cell and window. Logs carry counts, codes and milliseconds only (4560, 4561).

## Test plan
Unit: `DriftDetectorsTests` (constructed series, hysteresis, stationary false-alarm rates, no look-ahead, ties, order), `DriftReplayTests`, `ReplayDriftHandlerTests`; `LearnDriftOptionsTests`.

## Open questions
None blocking. Whether the stated alarm should later write the changelog and pause a cell on its own is for the live slices (P10).

## Done when
- [x] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] ADR 0074; docs and roadmap updated
