# 0146 — A regime label exists only where the stored context is ready, it is read from the days before the decision day, and a regime cell never mixes the two bases

- **Status:** Accepted (decided in P6-02 under the owner's standing run instruction; spec [P6-02](../specs/P6-02-regime-labels-and-regime-cells.md)).
- **Date:** 2026-10-27.
- **Relation to other ADRs:** [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) (the regime, Amendments 1, 2, 13, 14), [0062](0062-robustness-sensitivity-monte-carlo-by-year-and-a-sign-flip-baseline.md) (`by_regime`), [0064](0064-cell-statistics-bayesian-shrinkage-recency-weights-and-point-in-time-snapshots.md) (cells), [0145](0145-the-simulator-reads-the-market-context-point-in-time-from-stored-days-and-the-run-key-carries-their-fingerprint.md).

## Context
The context exists (P3-01d/e) and cells carry a regime part that was always `*`. The honest-state rule of the system says a number that cannot be computed is `unavailable`, never a default; ADR 0033 and P3-01d D2 added that `regime_vol` falls back to the index percentile when the VIX percentile is not ready, so one regime name can mean two things.

## Decision
1. **A label is a read, not a stored fact.** `RegimeLabel` is derived from `IContextFigures.AsOfAsync(date)`: the regime of the latest stored day strictly before the date. It is available only when the context returns a regime; otherwise it is `unavailable` with one of five reasons (`no_context`, `before_history`, `stale`, `context_invalid`, `regime_not_ready`). No default, no carry-forward.
2. **A trade's decision day is its entry date**, and its tag is the label of that date. Tags are computed in the use case and are not written on the trade record.
3. **The basis is part of the cell.** The regime part of a cell key is `NAME` for `index_and_vix` and `NAME~index_only` for `index_only`. A cell never holds both.
4. **Regime cells are an option of the snapshot** (`--by-regime`, default off), computed beside the `*` cells with the priors of the `*` family, not added to them. The `*` rows of a snapshot are identical with and without the option. Readers of the newest snapshot (selection, plan, digest, lifecycle, `/api/cells`) read only `*` cells; regime rows have their own read and endpoint. No migration: the regime column already holds free text up to 64 characters.
5. **The Lab `by_regime` block keeps its text and gains optional buckets** (trades, mean R, total R, enough, insufficient note, unavailable counts by reason).
6. **Cut:** the Reports dimension, the training-set `regime` feature and the `against_regime` loss tag (each needs its own measured or re-validated slice), and regime as a shrinkage parent (CF-02).

## Consequences
- Regime cells are thin by design (up to 18 parts per cell); most read "not enough evidence", and that is the stated result. A regime with no day shows 0.
- A label for a date uses only data before it, so adding days never changes an earlier label (prefix-stable).
- The names are duplicated in the Domain and pinned by a test against the indicator library.
- Rejected: `research.trade_regimes` or a stored per-day table (a second source of truth); a basis column (a migration and a seventh key part); regime rows in the same reads as `*` rows (silent duplicates for consumers).
- Limit: the decision day is the entry date; an order that rested overnight was decided a session earlier, so its label may read one context day later than the decision.
