# 0242 — Few visible settings, and learned changes tighten automatically while loosening needs the owner's password

- **Status:** Accepted (owner, 2026-11-09, in chat, as part of the AU programme). Not yet built.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Narrows the P7 safety answer Q6 and the P10 row for tightening of allow-listed thresholds only. Amends doc 09 Revert. Consistent with tighten-only settings ([0228](0228-the-probability-of-backtest-overfitting-is-computed-by-cscv-on-the-sweeps-daily-series-and-is-report-only-by-default-with-gate-config-tighten-only-it-can-only-cap-a-pass-at-marginal.md), [0230](0230-the-min-turnover-filter-passes-on-a-20-day-median-traded-value-of-at-least-20-crore-the-tier-bps-and-range-share-are-tighten-only-settings-and-the-defaults-are-judgment-calls-to-be-replaced-by-measured-paper-fills.md)).

## Context
The owner ranks simplicity first. Today there are many settings over several surfaces. The P7 safety answers said no automatic learning, and the P10 row sends configuration changes through the hypothesis loop.

## Decision
1. Every setting has a descriptor: tier (`Essential`, at most eight; `Advanced`; `Internal`), default, validated range, safer direction, learnable flag. No descriptor, no merge.
2. Settings show Essential first, with one named `Advanced settings` level. Nothing existing is removed, and existing password acts stay.
3. Only allow-listed thresholds can be changed from learning. A change in the safer direction is automatic when Auto is ON and is recorded as `auto` with the setting snapshot. Any other direction, and an unknown direction, needs a fresh owner password.
4. Undoing an automatic tightening is a loosening and asks for the password.
5. Doc 09 guardrails hold: one variable, out-of-sample proof, weekly cap. `unavailable` never passes.

## Consequences
An automatic tightening can reduce the number of suggestions; a notice says so and it does not reverse itself.
