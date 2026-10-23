# 0074 — Drift monitors: a frozen band, a CUSUM and calibration on replayed history, proposals only

- **Status:** Proposed (spec [P6-09](../specs/P6-09-drift-monitors.md), approved under the owner's standing run instruction of 2026-10-22, every open question as recommended; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0064](0064-cell-statistics-bayesian-shrinkage-recency-weights-and-point-in-time-snapshots.md) (cells, unit of R), [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (metrics), [ADR 0070](0070-model-registry-content-addressed-files-and-an-append-only-champion-record.md) (models, hash-checked files), [ADR 0073](0073-the-learning-changelog-and-the-cell-lifecycle-the-log-is-the-state.md) (the changelog this slice does not write), ADR 0071 (the other read-only `xd learn` verb).

## Context
Doc 09 layer 4 says rolling expectancy below the backtest band for K checks pauses a cell, and lists CUSUM and calibration as lesson monitors. There is no live stream until P10, and P6-08 made the pause a recorded move that needs evidence.

## Decision
1. **Three pure detectors** over time-ordered results, deterministic, each with hysteresis and evidence: rolling expectancy against a band (3 consecutive checks below, 3 back above the half-way level to clear), a downward CUSUM on R (k 0.5, h 8, capped at 2 h, cleared at 0.5 h), and calibration drift of a model (gap in pp beyond a limit and beyond 2.5 standard errors, 3 in a row; evidence includes the window ECE and Brier from `MetricsCalculator`).
2. **The band is frozen** from the reference period (trades known before the monitored period), so the data it judges cannot move it. It is a normal approximation of the window mean, the stated stand-in for a credible band of expectancy; a cell without 30 reference trades or without spread is not monitored.
3. **No look-ahead:** results are ordered by the instant they became known (exit, or for calibration the end of the label horizon) and then by key; a check is made only after the last result of an instant. A test proves later trades never change earlier events.
4. **CUSUM threshold 8, not 5.** With parameters estimated from a few dozen trades the textbook 5 raised a false alarm on about one stationary series in five over 120 trades; 8 is below 8 % in the test. The false-alarm rates are asserted on seeded series.
5. **Calibration reads only unseen outcomes** (dated after the model's last training trade), scored through the hash-checked model file; the noise term stops a 100-outcome window from alarming on 5 pp of luck.
6. **Replay driver, read only.** The last N months (default 6) of stored runs are the monitored period. `DriftReplay` returns a report with the events and **proposed** transitions (pause on the first alarm of a cell, "recovered in shadow" on the last to clear). Nothing is persisted, no changelog row is written, the cell state is not read. Acting on alarms is later, with live data (P10).
7. **Verb** `xd learn drift`, handler `ReplayDriftHandler`, events 4560 and 4561. No migration, no endpoint.

## Consequences
- P6-16 can reuse the detectors for its lesson monitors without a schema; the changelog slice P6-08 gets its "within band" and "recovered in shadow" facts from a later wiring, not from this report.
- Every default is a judgement default and is printed in the report; changing a detector's rule changes its output, so the options are part of the result.

## Honest limits
Replayed history with modelled fills is not live trading. R is skewed, so band false-alarm rates are approximate. Many cells and three monitors are watched at once, so some alarms are chance. The in-sample exclusion for calibration leaves a model without a hold-out with nothing to watch.
