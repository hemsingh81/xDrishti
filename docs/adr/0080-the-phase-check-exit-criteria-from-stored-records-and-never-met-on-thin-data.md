# 0080 — The phase check: exit criteria from stored records, and never MET on thin data

- **Status:** Proposed (spec [P6-18](../specs/P6-18-the-phase-check-and-the-learning-api.md), approved under the owner's standing run instruction of 2026-10-22; built 2026-10-23). Becomes Accepted when the owner has read it.
- **Date:** 2026-10-23.
- **Relation to other ADRs:** [ADR 0067](0067-validation-harness-and-baselines-purged-folds-and-an-honest-eligibility-rule.md) (eligibility, minimum folds), [0070](0070-model-registry-content-addressed-files-and-an-append-only-champion-record.md) (champion rule), [0072](0072-experiments-same-entries-purged-folds-an-honest-rule-and-an-append-only-ledger.md) (the experiment judge and its timing note), [0061](0061-the-xd-cli-is-a-verb-of-the-api-host-behind-a-thin-script.md) (the verb).

## Context
The roadmap exit of P6 is "meta-model beats baselines OOS; calibration error at most 5 pp; a knob change can be replayed and compared in under 10 minutes". The data is about 2.1 years with no hold-out policy yet (ISS-19), quarterly folds leave about 3 usable folds, and the earlier slices already cap a verdict on fewer than 6 folds. A phase check that simply echoed a stored "Eligible" or a fast stopwatch would claim more than the data can carry.

## Decision
1. **One pure function, three verdicts.** `PhaseCheck.Evaluate` takes plain facts (the model's stored record, the measured comparison) and returns the criteria and the limits. Each criterion is `MET`, `NOT MET` or `NOT EVALUABLE`; `NOT EVALUABLE` is an answer, not a failure.
2. **MET needs what the data can support.** Criterion 1: the champion record (stored verdict Eligible and `ChampionRule`: hold-out kept, at least 6 folds, the registry's trials). Criterion 2: pooled calibration error at most 5 pp and at least 6 folds. Criterion 3: a replay time as well as a comparison time. `NOT MET` is printed only for a fact: a `CannotBeatBaselines` verdict, an error over 5 pp, a total at or over 600 s.
3. **The timing is half measured.** The check re-reads the two stored runs of the newest experiment and judges them again with the experiment judge, timed with `TimeProvider`. That is the comparison half only. The replay itself (the Lab on 214 instruments) is not measured; the owner can pass `--replay-seconds` from a replay they timed, which is printed as supplied and not verified. The judged outcome must equal the stored outcome, otherwise the comparison is not called.
4. **Limits are part of the report**, in the object and in every text: 2.1 years, about 3 usable folds against 6 needed, the model's hold-out and data range, seen-data folds and modelled fills, the unmeasured Lab replay, stored records only and the model advisory until P7.
5. **A verb, not an endpoint.** `xd learn phase-check [--replay-seconds X]`; exit code 0 whatever the criteria say (a report, not a gate). Nothing is written.
6. **One read endpoint for the Learning screen.** `GET /api/learning/overview`: counts (the newest 200 of each kind, flagged when there may be more), the split by role, outcome or state, and the newest stored verdicts. No statistic is computed. The other read endpoints (cells, models, experiments, hypotheses, lessons, changelog) already exist; the Journey endpoint stays with P8.
7. **Log ids 4620 to 4623**; no table, no migration.

## Consequences
- On 2.1 years the honest answer for criteria 1 and 2 is likely `NOT EVALUABLE`, and for criterion 3 always so without a timed replay. The phase is not declared finished by this check; the owner confirms the exit.
- The owner's two levers, the hold-out at the phase end and the longer history (P6-00 questions 1 and 2), are what can turn a criterion into a call.
- Re-judging costs one read of two runs per check; the judge rules are the defaults, so an experiment recorded with other rules can reproduce differently and is then reported as not reproduced rather than forced.

## First read on the owner's data (2026-10-23)
`scripts/xd.sh learn phase-check` against the local database: no model is registered and no experiment is stored, so all three criteria are NOT EVALUABLE; nothing was fabricated. The criteria are to be re-read once a model has been trained (`xd learn model`) and an experiment recorded (`xd learn experiment`).

## Alternatives considered
- **Echo the stored verdict as MET.** Rejected: it would print MET on 3 folds or without a hold-out, which the champion rule already forbids.
- **Time a fresh Lab replay inside the check.** Rejected for this slice: it needs the stack, a queue and minutes of compute, and the result would depend on the machine's load; the owner can time one and pass it.
- **An endpoint for the phase check.** Rejected: it would be a way to run a re-read on every page load; the Learning screen needs the stored records, which the overview gives.
